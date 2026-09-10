using SkiaSharp;

if (args.Length != 3)
    throw new ArgumentException("Usage: <sprite-directory> <mask-directory> <output-directory>");
var sourceDirectory = Path.GetFullPath(args[0]);
var maskDirectory = Path.GetFullPath(args[1]);
var outputDirectory = Path.GetFullPath(args[2]);
if (sourceDirectory == outputDirectory)
    throw new ArgumentException("Use a separate output directory; never overwrite the source snapshot.");
Directory.CreateDirectory(outputDirectory);
int[] poses = [1, 2, 3, 4, 3, 2, 1, 2];
foreach (var role in new[] { "police", "robber" })
{
    for (var frame = 1; frame <= 8; frame++)
    {
        var name = $"char_{role}_run_{frame}.png";
        using var source = Load(Path.Combine(sourceDirectory, name));
        using var guide = Load(Path.Combine(maskDirectory, $"{role}-{poses[frame - 1]}.png"));
        using var result = Clean(source, guide, role, poses[frame - 1]);
        Validate(source, result, name);
        Save(result, Path.Combine(outputDirectory, name));
    }
    RenderSheet(role);
}
RenderComparison();
foreach (var role in new[] { "police", "robber" })
foreach (var pair in new[] { (1, 7), (2, 6), (2, 8), (3, 5) })
{
    var first = File.ReadAllBytes(Path.Combine(outputDirectory, $"char_{role}_run_{pair.Item1}.png"));
    var second = File.ReadAllBytes(Path.Combine(outputDirectory, $"char_{role}_run_{pair.Item2}.png"));
    if (!first.SequenceEqual(second)) throw new InvalidOperationException($"Duplicate pose mismatch: {role} {pair}");
}
Console.WriteLine("Validated 16 frames: 627x627, original visible RGB, no new opacity, duplicate poses identical.");

SKBitmap Clean(SKBitmap source, SKBitmap guide, string role, int pose)
{
    var width = source.Width;
    var height = source.Height;
    // Fit the image-generated silhouette to the original frame's near-opaque
    // pixels. The generous registration band absorbs the generator's small
    // scale/placement variations without moving any source artwork.
    var guideMask = new bool[width * height];
    for (var y = 0; y < height; y++)
    for (var x = 0; x < width; x++)
        guideMask[y * width + x] = guide.GetPixel(x * guide.Width / width, y * guide.Height / height).Red > 128;
    var distance = DistanceFrom(guideMask, width, height);
    var solid = new bool[width * height];
    for (var y = 0; y < height; y++)
    for (var x = 0; x < width; x++)
        solid[y * width + x] = source.GetPixel(x, y).Alpha >= 250 && distance[y * width + x] < 48;
    solid = LargestComponent(solid, width, height);
    // Fill only the silhouette mask. Actual source alpha (including the two
    // eye openings) is retained during compositing below.
    FillHoles(solid, width, height);
    var smooth = Blur(solid.Select(value => value ? 1f : 0f).ToArray(), width, height, 1.5f);
    var coverageMask = SmoothContour(smooth.Select(v => v >= 0.5f).ToArray(), width, height);
    var output = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
    for (var y = 0; y < height; y++)
    for (var x = 0; x < width; x++)
    {
        // A narrow continuous coverage ramp, not a hard color deletion.
        var coverage = coverageMask[y * width + x];
        if (role == "police") coverage = Math.Min(coverage, BuckleCoverage(x + 0.5f, y + 0.5f, pose));
        var original = source.GetPixel(x, y);
        var alpha = (byte)Math.Round(original.Alpha * coverage);
        output.SetPixel(x, y, alpha == 0 ? SKColors.Transparent : original.WithAlpha(alpha));
    }
    return output;
}

