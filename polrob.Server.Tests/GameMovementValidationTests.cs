using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using polrob.Server.Controllers;
using polrob.Server.Network;
using polrob.Shared;

namespace polrob.Server.Tests;

public sealed class GameMovementValidationTests
{
    [Test]
    public void NonFiniteAndReplayPacketsCannotReplaceLastAcceptedMovement()
    {
        var loginToken = (string)typeof(AuthController)
            .GetMethod("CreateSession", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, ["player-1"])!;

        try
        {
            var room = new GameSession(8);
            var endpoint = new IPEndPoint(IPAddress.Loopback, 32001);
            var session = new PlayerSession
            {
                PlayerState = new Player { Id = "player-1", Role = PlayerRole.Robber },
                SessionToken = loginToken,
                MovementSessionToken = "movement-secret",
                UdpEndPoint = endpoint
            };
            room.Sessions["player-1"] = session;
            var server = (GameNetworkServer)RuntimeHelpers.GetUninitializedObject(typeof(GameNetworkServer));

            Move(server, room, endpoint, 1, 0.5f, 0f);
            Assert.That((session.InputX, session.InputY, session.LastMovementInputSequence),
                Is.EqualTo((0.5f, 0f, 1UL)));

            Move(server, room, endpoint, 2, float.NaN, 0f);
            Move(server, room, endpoint, 3, 0f, float.PositiveInfinity);
            Move(server, room, endpoint, 1, -1f, 0f);
            Move(server, room, new IPEndPoint(IPAddress.Loopback, 32002), 4, -1f, 0f);
            Assert.That((session.InputX, session.InputY, session.LastMovementInputSequence),
                Is.EqualTo((0.5f, 0f, 1UL)));

            Move(server, room, endpoint, 2, -1f, 0f);
            Assert.That((session.InputX, session.InputY, session.LastMovementInputSequence),
                Is.EqualTo((-1f, 0f, 2UL)), "Rejected packets must not consume a valid sequence number.");
        }
        finally
        {
            new AuthController(null!, null!, null!).Logout(new AuthController.LogoutRequest(loginToken));
        }
    }

    private static void Move(
        GameNetworkServer server,
        GameSession room,
        IPEndPoint endpoint,
        ulong sequence,
        float x,
        float y)
    {
        var command = new MoveRoomCommand(new PlayerMovementInput
        {
            Id = "player-1", Token = "movement-secret", Sequence = sequence, X = x, Y = y
        }, endpoint);
        typeof(GameNetworkServer).GetMethod(
                "HandleRoomMove", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(server, ["room-1", room, command]);
    }
}
