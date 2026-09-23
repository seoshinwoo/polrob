using System.Text.Json;
using polrob.Shared;
using polrob.Client;
using SkiaSharp;

var root = new DirectoryInfo(Environment.CurrentDirectory);
while (root != null && !File.Exists(Path.Combine(root.FullName, "polrob.slnx"))) root = root.Parent;
if (root == null) throw new InvalidOperationException("Run from the PolRob repository.");
var repo = root.FullName;
var output = Path.Combine(repo, "docs", "chase-town-map");
Directory.CreateDirectory(output);
var assets = ChaseTownLayout.Props.Select(p => p.AssetPath).Concat(TownMapRenderer.TileAssets).Distinct()
    .ToDictionary(p => p, p => (SKBitmap?)(SKBitmap.Decode(Path.Combine(repo, "polrob.Client/Resources/Raw", p))
        ?? throw new InvalidOperationException($"Missing asset: {p}")));
using var renderer = new TownMapRenderer(assets);
foreach (var (name, color) in new[] { ("grass", ChaseTownLayout.GrassColor),
    ("road", ChaseTownLayout.RoadColor), ("paving", ChaseTownLayout.PavingColor) })
{
    var tile = assets[$"ChaseTownV7/tiles/{name}.png"]!;
    if (tile.Width != 64 || tile.Height != 64 || tile.Pixels.Any(pixel => pixel != SKColor.Parse(color)))
        throw new InvalidOperationException($"Stale terrain tile: {name}. Rebuild ChaseTownAssets.");
}
var map = new GameMap();
var world = new SKRect(0, 0, map.Width, map.Height);
using (var overview = SKSurface.Create(new SKImageInfo(1024, 1536)))
{
    overview.Canvas.Scale(.4f);
    renderer.DrawBackground(overview.Canvas, world);
    renderer.DrawProps(overview.Canvas, world);
    Save(overview, "map-overview.png");
    renderer.DrawCollisionOverlay(overview.Canvas, map);
    Save(overview, "collisions-overview.png");
}

// Original characters at the game's body scale, using the production renderer.
foreach (var (name, x, y, px, py, rx, ry) in new[]
{
    ("market", 450f, 1050f, 855f, 1270f, 850f, 1460f),
    ("warehouse", 1090f, 2600f, 1425f, 2850f, 1390f, 3070f),
    ("jail", 2110f, 260f, 2220f, 600f, 2350f, 667f)
})
{
    using var detail = SKSurface.Create(new SKImageInfo(900, 1000));
    var canvas = detail.Canvas;
    canvas.Scale(2); canvas.Translate(-x, -y);
    var visible = new SKRect(x, y, x + 450, y + 500);
    renderer.DrawBackground(canvas, visible);
    renderer.DrawProps(canvas, visible);
    using (var reference = SKSurface.Create(new SKImageInfo(900, 1000)))
    {
        reference.Canvas.Scale(2); reference.Canvas.Translate(-x, -y);
        renderer.DrawBackground(reference.Canvas, world);
        renderer.DrawProps(reference.Canvas, world);
        using var actualImage = detail.Snapshot();
        using var expectedImage = reference.Snapshot();
        using var actual = SKBitmap.FromImage(actualImage);
        using var expected = SKBitmap.FromImage(expectedImage);
        if (!actual.Bytes.SequenceEqual(expected.Bytes))
            throw new InvalidOperationException($"Viewport culling changes rendered pixels: {name}");
    }
    renderer.DrawBackground(canvas, visible);
    DrawCharacter(canvas, "char_police.png", px, py);
    DrawCharacter(canvas, "char_robber.png", rx, ry);
    renderer.DrawProps(canvas, visible);
    Save(detail, $"play-{name}.png");
}
// Same camera and unchanged character pixels, only prop source resolution changes.
var beforeAssets = assets.ToDictionary(pair => pair.Key, pair => pair.Value);
var beforeProps = new List<SKBitmap>();
foreach (var asset in ChaseTownAssetCatalog.Assets.Where(a => !a.IsTile && beforeAssets.ContainsKey(a.AssetPath)))
{
    var bitmap = SKBitmap.Decode(Path.Combine(repo, "docs/chase-town-assets/hd/original-props", asset.Id + ".png"))
        ?? throw new InvalidOperationException($"Missing pre-HD comparison asset: {asset.Id}");
    beforeAssets[asset.AssetPath] = bitmap; beforeProps.Add(bitmap);
}
using (var beforeRenderer = new TownMapRenderer(beforeAssets))
using (var comparison = SKSurface.Create(new SKImageInfo(1800, 1130)))
{
    using var label = new SKPaint { Color = SKColors.White, IsAntialias = true };
    using var font = new SKFont(SKTypeface.Default, 27);
    for (var i = 0; i < 2; i++)
    {
        using var detail = SKSurface.Create(new SKImageInfo(900, 1050));
        var c = detail.Canvas; c.Scale(2); c.Translate(-1200, -180);
        var visible = new SKRect(1200, 180, 1650, 705);
        var selected = i == 0 ? beforeRenderer : renderer;
        selected.DrawBackground(c, visible);
        DrawCharacter(c, "char_police.png", 1580, 640);
        selected.DrawProps(c, visible);
        if (i == 1) Save(detail, "play-police-hd.png");
        using var shot = detail.Snapshot();
        comparison.Canvas.DrawImage(shot, i * 900, 80);
        comparison.Canvas.DrawText(i == 0 ? "BEFORE / original map crops" : "AFTER / individual HD sprites",
            i * 900 + 20, 45, SKTextAlign.Left, font, label);
    }
    Save(comparison, "hd-before-after.png");
}
foreach (var bitmap in beforeProps) bitmap.Dispose();
// Two actual game-scale views per building: the plain occlusion, then the collider overlay.
// Coordinates come from shared physics; this is not an AI-generated prediction of overlap.
using (var rear = SKSurface.Create(new SKImageInfo(1260, 680)))
{
    // Pick a real house by its type prefix rather than depending on placement numbering.
    var examples = new[] { map.PoliceStation, map.Buildings.Single(b => b.Type == "Cafe"),
        map.Buildings.First(b => b.Type.StartsWith("House-",StringComparison.Ordinal)) };
    using var label = new SKPaint { Color = SKColors.White, IsAntialias = true };
    using var font = new SKFont(SKTypeface.Default, 18);
    for (var column = 0; column < examples.Length; column++)
    {
        var building = examples[column];
        var bounds = GameMap.GetBuildingCollisionBounds(building);
        var px = building.Center.X;
        var py = bounds.Top - 25.5f;
        if (map.IsMovementPositionBlocked(px,py,25,[]))
            throw new InvalidOperationException($"Rear preview player is blocked: {building.Type}");
        for (var row = 0; row < 2; row++)
        {
            var canvas = rear.Canvas;
            canvas.Save();
            canvas.ClipRect(new SKRect(column*420,row*340,(column+1)*420,(row+1)*340));
            canvas.Translate(column*420,row*340);
            canvas.Scale(2);
            var x = px - 105;
            var y = py - 48;
            canvas.Translate(-x,-y);
            var visible = new SKRect(x,y,x+210,y+170);
            renderer.DrawBackground(canvas,visible);
            DrawCharacter(canvas,"char_robber.png",px,py);
            renderer.DrawProps(canvas,visible);
            if (row == 1) renderer.DrawCollisionOverlay(canvas,map);
            canvas.Restore();
            canvas.DrawText($"{building.Type} / {(row == 0 ? "behind roof" : "solid footprint")}",
                column*420+12,row*340+24,SKTextAlign.Left,font,label);
        }
    }
    Save(rear,"rear-clearance.png");
}
File.WriteAllText(Path.Combine(output, "layout.json"), JsonSerializer.Serialize(new
{
    map.MapId, map.Width, map.Height, ChaseTownLayout.TileSize, ChaseTownLayout.Columns, ChaseTownLayout.Rows,
    Ground = Enumerable.Range(0, ChaseTownLayout.Rows).Select(y => string.Concat(
        Enumerable.Range(0, ChaseTownLayout.Columns).Select(x => ChaseTownLayout.Ground[x, y] switch
        { GroundTile.Road => 'R', GroundTile.Paving => 'P', _ => 'G' }))),
    Placements = ChaseTownLayout.Placements.Select(p => new { p.Id, p.AssetId, p.Center, p.Scale, p.BuildingType })
}, new JsonSerializerOptions { WriteIndented = true }));
foreach (var bitmap in assets.Values) bitmap?.Dispose();
// Keep a runnable preview of the preserved map; use its own original alpha crops.
var classic = new GameMap(MapRegistry.ClassicTown);
var classicAssets = classic.PropLayouts.Select(p => p.AssetPath).Concat(classic.Definition.TileAssets).Distinct()
    .ToDictionary(p => p, p => (SKBitmap?)(SKBitmap.Decode(Path.Combine(repo, "polrob.Client/Resources/Raw", p))
        ?? throw new InvalidOperationException($"Missing classic asset: {p}")));
