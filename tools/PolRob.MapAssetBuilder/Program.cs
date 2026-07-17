using System.Text.Json;
using System.Text.Json.Serialization;
using polrob.Shared;
using SkiaSharp;

const int sourceWidth = 5120;
const int sourceHeight = 7680;
const int tileSize = 512;
const int columns = sourceWidth / tileSize;
const int rows = sourceHeight / tileSize;
const int previewWidth = 1280;
const int previewHeight = 1920;

var repositoryRoot = FindRepositoryRoot();
var sourceImagePath = Path.Combine(repositoryRoot, "docs", "concepts", "polrob_map_upscale.png");
var annotationPath = Path.Combine(repositoryRoot, "docs", "concepts", "polrob_map_upscale.json");
var outputRoot = Path.Combine(repositoryRoot, "polrob.Client", "Resources", "Raw", "exact_map");
var baseTileDirectory = Path.Combine(outputRoot, "base");
var foregroundTileDirectory = Path.Combine(outputRoot, "foreground");
var previewDirectory = Path.Combine(repositoryRoot, "tmp_map_preview");
Directory.CreateDirectory(baseTileDirectory);
Directory.CreateDirectory(foregroundTileDirectory);
Directory.CreateDirectory(previewDirectory);

using var sourceBitmap = SKBitmap.Decode(sourceImagePath)
    ?? throw new InvalidOperationException($"Unable to decode {sourceImagePath}.");
if (sourceBitmap.Width != sourceWidth || sourceBitmap.Height != sourceHeight)
{
    throw new InvalidOperationException(
        $"Expected {sourceWidth}x{sourceHeight}, got {sourceBitmap.Width}x{sourceBitmap.Height}.");
}

var annotation = JsonSerializer.Deserialize<LabelMeDocument>(File.ReadAllText(annotationPath))
    ?? throw new InvalidOperationException($"Unable to parse {annotationPath}.");
var jailShape = annotation.Shapes.Single(shape => shape.Label == "jail");
var jailForegroundPath = CreateClosedPath(jailShape.Points);
var foregroundPaths = annotation.Shapes
    .Where(shape => shape.Label is not "map" and not "jail")
    .Select(shape => CreateClosedPath(shape.Points))
    .ToList();

// These fixed outer decorations are not separately annotated, but are part of
// the inaccessible forest/water edge and should still cover a player at the boundary.
foregroundPaths.Add(CreateRectPath(0f, 0f, sourceWidth, 540f));
foregroundPaths.Add(CreateRectPath(0f, 0f, 310f, sourceHeight));
foregroundPaths.Add(CreateRectPath(4770f, 0f, sourceWidth, sourceHeight));
foregroundPaths.Add(CreateRectPath(0f, 7250f, sourceWidth, sourceHeight));
foregroundPaths.Add(CreateRectPath(650f, 230f, 1530f, 1200f));
foregroundPaths.Add(CreateRectPath(2100f, 160f, 2920f, 920f));
foregroundPaths.Add(CreateRectPath(3650f, 6400f, 4850f, sourceHeight));

using var maskFillPaint = new SKPaint
{
    Color = SKColors.White,
    Style = SKPaintStyle.Fill,
    IsAntialias = true
};
using var maskPaddingPaint = new SKPaint
{
    Color = SKColors.White,
    Style = SKPaintStyle.Stroke,
    StrokeWidth = 20f,
    StrokeJoin = SKStrokeJoin.Round,
    StrokeCap = SKStrokeCap.Round,
    IsAntialias = true
};
using var jailFramePaint = new SKPaint
{
    Color = SKColors.White,
    Style = SKPaintStyle.Stroke,
    StrokeWidth = 34f,
    StrokeJoin = SKStrokeJoin.Round,
    StrokeCap = SKStrokeCap.Round,
    IsAntialias = true
};
using var exactPaint = new SKPaint
{
    IsAntialias = false
};

