using NUnit.Framework;
using polrob.Server.Network;

namespace polrob.Server.Tests;

public sealed class ActiveGameParticipantRegistryTests
{
    [Test]
    public void Unregister_FromReplacedConnection_DoesNotRemoveCurrentConnection()
    {
        var registry = new ActiveGameParticipantRegistry();
        registry.Register("room-1", "user-1", "old-connection");
        registry.Register("room-1", "user-1", "new-connection");

        registry.Unregister("room-1", "user-1", "old-connection");

        Assert.Multiple(() =>
        {
            Assert.That(registry.IsActive("room-1", "user-1"), Is.True);
            Assert.That(
                registry.TryGetConnectionId("room-1", "user-1", out var connectionId),
                Is.True);
            Assert.That(connectionId, Is.EqualTo("new-connection"));
        });
    }

    [Test]
    public void Unregister_CurrentConnection_RemovesParticipant()
    {
        var registry = new ActiveGameParticipantRegistry();
        registry.Register("room-1", "user-1", "connection-1");

        registry.Unregister("room-1", "user-1", "connection-1");

        Assert.That(registry.IsActive("room-1", "user-1"), Is.False);
    }

    [Test]
    public void ActiveLease_ExpiresAndCurrentHeartbeatRestoresIt()
    {
        var timeProvider = new ManualTimeProvider();
        var registry = new ActiveGameParticipantRegistry(
            timeProvider,
            TimeSpan.FromSeconds(45));
        registry.Register("room-1", "user-1", "connection-1");

        timeProvider.Advance(TimeSpan.FromSeconds(46));

        Assert.That(registry.IsActive("room-1", "user-1"), Is.False);
        Assert.That(
            registry.Refresh("room-1", "user-1", "connection-1"),
            Is.True);
        Assert.That(registry.IsActive("room-1", "user-1"), Is.True);
    }

    [Test]
    public void Heartbeat_FromReplacedConnection_CannotRefreshCurrentLease()
    {
        var registry = new ActiveGameParticipantRegistry();
        registry.Register("room-1", "user-1", "old-connection");
        registry.Register("room-1", "user-1", "new-connection");

        Assert.That(
            registry.Refresh("room-1", "user-1", "old-connection"),
            Is.False);
        Assert.That(
            registry.TryGetConnectionId("room-1", "user-1", out var connectionId),
            Is.True);
        Assert.That(connectionId, Is.EqualTo("new-connection"));
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration)
        {
            _utcNow += duration;
        }
    }
}
