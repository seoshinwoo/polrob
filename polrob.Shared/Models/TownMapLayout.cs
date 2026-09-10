using System.Drawing;

namespace polrob.Shared;

/// <summary>Authored chase-town geometry; screen coordinates in world pixels.</summary>
public static class TownMapLayout
{
    public const int TileSize = 256;
    public const float RoadWidth = 174f;
    public const string AssetRoot = "TownMapV2";

    // An asymmetric street network: civic bend, market loop and freight loop.
    // Enclosed road blocks are fully paved by the renderer; exterior land stays grass.
    public static readonly MapRoad[] Roads =
    [
        new("M 1190 -100 C 1190 220 1120 380 1110 700 C 1100 920 1280 1030 1500 1040 C 1830 1050 2020 1130 1880 1390 C 1770 1610 1570 1850 1510 2090 C 1460 2290 1390 2460 1410 2750 C 1440 3040 1640 3270 1560 3500 C 1510 3680 1370 3730 1390 3940", 174, true),
        new("M 1125 935 C 800 930 480 950 460 1170 C 440 1340 325 1540 310 1770 C 295 2000 430 2390 590 2510 C 780 2670 690 3120 1010 3200 C 1300 3280 1360 3460 1570 3460", 156),
        new("M 335 1880 C 650 1810 950 1880 1170 2060 C 1420 2280 1820 2470 2120 2440 C 2440 2400 2400 2050 2390 1870", 160),
        new("M 1500 1040 C 1900 1010 2110 720 2280 760 C 2480 820 2390 1390 2310 1510 C 2240 1630 2290 1760 2390 1870", 144),
        new("M 2120 2440 C 2250 2610 2320 2860 2330 3160 C 2350 3510 2000 3660 1560 3500", 152)
    ];
    public static readonly string[] RoadPaths = Roads.Select(r => r.Path).ToArray();

    // Forest masses with overlapping canopies, clearings and depth; never a perimeter row.
    public static readonly MapGrove[] Groves =
    [
        new(135, 125, 310, 280, 15, 41), new(555, 80, 420, 210, 18, 82),
        new(130, 535, 200, 320, 12, 112), new(2190, 110, 470, 260, 21, 212),
        new(2520, 615, 165, 415, 13, 281), new(40, 1650, 135, 330, 10, 324),
        new(2510, 2110, 125, 355, 10, 430), new(180, 2500, 145, 220, 8, 542),
        new(100, 3460, 360, 470, 22, 651), new(860, 3820, 380, 175, 16, 721),
        new(2310, 3790, 420, 200, 18, 832),
        new(1645, 260, 145, 100, 5, 881),
        new(1200, 3100, 145, 95, 5, 940)
    ];

    // Landmark targets used by navigation checks and the interactive preview.
    public static readonly PointF[] ChaseWaypoints =
    [new(1070, 1110), new(850, 1560), new(1550, 1800), new(1510, 2190), new(2220, 3080), new(960, 2960)];
    public static readonly MapPropLayout[] Props = CreateProps();

