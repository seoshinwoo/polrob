using SkiaSharp;

var root = new DirectoryInfo(Environment.CurrentDirectory);
while (root != null && !File.Exists(Path.Combine(root.FullName, "polrob.slnx"))) root = root.Parent;
if (root == null) throw new InvalidOperationException("Run from the repository.");
var repo = root.FullName;
var docs = Path.Combine(repo, "docs/chase-town-assets");
Directory.CreateDirectory(docs);

if (args.Length > 0 && args[0] == "build")
{
    if (args.Length != 1) throw new ArgumentException("build uses the versioned HD sources in docs/chase-town-assets/hd/sources.");
    AssetPackBuilder.Build(repo, docs);
    return;
}

if (args.Length == 0 || args[0] == "references")
{
    var approved = Path.Combine(repo, "tmp/map-v7-review/final-map.png");
    var source = Path.Combine(docs, "source-map.png");
    if (!File.Exists(source)) File.Copy(approved, source);
    using var bitmap = SKBitmap.Decode(source) ?? throw new InvalidOperationException("Missing approved map.");
    var crops = new (string Id, int X, int Y, int W, int H)[]
    {
        ("police-station", 478, 34, 230, 189),
        ("cafe", 196, 382, 132, 147),
        ("donut-shop", 208, 585, 126, 140),
        ("burger-shop", 552, 382, 132, 147),
        ("house", 698, 416, 120, 114),
        ("warehouse-large", 209, 1051, 297, 192),
        ("warehouse-small", 695, 608, 121, 130),
        ("jail", 874, 93, 137, 150),
        ("tree", 392, 1371, 82, 86),
        ("bush", 326, 548, 49, 49),
        ("crate", 519, 1088, 45, 51),
        ("wall-horizontal", 584, 752, 107, 44),
        ("wall-vertical", 586, 1033, 30, 209),
        ("wall-corner", 216, 532, 80, 62)
    };
    Directory.CreateDirectory(Path.Combine(docs, "references"));
    foreach (var (id, x, y, w, h) in crops)
    {
        var scale = 640f / Math.Max(w, h);
        using var surface = SKSurface.Create(new SKImageInfo((int)MathF.Round(w * scale), (int)MathF.Round(h * scale)));
        surface.Canvas.DrawBitmap(bitmap, new SKRect(x, y, x + w, y + h),
            new SKRect(0, 0, surface.Canvas.DeviceClipBounds.Width, surface.Canvas.DeviceClipBounds.Height));
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(Path.Combine(docs, "references", id + ".png"));
        data.SaveTo(file);
    }
    Console.WriteLine($"Prepared {crops.Length} exact reference crops from approved v7 map: {docs}");
    return;
}

throw new ArgumentException("Supported commands: references | build");
