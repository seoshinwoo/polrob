using System.Collections.Concurrent;
using polrob.Shared;

public class GameRoomService
{
    private readonly Lock _createLock = new();
    private readonly Lock _joinLock = new();
    private readonly List<Game> Games = new();
    private readonly LoginDbService _loginDbService;

    public GameRoomService(LoginDbService loginDbService)
    {
        _loginDbService = loginDbService;
    }

    public async Task CreateRoom(string userId, string type = "custom")
    {
        var game = new Game(type);

        // DB에서 유저 정보를 가져와서 Player 객체를 생성합니다.
        var user = await _loginDbService.GetItemAsync<LoginUser>(userId, userId);
        if (user == null) return;

        var player = new Player
        {
            Id = user.UserId,
            Name = user.DisplayName,
            Role = PlayerRole.Police
        };

        lock (_createLock)
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

    public async Task JoinRandomGame(string userId, string roomId, PlayerRole role)
    {
        if (Games.Count > 0)
        {
            lock (_joinLock)
            {
                foreach (var game in Games)
                {
                    if (game.Players.Count < 6)
                    {
                        if (role == PlayerRole.Police)
                        {
                            if (game.Players.Count(p => p.Role == PlayerRole.Police) < 2)
                            {
                                var player = new Player();
                                player.Id = userId;
                                player.Role = PlayerRole.Police;

                                game.Players.Add(player);
                                return;
                            }
                            else
                            {
                                continue;
                            }
                        }
                        else
                        {
                            if (game.Players.Count(p => p.Role == PlayerRole.Robber) < 4)
                            {
                                var player = new Player();
                                player.Id = userId;
                                player.Role = PlayerRole.Robber;

                                game.Players.Add(player);
                                return;
                            }
                            else
                            {
                                continue;
                            }
                        }
                    }
                    else
                    {
                        continue;
                    }
                }
            } // end lock

            // lock 블록 밖에서 비동기 호출을 진행함.
            await CreateRoom(userId, "random");
        }
        else
        {
            await CreateRoom(userId, "random");
        }
    }
}