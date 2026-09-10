using polrob.Shared;
using SkiaSharp;

internal static class CollisionProfilePreview
{
    public static void Export(string output, IReadOnlyDictionary<string, SKBitmap?> assets)
    {
        const int columns = 4, cellWidth = 400, cellHeight = 480;
        var rows = (CanvaMapCollisions.Profiles.Count + columns - 1) / columns;
        using var surface = SKSurface.Create(new SKImageInfo(columns * cellWidth, rows * cellHeight));
        var canvas = surface.Canvas;
        canvas.Clear(SKColor.Parse("#17232D"));
        using var titleFont = new SKFont(SKTypeface.Default, 20);
        using var captionFont = new SKFont(SKTypeface.Default, 14);
        using var label = new SKPaint { IsAntialias = true, Color = SKColors.White };
        using var fill = new SKPaint { IsAntialias = true };
        using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };
        var index = 0;
        foreach (var (name, profile) in CanvaMapCollisions.Profiles)
        {
            var x = (index % columns) * cellWidth; var y = (index / columns) * cellHeight;
            index++;
            fill.Color = SKColor.Parse("#22333F");
            canvas.DrawRoundRect(new SKRect(x+8, y+8, x+392, y+472), 10, 10, fill);
            canvas.DrawText(name, x+24, y+34, titleFont, label);
            var spec = profile.Shape == "Rect" ? $"Rect {profile.Image.Width} x {profile.RectangleHeight}" :
                profile.Shape == "Circle" ? $"C({profile.CircleCenter.X}, {profile.CircleCenter.Y})  R={profile.Radius}" : "Traced outline";
            canvas.DrawText($"{profile.Image.Width} x {profile.Image.Height}  |  {spec}", x+24, y+57, captionFont, label);
            var scale = MathF.Min(350f / profile.Image.Width, 340f / profile.Image.Height);
            canvas.Save();
            canvas.Translate(x+25+(350-profile.Image.Width*scale)/2, y+76+340-profile.Image.Height*scale);
            canvas.Scale(scale);
            using var image = SKImage.FromBitmap(assets[$"MapAssets/{name}"]!);
            canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            stroke.Color = profile.Shape switch
            {
                "Rect" => new SKColor(255, 78, 91),
                "Circle" => new SKColor(80, 225, 255),
                _ => new SKColor(218, 101, 255)
            };
            stroke.StrokeWidth = 2 / scale;
            fill.Color = stroke.Color.WithAlpha(55);
            if (profile.Shape == "Rect")
            {
                var rectangle = new SKRect(0, profile.Image.Height-profile.RectangleHeight, profile.Image.Width, profile.Image.Height);
                canvas.DrawRect(rectangle, fill); canvas.DrawRect(rectangle, stroke);
            }
            else if (profile.Shape == "Circle")
            {
                canvas.DrawCircle(profile.CircleCenter.X, profile.Image.Height-profile.CircleCenter.Y, profile.Radius, fill);
                canvas.DrawCircle(profile.CircleCenter.X, profile.Image.Height-profile.CircleCenter.Y, profile.Radius, stroke);
            }
            else
            {
                using var path = new SKPath();
                path.AddPoly(profile.Outline!.Select(p => new SKPoint(p.X, profile.Image.Height-p.Y)).ToArray(), true);
                canvas.DrawPath(path, fill); canvas.DrawPath(path, stroke);
            }
            stroke.Color = SKColor.Parse("#84EDAF");
            fill.Color = stroke.Color;
            canvas.DrawCircle(0, profile.Image.Height, 3 / scale, fill);
            canvas.DrawLine(0, profile.Image.Height, 28/scale, profile.Image.Height, stroke);
            canvas.DrawLine(0, profile.Image.Height, 0, profile.Image.Height-28/scale, stroke);
            canvas.Restore();
            canvas.DrawText("Origin (0, 0): bottom-left   +X right, +Y up", x+24, y+453, captionFont, label);
        }
        using var result = surface.Snapshot();
        using var data = result.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(Path.Combine(output, "collision-original-assets.png"));
        data.SaveTo(file);
    }
}