float[] SmoothContour(bool[] mask, int width, int height)
{
    var edges = new Dictionary<(int x, int y), (int x, int y)>();
    bool Inside(int x, int y) => x >= 0 && x < width && y >= 0 && y < height && mask[y * width + x];
    for (var y = 0; y < height; y++)
    for (var x = 0; x < width; x++)
    {
        if (!Inside(x, y)) continue;
        if (!Inside(x, y - 1)) edges[(x, y)] = (x + 1, y);
        if (!Inside(x + 1, y)) edges[(x + 1, y)] = (x + 1, y + 1);
        if (!Inside(x, y + 1)) edges[(x + 1, y + 1)] = (x, y + 1);
        if (!Inside(x - 1, y)) edges[(x, y + 1)] = (x, y);
    }
    List<SKPoint> longest = [];
    while (edges.Count > 0)
    {
        var current = edges.First().Key;
        List<SKPoint> loop = [];
        while (edges.Remove(current, out var next))
        {
            loop.Add(new SKPoint(current.x, current.y));
            current = next;
        }
        if (loop.Count > longest.Count) longest = loop;
    }
    // Smooth along arc length, rather than following every tiny threshold
    // step. This removes the old matte's scallops without a blurry outline.
    const float contourSigma = 8f;
    const int contourRadius = 24;
    var weights = Enumerable.Range(-contourRadius, 2 * contourRadius + 1)
        .Select(k => MathF.Exp(-k * k / (2 * contourSigma * contourSigma))).ToArray();
    var total = weights.Sum();
    var points = Enumerable.Range(0, (longest.Count + 5) / 6).Select(sample =>
    {
        var center = sample * 6;
        var point = new SKPoint();
        for (var k = -contourRadius; k <= contourRadius; k++)
            point += Multiply(longest[(center + k + longest.Count) % longest.Count], weights[k + contourRadius] / total);
        return point;
    }).ToArray();
    using var path = new SKPath();
    path.MoveTo(points[0]);
    for (var i = 0; i < points.Length; i++)
    {
        var previous = points[(i + points.Length - 1) % points.Length];
        var start = points[i];
        var end = points[(i + 1) % points.Length];
        var following = points[(i + 2) % points.Length];
        path.CubicTo(start + Multiply(end - previous, 1f / 6f), end - Multiply(following - start, 1f / 6f), end);
    }
    path.Close();
    const int sampleCount = 4;
    using var large = new SKBitmap(width * sampleCount, height * sampleCount);
    using var canvas = new SKCanvas(large);
    canvas.Clear(SKColors.Transparent);
    canvas.Scale(sampleCount);
    using var fill = new SKPaint { Color = SKColors.White, IsAntialias = true };
    canvas.DrawPath(path, fill);
    var result = new float[mask.Length];
    for (var y = 0; y < height; y++)
    for (var x = 0; x < width; x++)
    {
        var coverage = 0;
        for (var sy = 0; sy < sampleCount; sy++)
        for (var sx = 0; sx < sampleCount; sx++)
            coverage += large.GetPixel(x * sampleCount + sx, y * sampleCount + sy).Alpha;
        result[y * width + x] = coverage / (255f * sampleCount * sampleCount);
    }
    return result;
}

SKPoint Multiply(SKPoint point, float scale) => new(point.X * scale, point.Y * scale);

void Validate(SKBitmap source, SKBitmap result, string name)
{
    if (source.Width != 627 || source.Height != 627 || result.Width != 627 || result.Height != 627)
        throw new InvalidOperationException($"Unexpected canvas: {name}");
    for (var y = 0; y < 627; y++)
    for (var x = 0; x < 627; x++)
    {
        var before = source.GetPixel(x, y);
        var after = result.GetPixel(x, y);
        if (after.Alpha > before.Alpha || after.Alpha != 0 &&
            (before.Red != after.Red || before.Green != after.Green || before.Blue != after.Blue))
            throw new InvalidOperationException($"Artwork/opacity changed: {name} ({x}, {y})");
    }
}

float BuckleCoverage(float x, float y, int pose)
{
    // The old bloom is almost opaque directly under the buckle. Reconstruct
    // that short lower arc from the visible gold ring, not from glow alpha.
    float[] left = [288f, 257f, 286f, 258f];
    float[] right = [374f, 342f, 371f, 342f];
    float[] bottom = [494f, 496f, 517f, 516f];
    const float radius = 13f;
    var l = left[pose - 1];
    var r = right[pose - 1];
    var b = bottom[pose - 1];
    if (y < b - radius || x < l - 1 || x > r + 1) return 1;
    var cornerDistance = Math.Max(l + radius - x, x - (r - radius));
    var limit = cornerDistance <= 0 ? b
        : b - radius + MathF.Sqrt(Math.Max(0, radius * radius - cornerDistance * cornerDistance));
    return Math.Clamp(limit - y + 0.5f, 0, 1);
}

