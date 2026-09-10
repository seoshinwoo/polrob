using System.Drawing;

namespace polrob.Shared;

/// <summary>Original PNG coordinates: origin at the bottom-left, including transparent margins.</summary>
public static class CanvaMapCollisions
{
    public static readonly IReadOnlyDictionary<string, SourceCollisionProfile> Profiles =
        new Dictionary<string, SourceCollisionProfile>(StringComparer.Ordinal)
        {
            ["police_station.png"] = new(new(764, 700, 2, 2, 762, 698), "Rect", RectangleHeight: 565),
            ["donut.png"] = new(new(880, 850, 0, 0, 880, 850), "Rect", RectangleHeight: 700),
            ["cafe.png"] = new(new(926, 969, 1, 0, 925, 967), "Rect", RectangleHeight: 875),
            // The three houses in the supplied map use house-orange.png.
            ["house-orange.png"] = new(new(975, 960, 2, 3, 975, 959), "Rect", RectangleHeight: 825),
            ["burger.png"] = new(new(1053, 1058, 0, 0, 1053, 1055), "Rect", RectangleHeight: 885),
            ["jail.png"] = new(new(979, 858, 0, 0, 979, 856), "Rect", RectangleHeight: 600),
            ["warehouse.png"] = new(new(951, 1000, 4, 4, 948, 992), "Rect", RectangleHeight: 900),
            ["box.png"] = new(new(1044, 1199, 7, 8, 1037, 1190), "Rect", RectangleHeight: 1199),
            ["tree.png"] = new(new(1009, 1080, 10, 6, 1001, 1073), "Circle", CircleCenter: new(504.5f, 150), Radius: 150),
            ["streetlamp.png"] = new(new(606, 1202, 4, 3, 601, 1192), "Circle", CircleCenter: new(303, 131), Radius: 131),
            ["rock.png"] = new(new(1251, 1121, 8, 8, 1245, 1113), "Circle", CircleCenter: new(625.5f, 560.5f), Radius: 560),
            ["bush.png"] = new(new(791, 777, 2, 0, 791, 777), "Circle", CircleCenter: new(395.5f, 388.5f), Radius: 390),
            // Alpha silhouette simplified to 5 source pixels; preserve the empty upper-right corner.
            ["boxes.png"] = new(new(1039, 1094, 8, 9, 1029, 1082), "Polygon", Outline:
            [
                new(39, 1081), new(32, 1070), new(9, 741), new(20, 22), new(36, 13),
                new(1015, 18), new(1022, 41), new(1027, 346), new(1009, 609),
                new(998, 615), new(538, 614), new(538, 731), new(519, 1055), new(509, 1082)
            ]),
            // Trace the stone rim's oval silhouette, bridging the two protruding reed clusters.
            // Bounds in source pixels: x=5..1381, y=1..831 (1376 × 830).
            ["pond.png"] = new(new(1389, 842, 2, 0, 1385, 842), "Polygon", Outline:
            [
                new(5, 478), new(6, 349), new(31, 313), new(35, 288), new(66, 235),
                new(110, 198), new(116, 178), new(137, 157), new(202, 116), new(235, 114),
                new(251, 95), new(353, 62), new(380, 68), new(394, 57), new(488, 37),
                new(525, 38), new(543, 22), new(593, 5), new(639, 4), new(646, 12),
                new(673, 1), new(787, 3), new(812, 19), new(843, 12), new(940, 28),
                new(959, 41), new(973, 34), new(1070, 55), new(1087, 69), new(1108, 67),
                new(1200, 113), new(1216, 135), new(1241, 143), new(1289, 185), new(1314, 216),
                new(1321, 240), new(1338, 249), new(1359, 284), new(1380, 345), new(1381, 469),
                new(1371, 524), new(1349, 556), new(1343, 587), new(1314, 624), new(1224, 707),
                new(1190, 723), new(1169, 748), new(1077, 781), new(1046, 787), new(1026, 780),
                new(1007, 791), new(876, 783), new(852, 772), new(819, 791), new(777, 796),
                new(754, 816), new(677, 830), new(655, 822), new(634, 831), new(521, 821),
                new(509, 809), new(481, 815), new(416, 805), new(331, 791), new(294, 794),
                new(289, 783), new(201, 754), new(183, 733), new(142, 716), new(84, 658),
                new(74, 625), new(57, 612), new(21, 549), new(18, 503)
            ])
        };

    public static MapPropLayout Apply(MapPropLayout placement)
    {
        var profile = Profiles[Path.GetFileName(placement.AssetPath)];
        var image = profile.Image;
        var scaleX = placement.Width / (image.VisibleRight - image.VisibleLeft);
        var scaleY = placement.Height / (image.VisibleBottom - image.VisibleTop);
        PointF center;
        float width, height, radiusX = 0, radiusY = 0;
        PointF[]? polygon = null;

        if (profile.Shape == "Rect")
        {
            center = image.ToWorld(placement, new(image.Width / 2f, profile.RectangleHeight / 2f));
            width = image.Width * scaleX;
            height = profile.RectangleHeight * scaleY;
        }
        else if (profile.Shape == "Circle")
        {
            center = image.ToWorld(placement, profile.CircleCenter);
            radiusX = profile.Radius * scaleX;
            radiusY = profile.Radius * scaleY;
            width = radiusX * 2;
            height = radiusY * 2;
        }
        else
        {
            polygon = profile.Outline!.Select(point => image.ToWorld(placement, point)).ToArray();
            var left = polygon.Min(p => p.X); var right = polygon.Max(p => p.X);
            var top = polygon.Min(p => p.Y); var bottom = polygon.Max(p => p.Y);
            center = new((left + right) / 2, (top + bottom) / 2);
            width = right - left;
            height = bottom - top;
        }

        return placement with
        {
            BlocksMovement = true, CollisionShape = profile.Shape,
            CollisionWidth = width, CollisionHeight = height,
            CollisionOffsetX = center.X - placement.CenterX, CollisionOffsetY = center.Y - placement.CenterY,
            CollisionRadius = radiusX, CollisionRadiusY = radiusY, CollisionPolygon = polygon
        };
    }
}

public sealed record SourceCollisionProfile(
    SourceImageGeometry Image, string Shape, float RectangleHeight = 0,
    PointF CircleCenter = default, float Radius = 0, PointF[]? Outline = null);

// Visible bounds use top-left image coordinates and the same alpha>=16 crop as the renderer.
public readonly record struct SourceImageGeometry(
    int Width, int Height, int VisibleLeft, int VisibleTop, int VisibleRight, int VisibleBottom)
{
    public PointF ToWorld(MapPropLayout placement, PointF sourceBottomLeft) => new(
        placement.CenterX - placement.Width / 2 + (sourceBottomLeft.X - VisibleLeft) * placement.Width / (VisibleRight - VisibleLeft),
        placement.CenterY - placement.Height / 2 + (Height - sourceBottomLeft.Y - VisibleTop) * placement.Height / (VisibleBottom - VisibleTop));
}
