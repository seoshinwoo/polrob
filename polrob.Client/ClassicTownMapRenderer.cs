using polrob.Shared;
using SkiaSharp;

namespace polrob.Client;

/// <summary>Also linked into the preview tool, so exported maps use the actual game renderer.</summary>
public sealed class ClassicTownMapRenderer : IMapRenderer
{
    public static readonly string[] TileAssets =
        ["TownMap/tiles/grass.png", "TownMap/tiles/asphalt.png", "TownMap/tiles/paving.png"];
    private readonly Dictionary<string, SKImage> _sprites = new(StringComparer.Ordinal);
    private static readonly SKSamplingOptions SpriteSampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);
    private readonly Dictionary<string, SKRect> _sources = new(StringComparer.Ordinal);
    private readonly SKPath[] _roads = CanvaMapLayout.Roads.Select(r => SKPath.ParseSvgPathData(r.Path)).ToArray();
    private readonly SKPath _roadArea = MergeAreas([], CanvaMapLayout.Roads.Select(r => (r.Path, r.Width)));
    private readonly SKPath _pavedBlocks;
    private readonly Dictionary<string, SKPaint> _tiles = new(StringComparer.Ordinal);
    private readonly SKPaint _spritePaint = new() { IsAntialias = true };
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _fill = new() { IsAntialias = true };
    private readonly SKPathEffect _dash = SKPathEffect.CreateDash([30f, 30f], 0);
    private readonly MapPropLayout[] _props = CanvaMapLayout.Props.OrderBy(p => p.CenterY + p.Height / 2).ToArray();

    public ClassicTownMapRenderer(
        IReadOnlyDictionary<string, SKBitmap?> bitmaps,
        Func<string, SKBitmap, SKRect> getSourceBounds)
    {
        _pavedBlocks = CreatePavedBlocks(_roadArea, GameMap.WorldWidth, GameMap.WorldHeight);
        foreach (var name in new[] { "grass", "asphalt", "paving" })
        {
            var paint = new SKPaint { IsAntialias = true, Color = SKColor.Parse(name switch
            {
                "grass" => "#758B60", "asphalt" => "#505960", "dirt" => "#A8936D", _ => "#AEA997"
            }) };
            if (bitmaps.TryGetValue($"{CanvaMapLayout.GroundAssetRoot}/{name}.png", out var tile) && tile != null)
            {
                paint.Color = SKColors.White;
                var repeatSize = name == "paving" ? CanvaMapLayout.PavingRepeatSize : CanvaMapLayout.TileSize;
                paint.Shader = SKShader.CreateBitmap(tile, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat,
                    SKMatrix.CreateScale(repeatSize / tile.Width, repeatSize / tile.Height));
            }
            _tiles[name] = paint;
        }
        foreach (var (name, bitmap) in bitmaps)
        {
            if (bitmap != null && !TileAssets.Contains(name))
            {
                _sources[name] = getSourceBounds(name, bitmap);
                _sprites[name] = SKImage.FromBitmap(bitmap);
            }
        }
    }

    public void DrawBackground(SKCanvas canvas, SKRect visible)
    {
        canvas.Save();
        canvas.ClipRect(new SKRect(0, 0, GameMap.WorldWidth, GameMap.WorldHeight));
        canvas.DrawRect(visible, _tiles["grass"]);

        // Fill complete enclosed blocks first; the curved roads and curbs cover their edges.
        canvas.DrawPath(_pavedBlocks, _tiles["paving"]);

        // Layer the whole network together so joined roads never leave curb seams.
        for (var layer = 0; layer < 3; layer++)
        for (var i = 0; i < _roads.Length; i++)
        {
            _stroke.PathEffect = null;
            _stroke.StrokeWidth = CanvaMapLayout.Roads[i].Width + (layer == 0 ? 52 : layer == 1 ? 40 : 6);
            _stroke.Color = SKColor.Parse(layer == 0 ? "#69735B" : layer == 1 ? "#D2CAAB" : "#72766F");
            canvas.DrawPath(_roads[i], _stroke);
        }
        for (var i = 0; i < _roads.Length; i++) DrawTexturedStroke(canvas, _roads[i], "asphalt", CanvaMapLayout.Roads[i].Width);

        _stroke.Color = SKColor.Parse("#E5C568"); _stroke.StrokeWidth = 5;
        _stroke.PathEffect = _dash; _stroke.StrokeCap = SKStrokeCap.Butt;
        for (var i = 0; i < _roads.Length; i++)
            if (CanvaMapLayout.Roads[i].Marked) canvas.DrawPath(_roads[i], _stroke);
        _stroke.PathEffect = null; _stroke.StrokeCap = SKStrokeCap.Round;

        foreach (var crossing in CanvaMapLayout.Crosswalks)
            DrawCrosswalk(canvas, crossing.X, crossing.Y, crossing.VerticalRoad);
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
            var b = GameMap.GetBuildingCollisionBounds(building);
            var rect = new SKRect(b.Left, b.Top, b.Right, b.Bottom);
            canvas.DrawRect(rect, _fill); canvas.DrawRect(rect, _stroke);
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

    private void DrawCrosswalk(SKCanvas canvas, float x, float y, bool verticalRoad)
    {
        // Clear the center line beneath the crosswalk using the same world-anchored texture.
        canvas.Save();
        canvas.ClipPath(_roadArea, SKClipOperation.Intersect, antialias: true);
        var patch = verticalRoad ? new SKRect(x - 106, y - 34, x + 106, y + 34) : new SKRect(x - 34, y - 106, x + 34, y + 106);
        canvas.DrawRect(patch, _tiles["asphalt"]);
        canvas.Translate(x, y); if (verticalRoad) canvas.RotateDegrees(90);
        _fill.Color = SKColor.Parse("#EEE7CC");
        for (var i = -3; i <= 3; i++) canvas.DrawRoundRect(new SKRect(-27, i * 25 - 7, 27, i * 25 + 7), 2, 2, _fill);
        canvas.Restore();
    }

    private static bool Intersects(SKRect a, SKRect b) => a.Left <= b.Right && a.Right >= b.Left && a.Top <= b.Bottom && a.Bottom >= b.Top;

    public void Dispose()
    {
        foreach (var path in _roads) path.Dispose();
        _pavedBlocks.Dispose();
        _roadArea.Dispose();
        foreach (var sprite in _sprites.Values) sprite.Dispose();
        foreach (var paint in _tiles.Values) { paint.Shader?.Dispose(); paint.Dispose(); }
        _spritePaint.Dispose(); _stroke.Dispose(); _fill.Dispose(); _dash.Dispose();
    }
}