using (var classicRenderer = new ClassicTownMapRenderer(classicAssets,
    (name, bitmap) => PreviewAssetAnalysis.VisibleBounds(bitmap)))
using (var overview = SKSurface.Create(new SKImageInfo(1024, 1536)))
{
    overview.Canvas.Scale(.4f);
    classicRenderer.DrawBackground(overview.Canvas, world);
    classicRenderer.DrawProps(overview.Canvas, world);
    Save(overview, "classic-map-overview.png");
    // The restored renderer must also agree when rendering only a camera viewport.
    var viewport = new SKRect(400, 1050, 850, 1550);
    using var cropped = SKSurface.Create(new SKImageInfo(900, 1000));
    using var full = SKSurface.Create(new SKImageInfo(900, 1000));
    foreach (var surface in new[] { cropped, full })
    {
        surface.Canvas.Scale(2); surface.Canvas.Translate(-viewport.Left, -viewport.Top);
        var bounds = ReferenceEquals(surface, cropped) ? viewport : world;
        classicRenderer.DrawBackground(surface.Canvas, bounds);
        classicRenderer.DrawProps(surface.Canvas, bounds);
    }
    using var croppedImage = cropped.Snapshot();
    using var fullImage = full.Snapshot();
    using var croppedBitmap = SKBitmap.FromImage(croppedImage);
    using var fullBitmap = SKBitmap.FromImage(fullImage);
    if (!croppedBitmap.Bytes.SequenceEqual(fullBitmap.Bytes))
        throw new InvalidOperationException("Classic map viewport culling changes rendered pixels.");
}
foreach (var bitmap in classicAssets.Values) bitmap?.Dispose();
Console.WriteLine($"Exported active tiled map and physics previews to {output}");

void DrawCharacter(SKCanvas canvas, string name, float x, float y)
{
    using var bitmap = SKBitmap.Decode(Path.Combine(repo, "polrob.Client/Resources/Raw", name));
    var visible = PreviewAssetAnalysis.VisibleBounds(bitmap);
    const float scale = 50 * .86f / 512;
    canvas.DrawBitmap(bitmap, visible, new SKRect(x + (visible.Left - 544) * scale,
        y + (visible.Top - 544) * scale, x + (visible.Right - 544) * scale, y + (visible.Bottom - 544) * scale));
}
void Save(SKSurface surface, string filename)
{
    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    using var stream = File.Create(Path.Combine(output, filename));
    data.SaveTo(stream);
}
