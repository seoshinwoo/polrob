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

            var createdSession = new GameSession(_roomCommandQueueCapacity);
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

                await Task.Delay(RoomTickInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Server is stopping.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Room tick loop failed for room {RoomId}.", roomId);
        }
        finally
        {
            if (_gameSessions.TryGetValue(roomId, out var currentSession)
                && ReferenceEquals(currentSession, gameSession))
            {
                _gameSessions.TryRemove(roomId, out _);
            }

            gameSession.Commands.Writer.TryComplete();
        }
    }

    private bool TryStopRoomLoop(string roomId, GameSession gameSession, DateTime now)
    {
        if (!gameSession.HasHadPlayers || gameSession.Sessions.Count > 0)
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

            gameSession.IsStopping = true;
            if (!_gameSessions.TryRemove(roomId, out var removedSession)
                || !ReferenceEquals(removedSession, gameSession))
            {
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

        while (gameSession.Commands.Reader.TryRead(out var command)) // 큐에 명령이 있으면 하나 꺼냄..
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
                    latestMoveByPlayerId[move.Input.Id] = move;
                    break;
            }
        }

        FlushCoalescedMoves(roomId, gameSession, latestMoveByPlayerId);
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

        if (gameSession.Sessions.Count == 0)
        {
            return;
        }

        if (gameSession.GamePhase == GamePhase.Waiting)
        {
            if (IsRoomReadyForCountdown(roomId, gameSession))
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
                if (isManagedRoom &&
                    (!IsRoomReadyForCountdown(roomId, gameSession) ||
                     !HasRequiredConnectedRoles(gameSession)))
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
                TryEnqueueCompletedGameRecord(roomId, gameSession, endedAtUtc);
                _gameRoomService.CompleteGame(roomId);
            }
        }

        var syncData = new GameStateSync
        {
            RoomId = roomId,
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

    private void TryEnqueueCompletedGameRecord(
        string roomId,
        GameSession gameSession,
        DateTime endedAtUtc)
    {
        if (gameSession.GameRecordEnqueueAttempted)
        {
            return;
        }

        // The room loop is single-reader, but mark the attempt before calling the queue so
        // an unexpected queue failure can never produce a duplicate record attempt.
        gameSession.GameRecordEnqueueAttempted = true;

        if (string.IsNullOrWhiteSpace(gameSession.GameRecordId) ||
            gameSession.GameStartedAtUtc is not { } startedAtUtc ||
            gameSession.WinnerRole is not { } winnerRole)
        {
            _logger.LogWarning(
                "Skipped completed game record for room {RoomId} because its start or result snapshot was missing.",
                roomId);
            return;
        }

        try
        {
            var accepted = _gameRecordQueue.TryEnqueue(new CompletedGameRecord(
                Id: gameSession.GameRecordId,
                RoomId: roomId,
                WinnerRole: winnerRole,
                PolicePlayerIds: gameSession.StartingPolicePlayerIds,
                RobberPlayerIds: gameSession.StartingRobberPlayerIds,
                StartedAtUtc: startedAtUtc,
                EndedAtUtc: endedAtUtc,
                DurationSeconds: gameSession.ElapsedGameTime));

            if (!accepted)
            {
                _logger.LogWarning(
                    "Completed game record queue rejected game {GameRecordId} for room {RoomId}.",
                    gameSession.GameRecordId,
                    roomId);
            }
        }
        catch (Exception ex)
        {
            // Persistence must never prevent room cleanup or the final state broadcast.
            _logger.LogError(
                ex,
                "Failed to enqueue completed game record {GameRecordId} for room {RoomId}.",
                gameSession.GameRecordId,
                roomId);
        }
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
