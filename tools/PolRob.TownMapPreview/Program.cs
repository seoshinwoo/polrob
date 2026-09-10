using System.Text.Json;
using polrob.Shared;
using polrob.Client;
using SkiaSharp;

var root = new DirectoryInfo(Environment.CurrentDirectory);
while (root != null && !File.Exists(Path.Combine(root.FullName, "polrob.slnx"))) root = root.Parent;
if (root == null) throw new InvalidOperationException("Run from the PolRob repository.");
var repo = root.FullName;
// Both the default invocation and the existing --town-map command export the active game map.
var output = Path.Combine(repo, "docs", "town-map");
Directory.CreateDirectory(output);
var assets = new Dictionary<string, SKBitmap?>(StringComparer.Ordinal);
var assetAudit = new List<object>();
foreach (var name in GameMap.PropLayouts.Select(p => p.AssetPath).Where(p => p.Length > 0).Concat(TownMapRenderer.TileAssets).Distinct())
{
    if (!name.StartsWith(CanvaMapLayout.AssetRoot + "/", StringComparison.Ordinal) && !TownMapRenderer.TileAssets.Contains(name))
        throw new InvalidOperationException($"The Canva map must use the provided MapAssets sprites: {name}");
    var path = Path.Combine(repo, "polrob.Client/Resources/Raw", name);
    var bitmap = SKBitmap.Decode(path) ?? throw new InvalidOperationException($"Missing asset: {path}");
    assets[name] = bitmap;
    var transparent = 0;
    for (var y = 0; y < bitmap.Height; y++)
    for (var x = 0; x < bitmap.Width; x++)
        if (bitmap.GetPixel(x, y).Alpha == 0) transparent++;
    if (!TownMapRenderer.TileAssets.Contains(name) && transparent == 0)
        throw new InvalidOperationException($"Sprite lacks a genuine transparent background: {name}");
    var bounds = TownMapRenderer.VisibleBounds(bitmap);
    if (name.StartsWith(CanvaMapLayout.AssetRoot + "/", StringComparison.Ordinal))
    {
        var source = CanvaMapCollisions.Profiles[Path.GetFileName(name)].Image;
        if (bitmap.Width != source.Width || bitmap.Height != source.Height ||
            bounds != new SKRect(source.VisibleLeft, source.VisibleTop, source.VisibleRight, source.VisibleBottom))
            throw new InvalidOperationException($"Collision source geometry differs from the rendered sprite: {name}");
    }
    assetAudit.Add(new { Name = name, bitmap.Width, bitmap.Height, TransparentPixels = transparent,
        VisibleBounds = new[] { bounds.Left, bounds.Top, bounds.Right, bounds.Bottom } });
}
File.WriteAllText(Path.Combine(output, "asset-audit.json"), JsonSerializer.Serialize(assetAudit, new JsonSerializerOptions { WriteIndented = true }));
using var renderer = new TownMapRenderer(assets);
var map = new GameMap();
var world = new SKRect(0, 0, map.Width, map.Height);
using var surface = SKSurface.Create(new SKImageInfo((int)map.Width, (int)map.Height));
renderer.DrawBackground(surface.Canvas, world);
Save(surface, "background-2560x3840.png");
renderer.DrawProps(surface.Canvas, world);
Save(surface, "map-2560x3840.png");
Save(surface, "map-canva-2560x3840.png");

using (var props = SKSurface.Create(new SKImageInfo((int)map.Width, (int)map.Height)))
{
    props.Canvas.Clear(SKColors.Transparent);
    renderer.DrawProps(props.Canvas, world);
    Save(props, "props-2560x3840.png");
}

using (var overview = SKSurface.Create(new SKImageInfo(1024, 1536)))
{
    overview.Canvas.Scale(.4f); renderer.DrawBackground(overview.Canvas, world); renderer.DrawProps(overview.Canvas, world);
    Save(overview, "map-overview.png");
    Save(overview, "map-canva-overview.png");
}

renderer.DrawCollisionOverlay(surface.Canvas, map);
Save(surface, "collisions-2560x3840.png");
Save(surface, "collisions-source-profiles-2560x3840.png");
using (var overview = SKSurface.Create(new SKImageInfo(1024, 1536)))
{
    overview.Canvas.Scale(.4f);
    renderer.DrawBackground(overview.Canvas, world); renderer.DrawProps(overview.Canvas, world);
    renderer.DrawCollisionOverlay(overview.Canvas, map);
    Save(overview, "collisions-overview.png");
}

// Export a 1:1 game-scale view with the actual character art at the requested 50×50.
using (var detail = SKSurface.Create(new SKImageInfo(1120, 1000)))
{
    detail.Canvas.Translate(-1030, -750);
    var bounds = new SKRect(1030, 750, 2150, 1750);
    renderer.DrawBackground(detail.Canvas, bounds); renderer.DrawProps(detail.Canvas, bounds);
    foreach (var (name, x, y) in new[] { ("char_police.png", 1770f, 1580f), ("char_robber.png", 1460f, 1660f) })
    {
        using var sprite = SKBitmap.Decode(Path.Combine(repo, "polrob.Client/Resources/Raw", name));
        detail.Canvas.DrawBitmap(sprite, TownMapRenderer.VisibleBounds(sprite), new SKRect(x-25, y-25, x+25, y+25));
    }
    Save(detail, "detail-50px-characters.png");
}

File.WriteAllText(Path.Combine(output, "layout.json"), JsonSerializer.Serialize(new
{
    Width = map.Width, Height = map.Height, TileSize = CanvaMapLayout.TileSize,
    RoadWidth = CanvaMapLayout.RoadWidth, RoadPaths = CanvaMapLayout.Roads.Select(r => r.Path),
    PavingRepeatSize = CanvaMapLayout.PavingRepeatSize,
    PropCollisionsEnabled = true,
    GroundFill = "Enclosed road blocks: paving; exterior land: grass",
    Roads = CanvaMapLayout.Roads, Crosswalks = CanvaMapLayout.Crosswalks, Props = GameMap.PropLayouts
}, new JsonSerializerOptions { WriteIndented = true }));
File.WriteAllText(Path.Combine(output, "collision-source-profiles.json"),
    JsonSerializer.Serialize(CanvaMapCollisions.Profiles, new JsonSerializerOptions { WriteIndented = true }));
CollisionProfilePreview.Export(output, assets);
foreach (var bitmap in assets.Values) bitmap?.Dispose();
Console.WriteLine($"Saved full-resolution map, ground layer, collision overlay, overview, detail and layout.json to {output}");

void Save(SKSurface source, string filename)
{
    using var image = source.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    using var stream = File.Create(Path.Combine(output, filename));
    data.SaveTo(stream);
}