long persistedBasePixelDifferences = 0;
for (var row = 0; row < rows; row++)
{
    for (var column = 0; column < columns; column++)
    {
        var sourceRect = new SKRect(
            column * tileSize,
            row * tileSize,
            (column + 1) * tileSize,
            (row + 1) * tileSize);
        var tileRect = new SKRect(0f, 0f, tileSize, tileSize);

        using var baseSurface = SKSurface.Create(new SKImageInfo(tileSize, tileSize));
        baseSurface.Canvas.Clear(SKColors.Transparent);
        baseSurface.Canvas.DrawBitmap(sourceBitmap, sourceRect, tileRect, exactPaint);
        using var baseImage = baseSurface.Snapshot();
        var basePath = GetTilePath(baseTileDirectory, row, column);
        SavePng(baseImage, basePath);

        using var maskSurface = SKSurface.Create(new SKImageInfo(tileSize, tileSize));
        var maskCanvas = maskSurface.Canvas;
        maskCanvas.Clear(SKColors.Transparent);
        maskCanvas.Translate(-column * tileSize, -row * tileSize);
        foreach (var path in foregroundPaths)
        {
            maskCanvas.DrawPath(path, maskPaddingPaint);
            maskCanvas.DrawPath(path, maskFillPaint);
        }
        maskCanvas.DrawPath(jailForegroundPath, jailFramePaint);
        using var jailDetailMask = CreateJailDetailMask(
            sourceBitmap,
            jailForegroundPath,
            column * tileSize,
            row * tileSize,
            tileSize);
        maskCanvas.ResetMatrix();
        maskCanvas.DrawBitmap(jailDetailMask, 0f, 0f, exactPaint);
        using var maskImage = maskSurface.Snapshot();

        using var foregroundSurface = SKSurface.Create(new SKImageInfo(tileSize, tileSize));
        foregroundSurface.Canvas.Clear(SKColors.Transparent);
        foregroundSurface.Canvas.DrawImage(baseImage, 0f, 0f);
        using var destinationInPaint = new SKPaint
        {
            BlendMode = SKBlendMode.DstIn,
            IsAntialias = true
        };
        foregroundSurface.Canvas.DrawImage(maskImage, 0f, 0f, destinationInPaint);
        using var foregroundImage = foregroundSurface.Snapshot();
        SavePng(foregroundImage, GetTilePath(foregroundTileDirectory, row, column));

        using var persistedTile = SKBitmap.Decode(basePath)
            ?? throw new InvalidOperationException($"Unable to decode generated tile {basePath}.");
        for (var y = 0; y < tileSize; y++)
        {
            for (var x = 0; x < tileSize; x++)
            {
                if (persistedTile.GetPixel(x, y) !=
                    sourceBitmap.GetPixel(column * tileSize + x, row * tileSize + y))
                {
                    persistedBasePixelDifferences++;
                }
            }
        }
    }
}

if (persistedBasePixelDifferences != 0)
{
    throw new InvalidOperationException(
        $"Base-tile persistence changed {persistedBasePixelDifferences:N0} source pixels.");
}

ValidatePhysicsGeometry();

var exactPreviewPath = Path.Combine(previewDirectory, "exact_runtime_map_preview.png");
RenderPreview(exactPreviewPath, includePlayers: false, showMask: false);
var runtimePreviewPixelDifferences = CountRuntimePreviewPixelDifferences(exactPreviewPath);
if (runtimePreviewPixelDifferences != 0)
{
    throw new InvalidOperationException(
        $"Runtime base/foreground composition changed {runtimePreviewPixelDifferences:N0} preview pixels.");
}
var generatedRuntimePreviewPath = Path.Combine(previewDirectory, "generated_runtime_map_preview.png");
RenderPreview(generatedRuntimePreviewPath, includePlayers: false, showMask: false);
var occlusionPreviewPath = Path.Combine(previewDirectory, "exact_runtime_occlusion_preview.png");
RenderPreview(occlusionPreviewPath, includePlayers: true, showMask: false);
var maskPreviewPath = Path.Combine(previewDirectory, "exact_runtime_mask_preview.png");
RenderPreview(maskPreviewPath, includePlayers: true, showMask: true);
var collisionPreviewPath = Path.Combine(previewDirectory, "exact_runtime_collision_preview.png");
RenderCollisionPreview(collisionPreviewPath);

foreach (var path in foregroundPaths)
{
    path.Dispose();
}
jailForegroundPath.Dispose();

