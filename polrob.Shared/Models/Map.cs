using System.Drawing;

namespace polrob.Shared;

public class Map
{
    public float Width = 3000;
    public float Height = 4500;
    public List<Obstacle> Obstacles = new();
    public Map()
    {
        Obstacles.Add(new Obstacle() { Type = "Rect", LeftTop = new PointF(500f, 1800f), LeftBottom = new PointF(500f, 3000f), RightTop = new PointF(800f, 1800f), RightBottom = new PointF(800f, 3000f) });
        Obstacles.Add(new Obstacle() { Type = "Rect", LeftTop = new PointF(1600f, 3300f), LeftBottom = new PointF(1600f, 4200f), RightTop = new PointF(2500f, 3300f), RightBottom = new PointF(2500f, 4200f) });
        Obstacles.Add(new Obstacle() { Type = "Circle", CenterX = new PointF(1800f, 900f), Radius = 150f });
        Obstacles.Add(new Obstacle() { Type = "Circle", CenterX = new PointF(1600f, 1400f), Radius = 150f });
        Obstacles.Add(new Obstacle() { Type = "Circle", CenterX = new PointF(2200f, 1200f), Radius = 150f });
    }
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