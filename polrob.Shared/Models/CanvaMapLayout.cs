using System.Drawing;

namespace polrob.Shared;

/// <summary>Visual placements measured from polrob-new-map-example.png, in 2560 × 3840 world coordinates.</summary>
public static class CanvaMapLayout
{
    public const int TileSize = 256;
    public const float PavingRepeatSize = 240f;
    public const float RoadWidth = 204f;
    public const string AssetRoot = "MapAssets";
    public const string GroundAssetRoot = "TownMap/tiles";

    public static readonly MapRoad[] Roads =
    [
        new("M 560 850 H 1990 Q 2290 850 2290 1150 V 3160 Q 2290 3460 1990 3460 H 560 Q 260 3460 260 3160 V 1150 Q 260 850 560 850 Z", RoadWidth, true),
        new("M 1160 0 C 1160 400 1170 550 980 850 C 860 1120 940 1400 1100 1540 C 1300 1710 1450 1820 1450 2090 C 1450 2400 1120 2560 1000 2790 C 900 3070 1060 3250 1330 3460 V 3840", RoadWidth, true),
        new("M 260 1700 H 2290", RoadWidth, true),
        new("M 260 2520 H 2290", RoadWidth, true)
    ];

    public static readonly MapCrosswalk[] Crosswalks =
    [
        new(650, 850, false), new(1940, 850, false), new(260, 1570, true),
        new(2290, 2390, true), new(710, 2520, false), new(1790, 3460, false)
    ];

    public static readonly PointF[] ChaseWaypoints =
    [new(650, 850), new(1450, 1700), new(710, 2520), new(2290, 1700), new(1790, 3460)];

    // Visual bounds describe the alpha silhouette; collision profiles use full original PNG coordinates.
    public static readonly MapPropLayout[] Props =
    [
        Sprite("police_station", 610, 376, 336, 308, "PoliceStation"),
        Sprite("jail", 1634, 362, 384, 336, "Jail"),
        Sprite("warehouse", 610, 1281, 320, 334, "Warehouse"),
        Sprite("donut", 1431, 1250, 302, 292, "DonutShop"),
        Sprite("burger", 1864, 1258, 308, 308, "BurgerShop"),
        Sprite("cafe", 822, 2119, 300, 314, "Cafe"),
        Sprite("house-orange", 1519, 2849, 250, 246, "House1"),
        Sprite("house-orange", 1933, 2849, 250, 246, "House2"),
        Sprite("house-orange", 1728, 3140, 252, 248, "House3"),
        Sprite("pond", 1880, 2107, 324, 198),
        Sprite("boxes", 1195, 1024, 106, 112),
        Sprite("streetlamp", 2111, 1024, 78, 156),
        Sprite("streetlamp", 817, 1796, 78, 156),
        Sprite("streetlamp", 1623, 2016, 78, 156),
        Sprite("streetlamp", 456, 2646, 76, 152),
        Sprite("streetlamp", 1391, 3140, 78, 156),
        Sprite("tree", 524, 1844, 148, 160),
        Sprite("tree", 525, 2019, 150, 162),
        Sprite("tree", 520, 2188, 148, 160),
        Sprite("tree", 1120, 1862, 148, 160),
        Sprite("tree", 1119, 2025, 150, 162),
        Sprite("tree", 1119, 2183, 150, 162),
        Sprite("bush", 534, 2341, 76, 74),
        Sprite("bush", 610, 2341, 76, 74),
        Sprite("bush", 1010, 2341, 76, 74),
        Sprite("bush", 1082, 2341, 76, 74),
        Sprite("bush", 1953, 1876, 78, 76),
        Sprite("bush", 2127, 2220, 74, 72),
        Sprite("bush", 1818, 2333, 76, 74),
        Sprite("bush", 1506, 2677, 76, 74),
        Sprite("bush", 1583, 2678, 78, 76),
        Sprite("bush", 1661, 2678, 78, 76),
        Sprite("bush", 1738, 2677, 76, 74),
        Sprite("bush", 1816, 2677, 76, 74),
        Sprite("bush", 1894, 2677, 76, 74),
        Sprite("bush", 1971, 2678, 78, 76),
        Sprite("rock", 1786, 1904, 76, 68),
        Sprite("rock", 2088, 1976, 76, 68),
        Sprite("rock", 1691, 2241, 78, 70),
        Sprite("rock", 2006, 2320, 76, 68),
        Sprite("box", 463, 2812, 66, 76),
        Sprite("box", 522, 2813, 64, 74),
        Sprite("box", 580, 2813, 64, 74),
        Sprite("box", 638, 2813, 64, 74),
        Sprite("box", 698, 2811, 64, 74),
        Sprite("box", 756, 2811, 64, 74),
        Sprite("box", 463, 2852, 66, 76),
        Sprite("box", 463, 2888, 66, 76),
        Sprite("box", 463, 2926, 66, 76),
        Sprite("box", 463, 2968, 66, 76),
        Sprite("box", 463, 3004, 66, 76),
        Sprite("box", 463, 3042, 66, 76),
        Sprite("box", 463, 3080, 66, 76),
        Sprite("box", 463, 3118, 66, 76),
        Sprite("box", 463, 3156, 66, 76),
        Sprite("box", 463, 3194, 66, 76),
        Sprite("box", 463, 3232, 66, 76),
        Sprite("box", 521, 3232, 66, 76),
        Sprite("box", 580, 3231, 64, 74),
        Sprite("box", 640, 3231, 64, 74),
        Sprite("box", 698, 3231, 64, 74),
        Sprite("box", 825, 3232, 66, 76),
        Sprite("box", 629, 2974, 66, 76),
        Sprite("box", 689, 2974, 66, 76),
        Sprite("box", 629, 3016, 66, 76),
        Sprite("box", 689, 3016, 66, 76),
        Sprite("box", 632, 3061, 64, 74),
        Sprite("box", 692, 3061, 64, 74),
        Sprite("box", 2004, 3225, 64, 74),
        Sprite("box", 2058, 3225, 64, 74),
    ];

    private static MapPropLayout Sprite(string name, float x, float y, float width, float height, string? buildingType = null) =>
        CanvaMapCollisions.Apply(new($"{AssetRoot}/{name}.png", x, y, width, height, BuildingType: buildingType));
}

public readonly record struct MapCrosswalk(float X, float Y, bool VerticalRoad);
