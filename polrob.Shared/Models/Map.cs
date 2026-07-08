using System.Drawing;

namespace polrob.Shared;

public class GameMap
{
    private const float SpatialCellSize = 500f;
    private const float LayoutScale = 10f;
    private const float WallSize = 15f * LayoutScale;
    private const float BuildingStructureSize = 75f * LayoutScale;
    private const float HouseSize = 50f * LayoutScale;
    private const float TreeRadius = 25f * LayoutScale;
    private const float PondRadius = 36f * LayoutScale;
    private const float BushSize = 32f * LayoutScale;
    private readonly Dictionary<(int X, int Y), List<Obstacle>> _obstaclesByCell = new();
    private readonly List<Obstacle> _bushes = new();
    private float _maximumObstacleExtent;

    public float Width = 5000;
    public float Height = 7500;
    public float BuildingSize = 1000f;
    public List<Obstacle> Obstacles = new();
    public List<MapBuilding> Buildings = new();
    public MapBuilding PoliceStation { get; private set; } = null!;
    public MapBuilding Jail { get; private set; } = null!;

    public GameMap()
    {
        PoliceStation = new MapBuilding()
        {
            Type = "PoliceStation",
            ImageFileName = "police_station.png",
            LeftTop = new PointF(0f, 0f),
            RightBottom = new PointF(BuildingSize, BuildingSize)
        };

        Jail = new MapBuilding()
        {
            Type = "Jail",
            ImageFileName = "jail_v2.png",
            LeftTop = new PointF(PoliceStation.RightBottom.X + Width / 12f, 150f),
            RightBottom = new PointF(PoliceStation.RightBottom.X + Width / 12f + 700f, 850f)
        };

        // The full-map background currently contains these visual landmarks already.
        // Leave the old generated geometry commented out until collision bounds are
        // re-authored against the new 5000x7500 concept map.
        // Buildings.Add(PoliceStation);
        // Buildings.Add(Jail);

        // AddWalls();
        // AddStructures();
        // AddBushes();
        // AddTrees();
        // AddPonds();
        BuildSpatialIndex();
    }

    public void GetNearbyObstacles(float x, float y, float radius, List<Obstacle> results)
    {
        results.Clear();

        var searchRadius = radius + _maximumObstacleExtent;
        var minCellX = GetCellCoordinate(x - searchRadius);
        var maxCellX = GetCellCoordinate(x + searchRadius);
        var minCellY = GetCellCoordinate(y - searchRadius);
        var maxCellY = GetCellCoordinate(y + searchRadius);

        for (var cellY = minCellY; cellY <= maxCellY; cellY++)
        {
            for (var cellX = minCellX; cellX <= maxCellX; cellX++)
            {
                if (_obstaclesByCell.TryGetValue((cellX, cellY), out var obstacles))
                {
                    results.AddRange(obstacles);
                }
            }
        }
    }

    public Obstacle? FindBushContainingPoint(float x, float y)
    {
        foreach (var bush in _bushes)
        {
            if (ContainsPoint(bush, x, y))
            {
                return bush;
            }
        }

        return null;
    }

    public static bool IsBushObstacle(Obstacle obstacle) =>
        obstacle.ImageFileName == "bush.png";

    public static bool ContainsPoint(Obstacle obstacle, float x, float y)
    {
        if (obstacle.Type == "Rect")
        {
            return x >= obstacle.LeftTop.X &&
                   x <= obstacle.RightBottom.X &&
                   y >= obstacle.LeftTop.Y &&
                   y <= obstacle.RightBottom.Y;
        }

        if (obstacle.Type == "Circle")
        {
            var dx = x - obstacle.CenterX.X;
            var dy = y - obstacle.CenterX.Y;
            return (dx * dx) + (dy * dy) <= obstacle.Radius * obstacle.Radius;
        }

        return false;
    }

    private void AddWalls()
    {
        AddWallRow(252f, 34f, 6);
        AddWallRow(253f, 68f, 6);
        AddWallRow(253f, 100f, 6);
        AddWallColumn(377f, 31f, 6);
        AddWallColumn(419f, 31f, 6);
        AddWallColumn(461f, 31f, 6);

        AddWallRow(51f, 176f, 12);
        AddWallColumn(51f, 191f, 7);
        AddWallRow(274f, 178f, 4);
        AddWallRow(377f, 178f, 5);
        AddWallColumn(436f, 192f, 7);

        AddWallColumn(56f, 343f, 7);
        AddWallRow(56f, 445f, 12);
        AddWallColumn(437f, 343f, 7);
        AddWallRow(276f, 445f, 12);
    }

    private void AddStructures()
    {
        PointF[] buildingCenters =
        [
            new(148f, 249f),
            new(150f, 373f)
        ];

        foreach (var center in buildingCenters)
        {
            AddRectObstacle("building.png", center.X, center.Y, BuildingStructureSize);
        }

        PointF[] houseCenters =
        [
            new(299f, 240f), new(374f, 240f),
            new(373f, 369f), new(297f, 371f)
        ];

        foreach (var center in houseCenters)
        {
            AddRectObstacle("house_v2.png", center.X, center.Y, HouseSize);
        }
    }

