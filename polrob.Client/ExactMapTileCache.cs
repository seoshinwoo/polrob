using SkiaSharp;

namespace polrob.Client;

internal enum ExactMapTileLayer
{
    Base,
    Foreground
}

/// <summary>
/// Keeps only the exact-map tiles around the current viewport decoded in memory.
/// A cache entry owns the matching base/foreground pair so both render layers
/// always use the same tile coordinates and are evicted together.
/// </summary>
internal sealed class ExactMapTileCache : IDisposable
{
    internal const int TileSize = 512;
    internal const int ColumnCount = 10;
    internal const int RowCount = 15;
    internal const int WorldWidth = TileSize * ColumnCount;
    internal const int WorldHeight = TileSize * RowCount;

    private const int MaximumCachedTilePairs = 36;
    private const int MaximumConcurrentLoads = 2;

    private readonly object _sync = new();
    private readonly Dictionary<TileKey, CachedTilePair> _tiles = new();
    private readonly Dictionary<TileKey, Task> _pendingLoads = new();
    private readonly HashSet<TileKey> _failedTiles = new();
    private readonly LinkedList<TileKey> _leastRecentlyUsed = new();
    private readonly SemaphoreSlim _loadGate = new(MaximumConcurrentLoads, MaximumConcurrentLoads);
    private readonly Action _tileLoaded;
    private bool _disposed;

    internal ExactMapTileCache(Action tileLoaded)
    {
        _tileLoaded = tileLoaded;
    }

    internal Task PreloadAroundAsync(float worldX, float worldY)
    {
        // Five columns by seven rows covers the initial portrait viewport while
        // remaining below the cache limit (35 base/foreground pairs).
        var centerColumn = Math.Clamp((int)MathF.Floor(worldX / TileSize), 0, ColumnCount - 1);
        var centerRow = Math.Clamp((int)MathF.Floor(worldY / TileSize), 0, RowCount - 1);
        var loads = new List<Task>(35);

        for (var row = Math.Max(0, centerRow - 3); row <= Math.Min(RowCount - 1, centerRow + 3); row++)
        {
            for (var column = Math.Max(0, centerColumn - 2); column <= Math.Min(ColumnCount - 1, centerColumn + 2); column++)
            {
                loads.Add(EnsureLoadedAsync(new TileKey(row, column)));
            }
        }

        return Task.WhenAll(loads);
    }

    internal void QueueVisible(SKRect visibleWorldBounds)
    {
        foreach (var key in EnumerateTileKeys(visibleWorldBounds))
        {
            _ = EnsureLoadedAsync(key);
        }
    }

    internal void DrawLayer(SKCanvas canvas, ExactMapTileLayer layer, SKRect visibleWorldBounds)
    {
        QueueVisible(visibleWorldBounds);

        using var paint = new SKPaint
        {
            IsAntialias = false
        };

        // SKBitmap.Dispose must never race a native DrawBitmap call. Eviction
        // therefore shares this short lock with drawing the visible tile set.
        lock (_sync)
        {
            foreach (var key in EnumerateTileKeys(visibleWorldBounds))
            {
                if (!_tiles.TryGetValue(key, out var pair))
                {
                    continue;
                }

                TouchLocked(pair);
                var bitmap = layer == ExactMapTileLayer.Base
                    ? pair.Base
                    : pair.Foreground;
                var left = key.Column * TileSize;
                var top = key.Row * TileSize;
                var destination = new SKRect(left, top, left + TileSize, top + TileSize);
                canvas.DrawBitmap(bitmap, destination, paint);
            }
        }
    }

    private Task EnsureLoadedAsync(TileKey key)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            if (_tiles.TryGetValue(key, out var cached))
            {
                TouchLocked(cached);
                return Task.CompletedTask;
            }

            if (_failedTiles.Contains(key))
            {
                return Task.CompletedTask;
            }

