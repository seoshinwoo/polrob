using polrob.Shared;

namespace polrob.Test;

public class BotRunner
{
    private List<BotClient> _botClients = new List<BotClient>();
    public BotRunner()
    {

    }

    public async Task TestLogin()
    {
        for (int i = 0; i < 600; i++)
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
}
