using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using polrob.Shared;

namespace polrob.Test;

public class BotClient : IAsyncDisposable
{
    private const string DefaultServerUrl = "http://localhost:5174";
    private const string DefaultDevelopmentBotKey = "polrob-local-bot-key";
    private static readonly TimeSpan MovementInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan InitialStateTimeout = TimeSpan.FromSeconds(
        GetPositiveIntEnvironmentVariable("POLROB_BOT_INITIAL_STATE_TIMEOUT_SECONDS", 60));

    private HubConnection? _hubConnection;
    private BotGameNetworkClient? _gameNetworkClient;
    private BotMovementController? _movementController;
    private readonly object _playerStateLock = new();
    private readonly Dictionary<string, Player> _visibleTeamPlayers = new();
    private readonly TaskCompletionSource _matchCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource _initialStateReceived =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource _gameEnded =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Player? _localPlayer;
    private GamePhase _gamePhase = GamePhase.Waiting;
    private DateTime _movementLockedUntilUtc = DateTime.MinValue;

    public string Name { get; set; } = string.Empty;
    public string Id { get; private set; } = string.Empty;
    public PlayerRole Role { get; set; }
    public string RoomId { get; private set; } = string.Empty;
    public int CurrentRoomCount { get; private set; }
    public bool IsMatched { get; private set; }
    public PlayerRole? WinnerRole { get; private set; }
    public int ElapsedGameTime { get; private set; }

    private readonly HttpClient _httpClient;

