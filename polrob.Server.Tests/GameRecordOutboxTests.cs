using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using polrob.Server.Operations;
using polrob.Shared;

namespace polrob.Server.Tests;

public sealed class GameRecordOutboxTests
{
    private string _directory = null!;
    private GameRecordOutboxOptions _options = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "polrob-outbox-tests-" + Guid.NewGuid().ToString("N"));
        _options = new GameRecordOutboxOptions { Directory = _directory };
    }

    [TearDown]
    public void TearDown() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    [Test]
    public async Task AcceptedRecordSurvivesRestartAndIsDeletedOnlyAfterStoreAcknowledges()
    {
        var record = Record("game-1");
        using (var outbox = Open())
        {
            Assert.That(outbox.TryAppend(record), Is.True);
            var failedStore = new FakeStore((_, _) => throw new HttpRequestException("database offline"));
            await Writer(outbox, failedStore).ProcessPendingAsync(CancellationToken.None);
            Assert.That(outbox.Snapshot().Pending, Is.EqualTo(1));
        }

        using var reopened = Open();
        var saved = new List<CompletedGameRecord>();
        await Writer(reopened, new FakeStore((value, _) => { saved.Add(value); return Task.CompletedTask; }))
            .ProcessPendingAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(saved.Single().Id, Is.EqualTo(record.Id));
            Assert.That(saved.Single().PolicePlayerIds, Is.EqualTo(record.PolicePlayerIds));
            Assert.That(reopened.Snapshot().Pending, Is.Zero);
            Assert.That(reopened.Snapshot().Bytes, Is.Zero);
        });
    }

    [Test]
    public async Task StoreCommitFollowedByLostAcknowledgementReplaysTheSameGameId()
    {
        var uniqueIds = new HashSet<string>();
        var attempts = 0;
        var store = new FakeStore((record, _) =>
        {
            uniqueIds.Add(record.Id);
            if (++attempts == 1) throw new TimeoutException("commit succeeded but response was lost");
            return Task.CompletedTask;
        });
        using (var first = Open())
        {
            first.TryAppend(Record("idempotent-game"));
            await Writer(first, store).ProcessPendingAsync(CancellationToken.None);
        }
        using var next = Open();
        await Writer(next, store).ProcessPendingAsync(CancellationToken.None);
        Assert.That(attempts, Is.EqualTo(2));
        Assert.That(uniqueIds, Has.Count.EqualTo(1));
        Assert.That(next.Snapshot().Pending, Is.Zero);
    }

    [Test]
    public async Task PermanentFailureAndCorruptJsonArePreservedAndDoNotBlockValidRecords()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "corrupt.json"), "{broken");
        using var outbox = Open();
        outbox.TryAppend(Record("bad"));
        outbox.TryAppend(Record("good"));
        var saved = new List<string>();
        await Writer(outbox, new FakeStore((record, _) =>
        {
            if (record.Id == "bad") throw new ArgumentException("invalid result");
            saved.Add(record.Id);
            return Task.CompletedTask;
        })).ProcessPendingAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(saved, Is.EqualTo(new[] { "good" }));
            Assert.That(outbox.Snapshot().Pending, Is.Zero);
            Assert.That(outbox.Snapshot().Quarantined, Is.EqualTo(2));
            Assert.That(Directory.GetFiles(_directory, "*.failed"), Has.Length.EqualTo(2));
        });
    }

    [Test]
    public void QuotaBlocksAdmissionBeforeHardLimitAndRejectedRecordsDoNotGrowTheQueue()
    {
        _options.MaxRecords = 2;
        _options.AdmissionThreshold = 0.5;
        using var outbox = Open();
        Assert.That(outbox.CanAcceptGames, Is.True);
        Assert.That(outbox.TryAppend(Record("one")), Is.True);
        Assert.That(outbox.CanAcceptGames, Is.False);
        Assert.That(outbox.TryAppend(Record("two")), Is.True, "An already-running match can use reserved headroom.");
        Assert.That(outbox.TryAppend(Record("three")), Is.False);
        Assert.That(outbox.Snapshot().Pending, Is.EqualTo(2));
    }

    [Test]
    public void DuplicateIsIdempotentButConflictingSnapshotIsRejected()
    {
        using var outbox = Open();
        var record = Record("same-id");
        Assert.That(outbox.TryAppend(record), Is.True);
        Assert.That(outbox.TryAppend(record), Is.True);
        Assert.That(outbox.TryAppend(record with { WinnerRole = PlayerRole.Robber }), Is.False);
        Assert.That(outbox.Snapshot().Pending, Is.EqualTo(1));
    }

    [Test]
    public void ByteQuotaIsEnforcedIndependentlyOfRecordCount()
    {
        _options.MaxRecordBytes = 1024;
        _options.MaxBytes = 1024;
        using var outbox = Open();
        var record = Record("large-one") with
        { PolicePlayerIds = new[] { new string('p', 250) }, RobberPlayerIds = new[] { new string('r', 250) } };
        Assert.That(outbox.TryAppend(record), Is.True);
        Assert.That(outbox.TryAppend(record with { Id = "large-two" }), Is.False);
        Assert.That(outbox.Snapshot().Pending, Is.EqualTo(1));
        Assert.That(outbox.Snapshot().Bytes, Is.LessThanOrEqualTo(1024));
    }

    [Test]
    public void DiskWriteFailureClosesAdmissionAndProbeAllowsRecovery()
    {
        using var outbox = Open();
        var record = Record("disk-failure");
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(record.Id)));
        var obstruction = Path.Combine(_directory, hash + ".tmp");
        Directory.CreateDirectory(obstruction); // A directory cannot be opened as the result file.
        Assert.That(outbox.TryAppend(record), Is.False);
        Assert.That(outbox.CanAcceptGames, Is.False);
        Assert.That(outbox.Snapshot().WriteFailed, Is.True);
        Directory.Delete(obstruction);
        outbox.ProbeWritable();
        Assert.That(outbox.CanAcceptGames, Is.True);
        Assert.That(outbox.TryAppend(record), Is.True);
    }

    [Test]
    public void SecondProcessCannotOwnSameSpool()
    {
        using var first = Open();
        Assert.Throws<IOException>(() => { using var second = Open(); });
    }

    [Test]
    public async Task InterruptedTemporaryFileIsRecoveredAndReplayed()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "interrupted.tmp"), JsonSerializer.Serialize(Record("interrupted")));
        using var outbox = Open();
        var saved = new List<string>();
        await Writer(outbox, new FakeStore((record, _) => { saved.Add(record.Id); return Task.CompletedTask; }))
            .ProcessPendingAsync(CancellationToken.None);
        Assert.That(saved, Is.EqualTo(new[] { "interrupted" }));
        Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
    }

    [Test]
    public async Task CancellationKeepsTheRecordOnDisk()
    {
        using var outbox = Open();
        outbox.TryAppend(Record("cancelled"));
        using var cancel = new CancellationTokenSource();
        var store = new FakeStore((_, token) => { cancel.Cancel(); return Task.FromCanceled(token); });
        Assert.ThrowsAsync<TaskCanceledException>(() => Writer(outbox, store).ProcessPendingAsync(cancel.Token));
        Assert.That(outbox.Snapshot().Pending, Is.EqualTo(1));
        await Task.CompletedTask;
    }

    [Test]
    public async Task RestartMarkerDistinguishesInterruptedAndCleanShutdown()
    {
        using var outbox = Open();
        var first = new ServerOperations();
        await Restart(outbox, first).StartAsync(CancellationToken.None);
        var interrupted = new ServerOperations();
        var restarted = Restart(outbox, interrupted);
        await restarted.StartAsync(CancellationToken.None);
        Assert.That(interrupted.PreviousShutdownClean, Is.False);
        Assert.That(interrupted.BootId, Is.Not.EqualTo(first.BootId));
        await restarted.StopAsync(CancellationToken.None);
        var clean = new ServerOperations();
        await Restart(outbox, clean).StartAsync(CancellationToken.None);
        Assert.That(clean.PreviousShutdownClean, Is.True);
    }

    private RestartPolicyService Restart(GameRecordOutbox outbox, ServerOperations operations) =>
        new(outbox, operations, new OperationalMetrics(), NullLogger<RestartPolicyService>.Instance);
    private GameRecordOutbox Open() => new(Options.Create(_options), NullLogger<GameRecordOutbox>.Instance);
    private GameRecordWriter Writer(GameRecordOutbox outbox, IGameRecordStore store) =>
        new(outbox, store, Options.Create(_options), new OperationalMetrics(), NullLogger<GameRecordWriter>.Instance);
    private static CompletedGameRecord Record(string id) => new(id, "room", PlayerRole.Police,
        new[] { "police" }, new[] { "robber" }, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, 60);
    private sealed class FakeStore(Func<CompletedGameRecord, CancellationToken, Task> save) : IGameRecordStore
    {
        public Task SaveGameRecordAsync(CompletedGameRecord record, CancellationToken cancellationToken = default) => save(record, cancellationToken);
    }
}
