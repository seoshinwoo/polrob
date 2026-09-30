using System.Net;
using System.Reflection;
using NUnit.Framework;
using polrob.Server.Controllers;
using polrob.Server.Network;
using polrob.Shared;

namespace polrob.Server.Tests;

public sealed class MovementAuthenticationTests
{
    [Test]
    public void UdpMovementRequiresCurrentConnectionTokenEndpointAndLoginSession()
    {
        var loginToken = (string)typeof(AuthController)
            .GetMethod("CreateSession", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, ["player-1"])!;

        try
        {
            var endpoint = new IPEndPoint(IPAddress.Loopback, 32001);
            var registration = new PlayerRoomRegistration("room-1", "connection-1");
            var session = new PlayerSession
            {
                ConnectionId = "connection-1",
                SessionToken = loginToken,
                MovementSessionToken = "movement-secret",
                UdpEndPoint = endpoint
            };
            var movement = new PlayerMovementInput { Id = "player-1", Token = "movement-secret" };

            Assert.Multiple(() =>
            {
                Assert.That(IsAuthorized(movement, endpoint, registration, session), Is.True);
                Assert.That(IsAuthorized(new PlayerMovementInput { Id = "player-2", Token = movement.Token }, endpoint, registration, session), Is.False);
                Assert.That(IsAuthorized(new PlayerMovementInput { Id = movement.Id, Token = "wrong-secret" }, endpoint, registration, session), Is.False);
                Assert.That(IsAuthorized(movement, new IPEndPoint(IPAddress.Loopback, 32002), registration, session), Is.False);
                Assert.That(IsAuthorized(movement, endpoint, registration with { ConnectionId = "old-connection" }, session), Is.False);
            });

            new AuthController(null!, null!, null!).Logout(new AuthController.LogoutRequest(loginToken));
            Assert.That(IsAuthorized(movement, endpoint, registration, session), Is.False);
        }
        finally
        {
            new AuthController(null!, null!, null!).Logout(new AuthController.LogoutRequest(loginToken));
        }
    }

    private static bool IsAuthorized(
        PlayerMovementInput movement,
        IPEndPoint endpoint,
        PlayerRoomRegistration registration,
        PlayerSession session)
    {
        var method = typeof(GameNetworkServer).GetMethod(
            "IsAuthorizedMovement",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return (bool)method.Invoke(null, [movement, endpoint, registration, session])!;
    }
}
