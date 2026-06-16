using polrob.Shared;

namespace polrob.Test;

public class BotRunner
{
    private readonly List<BotClient> _botClients = new();
    public BotRunner()
    {

    }

    public async Task TestLogin()
    {
        for (int i = 0; i < 60; i++)
        {
            var bot = new BotClient();

            bot.Name = $"Bot_{i:D4}";
            bot.Role = i % 3 < 2 ? PlayerRole.Robber : PlayerRole.Police;

            await bot.Login();
            _botClients.Add(bot);
        }
    }

    public async Task TestRandomMatching()
    {
        foreach (var bot in _botClients)
        {
            await bot.Matching();
        }

        await Task.WhenAll(_botClients.Select(bot =>
            bot.WaitForMatchAsync(TimeSpan.FromSeconds(30))));

        var matchedRoomCount = _botClients
            .Select(bot => bot.RoomId)
            .Distinct()
            .Count();

        Console.WriteLine(
            $"랜덤 매칭 완료: {_botClients.Count}명, {matchedRoomCount}개 방");
    }

    public async Task TestGamePlay()
    {
        Console.WriteLine($"{_botClients.Count}개 봇 게임 플레이 시작");

        try
        {
            await Task.WhenAll(_botClients.Select(bot => bot.GamePlay()));
        }
        finally
        {
            await Task.WhenAll(_botClients.Select(
                async bot => await bot.DisposeAsync()));
        }

        foreach (var roomResult in _botClients
                     .GroupBy(bot => bot.RoomId)
                     .Select(room => room.First())
                     .OrderBy(bot => bot.RoomId, StringComparer.Ordinal))
        {
            var winner = roomResult.WinnerRole switch
            {
                PlayerRole.Police => "경찰",
                PlayerRole.Robber => "도둑",
                _ => "알 수 없음"
            };
            var elapsed = TimeSpan.FromSeconds(roomResult.ElapsedGameTime);

            Console.WriteLine(
                $"[{roomResult.RoomId}] {winner} 승리 - " +
                $"{elapsed.Minutes:D2}분 {elapsed.Seconds:D2}초 만에 종료");
        }

        Console.WriteLine("모든 봇 게임 플레이 종료");
    }
}