Console.WriteLine($"Base tiles: {baseTileDirectory}");
Console.WriteLine($"Foreground tiles: {foregroundTileDirectory}");
Console.WriteLine($"Tile grid: {columns}x{rows} ({columns * rows} tiles per layer)");
Console.WriteLine("Persisted base pixel differences: 0");
Console.WriteLine("Runtime composition pixel differences: 0");
Console.WriteLine("Physics probes: passed");
Console.WriteLine($"Preview: {exactPreviewPath}");
Console.WriteLine($"Runtime preview: {generatedRuntimePreviewPath}");
Console.WriteLine($"Occlusion preview: {occlusionPreviewPath}");
Console.WriteLine($"Mask preview: {maskPreviewPath}");
Console.WriteLine($"Collision preview: {collisionPreviewPath}");

void RenderPreview(string outputPath, bool includePlayers, bool showMask)
{
    using var previewSurface = SKSurface.Create(new SKImageInfo(previewWidth, previewHeight));
    var canvas = previewSurface.Canvas;
    canvas.Clear(SKColors.Black);
    canvas.Scale(previewWidth / (float)sourceWidth, previewHeight / (float)sourceHeight);
    DrawPersistedTiles(canvas, baseTileDirectory);

    if (includePlayers)
    {
        var playerBitmapPath = Path.Combine(
            repositoryRoot,
            "polrob.Client",
            "Resources",
            "Images",
            "char_robber_v3.png");
        using var playerBitmap = SKBitmap.Decode(playerBitmapPath)
            ?? throw new InvalidOperationException($"Unable to decode {playerBitmapPath}.");
        DrawPlayer(canvas, playerBitmap, 1155f, 1510f);
        DrawPlayer(canvas, playerBitmap, 2068f, 1710f);
        DrawPlayer(canvas, playerBitmap, 3985f, 2440f);
        DrawPlayer(canvas, playerBitmap, 2500f, 3750f);
    }

    DrawPersistedTiles(canvas, foregroundTileDirectory);

    if (showMask)
    {
        using var maskPaint = new SKPaint
        {
            Color = new SKColor(0, 255, 255, 85),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        foreach (var path in foregroundPaths)
        {
            canvas.DrawPath(path, maskPaint);
        }
    }

    using var image = previewSurface.Snapshot();
    SavePng(image, outputPath);
}

void DrawPersistedTiles(SKCanvas canvas, string tileDirectory)
{
    for (var row = 0; row < rows; row++)
    {
        for (var column = 0; column < columns; column++)
        {
            using var tile = SKBitmap.Decode(GetTilePath(tileDirectory, row, column))
                ?? throw new InvalidOperationException($"Unable to decode tile {row},{column}.");
            var destination = new SKRect(
                column * tileSize,
                row * tileSize,
                (column + 1) * tileSize,
                (row + 1) * tileSize);
            canvas.DrawBitmap(tile, destination, exactPaint);
        }
    }
}

long CountRuntimePreviewPixelDifferences(string runtimePreviewPath)
{
    using var runtimePreview = SKBitmap.Decode(runtimePreviewPath)
        ?? throw new InvalidOperationException($"Unable to decode {runtimePreviewPath}.");
    using var referencePreview = new SKBitmap(previewWidth, previewHeight);
    using (var referenceCanvas = new SKCanvas(referencePreview))
    {
        referenceCanvas.DrawBitmap(
            sourceBitmap,
            new SKRect(0f, 0f, previewWidth, previewHeight),
            exactPaint);
    }

    long differences = 0;
    for (var y = 0; y < previewHeight; y++)
    {
        for (var x = 0; x < previewWidth; x++)
        {
            if (runtimePreview.GetPixel(x, y) != referencePreview.GetPixel(x, y))
            {
                differences++;
            }
        }
    }

    return differences;
}

void RenderCollisionPreview(string outputPath)
{
    using var previewSurface = SKSurface.Create(new SKImageInfo(previewWidth, previewHeight));
    var canvas = previewSurface.Canvas;
    canvas.Scale(previewWidth / (float)sourceWidth, previewHeight / (float)sourceHeight);
    DrawPersistedTiles(canvas, baseTileDirectory);

    var map = new GameMap();
    var boundaryShape = annotation.Shapes.Single(shape => shape.Label == "map");
    using var boundaryPath = CreateClosedPath(boundaryShape.Points);
    using var blockedExteriorPath = new SKPath { FillType = SKPathFillType.EvenOdd };
    blockedExteriorPath.AddRect(new SKRect(0f, 0f, sourceWidth, sourceHeight));
    blockedExteriorPath.AddPath(boundaryPath);
    using var blockedExteriorPaint = new SKPaint
    {
        Color = new SKColor(180, 0, 210, 92),
        Style = SKPaintStyle.Fill,
        IsAntialias = true
    };
    canvas.DrawPath(blockedExteriorPath, blockedExteriorPaint);

    using var boundaryPaint = new SKPaint
    {
        Color = new SKColor(0, 255, 255, 230),
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 16f,
        IsAntialias = true
    };
    canvas.DrawPath(boundaryPath, boundaryPaint);

    using var buildingPaint = new SKPaint
    {
        Color = new SKColor(255, 0, 0, 90),
        Style = SKPaintStyle.Fill,
        IsAntialias = true
    };
    using var buildingOutlinePaint = new SKPaint
    {
        Color = new SKColor(255, 20, 20, 235),
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 10f,
        IsAntialias = true
    };
    foreach (var building in map.Buildings.Where(building => building.BlocksMovement))
    {
        var corners = GameMap.GetBuildingCollisionCorners(building);
        using var path = new SKPath();
        path.MoveTo(corners[0].X, corners[0].Y);
        for (var index = 1; index < corners.Length; index++)
        {
            path.LineTo(corners[index].X, corners[index].Y);
        }
        path.Close();
        canvas.DrawPath(path, buildingPaint);
        canvas.DrawPath(path, buildingOutlinePaint);
    }

    using var obstaclePaint = new SKPaint
    {
        Color = new SKColor(255, 175, 0, 105),
        Style = SKPaintStyle.Fill,
        IsAntialias = true
    };
    using var obstacleOutlinePaint = new SKPaint
    {
        Color = new SKColor(255, 175, 0, 240),
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 8f,
        IsAntialias = true
    };
    foreach (var obstacle in map.Obstacles.Where(obstacle => obstacle.BlocksMovement))
    {
        if (obstacle.Type == "Circle")
        {
            canvas.DrawCircle(obstacle.CenterX.X, obstacle.CenterX.Y, obstacle.Radius, obstaclePaint);
            canvas.DrawCircle(obstacle.CenterX.X, obstacle.CenterX.Y, obstacle.Radius, obstacleOutlinePaint);
        }
        else if (obstacle.Type == "Polygon")
        {
            var polygonPoints = obstacle.PolygonPoints.ToArray();
            if (polygonPoints.Length < 3)
            {
                continue;
            }

            using var path = new SKPath();
            path.MoveTo(polygonPoints[0].X, polygonPoints[0].Y);
            for (var index = 1; index < polygonPoints.Length; index++)
            {
                path.LineTo(polygonPoints[index].X, polygonPoints[index].Y);
            }
            path.Close();
            canvas.DrawPath(path, obstaclePaint);
            canvas.DrawPath(path, obstacleOutlinePaint);
        }
        else
        {
            var obstacleRect = new SKRect(
                obstacle.LeftTop.X,
                obstacle.LeftTop.Y,
                obstacle.RightBottom.X,
                obstacle.RightBottom.Y);
            canvas.DrawRect(obstacleRect, obstaclePaint);
            canvas.DrawRect(obstacleRect, obstacleOutlinePaint);
        }
    }

    using var image = previewSurface.Snapshot();
    SavePng(image, outputPath);
}

static void ValidatePhysicsGeometry()
{
    var map = new GameMap();
    var nearby = new List<Obstacle>();

    Assert(map.Width == 5120f && map.Height == 7680f, "Canonical world dimensions are incorrect.");
    Assert(map.Buildings.Count == 9, "Expected all 9 LabelMe building colliders.");
    Assert(
        map.Obstacles.Count(obstacle => obstacle.Type == "Polygon") == 20,
        "Expected all 20 LabelMe obstacle colliders.");
    Assert(map.Obstacles.Count >= 21, "Expected LabelMe obstacles and preserved outer reinforcement.");
    Assert(map.Buildings.All(building => !building.IsVisible), "Buildings must be physics-only.");
    Assert(map.Obstacles.All(obstacle => !obstacle.IsVisible), "Obstacles must be physics-only.");

    Assert(!map.IsMovementPositionBlocked(2485f, 3840f, 50f, nearby), "Robber spawn is blocked.");
    Assert(!map.IsMovementPositionBlocked(1258f, 2493f, 50f, nearby), "Police spawn is blocked.");
    Assert(map.IsMovementPositionBlocked(100f, 100f, 50f, nearby), "Top forest boundary is open.");
    Assert(map.IsMovementPositionBlocked(5000f, 7400f, 50f, nearby), "Water boundary is open.");

    foreach (var building in map.Buildings.Where(building => building.BlocksMovement))
    {
        Assert(
            map.IsMovementPositionBlocked(
                building.CollisionCenter.X,
                building.CollisionCenter.Y,
                50f,
                nearby),
            $"Building collider is open: {building.Type}.");
    }

    foreach (var obstacle in map.Obstacles.Where(obstacle => obstacle.Type == "Polygon"))
    {
        var authoredEdgePoint = obstacle.PolygonPoints[0];
        Assert(
            map.IsMovementPositionBlocked(
                authoredEdgePoint.X,
                authoredEdgePoint.Y,
                1f,
                nearby),
            $"Polygon collider is open: {obstacle.ImageFileName}.");
    }

    static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

static void DrawPlayer(SKCanvas canvas, SKBitmap playerBitmap, float x, float y)
{
    const float halfSize = 100f;
    canvas.DrawBitmap(
        playerBitmap,
        new SKRect(x - halfSize, y - halfSize, x + halfSize, y + halfSize));
}

static SKBitmap CreateJailDetailMask(
    SKBitmap source,
    SKPath jailPath,
    int tileLeft,
    int tileTop,
    int tileSize)
{
    var mask = new SKBitmap(tileSize, tileSize);
    mask.Erase(SKColors.Transparent);

    for (var localY = 0; localY < tileSize; localY++)
    {
        var worldY = tileTop + localY;
        for (var localX = 0; localX < tileSize; localX++)
        {
            var worldX = tileLeft + localX;
            if (!jailPath.Contains(worldX, worldY))
            {
                continue;
            }

            var color = source.GetPixel(worldX, worldY);
            var luminance =
                (0.2126f * color.Red) +
                (0.7152f * color.Green) +
                (0.0722f * color.Blue);

            // The cage bars and their dark joints are materially darker than
            // the open stone floor. Copy only those source pixels above players.
            if (luminance <= 102f)
            {
                mask.SetPixel(localX, localY, SKColors.White);
            }
        }
    }

    return mask;
}

static string GetTilePath(string directory, int row, int column) =>
    Path.Combine(directory, $"map_{row:D2}_{column:D2}.png");

static SKPath CreateClosedPath(IReadOnlyList<double[]> points)
{
    if (points.Count < 3)
    {
        throw new InvalidOperationException("A polygon requires at least three points.");
    }

    var path = new SKPath();
    path.MoveTo((float)points[0][0], (float)points[0][1]);
    for (var index = 1; index < points.Count; index++)
    {
        path.LineTo((float)points[index][0], (float)points[index][1]);
    }
    path.Close();
    return path;
}

static SKPath CreateRectPath(float left, float top, float right, float bottom)
{
    var path = new SKPath();
    path.AddRect(new SKRect(left, top, right, bottom));
    return path;
}

static void SavePng(SKImage image, string path)
{
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    using var stream = File.Create(path);
    data.SaveTo(stream);
}

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(Environment.CurrentDirectory);
    while (directory != null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "polrob.slnx")))
        {
            return directory.FullName;
        }
        directory = directory.Parent;
    }

    throw new InvalidOperationException("Could not locate polrob.slnx.");
}

internal sealed class LabelMeDocument
{
    [JsonPropertyName("shapes")]
    public List<LabelMeShape> Shapes { get; set; } = [];
}

internal sealed class LabelMeShape
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("points")]
    public List<double[]> Points { get; set; } = [];
}
