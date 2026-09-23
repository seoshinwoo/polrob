using System.Text.Json;
using polrob.Shared;
using SkiaSharp;

internal static class AssetPackBuilder
{
    public static void Build(string repo, string docs)
    {
        var raw = Path.Combine(repo, "polrob.Client/Resources/Raw");
        var output = Path.Combine(raw, ChaseTownAssetCatalog.AssetRoot);
        Directory.CreateDirectory(Path.Combine(output, "props"));
        Directory.CreateDirectory(Path.Combine(output, "tiles"));
        var images = new Dictionary<string, SKBitmap>();
        var audit = new List<object>();
        foreach (var asset in ChaseTownAssetCatalog.Assets)
        {
            SKBitmap bitmap;
            if (asset.IsTile)
            {
                bitmap = new SKBitmap(asset.Width, asset.Height);
                // Approved gray palette; recolor ground only, never prop sprites.
                bitmap.Erase(SKColor.Parse(asset.Id switch
                {
                    "grass" => ChaseTownLayout.GrassColor,
                    "road" => ChaseTownLayout.RoadColor,
                    _ => ChaseTownLayout.PavingColor
                }));
            }
            else bitmap = HighResolutionAssets.Load(docs, asset);
            var pixels = bitmap.Pixels;
            if (!asset.IsTile && (!pixels.Any(p => p.Alpha == 0) || !pixels.Any(p => p.Alpha > 240)))
                throw new InvalidOperationException($"Invalid transparent sprite: {asset.Id}");
            if (!asset.IsTile && (pixels[0].Alpha != 0 || pixels[^1].Alpha != 0))
                throw new InvalidOperationException($"Uncleared sprite corner: {asset.Id}");
            images[asset.Id] = bitmap;
            audit.Add(new
            {
                asset.Id,
                asset.AssetPath,
                bitmap.Width,
                bitmap.Height,
                TransparentPixels = pixels.Count(p => p.Alpha == 0),
                OpaquePixels = pixels.Count(p => p.Alpha == 255),
                LogicalWidth = asset.Width,
                LogicalHeight = asset.Height,
                asset.TextureScale,
                Source = asset.IsTile ? "solid-palette" : "individually-remastered-transparent-sprite",
                MovementRegions = asset.Regions.Count(r => r.BlocksMovement),
                Triggers = asset.Regions.Count(r => r.Kind is "hiding" or "holding" or "interaction")
            });
        }
        // Validate the complete pack first; missing/invalid HD sources must not
        // partially replace the runtime pack with a mix of densities.
        foreach (var asset in ChaseTownAssetCatalog.Assets)
            Save(images[asset.Id], Path.Combine(raw, asset.AssetPath));
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        var manifest = JsonSerializer.Serialize(new
        {
            Version = 2,
            Coordinates = "logical design pixels; top-left; +X right; +Y down; textures scale independently",
            WorldScale = ChaseTownAssetCatalog.DefaultWorldScale,
            SourceMapSize = new[] { 1024, 1536 },
            RuntimeActivated = true,
            Assets = ChaseTownAssetCatalog.Assets
        }, jsonOptions);
        File.WriteAllText(Path.Combine(output, "manifest.json"), manifest);
        File.WriteAllText(Path.Combine(docs, "asset-audit.json"), JsonSerializer.Serialize(audit, jsonOptions));
        RenderCatalog(images, Path.Combine(docs, "asset-catalog.png"), false);
        RenderCatalog(images, Path.Combine(docs, "physics-catalog.png"), true);
        RenderDetail(images, Path.Combine(docs, "physics-detail.png"));
        foreach (var image in images.Values) image.Dispose();
        Console.WriteLine($"Built {ChaseTownAssetCatalog.Assets.Count} assets with alpha, physics manifest and inspection sheets: {output}");
    }

    private static void RenderCatalog(Dictionary<string, SKBitmap> images, string path, bool physics)
    {
        const int cw = 320, ch = 300, columns = 4;
        var rows = (ChaseTownAssetCatalog.Assets.Count + columns - 1) / columns;
        using var surface = SKSurface.Create(new SKImageInfo(cw * columns, ch * rows + 54));
        var c = surface.Canvas; c.Clear(SKColor.Parse("#edf0f2"));
        Label(c, physics ? "PHYSICS  |  red: solid   green: hide   cyan: canopy   yellow: holding   purple: interact" : "CHASE TOWN V7  |  transparent sprite & tile catalog", 18, 32, 17);
        for (var i = 0; i < ChaseTownAssetCatalog.Assets.Count; i++)
        {
            var a = ChaseTownAssetCatalog.Assets[i]; var x = (i % columns) * cw; var y = (i / columns) * ch + 54;
            Checker(c, new SKRect(x + 8, y + 8, x + cw - 8, y + ch - 40));
            var displayHeight = a.Regions.SelectMany(r => r.Points).Select(p => p.Y).Append(a.Height).Max();
            var scale = Math.Min((cw - 38f) / a.Width, (ch - 68f) / displayHeight);
            c.Save(); c.Translate(x + (cw - a.Width * scale) / 2, y + 12 + (ch - 68 - displayHeight * scale) / 2); c.Scale(scale);
            DrawSprite(c, images[a.Id], a);
            if (physics) DrawRegions(c, a, 1.6f / scale);
            c.Restore(); Label(c, a.Id, x + 12, y + ch - 16, 18);
        }
        Save(surface, path);
    }