    private void AddBushes()
    {
        PointF[] centers =
        [
            new(131f, 493f), new(163f, 493f), new(99f, 502f), new(42f, 505f),
            new(221f, 515f), new(42f, 537f), new(30f, 569f), new(230f, 577f),
            new(94f, 594f), new(30f, 601f), new(126f, 606f), new(158f, 606f),
            new(218f, 610f), new(158f, 639f), new(204f, 661f), new(24f, 671f),
            new(203f, 694f), new(24f, 703f), new(146f, 708f), new(88f, 721f),
            new(56f, 721f)
        ];

        foreach (var center in centers)
        {
            AddRectObstacle("bush.png", center.X, center.Y, BushSize);
        }
    }

    private void AddTrees()
    {
        PointF[] centers =
        [
            new(352f, 511f), new(429f, 542f), new(286f, 552f),
            new(356f, 595f), new(265f, 635f), new(435f, 635f),
            new(364f, 675f), new(286f, 704f), new(445f, 704f)
        ];

        foreach (var center in centers)
        {
            AddCircleObstacle("tree.png", center.X, center.Y, TreeRadius);
        }
    }

    private void AddPonds()
    {
        AddCircleObstacle("pond_v2.png", 152f, 553f, PondRadius);
        AddCircleObstacle("pond_v2.png", 88f, 667f, PondRadius);
    }

    private void BuildSpatialIndex()
    {
        _obstaclesByCell.Clear();
        _maximumObstacleExtent = 0f;

        foreach (var obstacle in Obstacles)
        {
            var center = obstacle.Center;
            var extent = obstacle.Type == "Circle"
                ? obstacle.Radius
                : Math.Max(obstacle.Width, obstacle.Height) / 2f;
            _maximumObstacleExtent = Math.Max(_maximumObstacleExtent, extent);

            var cell = (GetCellCoordinate(center.X), GetCellCoordinate(center.Y));
            if (!_obstaclesByCell.TryGetValue(cell, out var cellObstacles))
            {
                cellObstacles = new List<Obstacle>();
                _obstaclesByCell[cell] = cellObstacles;
            }

            cellObstacles.Add(obstacle);
        }
    }

    private static int GetCellCoordinate(float position) =>
        (int)MathF.Floor(position / SpatialCellSize);

    private void AddWallRow(float startCenterX, float centerY, int count)
    {
        for (var i = 0; i < count; i++)
        {
            AddRectObstacle("wall.png", startCenterX + i * 15f, centerY, WallSize);
        }
    }

    private void AddWallColumn(float centerX, float startCenterY, int count)
    {
        for (var i = 0; i < count; i++)
        {
            AddRectObstacle("wall.png", centerX, startCenterY + i * 15f, WallSize);
        }
    }

    private void AddRectObstacle(string imageFileName, float layoutCenterX, float layoutCenterY, float size)
    {
        var centerX = layoutCenterX * LayoutScale;
        var centerY = layoutCenterY * LayoutScale;
        var halfSize = size / 2f;

        var obstacle = new Obstacle
        {
            Type = "Rect",
            ImageFileName = imageFileName,
            LeftTop = new PointF(centerX - halfSize, centerY - halfSize),
            LeftBottom = new PointF(centerX - halfSize, centerY + halfSize),
            RightTop = new PointF(centerX + halfSize, centerY - halfSize),
            RightBottom = new PointF(centerX + halfSize, centerY + halfSize)
        };

        Obstacles.Add(obstacle);
        if (IsBushObstacle(obstacle))
        {
            _bushes.Add(obstacle);
        }
    }

    private void AddCircleObstacle(string imageFileName, float layoutCenterX, float layoutCenterY, float radius)
    {
        Obstacles.Add(new Obstacle
        {
            Type = "Circle",
            ImageFileName = imageFileName,
            CenterX = new PointF(layoutCenterX * LayoutScale, layoutCenterY * LayoutScale),
            Radius = radius
        });
    }
}

public class MapBuilding
{
    public string Type { get; set; } = string.Empty;
    public string ImageFileName { get; set; } = string.Empty;
    public PointF LeftTop { get; set; }
    public PointF RightBottom { get; set; }
    public float Width => RightBottom.X - LeftTop.X;
    public float Height => RightBottom.Y - LeftTop.Y;
    public PointF Center => new((LeftTop.X + RightBottom.X) / 2f, (LeftTop.Y + RightBottom.Y) / 2f);
}

public class Obstacle
{
    public string Type { get; set; } = string.Empty;
    public string ImageFileName { get; set; } = string.Empty;
    public PointF LeftTop { get; set; }
    public PointF LeftBottom { get; set; }
    public PointF RightTop { get; set; }
    public PointF RightBottom { get; set; }
    public PointF CenterX { get; set; }
    public PointF CenterY { get; set; }
    public float Radius { get; set; }
    public float Width => RightBottom.X - LeftTop.X;
    public float Height => RightBottom.Y - LeftTop.Y;
    public PointF Center => Type == "Circle"
        ? CenterX
        : new PointF((LeftTop.X + RightBottom.X) / 2f, (LeftTop.Y + RightBottom.Y) / 2f);
}
