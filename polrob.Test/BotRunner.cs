using polrob.Shared;

namespace polrob.Test;

public class BotRunner
{
    public BotRunner()
    {

    }

    public async Task StartTest()
    {
        for (int i = 0; i < 600; i++)
        {
            var bot = new BotClient();

            bot.Name = $"Bot_{i:D4}";
            bot.Role = i % 3 < 2 ? PlayerRole.Robber : PlayerRole.Police;

            await bot.Login();
            await bot.Matching();
            await bot.GamePlay();
            await bot.GameOver();
        }
    }
}
