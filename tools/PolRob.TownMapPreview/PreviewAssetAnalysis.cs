using SkiaSharp;

internal static class PreviewAssetAnalysis
{
    public static SKRect VisibleBounds(SKBitmap bitmap)
    {
        var left = bitmap.Width;
        var top = bitmap.Height;
        var right = 0;
        var bottom = 0;
        var pixels = bitmap.Pixels;
        for (var index = 0; index < pixels.Length; index++)
        {
            if (pixels[index].Alpha < 16)
            {
                continue;
            }

            var x = index % bitmap.Width;
            var y = index / bitmap.Width;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x + 1);
            bottom = Math.Max(bottom, y + 1);
        }

        return right > left && bottom > top
            ? new SKRect(left, top, right, bottom)
            : new SKRect(0, 0, bitmap.Width, bitmap.Height);
    }
}
