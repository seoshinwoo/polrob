using polrob.Shared;

public class GameRoomService
{
    private readonly Lock _roomLock = new();
    private readonly List<Game> Games = new();
    private readonly LoginDbService _loginDbService;

    public GameRoomService(LoginDbService loginDbService)
    {
        _loginDbService = loginDbService;
    }

    public async Task<ServerResponse> CreateRoom(
        string userId,
        string type = "custom",
        PlayerRole role = PlayerRole.Police,
        bool isPrivate = true)
    {
        var user = await _loginDbService.GetItemAsync<LoginUser>(userId, userId);
        if (user == null)
        {
            return new ServerResponse
            {
                Success = false,
                Message = "사용자를 찾을 수 없습니다.",
                Role = role
            };
        }

        lock (_roomLock)
        {
            var game = new Game(type, isPrivate)
            {
                RoomCode = CreateUniqueRoomCode()
            };

            var player = CreatePlayer(user, game.Id, role);
            game.Players.Add(player);
            Games.Add(game);

            return CreateRoomStatusResponse(
                game,
                role,
                createdRoom: true,
                message: isPrivate ? "커스텀 방을 만들었습니다." : "방을 만들었습니다.");
        }
    }

    public async Task<ServerResponse> JoinCustomGame(
        string userId,
        string roomCode,
        PlayerRole role = PlayerRole.Robber)
    {
        var user = await _loginDbService.GetItemAsync<LoginUser>(userId, userId);
        if (user == null)
        {
            return new ServerResponse
            {
                Success = false,
                Message = "사용자를 찾을 수 없습니다.",
                Role = role
            };
        }

        lock (_roomLock)
        {
            var normalizedCode = NormalizeRoomCode(roomCode);
            var game = Games.FirstOrDefault(g =>
                g.IsPrivate
                && (string.Equals(g.RoomCode, normalizedCode, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(g.Id, roomCode, StringComparison.OrdinalIgnoreCase)));

            if (game == null)
            {
                return new ServerResponse
                {
                    Success = false,
                    Message = "방 코드를 확인할 수 없습니다.",
                    RoomCode = normalizedCode,
                    Role = role
                };
            }

            if (game.IsOnGame)
            {
                return CreateRoomFailureResponse(game, role, "이미 시작된 방입니다.");
            }

            var existingPlayer = game.Players.FirstOrDefault(p => p.Id == userId);
            if (existingPlayer != null)
            {
                return CreateRoomStatusResponse(game, existingPlayer.Role, message: "이미 참여 중인 방입니다.");
            }

            if (game.Players.Count >= 6)
            {
                return CreateRoomFailureResponse(game, role, "방 인원이 가득 찼습니다.");
            }

            if (role == PlayerRole.Police && game.Players.Count(p => p.Role == PlayerRole.Police) >= 2)
            {
                return CreateRoomFailureResponse(game, role, "경찰 인원이 가득 찼습니다.");
            }

            if (role == PlayerRole.Robber && game.Players.Count(p => p.Role == PlayerRole.Robber) >= 4)
            {
                return CreateRoomFailureResponse(game, role, "도둑 인원이 가득 찼습니다.");
            }

            game.Players.Add(CreatePlayer(user, game.Id, role));
            return CreateRoomStatusResponse(game, role, message: "커스텀 방에 참여했습니다.");
        }
    }

    public async Task<ServerResponse> JoinRandomGame(string userId, string roomId, PlayerRole role)
    {
        var user = await _loginDbService.GetItemAsync<LoginUser>(userId, userId);
        if (user == null)
        {
            return new ServerResponse
            {
                Success = false,
                Message = "사용자를 찾을 수 없습니다.",
                Role = role
            };
        }

        lock (_roomLock)
        {
            foreach (var game in Games)
            {
                var existingPlayer = game.Players.FirstOrDefault(p => p.Id == userId);
                if (existingPlayer != null)
                {
                    return CreateRandomJoinResponse(
                        game,
                        existingPlayer.Role,
                        createdRoom: false,
                        message: "이미 참여 중인 방입니다.");
                }

                if (game.IsPrivate || !string.Equals(game.Type, "random", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (game.Players.Count < 6)
                {
                    if (role == PlayerRole.Police)
                    {
                        if (game.Players.Count(p => p.Role == PlayerRole.Police) < 2)
                        {
                            game.Players.Add(CreatePlayer(user, game.Id, PlayerRole.Police));
                            return CreateRandomJoinResponse(
                                game,
                                role,
                                createdRoom: false,
                                message: "랜덤 방에 참여했습니다.");
                        }

                        continue;
                    }

                    if (game.Players.Count(p => p.Role == PlayerRole.Robber) < 4)
                    {
                        game.Players.Add(CreatePlayer(user, game.Id, PlayerRole.Robber));
                        return CreateRandomJoinResponse(
                            game,
                            role,
                            createdRoom: false,
                            message: "랜덤 방에 참여했습니다.");
                    }
                }

                continue;
            }

            return CreateRandomRoom(user, role);
        }
    }

    public ServerResponse GetRoomStatus(string roomId)
    {
        lock (_roomLock)
        {
            var game = Games.FirstOrDefault(g => g.Id == roomId);
            if (game == null)
            {
                return new ServerResponse
                {
                    Success = false,
                    Message = "방을 찾을 수 없습니다.",
                    RoomId = roomId
                };
            }

            return CreateRoomStatusResponse(game);
        }
    }

    public ServerResponse RemovePlayer(string roomId, string userId)
    {
        lock (_roomLock)
        {
            var game = Games.FirstOrDefault(g => g.Id == roomId);
            if (game == null)
            {
                return new ServerResponse
                {
                    Success = false,
                    Message = "방을 찾을 수 없습니다.",
                    RoomId = roomId
                };
            }

            var player = game.Players.FirstOrDefault(p => p.Id == userId);
            if (player != null)
            {
                game.Players.Remove(player);
            }

            if (game.Players.Count == 0 && !game.IsOnGame)
            {
                Games.Remove(game);
            }

            return CreateRoomStatusResponse(game);
        }
    }

    public ServerResponse ChangePlayerRole(string roomId, string userId, PlayerRole role)
    {
        lock (_roomLock)
        {
            var game = Games.FirstOrDefault(g => g.Id == roomId);
            if (game == null)
            {
                return new ServerResponse
                {
                    Success = false,
                    Message = "방을 찾을 수 없습니다.",
                    RoomId = roomId,
                    Role = role
                };
            }

            if (game.IsOnGame)
            {
                return CreateRoomFailureResponse(game, role, "이미 시작된 방에서는 역할을 바꿀 수 없습니다.");
            }

            var player = game.Players.FirstOrDefault(p => p.Id == userId);
            if (player == null)
            {
                return CreateRoomFailureResponse(game, role, "방에 참여 중인 사용자를 찾을 수 없습니다.");
            }

            if (player.Role == role)
            {
                return CreateRoomStatusResponse(game, role);
            }

            if (role == PlayerRole.Police && game.Players.Count(p => p.Role == PlayerRole.Police) >= 2)
            {
                return CreateRoomFailureResponse(game, role, "경찰 인원이 가득 찼습니다.");
            }

            if (role == PlayerRole.Robber && game.Players.Count(p => p.Role == PlayerRole.Robber) >= 4)
            {
                return CreateRoomFailureResponse(game, role, "도둑 인원이 가득 찼습니다.");
            }

            player.Role = role;
            return CreateRoomStatusResponse(game, role);
        }
    }

    public bool IsRoomMatched(string roomId)
    {
        lock (_roomLock)
        {
            var game = Games.FirstOrDefault(g => g.Id == roomId);
            return game is { IsOnGame: true } || game?.Players.Count >= 6;
        }
    }

    public ServerResponse StartGameIfMatched(string roomId)
    {
        lock (_roomLock)
        {
            var game = Games.FirstOrDefault(g => g.Id == roomId);
            if (game == null)
            {
                return new ServerResponse
                {
                    Success = false,
                    Message = "방을 찾을 수 없습니다.",
                    RoomId = roomId
                };
            }

            if (game.Players.Count == 0)
            {
                return CreateRoomFailureResponse(game, role: PlayerRole.Robber, "참여 중인 플레이어가 없습니다.");
            }

            if (!HasRequiredRoles(game))
            {
                return CreateRoomFailureResponse(game, role: PlayerRole.Robber, "Police와 Robber가 각각 1명 이상 필요합니다.");
            }

            if (!game.IsPrivate && game.Players.Count < 6)
            {
                return CreateRoomStatusResponse(game);
            }

            game.IsOnGame = true;
            return CreateRoomStatusResponse(game, message: "게임을 시작합니다.");
        }
    }

    private ServerResponse CreateRandomRoom(LoginUser user, PlayerRole role)
    {
        var game = new Game("random", isPrivate: false);
        var player = CreatePlayer(user, game.Id, role);

        game.Players.Add(player);
        Games.Add(game);

        return CreateRandomJoinResponse(
            game,
            role,
            createdRoom: true,
            message: "참여 가능한 방이 없어 새 랜덤 방을 만들었습니다.");
    }

    private static ServerResponse CreateRandomJoinResponse(
        Game game,
        PlayerRole role,
        bool createdRoom,
        string message)
    {
        return new ServerResponse
        {
            Success = true,
            Message = message,
            RoomId = game.Id,
            RoomCode = game.RoomCode,
            Role = role,
            CurrentCount = game.Players.Count,
            MaxCount = 6,
            CreatedRoom = createdRoom,
            Matched = game.IsOnGame || game.Players.Count >= 6,
            IsPrivate = game.IsPrivate,
            Players = game.Players.ToList()
        };
    }

    private static ServerResponse CreateRoomStatusResponse(
        Game game,
        PlayerRole? role = null,
        bool createdRoom = false,
        string? message = null)
    {
        return new ServerResponse
        {
            Success = true,
            Message = message,
            RoomId = game.Id,
            RoomCode = game.RoomCode,
            Role = role,
            CurrentCount = game.Players.Count,
            MaxCount = 6,
            CreatedRoom = createdRoom,
            Matched = game.IsOnGame || game.Players.Count >= 6,
            IsPrivate = game.IsPrivate,
            Players = game.Players.ToList()
        };
    }

    private static ServerResponse CreateRoomFailureResponse(Game game, PlayerRole role, string message)
    {
        var response = CreateRoomStatusResponse(game, role, message: message);
        response.Success = false;
        return response;
    }

    private static bool HasRequiredRoles(Game game)
    {
        return game.Players.Any(p => p.Role == PlayerRole.Police)
            && game.Players.Any(p => p.Role == PlayerRole.Robber);
    }

    private Player CreatePlayer(LoginUser user, string roomId, PlayerRole role)
    {
        return new Player
        {
            Id = user.UserId,
            Name = user.DisplayName,
            RoomId = roomId,
            Role = role
        };
    }

    private string CreateUniqueRoomCode()
    {
        string roomCode;
        do
        {
            roomCode = GenerateRoomCode();
        }
        while (Games.Any(g => string.Equals(g.RoomCode, roomCode, StringComparison.OrdinalIgnoreCase)));

        return roomCode;
    }

    private static string GenerateRoomCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return string.Create(6, alphabet, (buffer, source) =>
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                buffer[i] = source[Random.Shared.Next(source.Length)];
            }
        });
    }

    private static string NormalizeRoomCode(string roomCode)
    {
        return roomCode.Trim().Replace(" ", string.Empty).ToUpperInvariant();
    }
}
