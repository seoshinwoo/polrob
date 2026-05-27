using System.Drawing;

namespace polrob.Shared;

public class GameMap
{
    public float Width = 3000;
    public float Height = 4500;
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
            ImageFileName = "jail.png",
            LeftTop = new PointF(PoliceStation.RightBottom.X + Width / 12f, 150f),
            RightBottom = new PointF(PoliceStation.RightBottom.X + Width / 12f + 700f, 850f)
        };

        Buildings.Add(PoliceStation);
        Buildings.Add(Jail);

        Obstacles.Add(new Obstacle() { Type = "Rect", LeftTop = new PointF(500f, 1800f), LeftBottom = new PointF(500f, 3000f), RightTop = new PointF(800f, 1800f), RightBottom = new PointF(800f, 3000f) });
        Obstacles.Add(new Obstacle() { Type = "Rect", LeftTop = new PointF(1600f, 3300f), LeftBottom = new PointF(1600f, 4200f), RightTop = new PointF(2500f, 3300f), RightBottom = new PointF(2500f, 4200f) });
        Obstacles.Add(new Obstacle() { Type = "Circle", CenterX = new PointF(1800f, 900f), Radius = 150f });
        Obstacles.Add(new Obstacle() { Type = "Circle", CenterX = new PointF(1600f, 1400f), Radius = 150f });
        Obstacles.Add(new Obstacle() { Type = "Circle", CenterX = new PointF(2200f, 1200f), Radius = 150f });
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
    public PointF LeftTop { get; set; }
    public PointF LeftBottom { get; set; }
    public PointF RightTop { get; set; }
    public PointF RightBottom { get; set; }
    public PointF CenterX { get; set; }
    public PointF CenterY { get; set; }
    public float Radius { get; set; }
}