    private static void RenderDetail(Dictionary<string, SKBitmap> images, string path)
    {
        var ids = new[] { "tree", "bush", "wall-corner", "jail" };
        using var surface = SKSurface.Create(new SKImageInfo(1280, 380)); var c = surface.Canvas; c.Clear(SKColors.White);
        for (var i = 0; i < ids.Length; i++)
        {
            var a = ChaseTownAssetCatalog.Get(ids[i]); var displayHeight = a.Regions.SelectMany(r => r.Points).Select(p => p.Y).Append(a.Height).Max();
            var s = Math.Min(275f / a.Width, 300f / displayHeight);
            Checker(c, new SKRect(i * 320 + 5, 5, i * 320 + 315, 340)); c.Save();
            c.Translate(i * 320 + (320 - a.Width * s) / 2, 15 + (310 - displayHeight * s) / 2); c.Scale(s);
            DrawSprite(c, images[a.Id], a); DrawRegions(c, a, 2 / s); c.Restore(); Label(c, a.Id, i * 320 + 18, 367, 22);
        }
        Save(surface, path);
    }

    private static void DrawSprite(SKCanvas canvas, SKBitmap bitmap, ChaseTownAsset asset)
    {
        using var image = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(image, new SKRect(0, 0, asset.Width, asset.Height),
            new SKSamplingOptions(SKFilterMode.Linear));
    }

    private static void DrawRegions(SKCanvas c, ChaseTownAsset a, float stroke)
    {
        foreach (var r in a.Regions.OrderBy(r => r.BlocksMovement))
        {
            var color = SKColor.Parse(r.Kind switch { "hiding" => "#16ba4d", "holding" => "#f3ce18", "interaction" => "#b233e5", "occlusion" => "#00bbd6", _ => "#f02d35" });
            using var fill = new SKPaint { Color = color.WithAlpha(62), IsAntialias = true };
            using var border = new SKPaint { Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = stroke, IsAntialias = true };
            if (r.Shape == "circle") { c.DrawCircle(r.Center.X, r.Center.Y, r.Radius, fill); c.DrawCircle(r.Center.X, r.Center.Y, r.Radius, border); }
            else { using var p = PolygonPath(r.Points.SelectMany(p => new[] { p.X, p.Y }).ToArray()); c.DrawPath(p, fill); c.DrawPath(p, border); }
        }
    }
    private static SKPath PolygonPath(float[] xy)
    { var p = new SKPath(); p.MoveTo(xy[0], xy[1]); for (var i = 2; i < xy.Length; i += 2) p.LineTo(xy[i], xy[i + 1]); p.Close(); return p; }
    private static void Checker(SKCanvas c, SKRect r)
    { using var p = new SKPaint(); c.Save(); c.ClipRect(r); for (var y = (int)r.Top; y < r.Bottom; y += 16) for (var x = (int)r.Left; x < r.Right; x += 16) { p.Color = ((x - (int)r.Left) / 16 + (y - (int)r.Top) / 16) % 2 == 0 ? SKColor.Parse("#e5e8eb") : SKColor.Parse("#f8f9fa"); c.DrawRect(x, y, 16, 16, p); } c.Restore(); }
    private static void Label(SKCanvas c, string text, float x, float y, float size)
    { using var font = new SKFont(SKTypeface.Default, size); using var p = new SKPaint { Color = SKColor.Parse("#24313b"), IsAntialias = true }; c.DrawText(text, x, y, SKTextAlign.Left, font, p); }
    private static void Save(SKBitmap bitmap, string path) { using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100); using var file = File.Create(path); data.SaveTo(file); }
    private static void Save(SKSurface surface, string path) { using var image = surface.Snapshot(); using var data = image.Encode(SKEncodedImageFormat.Png, 100); using var file = File.Create(path); data.SaveTo(file); }
}
