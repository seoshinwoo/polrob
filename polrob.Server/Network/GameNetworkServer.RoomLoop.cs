using polrob.Shared;

namespace polrob.Server.Network;

public partial class GameNetworkServer
{
    private GameSession GetOrCreateGameSession(string roomId, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_gameSessions.TryGetValue(roomId, out var existingSession))
            {
                lock (existingSession.CommandGate)
                {
                    if (!existingSession.IsStopping)
                    {
                        return existingSession;
                    }
                }

                Thread.Yield();
                continue;
            }

            var roomStatus = _gameRoomService.GetRoomStatus(roomId);
            if (!roomStatus.Success && roomId != DefaultRoomId)
                throw new InvalidOperationException($"Room no longer exists: {roomId}");
            var createdSession = new GameSession(_roomCommandQueueCapacity, roomStatus.MapId);
            if (!_gameSessions.TryAdd(roomId, createdSession))
            {
                continue;
            }

            var loopId = Interlocked.Increment(ref _nextRoomLoopId);
            var loopTask = Task.Run(
                () => RunRoomTickLoopAsync(roomId, createdSession, stoppingToken),
                CancellationToken.None);
            _roomLoopTasks[loopId] = loopTask;
            _ = RemoveCompletedRoomLoopAsync(loopId, loopTask);
            return createdSession;
        }

        throw new OperationCanceledException(stoppingToken);
    }

    private async Task RemoveCompletedRoomLoopAsync(long loopId, Task loopTask)
    {
        try
        {
            await loopTask;
        }
        catch (Exception ex)
        {
            _metrics?.Add("room_loop_failures_total");
            _logger.LogError(ex, "Room loop cleanup failed for task {LoopId}.", loopId);
        }
        finally
        {
            _roomLoopTasks.TryRemove(loopId, out _);
        }
    }

    private bool TryWriteRoomCommand(GameSession gameSession, RoomCommand command)
    {
        lock (gameSession.CommandGate)
        {
            if (gameSession.IsStopping)
            {
                return false;
            }

            if (gameSession.Commands.Writer.TryWrite(command))
            {
                Interlocked.Increment(ref gameSession.QueuedCommandCount);
                return true;
            }
        }

        Interlocked.Increment(ref _roomCommandsDroppedThisSecond);
        return false;
    }

    // 방 하나의 입력 큐를 순서대로 비우고, 같은 루프에서 규칙/상태 tick을 처리합니다.
    private async Task RunRoomTickLoopAsync(
        string roomId,
        GameSession gameSession,
        CancellationToken stoppingToken)
    {
        var lastTickAt = DateTime.UtcNow;
        var udpBroadcastElapsed = TimeSpan.Zero;
        var ruleElapsed = TimeSpan.Zero;
        var stateElapsed = TimeSpan.Zero;
        using var logScope = _logger.BeginScope(new Dictionary<string, object> { ["RoomId"] = roomId });

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;
                var elapsed = now - lastTickAt;
                lastTickAt = now;
                udpBroadcastElapsed += elapsed;
                ruleElapsed += elapsed;
                stateElapsed += elapsed;

                var tickStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                DrainRoomCommands(roomId, gameSession);
                SimulateAuthoritativeMovement(gameSession, elapsed, now);

                while (udpBroadcastElapsed >= UdpMovementBroadcastInterval)
                {
                    FlushPendingUdpMovementBroadcasts(gameSession);
                    udpBroadcastElapsed -= UdpMovementBroadcastInterval;
                }

                while (ruleElapsed >= GameRuleTickInterval)
                {
                    ProcessRoomRuleTick(roomId, gameSession);
                    ruleElapsed -= GameRuleTickInterval;
                }

                while (stateElapsed >= GameStateSyncInterval)
                {
                    ProcessRoomStateSync(roomId, gameSession);
                    stateElapsed -= GameStateSyncInterval;
                }

                if (TryStopRoomLoop(roomId, gameSession, now))
                {
                    return;
                }

                var tickMs = System.Diagnostics.Stopwatch.GetElapsedTime(tickStarted).TotalMilliseconds;
                _metrics?.Add("room_ticks_total");
                _metrics?.Add("room_tick_duration_seconds_total", tickMs / 1000);
                if (tickMs > RoomTickInterval.TotalMilliseconds) _metrics?.Add("room_tick_overruns_total");
                await Task.Delay(RoomTickInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Server is stopping.
        }
        catch (Exception ex)
        {
            _metrics?.Add("room_loop_failures_total");
            _logger.LogError(ex, "Room tick loop failed for room {RoomId}.", roomId);
        }
        finally
        {
            lock (gameSession.CommandGate) gameSession.IsStopping = true;
            try
            {
                if (gameSession.PendingGameRecord != null)
                {
                    if (TryEnqueueCompletedGameRecord(roomId, gameSession, gameSession.PendingGameRecord.EndedAtUtc))
                        _gameRoomService.CompleteGame(roomId);
                    else
                        _logger.LogCritical("Game {GameRecordId} has NOT been durably accepted at shutdown; storage repair is required.",
                            gameSession.GameRecordId);
                }
                if (gameSession.GamePhase is GamePhase.Playing or GamePhase.Countdown)
                {
                    _metrics?.Add("games_aborted_total");
                    _logger.LogWarning("Aborted unfinished game in room {RoomId}; no win/loss record was created.", roomId);
                    _gameRoomService.AbandonGameAfterDisconnect(roomId);
                }
            }
            finally
            {
                foreach (var session in gameSession.Sessions.Values)
                {
                    RemovePlayerRoomRegistration(session.PlayerState.Id, session.ConnectionId);
                    _udpRateLimits.TryRemove(session.PlayerState.Id, out _);
                    session.Client.Close();
                }
                while (gameSession.Commands.Reader.TryRead(out var pending))
                    if (pending is JoinRoomCommand join) join.Client.Close();
                gameSession.Sessions.Clear();
                gameSession.PendingLeaves.Clear();
                var currentEntry = new KeyValuePair<string, GameSession>(roomId, gameSession);
                ((ICollection<KeyValuePair<string, GameSession>>)_gameSessions).Remove(currentEntry);

                gameSession.Commands.Writer.TryComplete();
            }
        }
    }

    private bool TryStopRoomLoop(string roomId, GameSession gameSession, DateTime now)
    {
        if (gameSession.PendingGameRecord != null || !gameSession.PendingLeaves.IsEmpty ||
            gameSession.Sessions.Count > 0)
        {
            gameSession.EmptySinceUtc = null;
            return false;
        }

        gameSession.EmptySinceUtc ??= now;
        if (now - gameSession.EmptySinceUtc < EmptyRoomStopDelay)
        {
            return false;
        }

        lock (gameSession.CommandGate)
        {
            if (gameSession.Sessions.Count > 0 || gameSession.Commands.Reader.TryPeek(out _))
            {
                gameSession.EmptySinceUtc = null;
                return false;
            }

            if (!_gameSessions.TryGetValue(roomId, out var currentSession) ||
                !ReferenceEquals(currentSession, gameSession))
            {
                return false;
            }

            gameSession.IsStopping = true;
            if (gameSession.GamePhase == GamePhase.Playing &&
                !string.Equals(roomId, DefaultRoomId, StringComparison.Ordinal))
            {
                // No TCP player returned during the reconnect grace period. The room
                // cannot finish its game tick with zero sessions, so release the
                // still-active lobby room without creating a winner or game record.
                // Keep this session in the dictionary while abandoning the lobby;
                // a concurrent join then waits for IsStopping instead of creating
                // a new room loop against a lobby about to be removed.
                try
                {
                    _gameRoomService.AbandonGameAfterDisconnect(roomId);
                }
                catch (Exception ex)
                {
                    gameSession.IsStopping = false;
                    _logger.LogError(ex, "Could not abandon disconnected game in room {RoomId}.", roomId);
                    return false;
                }
            }

            var currentEntry = new KeyValuePair<string, GameSession>(roomId, gameSession);
            if (!((ICollection<KeyValuePair<string, GameSession>>)_gameSessions).Remove(currentEntry))
            {
                gameSession.IsStopping = false;
                return false;
            }

            gameSession.Commands.Writer.TryComplete();
            return true;
        }
    }

    // 한 방의 명령 큐에 쌓인 네트워크 이벤트를 꺼내서 처리하는 함수. 처리하는 대상은 Join, Leave, Move 3가지..
    // 핵심은 이동 명령 전부를 처리하지 않고 최신 입력 1개만 남김. 이것을 coalescing(입력 병합)이라고 함.
    // UDP만 coalescing을 하고 입장 및 퇴장은 순서가 중요하기 때문에 즉시 처리해
    private void DrainRoomCommands(string roomId, GameSession gameSession)
    {
        // key : 플레이어ID, value : 최신 이동 명령
        Dictionary<string, MoveRoomCommand>? latestMoveByPlayerId = null;

        // Fixed work budget: continuous input must not starve simulation or shutdown.
        var budget = 512;
        while (budget-- > 0 && gameSession.Commands.Reader.TryRead(out var command))
        {
            Interlocked.Decrement(ref gameSession.QueuedCommandCount);
            switch (command)
            {
                case JoinRoomCommand join:
                    FlushCoalescedMoves(roomId, gameSession, latestMoveByPlayerId);
                    latestMoveByPlayerId?.Clear();
                    HandleRoomJoin(roomId, gameSession, join);
                    break;
                case LeaveRoomCommand leave:
                    FlushCoalescedMoves(roomId, gameSession, latestMoveByPlayerId);
                    latestMoveByPlayerId?.Clear();
                    HandleRoomLeave(roomId, gameSession, leave);
                    break;
                case MoveRoomCommand move:
                    latestMoveByPlayerId ??= new Dictionary<string, MoveRoomCommand>();
                    if (!latestMoveByPlayerId.TryGetValue(move.Input.Id, out var previous) ||
                        move.Input.Sequence > previous.Input.Sequence)
                        latestMoveByPlayerId[move.Input.Id] = move;
                    break;
            }
        }

        FlushCoalescedMoves(roomId, gameSession, latestMoveByPlayerId);
        foreach (var entry in gameSession.PendingLeaves)
            if (gameSession.PendingLeaves.TryRemove(entry.Key, out var leave))
                HandleRoomLeave(roomId, gameSession, leave);
    }

    // 모아 둔 최신 이동 입력들을 실제로 처리하는 함수..
    // 이동 패킷을 받자마자 처리하지 않고, 플레이별로 최신 것만 임시로 모아둠..
    private void FlushCoalescedMoves(
        string roomId,
        GameSession gameSession,
        Dictionary<string, MoveRoomCommand>? latestMoveByPlayerId)
    {
        if (latestMoveByPlayerId is not { Count: > 0 })
        {
            return;
        }

        foreach (var move in latestMoveByPlayerId.Values)
        {
            HandleRoomMove(roomId, gameSession, move);
        }
    }

    // 짧은 주기로 체포 판정, 체포 완료, 탈옥 진행 같은 실시간 게임 규칙을 갱신합니다.
    private void ProcessRoomRuleTick(string roomId, GameSession gameSession)
    {
        if (gameSession.GamePhase != GamePhase.Playing || gameSession.Sessions.Count == 0)
        {
            ClearJailBreakProgress(gameSession, roomId);
            return;
        }

        CompletePendingArrests(gameSession);
        DetectRobbersForArrest(gameSession);
        UpdateJailBreakProgress(roomId, gameSession);
    }

    // 1초마다 게임 페이즈와 남은 시간을 갱신하고 방 전체에 상태를 동기화합니다.
    private void ProcessRoomStateSync(string roomId, GameSession gameSession)
    {
        _gameRoomService.RemoveExpiredEmptyRooms();

        if (gameSession.PendingGameRecord != null)
        {
            if (!TryEnqueueCompletedGameRecord(roomId, gameSession, gameSession.PendingGameRecord.EndedAtUtc)) return;
            _gameRoomService.CompleteGame(roomId);
        }
        if (gameSession.Sessions.Count == 0)
        {
            return;
        }

        if (gameSession.GamePhase == GamePhase.Waiting)
        {
            if (CanStartNewGame() && IsRoomReadyForCountdown(roomId, gameSession))
            {
                gameSession.GamePhase = GamePhase.Countdown;
                gameSession.CountdownTime = 3;
                gameSession.GameTime = GameDurationSeconds;
                gameSession.WinnerRole = null;
                gameSession.ElapsedGameTime = 0;
            }
        }
        else if (gameSession.GamePhase == GamePhase.Countdown)
        {
            gameSession.CountdownTime--;
            if (gameSession.CountdownTime < 0)
            {
                var isManagedRoom = !string.Equals(roomId, DefaultRoomId, StringComparison.Ordinal);
                if (!CanStartNewGame() || (isManagedRoom &&
                    (!IsRoomReadyForCountdown(roomId, gameSession) ||
                     !HasRequiredConnectedRoles(gameSession))))
                {
                    // A custom-room player can disconnect during the countdown. Wait
                    // for the full roster again instead of creating a one-sided result.
                    gameSession.GamePhase = GamePhase.Waiting;
                    gameSession.CountdownTime = 3;
                    gameSession.GameStartedAtUtc = null;
                }
                else
                {
                    var startedAtUtc = DateTime.UtcNow;
                    gameSession.GamePhase = GamePhase.Playing;
                    gameSession.CountdownTime = 0;
                    gameSession.GameStartedAtUtc = startedAtUtc;
                    gameSession.GameRecordId = Guid.NewGuid().ToString("N");
                    gameSession.StartingPolicePlayerIds = gameSession.Sessions.Values
                        .Where(session => session.PlayerState.Role == PlayerRole.Police)
                        .Select(session => session.PlayerState.Id)
                        .Where(playerId => !string.IsNullOrWhiteSpace(playerId))
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(playerId => playerId, StringComparer.Ordinal)
                        .ToArray();
                    gameSession.StartingRobberPlayerIds = gameSession.Sessions.Values
                        .Where(session => session.PlayerState.Role == PlayerRole.Robber)
                        .Select(session => session.PlayerState.Id)
                        .Where(playerId => !string.IsNullOrWhiteSpace(playerId))
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(playerId => playerId, StringComparer.Ordinal)
                        .ToArray();
                    gameSession.GameRecordEnqueueAttempted = false;
                }
            }
        }
        else if (gameSession.GamePhase == GamePhase.Playing)
        {
            gameSession.GameTime--;

            var robbers = gameSession.Sessions.Values
                .Where(s => s.PlayerState.Role == PlayerRole.Robber)
                .ToList();

            var allRobbersCaught = false;
            if (robbers.Count > 0)
            {
                foreach (var robber in robbers)
                {
                    RefreshJailEntry(gameSession, robber.PlayerState);
                }

                allRobbersCaught = robbers.All(p => IsInJail(p.PlayerState));
            }

            if (gameSession.GameTime <= 0 || allRobbersCaught)
            {
                var endedAtUtc = DateTime.UtcNow;
                gameSession.GamePhase = GamePhase.Ended;
                gameSession.GameTime = Math.Max(0, gameSession.GameTime);
                gameSession.WinnerRole = gameSession.GameTime <= 0
                    ? PlayerRole.Robber
                    : PlayerRole.Police;
                gameSession.ElapsedGameTime = gameSession.GameStartedAtUtc.HasValue
                    ? Math.Clamp(
                        (int)Math.Round(
                            (endedAtUtc - gameSession.GameStartedAtUtc.Value).TotalSeconds),
                        0,
                        GameDurationSeconds)
                    : GameDurationSeconds - gameSession.GameTime;
                if (!TryEnqueueCompletedGameRecord(roomId, gameSession, endedAtUtc)) return;
                _gameRoomService.CompleteGame(roomId);
            }
        }

        var syncData = new GameStateSync
        {
            RoomId = roomId,
            MapId = gameSession.Map.MapId,
            Phase = gameSession.GamePhase,
            CountdownTime = gameSession.CountdownTime,
            GameTime = gameSession.GameTime,
            WinnerRole = gameSession.WinnerRole,
            ElapsedGameTime = gameSession.ElapsedGameTime,
            TotalRobbers = gameSession.Sessions.Values.Count(
                session => session.PlayerState.Role == PlayerRole.Robber),
            JailedRobbers = gameSession.Sessions.Values.Count(
                session => IsInJail(session.PlayerState))
        };

        BroadcastTcp(gameSession, TcpMessageType.GameState, SerializeForMetrics(syncData), null);
    }

    private bool CanStartNewGame() => Volatile.Read(ref _draining) == 0 &&
        _operations?.IsDraining != true && _admission?.CanAcceptNewGames != false;

    private bool TryEnqueueCompletedGameRecord(string roomId, GameSession gameSession, DateTime endedAtUtc)
    {
        if (gameSession.GameRecordEnqueueAttempted) return true;
        if (string.IsNullOrWhiteSpace(gameSession.GameRecordId) ||
            gameSession.GameStartedAtUtc is not { } startedAtUtc ||
            gameSession.WinnerRole is not { } winnerRole)
        {
            // A programming/state corruption error must not produce a false Ended broadcast.
            throw new InvalidOperationException($"Completed game in room {roomId} has no valid result snapshot.");
        }
        gameSession.PendingGameRecord ??= new CompletedGameRecord(
            gameSession.GameRecordId, roomId, winnerRole,
            gameSession.StartingPolicePlayerIds, gameSession.StartingRobberPlayerIds,
            startedAtUtc, endedAtUtc, gameSession.ElapsedGameTime);
        try
        {
            if (_gameRecordQueue.TryEnqueue(gameSession.PendingGameRecord))
            {
                gameSession.GameRecordEnqueueAttempted = true;
                gameSession.PendingGameRecord = null;
                _operations?.SetUnpersisted(gameSession.GameRecordId, false);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not persist game {GameRecordId} in room {RoomId}.",
                gameSession.GameRecordId, roomId);
        }
        _operations?.SetUnpersisted(gameSession.GameRecordId, true);
        _metrics?.Add("game_result_acceptance_failures_total");
        // Keep the immutable result in this bounded set of rooms, retry on the next state
        // tick, and do not tell the client the match finished until persistence succeeds.
        return false;
    }

    private bool IsRoomReadyForCountdown(string roomId, GameSession gameSession)
    {
        if (string.Equals(roomId, DefaultRoomId, StringComparison.Ordinal))
        {
            return gameSession.Sessions.Count > 0;
        }

        var roomStatus = _gameRoomService.GetRoomStatus(roomId);
        if (!roomStatus.Success || !roomStatus.Matched)
        {
            return false;
        }

        var expectedPlayerCount = Math.Max(1, roomStatus.CurrentCount);
        return gameSession.Sessions.Count >= expectedPlayerCount;
    }

    private static bool HasRequiredConnectedRoles(GameSession gameSession)
    {
        var connectedRoles = gameSession.Sessions.Values
            .Select(session => session.PlayerState.Role)
            .ToHashSet();
        return connectedRoles.Contains(PlayerRole.Police) &&
               connectedRoles.Contains(PlayerRole.Robber);
    }
}
