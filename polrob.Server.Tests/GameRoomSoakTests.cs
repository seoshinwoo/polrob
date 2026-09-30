using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using polrob.Shared;

namespace polrob.Server.Tests;

public sealed class GameRoomSoakTests
{
    [Test]
    [Category("Soak")]
    public async Task RepeatedGameCompletionAndDisconnectDoesNotRetainRooms()
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("POLROB_SOAK_SECONDS"), out var seconds) || seconds <= 0)
        {
            Assert.Ignore("Set POLROB_SOAK_SECONDS to run the optional in-memory soak test.");
        }

        var identities = new BotIdentityService(NullLogger<BotIdentityService>.Instance);
        var service = new GameRoomService(null!, identities, NullLogger<GameRoomService>.Instance);
        var police = identities.Create("soak-police", PlayerRole.Police);
        var robber = identities.Create("soak-robber", PlayerRole.Robber);
        var stopwatch = Stopwatch.StartNew();
        var iterations = 0;

        while (stopwatch.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            var created = await service.CreateRoom(police.Id);
            if (!created.Success || string.IsNullOrEmpty(created.RoomId) || string.IsNullOrEmpty(created.RoomCode))
            {
                throw new AssertionException($"Room creation failed on iteration {iterations}: {created.Message}");
            }

            var joined = await service.JoinCustomGame(robber.Id, created.RoomCode);
            var started = service.StartGameIfMatched(created.RoomId);
            if (!joined.Success || !started.Success)
            {
                throw new AssertionException($"Room start failed on iteration {iterations}.");
            }

            if (iterations % 2 == 0)
            {
                service.RemovePlayer(created.RoomId, police.Id);
                service.RemovePlayer(created.RoomId, robber.Id);
                service.CompleteGame(created.RoomId);
            }
            else
            {
                service.CompleteGame(created.RoomId);
                service.RemovePlayer(created.RoomId, police.Id);
                service.RemovePlayer(created.RoomId, robber.Id);
            }

            if (service.GetRoomStatus(created.RoomId).Success)
            {
                throw new AssertionException($"Room retained after completion on iteration {iterations}.");
            }

            iterations++;
            if (iterations % 100 == 0)
            {
                await Task.Yield();
            }
        }

        Assert.That(service.GetLoadSnapshot().TotalRooms, Is.Zero);
        TestContext.Progress.WriteLine(
            $"In-memory soak: {iterations} rooms, {iterations * 2} player joins, " +
            $"{stopwatch.Elapsed.TotalSeconds:F1} seconds, final rooms=0.");
    }
}