    public BotClient()
    {
        var serverUrl = Environment.GetEnvironmentVariable("POLROB_SERVER_URL")
            ?? DefaultServerUrl;
        var botKey = Environment.GetEnvironmentVariable("POLROB_BOT_KEY")
            ?? DefaultDevelopmentBotKey;

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(serverUrl)
        };
        _httpClient.DefaultRequestHeaders.Add("X-Polrob-Bot-Key", botKey);
    }

    public async Task Login()
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "auth/bot-login",
            new BotLoginRequest(Name, Role));

        response.EnsureSuccessStatusCode();

        var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>()
            ?? throw new InvalidOperationException("봇 로그인 응답을 읽을 수 없습니다.");

        Id = loginResponse.UserId;
    }

    public async Task Matching()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new InvalidOperationException($"{Name} 봇이 로그인되지 않았습니다.");
        }

        using var response = await _httpClient.PostAsJsonAsync(
            "game/join-random",
            new BotMatchingRequest(Id, Role));

        response.EnsureSuccessStatusCode();

        var matchingResponse = await response.Content.ReadFromJsonAsync<ServerResponse>()
            ?? throw new InvalidOperationException("매칭 실패");

        RoomId = matchingResponse.RoomId
            ?? throw new InvalidOperationException("RoomId가 없음");
        UpdateRoomStatus(matchingResponse);

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(new Uri(_httpClient.BaseAddress!, "hubs/game-room"))
            .Build();

        _hubConnection.On<ServerResponse>("RoomStatusUpdated", UpdateRoomStatus);
        _hubConnection.On<ServerResponse>("GameStarted", UpdateRoomStatus);

        await _hubConnection.StartAsync();
        await _hubConnection.InvokeAsync("JoinRoom", RoomId, Id);
    }

    public Task WaitForMatchAsync(TimeSpan timeout)
    {
        return _matchCompleted.Task.WaitAsync(timeout);
    }

    private void UpdateRoomStatus(ServerResponse response)
    {
        if (!response.Success)
        {
            return;
        }

        CurrentRoomCount = response.CurrentCount;
        IsMatched = response.Matched;

        if (IsMatched)
        {
            _matchCompleted.TrySetResult();
        }
    }

    public async Task GamePlay()
    {
        if (!IsMatched || string.IsNullOrWhiteSpace(RoomId))
        {
            throw new InvalidOperationException($"{Name} 봇의 매칭이 완료되지 않았습니다.");
        }

        await DisconnectMatchingHubAsync();

        _initialStateReceived = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _gameEnded = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _movementController = new BotMovementController(Id);
        _gameNetworkClient = new BotGameNetworkClient();

        RegisterGameNetworkEvents(_gameNetworkClient);

        var serverHost = Environment.GetEnvironmentVariable("POLROB_GAME_SERVER_HOST")
            ?? _httpClient.BaseAddress?.Host
            ?? "localhost";
        var joiningPlayer = new Player
        {
            Id = Id,
            RoomId = RoomId,
            Name = Name,
            Role = Role
        };

        using var gameplayCancellation = new CancellationTokenSource();
        try
        {
            await _gameNetworkClient.ConnectAsync(
                serverHost,
                joiningPlayer,
                gameplayCancellation.Token);
            try
            {
                await _initialStateReceived.Task.WaitAsync(InitialStateTimeout);
            }
            catch (TimeoutException ex)
            {
                throw new TimeoutException(
                    $"{Name} InitialState 수신 타임아웃: room={RoomId}, role={Role}, " +
                    $"timeout={InitialStateTimeout.TotalSeconds:0}s",
                    ex);
            }

            Console.WriteLine($"{Name} 게임 접속 완료: {RoomId} / {Role}");

            var previousTick = DateTime.UtcNow;
            var lastMovementSentAtUtc = DateTime.MinValue;
            while (!_gameEnded.Task.IsCompleted)
            {
                await Task.Delay(MovementInterval);

                var now = DateTime.UtcNow;
                var elapsed = now - previousTick;
                previousTick = now;

                Player? movementSnapshot = null;
                lock (_playerStateLock)
                {
                    if (_gamePhase != GamePhase.Playing ||
                        _localPlayer == null ||
                        _movementController == null)
                    {
                        continue;
                    }

                    if (now < _movementLockedUntilUtc)
                    {
                        _localPlayer.IsMoving = false;
                    }
                    else
                    {
                        _movementController.Update(
                            _localPlayer,
                            _visibleTeamPlayers.Values.ToList(),
                            elapsed);
                    }

                    movementSnapshot = CopyPlayer(_localPlayer);
                }

                var sendInterval = movementSnapshot.IsMoving
                    ? MovementInterval
                    : TimeSpan.FromMilliseconds(100);
                if (now - lastMovementSentAtUtc >= sendInterval)
                {
                    await _gameNetworkClient.SendMoveAsync(movementSnapshot);
                    lastMovementSentAtUtc = now;
                }
            }

            await _gameEnded.Task;
        }
        finally
        {
            await gameplayCancellation.CancelAsync();
            await _gameNetworkClient.DisposeAsync();
            _gameNetworkClient = null;
        }
    }

    public async Task GameOver()
    {
        await DisconnectMatchingHubAsync();
        if (_gameNetworkClient != null)
        {
            await _gameNetworkClient.DisposeAsync();
            _gameNetworkClient = null;
        }
    }

    private static int GetPositiveIntEnvironmentVariable(string name, int fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : fallback;
    }

    private void RegisterGameNetworkEvents(BotGameNetworkClient networkClient)
    {
        networkClient.InitialStateReceived += players =>
        {
            lock (_playerStateLock)
            {
                _visibleTeamPlayers.Clear();
                foreach (var player in players)
                {
                    _visibleTeamPlayers[player.Id] = player;
                }

                if (!_visibleTeamPlayers.TryGetValue(Id, out _localPlayer))
                {
                    _initialStateReceived.TrySetException(
                        new InvalidOperationException($"{Name}의 초기 플레이어 상태가 없습니다."));
                    return;
                }
            }

            _initialStateReceived.TrySetResult();
        };

        networkClient.PlayerJoined += player =>
        {
            lock (_playerStateLock)
            {
                _visibleTeamPlayers[player.Id] = player;
            }
        };

        networkClient.PlayerLeft += playerId =>
        {
            lock (_playerStateLock)
            {
                _visibleTeamPlayers.Remove(playerId);
            }
        };

        networkClient.PlayerStateReceived += player =>
        {
            lock (_playerStateLock)
            {
                if (_visibleTeamPlayers.TryGetValue(player.Id, out var current))
                {
                    CopyPlayerState(player, current);
                }
                else
                {
                    _visibleTeamPlayers[player.Id] = player;
                }

                if (player.Id == Id)
                {
                    _localPlayer = _visibleTeamPlayers[player.Id];
                }
            }
        };

        networkClient.PlayerArrested += (policeId, robberId) =>
        {
            if (Id != policeId && Id != robberId)
            {
                return;
            }

            lock (_playerStateLock)
            {
                _movementLockedUntilUtc = DateTime.UtcNow.AddSeconds(2.2);
                if (_localPlayer != null)
                {
                    _localPlayer.IsMoving = false;
                }
            }
        };

        networkClient.JailBreakReceived += jailBreak =>
        {
            lock (_playerStateLock)
            {
                if (!_visibleTeamPlayers.TryGetValue(jailBreak.RobberId, out var robber))
                {
                    return;
                }

                robber.X = jailBreak.X;
                robber.Y = jailBreak.Y;
                robber.IsMoving = false;
            }
        };

        networkClient.GameStateReceived += gameState =>
        {
            lock (_playerStateLock)
            {
                _gamePhase = gameState.Phase;
            }

            if (gameState.Phase == GamePhase.Ended)
            {
                WinnerRole = gameState.WinnerRole;
                ElapsedGameTime = gameState.ElapsedGameTime;
                _gameEnded.TrySetResult();
            }
            else if (gameState.Phase == GamePhase.Rematching)
            {
                WinnerRole = null;
                ElapsedGameTime = 0;
                _gameEnded.TrySetResult();
            }
        };
    }

    private async Task DisconnectMatchingHubAsync()
    {
        if (_hubConnection == null)
        {
            return;
        }

        var connection = _hubConnection;
        _hubConnection = null;

        try
        {
            if (connection.State == HubConnectionState.Connected &&
                !string.IsNullOrWhiteSpace(RoomId))
            {
                await connection.InvokeAsync("LeaveRoom", RoomId);
            }
        }
        catch
        {
            // Gameplay can continue even if lobby transport cleanup fails.
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    private static Player CopyPlayer(Player source)
    {
        return new Player
        {
            Id = source.Id,
            RoomId = source.RoomId,
            Name = source.Name,
            X = source.X,
            Y = source.Y,
            Speed = source.Speed,
            Radius = source.Radius,
            Angle = source.Angle,
            IsMoving = source.IsMoving,
            Role = source.Role
        };
    }

    private static void CopyPlayerState(Player source, Player destination)
    {
        destination.RoomId = source.RoomId;
        destination.Name = source.Name;
        destination.X = source.X;
        destination.Y = source.Y;
        destination.Speed = source.Speed;
        destination.Radius = source.Radius;
        destination.Angle = source.Angle;
        destination.IsMoving = source.IsMoving;
        destination.Role = source.Role;
    }

    public async ValueTask DisposeAsync()
    {
        await GameOver();
        _httpClient.Dispose();
    }

    private sealed record BotLoginRequest(string Name, PlayerRole Role);
    private sealed record LoginResponse(string UserId, string Name);
    private sealed record BotMatchingRequest(string UserId, PlayerRole Role);
}