            if (_pendingLoads.TryGetValue(key, out var pending))
            {
                return pending;
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingLoads[key] = completion.Task;
            _ = Task.Run(() => LoadPairAsync(key, completion));
            return completion.Task;
        }
    }

    private async Task LoadPairAsync(TileKey key, TaskCompletionSource completion)
    {
        SKBitmap? baseBitmap = null;
        SKBitmap? foregroundBitmap = null;
        var added = false;

        try
        {
            await _loadGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var baseTask = LoadBitmapAsync(TilePath(ExactMapTileLayer.Base, key));
                var foregroundTask = LoadBitmapAsync(TilePath(ExactMapTileLayer.Foreground, key));
                await Task.WhenAll(baseTask, foregroundTask).ConfigureAwait(false);
                baseBitmap = await baseTask.ConfigureAwait(false);
                foregroundBitmap = await foregroundTask.ConfigureAwait(false);
            }
            finally
            {
                _loadGate.Release();
            }

            if (baseBitmap == null || foregroundBitmap == null)
            {
                throw new InvalidOperationException($"Exact map tile pair {key.Row:00},{key.Column:00} could not be decoded.");
            }

            lock (_sync)
            {
                if (!_disposed && !_tiles.ContainsKey(key))
                {
                    var lruNode = _leastRecentlyUsed.AddLast(key);
                    _tiles.Add(key, new CachedTilePair(baseBitmap, foregroundBitmap, lruNode));
                    baseBitmap = null;
                    foregroundBitmap = null;
                    TrimLocked();
                    added = true;
                }
            }
        }
        catch (Exception ex)
        {
            lock (_sync)
            {
                _failedTiles.Add(key);
            }

            System.Diagnostics.Debug.WriteLine($"Failed to load exact map tile {key.Row:00},{key.Column:00}: {ex}");
        }
        finally
        {
            baseBitmap?.Dispose();
            foregroundBitmap?.Dispose();

            lock (_sync)
            {
                _pendingLoads.Remove(key);
            }

            completion.TrySetResult();
            if (added)
            {
                _tileLoaded();
            }
        }
    }

    private static async Task<SKBitmap?> LoadBitmapAsync(string assetPath)
    {
        using var stream = await FileSystem.OpenAppPackageFileAsync(assetPath).ConfigureAwait(false);
        return SKBitmap.Decode(stream);
    }

    private void TouchLocked(CachedTilePair pair)
    {
        _leastRecentlyUsed.Remove(pair.LruNode);
        _leastRecentlyUsed.AddLast(pair.LruNode);
    }

    private void TrimLocked()
    {
        while (_tiles.Count > MaximumCachedTilePairs && _leastRecentlyUsed.First != null)
        {
            var key = _leastRecentlyUsed.First.Value;
            _leastRecentlyUsed.RemoveFirst();
            if (_tiles.Remove(key, out var evicted))
            {
                evicted.Base.Dispose();
                evicted.Foreground.Dispose();
            }
        }
    }

    private static string TilePath(ExactMapTileLayer layer, TileKey key)
    {
        var directory = layer == ExactMapTileLayer.Base ? "base" : "foreground";
        return $"exact_map/{directory}/map_{key.Row:00}_{key.Column:00}.png";
    }

    private static IEnumerable<TileKey> EnumerateTileKeys(SKRect bounds)
    {
        if (bounds.Right <= 0f || bounds.Bottom <= 0f ||
            bounds.Left >= WorldWidth || bounds.Top >= WorldHeight)
        {
            yield break;
        }

        var left = Math.Clamp((int)MathF.Floor(MathF.Max(0f, bounds.Left) / TileSize), 0, ColumnCount - 1);
        var top = Math.Clamp((int)MathF.Floor(MathF.Max(0f, bounds.Top) / TileSize), 0, RowCount - 1);
        var rightEdge = MathF.Min(WorldWidth, bounds.Right);
        var bottomEdge = MathF.Min(WorldHeight, bounds.Bottom);
        var right = Math.Clamp((int)MathF.Floor((rightEdge - 0.001f) / TileSize), 0, ColumnCount - 1);
        var bottom = Math.Clamp((int)MathF.Floor((bottomEdge - 0.001f) / TileSize), 0, RowCount - 1);

        for (var row = top; row <= bottom; row++)
        {
            for (var column = left; column <= right; column++)
            {
                yield return new TileKey(row, column);
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var pair in _tiles.Values)
            {
                pair.Base.Dispose();
                pair.Foreground.Dispose();
            }

            _tiles.Clear();
            _leastRecentlyUsed.Clear();
            _failedTiles.Clear();
        }
    }

    private readonly record struct TileKey(int Row, int Column);

    private sealed record CachedTilePair(
        SKBitmap Base,
        SKBitmap Foreground,
        LinkedListNode<TileKey> LruNode);
}