    private static MapPropLayout[] CreateProps()
    {
        var props = new List<MapPropLayout>
        {
            Building("police_station", "PoliceStation", 685, 635, 475, 387),
            Building("jail", "Jail", 245, 1140, 350, 351),
            Building("bank", "Bank", 1730, 650, 440, 334),
            Building("townhouse", "NorthHouse", 2175, 1120, 295, 274),
            Building("cafe", "Cafe", 600, 1520, 330, 327),
            Building("market", "MarketNorth", 1200, 1275, 350, 185),
            Building("market", "Market", 1210, 1630, 450, 238),
            Building("townhouse", "EastHouse", 2100, 1720, 335, 311),
            Building("townhouse", "WestHouse", 660, 2160, 340, 316),
            Building("workshop", "WorkshopNorth", 1900, 2185, 340, 276),
            Building("workshop", "Workshop", 1050, 2630, 345, 280),
            Building("warehouse", "Warehouse", 1830, 2810, 475, 398),
            Building("townhouse", "SouthHouse", 495, 2940, 320, 297),
            Building("warehouse", "WarehouseSouth", 2020, 3300, 385, 323),
            Building("cafe", "CafeSouth", 705, 3450, 345, 342),
            new(P("fountain"), 1660, 1430, 250, 151, CollisionShape: "Circle", CollisionRadius: 72),
            new("", 1610, 1430, 0, 0, CollisionShape: "Circle", CollisionRadius: 68),
            new("", 1710, 1430, 0, 0, CollisionShape: "Circle", CollisionRadius: 68),

            Crates(1630, 2435, 140), Crates(1910, 2540, 150),
            Crates(1740, 3230, 175), Crates(1610, 3340, 135),
            Crates(2105, 2950, 125), Crates(2240, 3500, 130),
            Crates(1170, 2890, 115), Crates(740, 1810, 110),
            Crates(1440, 1200, 110), Crates(2210, 2040, 110),
            Lamp(958, 830), Lamp(920, 1070), Lamp(2020, 920),
            Lamp(932, 1725), Lamp(1745, 1655), Lamp(1730, 2330),
            Lamp(1280, 2780), Lamp(2200, 3200), Lamp(1065, 3310),
            Bush(445, 375, 130), Bush(925, 415, 110), Bush(525, 865, 95),
            Bush(1480, 1150, 115),
            Bush(785, 1420, 100), Bush(785, 1660, 90),
            Bush(1710, 1775, 135), Bush(1550, 1900, 100),
            Bush(925, 2015, 130), Bush(875, 2240, 100),
            Bush(1625, 2200, 150), Bush(1730, 1980, 95),
            Bush(810, 2590, 125), Bush(1640, 2600, 95),
            Bush(1900, 3510, 120), Bush(1090, 3690, 155),
            Tree(1020, 530, 190, false), Tree(1450, 870, 170, true),
            Tree(1440, 1490, 145, true), Tree(1000, 1830, 155, false),
            Tree(2250, 1400, 180, true), Tree(870, 2410, 170, false),
            Tree(1050, 2340, 140, true), Tree(1710, 1900, 180, false),
            Tree(2210, 2600, 165, true), Tree(860, 3160, 170, true),
            Tree(445, 3210, 185, false),
            Rocks(310, 290, 160), Rocks(2460, 1320, 125),
            Rocks(335, 2665, 120), Rocks(1160, 3655, 125),
            Rocks(2405, 3600, 165), Rocks(1580, 140, 135)
        };

        foreach (var grove in Groves)
        {
            var random = new Random(grove.Seed);
            var accepted = new List<PointF>();
            for (var attempt = 0; accepted.Count < grove.Count && attempt < 500; attempt++)
            {
                var angle = random.NextDouble() * Math.PI * 2;
                var radius = Math.Sqrt(random.NextDouble());
                var x = grove.X + (float)(Math.Cos(angle) * radius * grove.RadiusX);
                var y = grove.Y + (float)(Math.Sin(angle) * radius * grove.RadiusY);
                // Keep exits and the jail rescue frontage deliberately open.
                if (x > 1015 && x < 1385 && y < 450 || x > 1180 && x < 1580 && y > 3550) continue;
                if (accepted.Any(p => MathF.Pow(p.X-x, 2) + MathF.Pow(p.Y-y, 2) < 85*85)) continue;
                var size = 155 + random.Next(95);
                props.Add(Tree(x, y, size, random.Next(3) == 0));
                accepted.Add(new PointF(x, y));
                if (accepted.Count % 4 == 0) props.Add(Bush(x + 65, y + 80, 85 + random.Next(35)));
            }
        }
        return props.ToArray();
    }

    private static string P(string name) => $"{AssetRoot}/props/{name}.png";
    private static MapPropLayout Building(string file, string type, float x, float y, float width, float height) =>
        new(P(file), x, y, width, height, BuildingType: type);
    private static MapPropLayout Tree(float x, float y, float size, bool slender) =>
        new(P(slender ? "birch" : "oak"), x, y, size * (slender ? .78f : 1f), size * (slender ? 1.151f : .946f),
            CollisionShape: "Circle", CollisionRadius: size * (slender ? .34f : .43f), CollisionOffsetY: size * .03f);
    private static MapPropLayout Bush(float x, float y, float size) =>
        new(P("bush"), x, y, size, size * .533f, CollisionShape: "Circle", CollisionRadius: size * .32f);
    private static MapPropLayout Crates(float x, float y, float size) =>
        new(P("crates"), x, y, size, size * 1.134f, CollisionWidth: size*.90f, CollisionHeight: size*.96f, CollisionOffsetY: size*.035f);
    private static MapPropLayout Lamp(float x, float y) =>
        new(P("lamp"), x, y, 48, 75, CollisionShape: "Circle", CollisionRadius: 17, CollisionOffsetY: 22);
    private static MapPropLayout Rocks(float x, float y, float size) =>
        new(P("rocks"), x, y, size, size * .712f, CollisionShape: "Circle", CollisionRadius: size*.38f);
}

public readonly record struct MapRoad(string Path, float Width, bool Marked = false);
public readonly record struct MapGrove(float X, float Y, float RadiusX, float RadiusY, int Count, int Seed);
