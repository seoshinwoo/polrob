using System.Text.Json;
using polrob.Client;
using polrob.Shared;
using SkiaSharp;

// Historical exporter retained as a reference. Program.cs now exports the active Canva map,
// including for --town-map; this class is no longer called by the preview command.
internal static class ArchivedTownMapPreview
{
    public static void Export(string repo)
    {
        var output = Path.Combine(repo, "docs/town-map");
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "layout.json")));
        var layout = document.RootElement;
        var width = layout.GetProperty("Width").GetInt32();
        var height = layout.GetProperty("Height").GetInt32();
        var roadWidth = layout.GetProperty("RoadWidth").GetSingle();
        var paths = layout.GetProperty("RoadPaths").EnumerateArray().Select(p => p.GetString()!).ToArray();
        var roads = paths.Select(SKPath.ParseSvgPathData).ToArray();
        using var roadArea = TownMapRenderer.MergeAreas([], paths.Select(p => (p, roadWidth)));
        using var pavingArea = TownMapRenderer.CreatePavedBlocks(roadArea, width, height);
        using var grass = LoadTile("grass");
        using var paving = LoadTile("paving");
        using var asphalt = LoadTile("asphalt");
        using var grassShader = SKShader.CreateBitmap(grass, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
        // The running-bond brick texture repeats every 240 world pixels (~40 × 18 per brick).
        const float pavingRepeatSize = 240f;
        using var pavingShader = SKShader.CreateBitmap(paving, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat,
            SKMatrix.CreateScale(pavingRepeatSize / paving.Width, pavingRepeatSize / paving.Height));
        using var asphaltShader = SKShader.CreateBitmap(asphalt, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
        using var groundPaint = new SKPaint { IsAntialias = true, Shader = grassShader };
        using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round };
        using var fill = new SKPaint { IsAntialias = true, Color = SKColor.Parse("#EEE7CC") };
        using var dash = SKPathEffect.CreateDash([30f, 30f], 0);
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.DrawPaint(groundPaint);
        groundPaint.Shader = pavingShader;
        canvas.DrawPath(pavingArea, groundPaint);
        for (var layer = 0; layer < 3; layer++)
        {
            stroke.StrokeWidth = roadWidth + (layer == 0 ? 52 : layer == 1 ? 40 : 6);
            stroke.Color = SKColor.Parse(layer == 0 ? "#69735B" : layer == 1 ? "#D2CAAB" : "#72766F");
            foreach (var road in roads) canvas.DrawPath(road, stroke);
        }
        stroke.Shader = asphaltShader; stroke.StrokeWidth = roadWidth; stroke.Color = SKColors.White;
        foreach (var road in roads) canvas.DrawPath(road, stroke);
        stroke.Shader = null; stroke.PathEffect = dash; stroke.StrokeCap = SKStrokeCap.Butt;
        stroke.Color = SKColor.Parse("#E5C568"); stroke.StrokeWidth = 5;
        foreach (var road in roads) canvas.DrawPath(road, stroke);
        stroke.PathEffect = null;
        foreach (var (x, y, vertical) in new[] { (650f, 850f, false), (1940f, 850f, false),
            (260f, 1570f, true), (2290f, 2390f, true), (710f, 2520f, false), (1790f, 3460f, false) })
        {
            canvas.Save();
            canvas.ClipPath(roadArea, SKClipOperation.Intersect, true);
            groundPaint.Shader = asphaltShader;
            canvas.DrawRect(vertical ? new SKRect(x-106, y-34, x+106, y+34) : new SKRect(x-34, y-106, x+34, y+106), groundPaint);
            canvas.Translate(x, y); if (vertical) canvas.RotateDegrees(90);
            for (var i = -3; i <= 3; i++) canvas.DrawRoundRect(new SKRect(-27, i*25-7, 27, i*25+7), 2, 2, fill);
            canvas.Restore();
        }
        Save(surface, "background-2560x3840.png");
        // A distinct preview name avoids displaying the previous PNG from a UI cache.
        Save(surface, "background-paved-blocks.png");
        Save(surface, "background-brick-paving.png");
        Save(surface, "background-brick-paving-240.png");
        using var props = SKBitmap.Decode(Path.Combine(output, "props-2560x3840.png"));
        canvas.DrawBitmap(props, 0, 0);
        Save(surface, "map-2560x3840.png");
        using var mapImage = surface.Snapshot();
        using (var overview = SKSurface.Create(new SKImageInfo(1024, 1536)))
        {
            overview.Canvas.Scale(1024f / width, 1536f / height);
            overview.Canvas.DrawImage(mapImage, 0, 0);
            Save(overview, "map-overview.png");
        }
        using (var detail = SKSurface.Create(new SKImageInfo(1120, 1000)))
        {
            detail.Canvas.Translate(-1030, -750);
            detail.Canvas.DrawImage(mapImage, 0, 0);
            foreach (var (name, x, y) in new[] { ("char_police.png", 1770f, 1580f), ("char_robber.png", 1460f, 1660f) })
            {
                using var sprite = SKBitmap.Decode(Path.Combine(repo, "polrob.Client/Resources/Raw", name));
                detail.Canvas.DrawBitmap(sprite, PreviewAssetAnalysis.VisibleBounds(sprite), new SKRect(x-25, y-25, x+25, y+25));
            }
            Save(detail, "detail-50px-characters.png");
        }
        stroke.StrokeWidth = 3;
        foreach (var prop in layout.GetProperty("Props").Deserialize<MapPropLayout[]>()!)
        {
            var x = prop.CenterX + prop.CollisionOffsetX; var y = prop.CenterY + prop.CollisionOffsetY;
            stroke.Color = prop.BuildingType != null ? new SKColor(255, 78, 91, 220) : new SKColor(80, 225, 255, 240);
            fill.Color = stroke.Color.WithAlpha(35);
            if (prop.CollisionShape == "Circle")
            {
                var radius = prop.CollisionRadius > 0 ? prop.CollisionRadius : Math.Min(prop.Width, prop.Height) / 2;
                canvas.DrawCircle(x, y, radius, fill); canvas.DrawCircle(x, y, radius, stroke);
            }
            else
            {
                var w = prop.CollisionWidth > 0 ? prop.CollisionWidth : prop.Width;
                var h = prop.CollisionHeight > 0 ? prop.CollisionHeight : prop.Height;
                var rect = new SKRect(x-w/2, y-h/2, x+w/2, y+h/2);
                canvas.DrawRect(rect, fill); canvas.DrawRect(rect, stroke);
            }
        }
        Save(surface, "collisions-2560x3840.png");
        foreach (var road in roads) road.Dispose();
        Console.WriteLine($"Updated the original road layout in {output}");

        SKBitmap LoadTile(string name) => SKBitmap.Decode(Path.Combine(repo, "polrob.Client/Resources/Raw/TownMap/tiles", name + ".png"))
            ?? throw new InvalidOperationException($"Missing original town tile: {name}");
        void Save(SKSurface source, string name)
        {
            using var image = source.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(Path.Combine(output, name));
            data.SaveTo(file);
        }
    }
}
