using polrob.Shared;
using SkiaSharp;

/// <summary>Import generated transparent sprites without changing authored world geometry.</summary>
internal static class HighResolutionAssets
{
    public static SKBitmap Load(string docs, ChaseTownAsset asset)
    {
        using var original = SKBitmap.Decode(Path.Combine(docs, "hd/original-props", asset.Id + ".png"))
            ?? throw new InvalidOperationException($"Missing alignment reference: {asset.Id}");
        using var source = SKBitmap.Decode(Path.Combine(docs, "hd/sources", asset.Id + ".png"))
            ?? throw new InvalidOperationException($"Missing HD source: {asset.Id}. Never fall back to the low-resolution map crop.");
        var pixels = source.Pixels;
        if (!pixels.Any(p => p.Alpha == 0) || !pixels.Any(p => p.Alpha == 255))
            throw new InvalidOperationException($"HD source needs genuine transparency: {asset.Id}");
        var sourceBounds = Bounds(source);
        var oldBounds = Bounds(original);
        var result = new SKBitmap(asset.TextureWidth, asset.TextureHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(result))
        using (var image = SKImage.FromBitmap(source))
        {
            canvas.Clear(SKColors.Transparent);
            // Normalize only transparent padding and density, not the collision coordinates.
            var s = asset.TextureScale;
            canvas.DrawImage(image, sourceBounds,
                new SKRect(oldBounds.Left * s, oldBounds.Top * s, oldBounds.Right * s, oldBounds.Bottom * s),
                new SKSamplingOptions(SKCubicResampler.Mitchell));
        }
        if (asset.Id.StartsWith("jail", StringComparison.Ordinal) &&
            result.GetPixel(result.Width / 2, result.Height / 2).Alpha != 0)
            throw new InvalidOperationException($"Jail interior must remain transparent: {asset.Id}");
        return result;
    }

    public static SKRect Bounds(SKBitmap bitmap)
    {
        int left = bitmap.Width, top = bitmap.Height, right = 0, bottom = 0;
        var pixels = bitmap.Pixels;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            if (pixels[y * bitmap.Width + x].Alpha < 16) continue;
            left = Math.Min(left, x); top = Math.Min(top, y);
            right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1);
        }
        if (right <= left || bottom <= top) throw new InvalidOperationException("Empty sprite.");
        return new SKRect(left, top, right, bottom);
    }
}
