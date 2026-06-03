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

    public async Task CreateRoom(string userId, string type = "custom", PlayerRole role = PlayerRole.Police)
    {
        var game = new Game(type);

        // DB에서 유저 정보를 가져와서 Player 객체를 생성합니다.
        var user = await _loginDbService.GetItemAsync<LoginUser>(userId, userId);
        if (user == null) return;

        var player = new Player
        {
            Id = user.UserId,
            Name = user.DisplayName,
            Role = role
        };

        lock (_roomLock)
        {
            if (game.Players == null) game.Players = new List<Player>();
            game.Players.Add(player);
            Games.Add(game);
        }
    }
    public void JoinCustomGame(string userId, string roomId)
    {
        var game = Games.FirstOrDefault(g => g.Id == roomId);
        if (game != null)
        {
            if (!game.Players.Select(p => p.Id).Contains(userId))
            {
                var player = new Player();
                player.Id = userId;
                player.Role = PlayerRole.Robber;
            }
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

                if (game.Players.Count < 6)
                {
                    if (role == PlayerRole.Police)
                    {
                        if (game.Players.Count(p => p.Role == PlayerRole.Police) < 2)
                        {
                            var player = new Player
                            {
                                Id = user.UserId,
                                Name = user.DisplayName,
                                Role = PlayerRole.Police
                            };

                            game.Players.Add(player);
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
                        var player = new Player
                        {
                            Id = user.UserId,
                            Name = user.DisplayName,
                            Role = PlayerRole.Robber
                        };

                        game.Players.Add(player);
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

    private ServerResponse CreateRandomRoom(LoginUser user, PlayerRole role)
    {
        var game = new Game("random");
        var player = new Player
        {
            Id = user.UserId,
            Name = user.DisplayName,
            Role = role
        };

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
            Role = role,
            CurrentCount = game.Players.Count,
            MaxCount = 6,
            CreatedRoom = createdRoom,
            Matched = game.Players.Count >= 6
        };
    }
}
