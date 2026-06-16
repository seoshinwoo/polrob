using System.Collections.Concurrent;
using polrob.Shared;

namespace polrob.Test;

public class BotRunner
{
    private static readonly int BotCount =
        GetNonNegativeIntEnvironmentVariable("POLROB_BOT_COUNT", 600);
    private static readonly TimeSpan GamePlayConnectStagger = TimeSpan.FromMilliseconds(
        GetNonNegativeIntEnvironmentVariable("POLROB_BOT_GAMEPLAY_CONNECT_STAGGER_MS", 10));
    private static readonly TimeSpan InitialStateTimeout = TimeSpan.FromSeconds(
        GetPositiveIntEnvironmentVariable("POLROB_BOT_INITIAL_STATE_TIMEOUT_SECONDS", 60));

    private readonly List<BotClient> _botClients = new();
    public BotRunner()
    {

    }

    public async Task TestLogin()
    {
        for (int i = 0; i < BotCount; i++)
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
        var roomSummaries = _botClients
            .GroupBy(bot => bot.RoomId)
            .Select(room => new
            {
                RoomId = room.Key,
                Total = room.Count(),
                Police = room.Count(bot => bot.Role == PlayerRole.Police),
                Robber = room.Count(bot => bot.Role == PlayerRole.Robber)
            })
            .ToList();
        var fullRoomCount = roomSummaries.Count(room => room.Total == 6);
        var expectedRoleRoomCount = roomSummaries.Count(room => room.Police == 2 && room.Robber == 4);
        var abnormalRooms = roomSummaries
            .Where(room => room.Total != 6 || room.Police != 2 || room.Robber != 4)
            .Take(10)
            .ToList();

        Console.WriteLine(
            $"랜덤 매칭 완료: {_botClients.Count}명, {matchedRoomCount}개 방");
        Console.WriteLine(
            $"랜덤 매칭 검증: 6명 방 {fullRoomCount}개, 경찰2/도둑4 방 {expectedRoleRoomCount}개");

        foreach (var room in abnormalRooms)
        {
            Console.WriteLine(
                $"[매칭 이상] {room.RoomId}: total={room.Total}, police={room.Police}, robber={room.Robber}");
        }
    }

    public async Task TestGamePlay()
    {
        Console.WriteLine($"{_botClients.Count}개 봇 게임 플레이 시작");
        Console.WriteLine(
            $"게임 TCP 접속 간격: {GamePlayConnectStagger.TotalMilliseconds:0}ms, " +
            $"InitialState 타임아웃: {InitialStateTimeout.TotalSeconds:0}s");

        var failures = new ConcurrentBag<string>();

        try
        {
            await Task.WhenAll(_botClients.Select((bot, index) =>
                RunBotGamePlayAsync(bot, index, failures)));
        }
        finally
        {
            await Task.WhenAll(_botClients.Select(
                async bot => await bot.DisposeAsync()));
        }

        if (!failures.IsEmpty)
        {
            Console.WriteLine($"봇 게임 플레이 실패: {failures.Count}개");
            foreach (var failure in failures.Take(20))
            {
                Console.WriteLine($"[봇 실패] {failure}");
            }
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

    private static async Task RunBotGamePlayAsync(
        BotClient bot,
        int index,
        ConcurrentBag<string> failures)
    {
        if (GamePlayConnectStagger > TimeSpan.Zero)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(
                GamePlayConnectStagger.TotalMilliseconds * index));
        }

        try
        {
            await bot.GamePlay();
        }
        catch (Exception ex)
        {
            failures.Add(
                $"{bot.Name}({bot.Id}) room={bot.RoomId}, role={bot.Role}: " +
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static int GetNonNegativeIntEnvironmentVariable(string name, int fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, out var parsed) && parsed >= 0
            ? parsed
            : fallback;
    }

    private static int GetPositiveIntEnvironmentVariable(string name, int fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : fallback;
    }
}
