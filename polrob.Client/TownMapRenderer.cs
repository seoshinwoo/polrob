using polrob.Shared;
using SkiaSharp;

namespace polrob.Client;

/// <summary>Also linked into the preview tool, so exported maps use the actual game renderer.</summary>
public sealed class TownMapRenderer : IMapRenderer
{
    public static readonly string[] TileAssets =
        ["ChaseTownV7/tiles/grass.png", "ChaseTownV7/tiles/road.png", "ChaseTownV7/tiles/paving.png"];
    private readonly Dictionary<string, SKImage> _sprites = new(StringComparer.Ordinal);
    private static readonly SKSamplingOptions SpriteSampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);
    private readonly Dictionary<string, SKRect> _sources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SKPaint> _tiles = new(StringComparer.Ordinal);
    private readonly SKPaint _spritePaint = new() { IsAntialias = true };
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _fill = new() { IsAntialias = true };
    private readonly MapPropLayout[] _props = ChaseTownLayout.Props.OrderBy(p => p.CenterY + p.Height / 2).ToArray();
    private readonly Dictionary<MapPropLayout, Obstacle> _fadeAreas = new();

    public TownMapRenderer(IReadOnlyDictionary<string, SKBitmap?> bitmaps)
    {
        for (var i = 0; i < ChaseTownLayout.Placements.Length; i++)
        {
            var cover = ChaseTownLayout.Placements[i].Regions.FirstOrDefault(r => r.Kind is "occlusion" or "hiding");
            if (cover != null) _fadeAreas[ChaseTownLayout.Props[i]] = cover.Obstacle;
        }
        foreach (var name in new[] { "grass", "road", "paving" })
        {
            var paint = new SKPaint { IsAntialias = false, Color = SKColor.Parse(name switch
            {
                "grass" => ChaseTownLayout.GrassColor, "road" => ChaseTownLayout.RoadColor, _ => ChaseTownLayout.PavingColor
            }) };
            if (bitmaps.TryGetValue($"ChaseTownV7/tiles/{name}.png", out var tile) && tile != null)
            {
                paint.Color = SKColors.White;
                var repeatSize = ChaseTownLayout.TileSize;
                paint.Shader = SKShader.CreateBitmap(tile, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat,
                    SKMatrix.CreateScale(repeatSize / tile.Width, repeatSize / tile.Height));
            }
            _tiles[name] = paint;
        }
        foreach (var (name, bitmap) in bitmaps)
        {
            if (bitmap != null && !TileAssets.Contains(name))
            {
                // HD texture pixels map onto the original logical prop dimensions.
                // Never derive world size or collision geometry from bitmap.Width.
                _sources[name] = new SKRect(0, 0, bitmap.Width, bitmap.Height);
                _sprites[name] = SKImage.FromBitmap(bitmap);
            }
        }
    }

    public void DrawBackground(SKCanvas canvas, SKRect visible)
    {
        canvas.Save();
        canvas.ClipRect(new SKRect(0, 0, GameMap.WorldWidth, GameMap.WorldHeight));
        const int size = ChaseTownLayout.TileSize;
        var minX = Math.Max(0, (int)MathF.Floor(visible.Left / size));
        var maxX = Math.Min(ChaseTownLayout.Columns - 1, (int)MathF.Floor(visible.Right / size));
        var minY = Math.Max(0, (int)MathF.Floor(visible.Top / size));
        var maxY = Math.Min(ChaseTownLayout.Rows - 1, (int)MathF.Floor(visible.Bottom / size));
        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        {
            var kind = ChaseTownLayout.Ground[x, y];
            var rect = new SKRect(x * size, y * size, (x + 1) * size, (y + 1) * size);
            canvas.DrawRect(rect, _tiles[kind switch
            { GroundTile.Road => "road", GroundTile.Paving => "paving", _ => "grass" }]);
            if (kind != GroundTile.Road) continue;
            // 4-neighbour autotile edges: curbs only border land, never other road tiles.
            bool Land(int cx, int cy) => cx >= 0 && cy >= 0 && cx < ChaseTownLayout.Columns &&
                cy < ChaseTownLayout.Rows && ChaseTownLayout.Ground[cx, cy] != GroundTile.Road;
            const float curb = 14;
            if (Land(x - 1, y)) canvas.DrawRect(new(rect.Left, rect.Top, rect.Left + curb, rect.Bottom), _tiles["paving"]);
            if (Land(x + 1, y)) canvas.DrawRect(new(rect.Right - curb, rect.Top, rect.Right, rect.Bottom), _tiles["paving"]);
            if (Land(x, y - 1)) canvas.DrawRect(new(rect.Left, rect.Top, rect.Right, rect.Top + curb), _tiles["paving"]);
            if (Land(x, y + 1)) canvas.DrawRect(new(rect.Left, rect.Bottom - curb, rect.Right, rect.Bottom), _tiles["paving"]);
        }
        canvas.Restore();
    }

    private void DrawTexturedStroke(SKCanvas canvas, SKPath path, string tile, float width)
    {
        var paint = _tiles[tile];
        paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = width;
        paint.StrokeJoin = SKStrokeJoin.Round; paint.StrokeCap = SKStrokeCap.Round;
        canvas.DrawPath(path, paint); paint.Style = SKPaintStyle.Fill;
    }

    internal static SKPath CreatePavedBlocks(SKPath roadArea, float width, float height)
    {
        using var world = new SKPath();
        world.AddRect(new SKRect(0, 0, width, height));
        using var land = world.Op(roadArea, SKPathOp.Difference)
            ?? throw new InvalidOperationException("The town road blocks could not be resolved.");
        using var contours = new SKPathMeasure(land, true);
        var blocks = new SKPath();
        do
        {
            using var contour = contours.GetSegment(0, contours.Length, true);
            if (contour == null) continue;
            contour.Close();
            var bounds = contour.Bounds;
            // Land connected to a world edge is outside the road network and stays grass.
            if (bounds.Left <= 0 || bounds.Top <= 0 ||
                bounds.Right >= width || bounds.Bottom >= height) continue;
            blocks.AddPath(contour);
        } while (contours.NextContour());
        return blocks;
    }

    internal static SKPath MergeAreas(IEnumerable<string> surfaces, IEnumerable<(string Path, float Width)> trails)
    {
        // Union once at load time so connecting footpaths have only an outer edge.
        using var builder = new SKPath.OpBuilder();
        foreach (var source in surfaces)
        {
            using var path = SKPath.ParseSvgPathData(source);
            builder.Add(path, SKPathOp.Union);
        }
        using var stroke = new SKPaint { Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round };
        foreach (var trail in trails)
        {
            using var path = SKPath.ParseSvgPathData(trail.Path);
            stroke.StrokeWidth = trail.Width;
            using var area = stroke.GetFillPath(path);
            builder.Add(area, SKPathOp.Union);
        }
        var result = new SKPath();
        if (!builder.Resolve(result))
        {
            result.Dispose();
            throw new InvalidOperationException("The town ground paths could not be combined.");
        }
        return result;
    }

    public void DrawProps(SKCanvas canvas, SKRect visible, System.Drawing.PointF? viewer = null)
    {
        canvas.Save();
        canvas.ClipRect(new SKRect(0, 0, GameMap.WorldWidth, GameMap.WorldHeight));
        foreach (var prop in _props)
        {
            if (prop.Width <= 0 || prop.Height <= 0 || !_sprites.TryGetValue(prop.AssetPath, out var sprite)) continue;
            var destination = new SKRect(prop.CenterX - prop.Width / 2, prop.CenterY - prop.Height / 2,
                prop.CenterX + prop.Width / 2, prop.CenterY + prop.Height / 2);
            if (!Intersects(destination, visible)) continue;
            _spritePaint.Color = SKColors.White.WithAlpha(viewer is {} point &&
                _fadeAreas.TryGetValue(prop, out var cover) && GameMap.ContainsPoint(cover, point.X, point.Y)
                ? (byte)140 : (byte)255);
            canvas.DrawImage(sprite, _sources[prop.AssetPath], destination, SpriteSampling, _spritePaint);
        }
        canvas.Restore();
    }

    public void DrawCollisionOverlay(SKCanvas canvas, GameMap map)
    {
        _stroke.PathEffect = null; _stroke.StrokeWidth = 3;
        _stroke.Color = new SKColor(255, 78, 91, 220);
        _fill.Color = new SKColor(255, 78, 91, 35);
        foreach (var building in map.Buildings.Where(b => b.BlocksMovement))
        {
            using var path = new SKPath();
            path.AddPoly(building.CollisionPolygon.Select(p => new SKPoint(p.X, p.Y)).ToArray(), close: true);
            canvas.DrawPath(path, _fill); canvas.DrawPath(path, _stroke);
        }
        foreach (var obstacle in map.Obstacles.Where(o => o.BlocksMovement))
        {
            _stroke.Color = obstacle.Type switch
            {
                "Circle" => new SKColor(80, 225, 255, 240),
                "Polygon" => new SKColor(218, 101, 255, 240),
                _ => new SKColor(255, 78, 91, 220)
            };
            _fill.Color = _stroke.Color.WithAlpha(45);
            if (obstacle.Type == "Circle")
            {
                canvas.DrawOval(obstacle.Center.X, obstacle.Center.Y, obstacle.Radius, obstacle.EffectiveRadiusY, _fill);
                canvas.DrawOval(obstacle.Center.X, obstacle.Center.Y, obstacle.Radius, obstacle.EffectiveRadiusY, _stroke);
            }
            else if (obstacle.Type == "Polygon")
            {
                using var path = new SKPath();
                path.AddPoly(obstacle.PolygonPoints.Select(p => new SKPoint(p.X, p.Y)).ToArray(), close: true);
                canvas.DrawPath(path, _fill); canvas.DrawPath(path, _stroke);
            }
            else
            {
                var rect = new SKRect(obstacle.LeftTop.X, obstacle.LeftTop.Y, obstacle.RightBottom.X, obstacle.RightBottom.Y);
                canvas.DrawRect(rect, _fill); canvas.DrawRect(rect, _stroke);
            }
        }
    }

    private static bool Intersects(SKRect a, SKRect b) => a.Left <= b.Right && a.Right >= b.Left && a.Top <= b.Bottom && a.Bottom >= b.Top;

    public void Dispose()
    {
        foreach (var sprite in _sprites.Values) sprite.Dispose();
        foreach (var paint in _tiles.Values) { paint.Shader?.Dispose(); paint.Dispose(); }
        _spritePaint.Dispose(); _stroke.Dispose(); _fill.Dispose();
    }
}