float[] DistanceFrom(bool[] mask, int width, int height)
{
    var d = mask.Select(inside => inside ? 0f : 10000f).ToArray();
    for (var y = 0; y < height; y++)
    for (var x = 0; x < width; x++)
    {
        var i = y * width + x;
        if (x > 0) d[i] = Math.Min(d[i], d[i - 1] + 1);
        if (y > 0) d[i] = Math.Min(d[i], d[i - width] + 1);
    }
    for (var y = height - 1; y >= 0; y--)
    for (var x = width - 1; x >= 0; x--)
    {
        var i = y * width + x;
        if (x + 1 < width) d[i] = Math.Min(d[i], d[i + 1] + 1);
        if (y + 1 < height) d[i] = Math.Min(d[i], d[i + width] + 1);
    }
    return d;
}

IEnumerable<int> Neighbors(int i, int width, int height)
{
    var x = i % width;
    var y = i / width;
    if (x > 0) yield return i - 1;
    if (x + 1 < width) yield return i + 1;
    if (y > 0) yield return i - width;
    if (y + 1 < height) yield return i + width;
}

bool[] LargestComponent(bool[] mask, int width, int height)
{
    var seen = new bool[mask.Length];
    List<int> largest = [];
    for (var start = 0; start < mask.Length; start++)
    {
        if (!mask[start] || seen[start]) continue;
        var component = new List<int> { start };
        seen[start] = true;
        for (var cursor = 0; cursor < component.Count; cursor++)
        foreach (var next in Neighbors(component[cursor], width, height))
        {
            if (!mask[next] || seen[next]) continue;
            seen[next] = true;
            component.Add(next);
        }
        if (component.Count > largest.Count) largest = component;
    }
    var result = new bool[mask.Length];
    foreach (var i in largest) result[i] = true;
    return result;
}

void FillHoles(bool[] mask, int width, int height)
{
    var outside = new bool[mask.Length];
    var queue = new Queue<int>();
    for (var i = 0; i < mask.Length; i++)
    {
        if (i % width != 0 && i % width != width - 1 && i / width != 0 && i / width != height - 1) continue;
        if (mask[i]) continue;
        outside[i] = true;
        queue.Enqueue(i);
    }
    while (queue.TryDequeue(out var current))
    foreach (var next in Neighbors(current, width, height))
    {
        if (mask[next] || outside[next]) continue;
        outside[next] = true;
        queue.Enqueue(next);
    }
    for (var i = 0; i < mask.Length; i++) mask[i] = !outside[i];
}

float[] Blur(float[] values, int width, int height, float sigma)
{
    var radius = (int)Math.Ceiling(sigma * 3f);
    var weights = Enumerable.Range(-radius, 2 * radius + 1).Select(x => MathF.Exp(-x * x / (2 * sigma * sigma))).ToArray();
    var sum = weights.Sum();
    for (var i = 0; i < weights.Length; i++) weights[i] /= sum;
    var temp = new float[values.Length];
    var result = new float[values.Length];
    for (var y = 0; y < height; y++)
    for (var x = 0; x < width; x++)
    for (var k = -radius; k <= radius; k++)
        temp[y * width + x] += values[y * width + Math.Clamp(x + k, 0, width - 1)] * weights[k + radius];
    for (var y = 0; y < height; y++)
    for (var x = 0; x < width; x++)
    for (var k = -radius; k <= radius; k++)
        result[y * width + x] += temp[Math.Clamp(y + k, 0, height - 1) * width + x] * weights[k + radius];
    return result;
}

SKBitmap Load(string path)
{
    using var codec = SKCodec.Create(path) ?? throw new IOException(path);
    var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
    var result = new SKBitmap(info);
    if (codec.GetPixels(info, result.GetPixels()) != SKCodecResult.Success) throw new IOException(path);
    return result;
}

void Save(SKBitmap bitmap, string path)
{
    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    using var file = File.Create(path);
    data.SaveTo(file);
}

void RenderSheet(string role)
{
    using var sheet = new SKBitmap(4 * 420, 3 * 445);
    using var canvas = new SKCanvas(sheet);
    canvas.Clear(SKColors.White);
    using var paint = new SKPaint { IsAntialias = true };
    using var font = new SKFont(SKTypeface.Default, 18);
    for (var row = 0; row < 3; row++)
    for (var frame = 1; frame <= 4; frame++)
    {
        var ox = (frame - 1) * 420;
        var oy = row * 445;
        for (var y = 0; y < 420; y += 12)
        for (var x = 0; x < 420; x += 12)
        {
            paint.Color = row == 1 ? SKColor.Parse("#faf7ef") : row == 2 ? SKColor.Parse("#0c1420")
                : (x / 12 + y / 12) % 2 == 0 ? SKColor.Parse("#1d3b50") : SKColor.Parse("#121718");
            canvas.DrawRect(ox + x, oy + y + 25, 12, 12, paint);
        }
        using var sprite = Load(Path.Combine(outputDirectory, $"char_{role}_run_{frame}.png"));
        using var image = SKImage.FromBitmap(sprite);
        canvas.DrawImage(image, new SKRect(ox, oy + 25, ox + 420, oy + 445), new SKSamplingOptions(SKFilterMode.Linear));
        paint.Color = SKColors.Black;
        canvas.DrawText($"{role} / frame {frame}", ox + 10, oy + 20, SKTextAlign.Left, font, paint);
    }
    Save(sheet, Path.Combine(outputDirectory, $"{role}-backgrounds.png"));
    using var detail = new SKBitmap(1440, 720);
    using var detailCanvas = new SKCanvas(detail);
    detailCanvas.Clear(SKColor.Parse("#fffaf5"));
    for (var frame = 1; frame <= 4; frame++)
    {
        using var sprite = Load(Path.Combine(outputDirectory, $"char_{role}_run_{frame}.png"));
        using var image = SKImage.FromBitmap(sprite);
        var source = role == "police" ? new SKRect(220, 400, 400, 560) : new SKRect(420, 300, 600, 480);
        detailCanvas.DrawImage(image, source, new SKRect((frame - 1) * 360, 0, frame * 360, 360), new SKSamplingOptions(SKFilterMode.Linear));
        var top = role == "police" ? new SKRect(170, 30, 420, 160) : new SKRect(150, 0, 440, 130);
        detailCanvas.DrawImage(image, top, new SKRect((frame - 1) * 360, 400, frame * 360, 620), new SKSamplingOptions(SKFilterMode.Linear));
    }
    Save(detail, Path.Combine(outputDirectory, $"{role}-details.png"));
}

void RenderComparison()
{
    using var sheet = new SKBitmap(1120, 640);
    using var canvas = new SKCanvas(sheet);
    canvas.Clear(SKColor.Parse("#faf7ef"));
    using var textPaint = new SKPaint { Color = SKColor.Parse("#343a43"), IsAntialias = true };
    using var font = new SKFont(SKTypeface.Default, 18);
    for (var column = 0; column < 4; column++)
    {
        var role = column < 2 ? "police" : "robber";
        var repaired = column % 2 == 1;
        using var sprite = Load(Path.Combine(repaired ? outputDirectory : sourceDirectory, $"char_{role}_run_1.png"));
        using var image = SKImage.FromBitmap(sprite);
        canvas.DrawText($"{role} / {(repaired ? "AFTER" : "BEFORE")}", column * 280 + 12, 27, SKTextAlign.Left, font, textPaint);
        canvas.DrawImage(image, new SKRect(column * 280, 40, (column + 1) * 280, 320), new SKSamplingOptions(SKFilterMode.Linear));
        using var background = new SKPaint { Color = SKColor.Parse("#111c2b") };
        canvas.DrawRect(column * 280, 320, 280, 320, background);
        canvas.DrawImage(image, new SKRect(column * 280, 340, (column + 1) * 280, 620), new SKSamplingOptions(SKFilterMode.Linear));
    }
    Save(sheet, Path.Combine(outputDirectory, "before-after.png"));
}
