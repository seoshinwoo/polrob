using System.Drawing;

namespace polrob.Shared;

public class GameMap
{
    // The renderer and shared physics use the same authored prop placements.
    public static readonly MapPropLayout[] PropLayouts = CanvaMapLayout.Props;

    // Legacy geometry helpers remain only as migration references.
    private const float CanonicalCoordinateScale = 2560f / 5000f;
    private const float LegacyMapScale = 0.5f;
    private const float SpatialCellSize = 250f;
    private const float LayoutScale = 5f;
    private const float WallSize = 15f * LayoutScale;
    private const float BuildingStructureSize = 75f * LayoutScale;
    private const float HouseSize = 50f * LayoutScale;
    private const float TreeRadius = 25f * LayoutScale;
    private const float PondRadius = 36f * LayoutScale;
    private const float BushSize = 32f * LayoutScale;
    // Legacy LabelMe land contour retained for a later terrain/boundary pass.
    private static readonly PointF[] PlayableBoundary = ScaleLegacyBoundary(
    [
        new(3915.3f, 468.9f),
        new(3710f, 274.2f),
        new(3136.3f, 526.8f),
        new(2936.3f, 737.4f),
        new(2846.8f, 942.6f),
        new(2620.5f, 984.7f),
        new(2604.7f, 874.2f),
        new(2810f, 790f),
        new(2920.5f, 674.2f),
        new(2983.7f, 511.1f),
        new(2846.8f, 416.3f),
        new(2741.6f, 326.8f),
        new(2520.5f, 279.5f),
        new(2315.3f, 290f),
        new(2110f, 353.2f),
        new(2073.2f, 547.9f),
        new(2031.1f, 626.8f),
        new(1862.6f, 616.3f),
        new(1683.7f, 616.3f),
        new(1531.1f, 632.1f),
        new(1446.8f, 753.2f),
        new(1362.6f, 805.8f),
        new(1294.2f, 711.1f),
        new(1225.8f, 616.3f),
        new(1094.2f, 579.5f),
        new(978.4f, 568.9f),
        new(862.6f, 579.5f),
        new(831.1f, 653.2f),
        new(762.6f, 763.7f),
        new(704.7f, 811.1f),
        new(699.5f, 842.6f),
        new(704.7f, 890f),
        new(731.1f, 947.9f),
        new(920.5f, 1147.9f),
        new(899.5f, 1247.9f),
        new(810f, 1274.2f),
        new(715.3f, 1247.9f),
        new(610f, 1258.4f),
        new(536.3f, 1174.2f),
        new(478.4f, 1179.5f),
        new(415.3f, 1142.6f),
        new(357.4f, 1184.7f),
        new(410f, 1263.7f),
        new(457.4f, 1342.6f),
        new(341.6f, 1416.3f),
        new(194.2f, 1526.8f),
        new(110f, 1668.9f),
        new(99.5f, 1805.8f),
        new(83.7f, 2000.5f),
        new(131.1f, 2158.4f),
        new(183.7f, 2274.2f),
        new(378.4f, 2511.1f),
        new(462.6f, 2626.8f),
        new(588.9f, 2758.4f),
        new(783.7f, 2853.2f),
        new(910f, 2853.2f),
        new(1004.7f, 2968.9f),
        new(967.9f, 3095.3f),
        new(388.9f, 3411.1f),
        new(331.1f, 3453.2f),
        new(320.5f, 3574.2f),
        new(394.2f, 3653.2f),
        new(462.6f, 3768.9f),
        new(162.6f, 4232.1f),
        new(31.1f, 4495.3f),
        new(104.7f, 5037.4f),
        new(373.2f, 5700.5f),
        new(662.6f, 6268.9f),
        new(983.7f, 6890f),
        new(1110f, 7211.1f),
        new(1336.3f, 7358.4f),
        new(1473.2f, 7547.9f),
        new(1478.4f, 7679f),
        new(1641.6f, 7679f),
        new(1657.4f, 7505.8f),
        new(1773.2f, 7426.8f),
        new(1978.4f, 7374.2f),
        new(2115.3f, 7474.2f),
        new(2294.2f, 7353.2f),
        new(2452.1f, 7326.8f),
        new(2641.6f, 7295.3f),
        new(2794.2f, 7216.3f),
        new(2894.2f, 7132.1f),
        new(3036.3f, 7047.9f),
        new(3188.9f, 7005.8f),
        new(3362.6f, 7037.4f),
        new(3436.3f, 7037.4f),
        new(3125.8f, 6600.5f),
        new(3167.9f, 6558.4f),
        new(3731.1f, 7421.6f),
        new(3978.4f, 7253.2f),
        new(3362.6f, 6458.4f),
        new(3473.2f, 6442.6f),
        new(3620.5f, 6505.8f),
        new(3783.7f, 6537.4f),
        new(3899.5f, 6558.4f),
        new(3999.5f, 6490f),
        new(4052.1f, 6395.3f),
        new(4131.1f, 6268.9f),
        new(4299.5f, 6205.8f),
        new(4346.8f, 6079.5f),
        new(4410f, 5858.4f),
        new(4441.6f, 5690f),
        new(4394.2f, 5568.9f),
        new(4362.6f, 5342.6f),
        new(4425.8f, 5163.7f),
        new(4610f, 5116.3f),
        new(4867.9f, 5074.2f),
        new(5041.6f, 4963.7f),
        new(5119f, 4607.5f),
        new(5083.7f, 4484.7f),
        new(4115.3f, 3879.5f),
        new(4231.1f, 3616.3f),
        new(5099.5f, 4211.1f),
        new(5119f, 3913.3f),
        new(4594.2f, 3511.1f),
        new(4920.5f, 3168.9f),
        new(5041.6f, 3047.9f),
        new(5088.9f, 2942.6f),
        new(5119f, 2747.3f),
        new(5094.2f, 2521.6f),
        new(4978.4f, 2316.3f),
        new(4936.3f, 2121.6f),
        new(4815.3f, 2095.3f),
        new(4752.1f, 2000.5f),
        new(5015.3f, 1774.2f),
        new(4978.4f, 1532.1f),
        new(4641.6f, 1174.2f),
        new(4536.3f, 1016.3f),
        new(4352.1f, 937.4f),
        new(4210f, 853.2f),
        new(4094.2f, 737.4f)
    ]);

    private static PointF[] ScaleLegacyBoundary(PointF[] sourcePoints)
    {
        var scaledPoints = new PointF[sourcePoints.Length];
        for (var index = 0; index < sourcePoints.Length; index++)
        {
            scaledPoints[index] = new PointF(
                sourcePoints[index].X * LegacyMapScale,
                sourcePoints[index].Y * LegacyMapScale);
        }

        return scaledPoints;
    }

    private readonly Dictionary<(int X, int Y), List<Obstacle>> _obstaclesByCell = new();
    private readonly List<Obstacle> _bushes = new();

    public const float WorldWidth = 2560f;
    public const float WorldHeight = 3840f;

    public float Width = WorldWidth;
    public float Height = WorldHeight;
    public float BuildingSize = 512f;
    public List<Obstacle> Obstacles = new();
    public List<MapBuilding> Buildings = new();
    public MapBuilding PoliceStation { get; private set; } = null!;
    public MapBuilding Jail { get; private set; } = null!;

    public GameMap()
    {
        AddMapPropColliders();
        if (PoliceStation == null || Jail == null)
        {
            throw new InvalidOperationException("The town map requires a police station and jail.");
        }

        BuildSpatialIndex();
    }

    private void AddMapPropColliders()
    {
        foreach (var layout in PropLayouts)
        {
            var collisionWidth = layout.CollisionWidth > 0f ? layout.CollisionWidth : layout.Width;
            var collisionHeight = layout.CollisionHeight > 0f ? layout.CollisionHeight : layout.Height;
            var centerX = layout.CenterX + layout.CollisionOffsetX;
            var centerY = layout.CenterY + layout.CollisionOffsetY;
            var left = centerX - collisionWidth / 2f;
            var top = centerY - collisionHeight / 2f;
            var right = centerX + collisionWidth / 2f;
            var bottom = centerY + collisionHeight / 2f;

            if (layout.BuildingType != null)
            {
                var building = new MapBuilding
                {
                    Type = layout.BuildingType,
                    ImageFileName = layout.AssetPath,
                    LeftTop = new PointF(layout.CenterX - layout.Width / 2f, layout.CenterY - layout.Height / 2f),
                    RightBottom = new PointF(layout.CenterX + layout.Width / 2f, layout.CenterY + layout.Height / 2f),
                    CollisionWidth = collisionWidth,
                    CollisionHeight = collisionHeight,
                    CollisionOffsetX = layout.CollisionOffsetX,
                    CollisionOffsetY = layout.CollisionOffsetY,
                    CollisionPolygon = layout.CollisionPolygon ?? [],
                    IsVisible = false,
                    BlocksMovement = layout.BlocksMovement,
                    BlocksVision = false
                };
                Buildings.Add(building);
                if (building.Type == "PoliceStation") PoliceStation = building;
                if (building.Type == "Jail") Jail = building;
                continue;
            }

            // Visual-only placements must not leave invisible movement blockers.
            if (!layout.BlocksMovement) continue;

            var obstacle = new Obstacle
            {
                ImageFileName = layout.AssetPath,
                IsVisible = false,
                BlocksMovement = true,
                BlocksVision = false,
                Type = layout.CollisionShape,
                PolygonPoints = layout.CollisionPolygon ?? [],
                LeftTop = new PointF(left, top),
                LeftBottom = new PointF(left, bottom),
                RightTop = new PointF(right, top),
                RightBottom = new PointF(right, bottom),
                CenterX = new PointF(centerX, centerY),
                Radius = layout.CollisionRadius > 0f
                    ? layout.CollisionRadius
                    : MathF.Min(collisionWidth, collisionHeight) / 2f,
                RadiusY = layout.CollisionRadiusY,
                RenderWidth = layout.Width,
                RenderHeight = layout.Height,
                RenderOffsetX = -layout.CollisionOffsetX,
                RenderOffsetY = -layout.CollisionOffsetY
            };

            if (layout.IsTriangular)
            {
                obstacle.Type = "Polygon";
                obstacle.PolygonPoints =
                [
                    new PointF(centerX, top),
                    new PointF(right, bottom),
                    new PointF(left, bottom)
                ];
            }

            Obstacles.Add(obstacle);
        }
    }

    /// <summary>
    /// Returns stable, collision-free role spawn slots, first near the station's
    /// front apron or the central road, then in outward rings if props occupy a slot.
    /// </summary>
    public PointF GetSpawnPosition(PlayerRole role, int slot, float radius)
    {
        if (role is not (PlayerRole.Police or PlayerRole.Robber))
            throw new ArgumentOutOfRangeException(nameof(role));
        if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot));
        if (!float.IsFinite(radius) || radius <= 0f || radius * 2f > MathF.Min(Width, Height))
            throw new ArgumentOutOfRangeException(nameof(radius));

        var stationBounds = GetBuildingCollisionBounds(PoliceStation);
        var anchor = role == PlayerRole.Police
            ? new PointF(PoliceStation.CollisionCenter.X, stationBounds.Bottom + MathF.Max(100f, radius + 20f))
            : new PointF(Width / 2f, Height / 2f);
        var gap = MathF.Max(150f, radius * 2f + 20f);
        var maxRing = (int)MathF.Ceiling(MathF.Max(Width, Height) / gap) + 1;
        var nearby = new List<Obstacle>();
        var availableSlot = 0;

        for (var ring = 0; ring <= maxRing; ring++)
        {
            foreach (var offset in EnumerateSpawnRing(ring))
            {
                var x = anchor.X + offset.X * gap;
                var y = anchor.Y + offset.Y * gap;
                if (IsMovementPositionBlocked(x, y, radius, nearby)) continue;
                if (availableSlot++ == slot) return new PointF(x, y);
            }
        }

        throw new InvalidOperationException($"The map has no free spawn slot {slot} for {role}.");
    }

    /// <summary>
    /// Returns a compact, centered holding slot inside the jail artwork. The
    /// building collider describes its outside footprint, so it must not be
    /// used as the anchor for characters rendered behind the bars.
    /// </summary>
    public PointF GetJailHoldingPosition(int slot, int playerCount, float radius)
    {
        if (playerCount <= 0) throw new ArgumentOutOfRangeException(nameof(playerCount));
        if (slot < 0 || slot >= playerCount) throw new ArgumentOutOfRangeException(nameof(slot));
        if (!float.IsFinite(radius) || radius <= 0f) throw new ArgumentOutOfRangeException(nameof(radius));

        const float gap = 10f;
        const float horizontalInsetRatio = 0.2f;
        var spacing = radius * 2f + gap;
        var requiredWidth = radius * 2f + spacing * (playerCount - 1);
        var holdingWidth = Jail.Width * (1f - horizontalInsetRatio * 2f);
        if (requiredWidth > holdingWidth + 0.001f)
        {
            throw new InvalidOperationException(
                $"The jail holding area cannot fit {playerCount} players with radius {radius}.");
        }

        return new PointF(
            Jail.Center.X + (slot - (playerCount - 1) / 2f) * spacing,
            Jail.Center.Y);
    }

    private static IEnumerable<(int X, int Y)> EnumerateSpawnRing(int ring)
    {
        if (ring == 0)
        {
            yield return (0, 0);
            yield break;
        }

        // Prefer the station frontage before searching above or below it.
        yield return (-ring, 0);
        yield return (ring, 0);
        for (var y = 1; y < ring; y++)
        {
            yield return (-ring, y);
            yield return (ring, y);
            yield return (-ring, -y);
            yield return (ring, -y);
        }
        for (var x = -ring; x <= ring; x++)
        {
            yield return (x, ring);
            yield return (x, -ring);
        }
    }

    private void AddLabelMeCentralColliders()
    {
        // These polygons were authored in LabelMe against a 1280x1920 preview.
        // LabelMeCollisionData converts them x2 into the new 2560x3840 world.
        PoliceStation = AddLabelMeBuilding("PoliceStation", "police_station");
        Jail = AddLabelMeBuilding("Jail", "jail");
        AddLabelMeBuilding("CoffeeShop", "cafe");
        AddLabelMeBuilding("Houses", "houses");
        AddLabelMeBuilding("Store", "store");
        AddLabelMeBuilding("Bank", "bank");
        AddLabelMeBuilding("DonutShop", "donut");
        AddLabelMeBuilding("HelipadBuilding", "heli");
        AddLabelMeBuilding("BurgerShop", "burger");

        AddLabelMeObstacle("police_car", blocksVision: true);
        AddLabelMeObstacle("tower", blocksVision: true);
        AddLabelMeObstacle("display", blocksVision: true);
        AddLabelMeObstacle("bench1");
        AddLabelMeObstacle("bench2");
        AddLabelMeObstacle("bench3");
        AddLabelMeObstacle("vacant_lot", blocksVision: true);
        AddLabelMeObstacle("box", blocksVision: true);
        AddLabelMeObstacle("cart", blocksVision: true);

        for (var index = 1; index <= 11; index++)
        {
            // Keep small street furniture from changing arrest line-of-sight rules.
            AddLabelMeObstacle($"obastacle{index}");
        }
    }

    public void GetNearbyObstacles(float x, float y, float radius, List<Obstacle> results)
    {
        results.Clear();

        var minCellX = GetCellCoordinate(x - radius);
        var maxCellX = GetCellCoordinate(x + radius);
        var minCellY = GetCellCoordinate(y - radius);
        var maxCellY = GetCellCoordinate(y + radius);

        for (var cellY = minCellY; cellY <= maxCellY; cellY++)
        {
            for (var cellX = minCellX; cellX <= maxCellX; cellX++)
            {
                if (_obstaclesByCell.TryGetValue((cellX, cellY), out var obstacles))
                {
                    foreach (var obstacle in obstacles)
                    {
                        // Large objects occupy more than one cell. Keep the caller's
                        // reusable result list duplicate-free without shared mutable state.
                        if (!results.Contains(obstacle))
                        {
                            results.Add(obstacle);
                        }
                    }
                }
            }
        }
    }

    public bool IsMovementPositionBlocked(
        float x,
        float y,
        float radius,
        List<Obstacle> nearbyObstacles)
    {
        if (x - radius < 0f ||
            x + radius > Width ||
            y - radius < 0f ||
            y + radius > Height)
        {
            return true;
        }

        foreach (var building in Buildings)
        {
            if (building.BlocksMovement && IsCircleCollidingWithBuilding(x, y, radius, building))
            {
                return true;
            }
        }

        GetNearbyObstacles(x, y, radius, nearbyObstacles);
        foreach (var obstacle in nearbyObstacles)
        {
            if (obstacle.BlocksMovement && IsCircleCollidingWithObstacle(x, y, radius, obstacle))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCircleInsidePlayableBoundary(float x, float y, float radius)
    {
        if (!IsPointInsidePlayableBoundary(x, y))
        {
            return false;
        }

        var radiusSquared = radius * radius;
        for (var index = 0; index < PlayableBoundary.Length; index++)
        {
            var start = PlayableBoundary[index];
            var end = PlayableBoundary[(index + 1) % PlayableBoundary.Length];
            if (GetDistanceSquaredToSegment(x, y, start, end) < radiusSquared)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPointInsidePlayableBoundary(float x, float y)
    {
        var inside = false;
        for (var index = 0; index < PlayableBoundary.Length; index++)
        {
            var current = PlayableBoundary[index];
            var previous = PlayableBoundary[(index + PlayableBoundary.Length - 1) % PlayableBoundary.Length];
            if ((current.Y > y) == (previous.Y > y))
            {
                continue;
            }

            var intersectionX =
                ((previous.X - current.X) * (y - current.Y) / (previous.Y - current.Y)) + current.X;
            if (x < intersectionX)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static float GetDistanceSquaredToSegment(float x, float y, PointF start, PointF end)
    {
        var segmentX = end.X - start.X;
        var segmentY = end.Y - start.Y;
        var lengthSquared = segmentX * segmentX + segmentY * segmentY;
        if (lengthSquared <= float.Epsilon)
        {
            var pointX = x - start.X;
            var pointY = y - start.Y;
            return pointX * pointX + pointY * pointY;
        }

        var projection = Math.Clamp(
            ((x - start.X) * segmentX + (y - start.Y) * segmentY) / lengthSquared,
            0f,
            1f);
        var closestX = start.X + projection * segmentX;
        var closestY = start.Y + projection * segmentY;
        var distanceX = x - closestX;
        var distanceY = y - closestY;
        return distanceX * distanceX + distanceY * distanceY;
    }

    public static bool IsCircleCollidingWithObstacle(float x, float y, float radius, Obstacle obstacle)
    {
        if (obstacle.Type == "Polygon")
        {
            return GetDistanceSquaredToPolygon(x, y, obstacle.PolygonPoints) <= radius * radius;
        }

        if (obstacle.Type == "Rect")
        {
            var closestX = Math.Clamp(x, obstacle.LeftTop.X, obstacle.RightBottom.X);
            var closestY = Math.Clamp(y, obstacle.LeftTop.Y, obstacle.RightBottom.Y);
            var distanceX = x - closestX;
            var distanceY = y - closestY;
            return distanceX * distanceX + distanceY * distanceY < radius * radius;
        }

        if (obstacle.Type == "Circle")
        {
            if (obstacle.EffectiveRadiusY != obstacle.Radius)
                return GetDistanceSquaredToEllipse(x, y, obstacle.CenterX, obstacle.Radius, obstacle.EffectiveRadiusY) < radius * radius;
            var distanceX = x - obstacle.CenterX.X;
            var distanceY = y - obstacle.CenterX.Y;
            var combinedRadius = radius + obstacle.Radius;
            return distanceX * distanceX + distanceY * distanceY < combinedRadius * combinedRadius;
        }

        return false;
    }

    // A source circle may be scaled slightly differently in X/Y by the authored sprite rectangle.
    // Use its exact affine ellipse instead of changing the requested source radius or expanding its AABB.
    public static float GetDistanceSquaredToEllipse(float x, float y, PointF center, float radiusX, float radiusY)
    {
        if (radiusX <= 0 || radiusY <= 0) return float.PositiveInfinity;
        var px = Math.Abs((double)x - center.X); var py = Math.Abs((double)y - center.Y);
        var a2 = (double)radiusX * radiusX; var b2 = (double)radiusY * radiusY;
        if (px * px / a2 + py * py / b2 <= 1) return 0;
        // The closest exterior point solves a monotone Lagrange-multiplier equation.
        var low = 0d; var high = 2 * Math.Max(radiusX * px, radiusY * py);
        for (var i = 0; i < 40; i++)
        {
            var t = (low + high) / 2;
            var nx = radiusX * px / (t + a2); var ny = radiusY * py / (t + b2);
            if (nx * nx + ny * ny > 1) low = t;
            else high = t;
        }
        var lambda = (low + high) / 2;
        var dx = px - a2 * px / (lambda + a2); var dy = py - b2 * py / (lambda + b2);
        return (float)(dx * dx + dy * dy);
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
        if (obstacle.Type == "Polygon")
        {
            return IsPointInPolygon(x, y, obstacle.PolygonPoints);
        }

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
            return dx * dx / (obstacle.Radius * obstacle.Radius) +
                dy * dy / (obstacle.EffectiveRadiusY * obstacle.EffectiveRadiusY) <= 1;
        }

        return false;
    }

    private void AddCanonicalBuildingColliders()
    {
        AddBuilding(
            "DonutShop",
            "generated_map_assets/buildings/donut_shop.png",
            3610f,
            1103f,
            786f,
            580f,
            rotationDegrees: 7f,
            collisionWidth: 550f,
            collisionHeight: 260f,
            collisionOffsetY: 115f);

        AddBuilding(
            "HelipadBuilding",
            "generated_map_assets/buildings/helipad_building.png",
            4368f,
            1769f,
            946f,
            740f,
            rotationDegrees: 5f,
            collisionWidth: 680f,
            collisionHeight: 350f,
            collisionOffsetY: 145f);

        AddBuilding(
            "BurgerShop",
            "generated_map_assets/buildings/burger_shop.png",
            3985.5f,
            2681f,
            961f,
            828f,
            rotationDegrees: 5f,
            collisionWidth: 680f,
            collisionHeight: 360f,
            collisionOffsetY: 180f);

        AddBuilding(
            "CoffeeShop",
            "generated_map_assets/buildings/coffee_shop.png",
            950.5f,
            3341f,
            925f,
            730f,
            rotationDegrees: -7f,
            collisionWidth: 620f,
            collisionHeight: 300f,
            collisionOffsetY: 160f);

        AddBuilding(
            "PawnShop",
            "generated_map_assets/buildings/pawn_shop.png",
            2973f,
            3963.5f,
            766f,
            771f,
            rotationDegrees: -7f,
            collisionWidth: 520f,
            collisionHeight: 300f,
            collisionOffsetY: 175f);

        AddBuilding(
            "Bank",
            "generated_map_assets/buildings/bank.png",
            4193.5f,
            4554.5f,
            1193f,
            781f,
            rotationDegrees: 7f,
            collisionWidth: 820f,
            collisionHeight: 360f,
            collisionOffsetY: 165f);

        AddBuilding(
            "HouseBlue",
            "generated_map_assets/buildings/house_blue.png",
            900f,
            4780f,
            520f,
            600f,
            rotationDegrees: -8f,
            collisionWidth: 330f,
            collisionHeight: 230f,
            collisionOffsetY: 135f);

        AddBuilding(
            "HouseOrange",
            "generated_map_assets/buildings/house_orange.png",
            1375f,
            4770f,
            520f,
            590f,
            rotationDegrees: 7f,
            collisionWidth: 340f,
            collisionHeight: 230f,
            collisionOffsetY: 130f);

    }

    private void AddCanonicalObstacleColliders()
    {
        const string obstacleRoot = "generated_map_assets/obstacles/";

        // The police car is a baked visual in the canonical image, but still
        // needs its own invisible physics footprint.
        AddRectObstacleWorld(
            "police_car",
            1197f,
            2252f,
            340f,
            145f,
            blocksVision: true);

        AddCircleObstacleWorld(
            obstacleRoot + "pond.png",
            2466.5f,
            2624.5f,
            175f,
            renderWidth: 535f,
            renderHeight: 385f,
            blocksVision: false);

        AddTrafficCone(3017f, 1314f);
        AddTrafficCone(3122f, 1638f);
        AddTrafficCone(3009f, 2105f);
        AddTrafficCone(1583f, 3460f);

        AddRectObstacleWorld(
            obstacleRoot + "bench.png",
            1996f,
            2807f,
            180f,
            65f,
            renderWidth: 300f,
            renderHeight: 170f,
            renderOffsetY: -38f,
            blocksVision: false);
        AddRectObstacleWorld(
            obstacleRoot + "bench.png",
            2688f,
            2971f,
            180f,
            65f,
            renderWidth: 300f,
            renderHeight: 170f,
            renderOffsetY: -38f,
            blocksVision: false);

        AddStreetLamp(1819f, 2527f);
        AddStreetLamp(2747f, 2409f);
        AddStreetLamp(3423f, 2676f);
        AddStreetLamp(3089f, 3249f);
        AddStreetLamp(2528f, 3925f);
        AddStreetLamp(2479f, 4344f);
        AddStreetLamp(2125f, 5161f);

        AddRectObstacleWorld(
            obstacleRoot + "traffic_light.png",
            3170f,
            3330f,
            70f,
            70f,
            renderWidth: 140f,
            renderHeight: 330f,
            renderOffsetY: -130f,
            blocksVision: false);
        AddRectObstacleWorld(
            obstacleRoot + "traffic_light.png",
            1945f,
            2810f,
            70f,
            70f,
            renderWidth: 140f,
            renderHeight: 330f,
            renderOffsetY: -130f,
            blocksVision: false);

        AddCircleObstacleWorld(
            obstacleRoot + "parasol.png",
            3260f,
            1535f,
            48f,
            renderWidth: 210f,
            renderHeight: 300f,
            renderOffsetY: -105f,
            blocksVision: true);
        AddCircleObstacleWorld(
            obstacleRoot + "table.png",
            3340f,
            1590f,
            82f,
            renderWidth: 175f,
            renderHeight: 180f,
            renderOffsetY: -28f,
            blocksVision: false);
        AddCircleObstacleWorld(
            obstacleRoot + "chair.png",
            3210f,
            1615f,
            38f,
            renderWidth: 92f,
            renderHeight: 135f,
            renderOffsetY: -42f,
            blocksVision: false);
        AddCircleObstacleWorld(
            obstacleRoot + "chair.png",
            3450f,
            1575f,
            38f,
            renderWidth: 92f,
            renderHeight: 135f,
            renderOffsetY: -42f,
            blocksVision: false);

        AddMailbox(4420f, 3020f);
        AddMailbox(3710f, 4740f);

        AddJunkyardObstacles(obstacleRoot);
    }

    private void AddTrafficCone(float centerX, float centerY)
    {
        AddCircleObstacleWorld(
            "generated_map_assets/obstacles/traffic_cone.png",
            centerX,
            centerY,
            34f,
            renderWidth: 90f,
            renderHeight: 130f,
            renderOffsetY: -40f,
            blocksVision: false);
    }

    private void AddStreetLamp(float centerX, float centerY)
    {
        AddCircleObstacleWorld(
            "generated_map_assets/obstacles/street_lamp.png",
            centerX,
            centerY,
            34f,
            renderWidth: 95f,
            renderHeight: 360f,
            renderOffsetY: -150f,
            blocksVision: false);
    }

    private void AddMailbox(float centerX, float centerY)
    {
        AddCircleObstacleWorld(
            "generated_map_assets/obstacles/mailbox.png",
            centerX,
            centerY,
            38f,
            renderWidth: 105f,
            renderHeight: 175f,
            renderOffsetY: -58f,
            blocksVision: false);
    }

    private void AddJunkyardObstacles(string obstacleRoot)
    {
        AddRectObstacleWorld(
            obstacleRoot + "fence_left_down_180.png",
            1510f,
            5530f,
            390f,
            85f,
            renderWidth: 520f,
            renderHeight: 250f,
            renderOffsetY: -68f);
        AddRectObstacleWorld(
            obstacleRoot + "fence_right_down_0.png",
            1940f,
            5400f,
            460f,
            90f,
            renderWidth: 560f,
            renderHeight: 240f,
            renderOffsetY: -65f);
        AddRectObstacleWorld(
            obstacleRoot + "fence_left_down_270.png",
            2600f,
            5830f,
            100f,
            520f,
            renderWidth: 300f,
            renderHeight: 580f,
            renderOffsetX: -55f,
            renderOffsetY: -35f);
        AddRectObstacleWorld(
            obstacleRoot + "fence_right_down_180.png",
            2220f,
            6430f,
            610f,
            90f,
            renderWidth: 680f,
            renderHeight: 260f,
            renderOffsetY: -72f);

        AddRectObstacleWorld(
            obstacleRoot + "wall.png",
            1380f,
            5960f,
            90f,
            520f,
            renderWidth: 280f,
            renderHeight: 590f,
            renderOffsetX: 45f,
            renderOffsetY: -35f);
        AddRectObstacleWorld(
            obstacleRoot + "wall.png",
            2670f,
            6220f,
            90f,
            360f,
            renderWidth: 260f,
            renderHeight: 430f,
            renderOffsetX: -40f,
            renderOffsetY: -25f);
        AddRectObstacleWorld(
            obstacleRoot + "yellow_safety_fence.png",
            1970f,
            6520f,
            430f,
            75f,
            renderWidth: 500f,
            renderHeight: 210f,
            renderOffsetY: -58f,
            blocksVision: false);

        AddWoodenBox(1650f, 5870f, 165f);
        AddWoodenBox(2020f, 5710f, 150f);
        AddWoodenBox(2290f, 6030f, 170f);
        AddWoodenBox(1900f, 6240f, 155f);
        AddWoodenBox(2430f, 6320f, 145f);

        AddCircleObstacleWorld(
            obstacleRoot + "stacked_tires.png",
            2420f,
            5670f,
            70f,
            renderWidth: 180f,
            renderHeight: 190f,
            renderOffsetY: -45f);

        AddTrafficCone(1460f, 6420f);
        AddTrafficCone(2740f, 6380f);
    }

    private void AddWoodenBox(float centerX, float centerY, float renderSize)
    {
        AddRectObstacleWorld(
            "generated_map_assets/obstacles/wooden_box.png",
            centerX,
            centerY,
            92f,
            92f,
            renderWidth: renderSize,
            renderHeight: renderSize,
            renderOffsetY: -(renderSize - 92f) / 2f,
            blocksVision: true);
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

    public static bool IsPointInBuilding(float x, float y, MapBuilding building)
    {
        if (building.CollisionPolygon.Length >= 3)
        {
            return IsPointInPolygon(x, y, building.CollisionPolygon);
        }

        var localPoint = ToBuildingCollisionLocalPoint(x, y, building);
        return localPoint.X >= -building.EffectiveCollisionWidth / 2f &&
               localPoint.X <= building.EffectiveCollisionWidth / 2f &&
               localPoint.Y >= -building.EffectiveCollisionHeight / 2f &&
               localPoint.Y <= building.EffectiveCollisionHeight / 2f;
    }

    public static bool IsCircleCollidingWithBuilding(float x, float y, float radius, MapBuilding building) =>
        GetDistanceSquaredToBuilding(x, y, building) < radius * radius;

    public static float GetDistanceSquaredToBuilding(float x, float y, MapBuilding building)
    {
        if (building.CollisionPolygon.Length >= 3)
        {
            return GetDistanceSquaredToPolygon(x, y, building.CollisionPolygon);
        }

        var localPoint = ToBuildingCollisionLocalPoint(x, y, building);
        var halfWidth = building.EffectiveCollisionWidth / 2f;
        var halfHeight = building.EffectiveCollisionHeight / 2f;
        var closestX = Math.Clamp(localPoint.X, -halfWidth, halfWidth);
        var closestY = Math.Clamp(localPoint.Y, -halfHeight, halfHeight);
        var distanceX = localPoint.X - closestX;
        var distanceY = localPoint.Y - closestY;

        return distanceX * distanceX + distanceY * distanceY;
    }

    public static bool IsPointInPolygon(float x, float y, IReadOnlyList<PointF> polygon)
    {
        if (polygon.Count < 3)
        {
            return false;
        }

        var inside = false;
        for (var index = 0; index < polygon.Count; index++)
        {
            var current = polygon[index];
            var previous = polygon[(index + polygon.Count - 1) % polygon.Count];

            // Treat points on an authored edge as inside the collision area.
            if (GetDistanceSquaredToSegment(x, y, previous, current) <= 0.0001f)
            {
                return true;
            }

            if ((current.Y > y) == (previous.Y > y))
            {
                continue;
            }

            var intersectionX =
                ((previous.X - current.X) * (y - current.Y) / (previous.Y - current.Y)) + current.X;
            if (x < intersectionX)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    public static float GetDistanceSquaredToPolygon(float x, float y, IReadOnlyList<PointF> polygon)
    {
        if (polygon.Count < 3)
        {
            return float.PositiveInfinity;
        }

        if (IsPointInPolygon(x, y, polygon))
        {
            return 0f;
        }

        var minimumDistanceSquared = float.PositiveInfinity;
        for (var index = 0; index < polygon.Count; index++)
        {
            var start = polygon[index];
            var end = polygon[(index + 1) % polygon.Count];
            minimumDistanceSquared = MathF.Min(
                minimumDistanceSquared,
                GetDistanceSquaredToSegment(x, y, start, end));
        }

        return minimumDistanceSquared;
    }

    public static PointF ToBuildingLocalPoint(float x, float y, MapBuilding building)
    {
        var angle = -building.RotationDegrees * MathF.PI / 180f;
        var cos = MathF.Cos(angle);
        var sin = MathF.Sin(angle);
        var dx = x - building.Center.X;
        var dy = y - building.Center.Y;

        return new PointF(
            dx * cos - dy * sin,
            dx * sin + dy * cos);
    }

    public static PointF ToBuildingCollisionLocalPoint(float x, float y, MapBuilding building)
    {
        var localPoint = ToBuildingLocalPoint(x, y, building);
        return new PointF(
            localPoint.X - building.CollisionOffsetX,
            localPoint.Y - building.CollisionOffsetY);
    }

    public static PointF[] GetBuildingCorners(MapBuilding building)
    {
        var halfWidth = building.Width / 2f;
        var halfHeight = building.Height / 2f;

        return
        [
            RotateBuildingOffset(building, -halfWidth, -halfHeight),
            RotateBuildingOffset(building, halfWidth, -halfHeight),
            RotateBuildingOffset(building, halfWidth, halfHeight),
            RotateBuildingOffset(building, -halfWidth, halfHeight)
        ];
    }

    public static PointF[] GetBuildingCollisionCorners(MapBuilding building)
    {
        if (building.CollisionPolygon.Length >= 3)
        {
            return (PointF[])building.CollisionPolygon.Clone();
        }

        var halfWidth = building.EffectiveCollisionWidth / 2f;
        var halfHeight = building.EffectiveCollisionHeight / 2f;
        var offsetX = building.CollisionOffsetX;
        var offsetY = building.CollisionOffsetY;

        return
        [
            RotateBuildingOffset(building, offsetX - halfWidth, offsetY - halfHeight),
            RotateBuildingOffset(building, offsetX + halfWidth, offsetY - halfHeight),
            RotateBuildingOffset(building, offsetX + halfWidth, offsetY + halfHeight),
            RotateBuildingOffset(building, offsetX - halfWidth, offsetY + halfHeight)
        ];
    }

    public static (float Left, float Top, float Right, float Bottom) GetBuildingBounds(MapBuilding building)
    {
        var corners = GetBuildingCorners(building);
        var left = MathF.Min(MathF.Min(corners[0].X, corners[1].X), MathF.Min(corners[2].X, corners[3].X));
        var top = MathF.Min(MathF.Min(corners[0].Y, corners[1].Y), MathF.Min(corners[2].Y, corners[3].Y));
        var right = MathF.Max(MathF.Max(corners[0].X, corners[1].X), MathF.Max(corners[2].X, corners[3].X));
        var bottom = MathF.Max(MathF.Max(corners[0].Y, corners[1].Y), MathF.Max(corners[2].Y, corners[3].Y));

        return (left, top, right, bottom);
    }

    public static (float Left, float Top, float Right, float Bottom) GetBuildingCollisionBounds(MapBuilding building)
    {
        var corners = GetBuildingCollisionCorners(building);
        return GetPolygonBounds(corners);
    }

    private static PointF RotateBuildingOffset(MapBuilding building, float offsetX, float offsetY)
    {
        var angle = building.RotationDegrees * MathF.PI / 180f;
        var cos = MathF.Cos(angle);
        var sin = MathF.Sin(angle);

        return new PointF(
            building.Center.X + offsetX * cos - offsetY * sin,
            building.Center.Y + offsetX * sin + offsetY * cos);
    }

    private MapBuilding AddLabelMeBuilding(string type, string label)
    {
        var polygon = LabelMeCollisionData.GetWorldPolygon(label);
        var bounds = GetPolygonBounds(polygon);
        var building = new MapBuilding
        {
            Type = type,
            ImageFileName = string.Empty,
            LeftTop = new PointF(bounds.Left, bounds.Top),
            RightBottom = new PointF(bounds.Right, bounds.Bottom),
            CollisionWidth = bounds.Right - bounds.Left,
            CollisionHeight = bounds.Bottom - bounds.Top,
            CollisionPolygon = polygon,
            IsVisible = false,
            BlocksMovement = true,
            BlocksVision = true
        };

        Buildings.Add(building);
        return building;
    }

    private Obstacle AddLabelMeObstacle(string label, bool blocksVision = false)
    {
        var polygon = LabelMeCollisionData.GetWorldPolygon(label);
        var bounds = GetPolygonBounds(polygon);
        var obstacle = new Obstacle
        {
            Type = "Polygon",
            ImageFileName = label,
            LeftTop = new PointF(bounds.Left, bounds.Top),
            LeftBottom = new PointF(bounds.Left, bounds.Bottom),
            RightTop = new PointF(bounds.Right, bounds.Top),
            RightBottom = new PointF(bounds.Right, bounds.Bottom),
            PolygonPoints = polygon,
            IsVisible = false,
            BlocksMovement = true,
            BlocksVision = blocksVision
        };

        Obstacles.Add(obstacle);
        return obstacle;
    }

    private static (float Left, float Top, float Right, float Bottom) GetPolygonBounds(
        IReadOnlyList<PointF> polygon)
    {
        if (polygon.Count == 0)
        {
            throw new ArgumentException("A collision polygon must contain at least one point.", nameof(polygon));
        }

        var left = polygon[0].X;
        var top = polygon[0].Y;
        var right = polygon[0].X;
        var bottom = polygon[0].Y;

        for (var index = 1; index < polygon.Count; index++)
        {
            left = MathF.Min(left, polygon[index].X);
            top = MathF.Min(top, polygon[index].Y);
            right = MathF.Max(right, polygon[index].X);
            bottom = MathF.Max(bottom, polygon[index].Y);
        }

        return (left, top, right, bottom);
    }

    private static MapBuilding CreateMapReference(
        string type,
        string imageFileName,
        float centerX,
        float centerY,
        float width,
        float height,
        float rotationDegrees = 0f,
        float? collisionWidth = null,
        float? collisionHeight = null,
        float collisionOffsetX = 0f,
        float collisionOffsetY = 0f,
        bool visible = false,
        bool blocksMovement = true,
        bool blocksVision = true)
    {
        centerX *= CanonicalCoordinateScale;
        centerY *= CanonicalCoordinateScale;
        width *= CanonicalCoordinateScale;
        height *= CanonicalCoordinateScale;
        collisionWidth = (collisionWidth ?? width / CanonicalCoordinateScale) * CanonicalCoordinateScale;
        collisionHeight = (collisionHeight ?? height / CanonicalCoordinateScale) * CanonicalCoordinateScale;
        collisionOffsetX *= CanonicalCoordinateScale;
        collisionOffsetY *= CanonicalCoordinateScale;

        var halfWidth = width / 2f;
        var halfHeight = height / 2f;
        return new MapBuilding
        {
            Type = type,
            // Visuals come from exact original-pixel foreground tiles.  The
            // shared map owns only authoritative physics/vision geometry.
            ImageFileName = string.Empty,
            LeftTop = new PointF(centerX - halfWidth, centerY - halfHeight),
            RightBottom = new PointF(centerX + halfWidth, centerY + halfHeight),
            RotationDegrees = rotationDegrees,
            CollisionWidth = collisionWidth.Value,
            CollisionHeight = collisionHeight.Value,
            CollisionOffsetX = collisionOffsetX,
            CollisionOffsetY = collisionOffsetY,
            IsVisible = visible,
            BlocksMovement = blocksMovement,
            BlocksVision = blocksVision
        };
    }

    private MapBuilding AddBuilding(
        string type,
        string imageFileName,
        float centerX,
        float centerY,
        float width,
        float height,
        float rotationDegrees = 0f,
        float? collisionWidth = null,
        float? collisionHeight = null,
        float collisionOffsetX = 0f,
        float collisionOffsetY = 0f,
        bool visible = false,
        bool blocksMovement = true,
        bool blocksVision = true)
    {
        var building = CreateMapReference(
            type,
            imageFileName,
            centerX,
            centerY,
            width,
            height,
            rotationDegrees,
            collisionWidth,
            collisionHeight,
            collisionOffsetX,
            collisionOffsetY,
            visible,
            blocksMovement,
            blocksVision);

        Buildings.Add(building);
        return building;
    }

    private void AddOuterLandmarkColliders()
    {
        // This unannotated storage pocket sits inside a bend of the authored
        // outer contour. Water-tower and billboard geometry now comes from LabelMe.
        AddRectObstacleWorld(
            "baked_upper_left_storage",
            300f,
            850f,
            520f,
            500f,
            visible: false);
    }

    private void BuildSpatialIndex()
    {
        _obstaclesByCell.Clear();
        _bushes.Clear();

        foreach (var obstacle in Obstacles)
        {
            if (IsBushObstacle(obstacle))
            {
                _bushes.Add(obstacle);
            }

            var bounds = GetObstacleBounds(obstacle);
            var minCellX = GetCellCoordinate(bounds.Left);
            var maxCellX = GetCellCoordinate(bounds.Right);
            var minCellY = GetCellCoordinate(bounds.Top);
            var maxCellY = GetCellCoordinate(bounds.Bottom);

            for (var cellY = minCellY; cellY <= maxCellY; cellY++)
            {
                for (var cellX = minCellX; cellX <= maxCellX; cellX++)
                {
                    var cell = (cellX, cellY);
                    if (!_obstaclesByCell.TryGetValue(cell, out var cellObstacles))
                    {
                        cellObstacles = new List<Obstacle>();
                        _obstaclesByCell[cell] = cellObstacles;
                    }

                    cellObstacles.Add(obstacle);
                }
            }
        }
    }

    public static (float Left, float Top, float Right, float Bottom) GetObstacleBounds(Obstacle obstacle)
    {
        if (obstacle.Type == "Polygon")
        {
            return GetPolygonBounds(obstacle.PolygonPoints);
        }

        if (obstacle.Type == "Circle")
        {
            return (
                obstacle.CenterX.X - obstacle.Radius,
                obstacle.CenterX.Y - obstacle.EffectiveRadiusY,
                obstacle.CenterX.X + obstacle.Radius,
                obstacle.CenterX.Y + obstacle.EffectiveRadiusY);
        }

        return (
            obstacle.LeftTop.X,
            obstacle.LeftTop.Y,
            obstacle.RightBottom.X,
            obstacle.RightBottom.Y);
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
        AddRectObstacleWorld(
            imageFileName,
            centerX,
            centerY,
            size,
            size,
            blocksMovement: imageFileName != "bush.png");
    }

    private Obstacle AddRectObstacleWorld(
        string imageFileName,
        float centerX,
        float centerY,
        float width,
        float height,
        float? renderWidth = null,
        float? renderHeight = null,
        float renderOffsetX = 0f,
        float renderOffsetY = 0f,
        bool visible = false,
        bool blocksMovement = true,
        bool blocksVision = true)
    {
        centerX *= CanonicalCoordinateScale;
        centerY *= CanonicalCoordinateScale;
        width *= CanonicalCoordinateScale;
        height *= CanonicalCoordinateScale;
        renderWidth = (renderWidth ?? width / CanonicalCoordinateScale) * CanonicalCoordinateScale;
        renderHeight = (renderHeight ?? height / CanonicalCoordinateScale) * CanonicalCoordinateScale;
        renderOffsetX *= CanonicalCoordinateScale;
        renderOffsetY *= CanonicalCoordinateScale;

        var halfWidth = width / 2f;
        var halfHeight = height / 2f;

        var obstacle = new Obstacle
        {
            Type = "Rect",
            ImageFileName = string.Empty,
            LeftTop = new PointF(centerX - halfWidth, centerY - halfHeight),
            LeftBottom = new PointF(centerX - halfWidth, centerY + halfHeight),
            RightTop = new PointF(centerX + halfWidth, centerY - halfHeight),
            RightBottom = new PointF(centerX + halfWidth, centerY + halfHeight),
            RenderWidth = renderWidth.Value,
            RenderHeight = renderHeight.Value,
            RenderOffsetX = renderOffsetX,
            RenderOffsetY = renderOffsetY,
            IsVisible = visible,
            BlocksMovement = blocksMovement,
            BlocksVision = blocksVision
        };

        Obstacles.Add(obstacle);
        return obstacle;
    }

    private void AddCircleObstacle(string imageFileName, float layoutCenterX, float layoutCenterY, float radius)
    {
        AddCircleObstacleWorld(
            imageFileName,
            layoutCenterX * LayoutScale,
            layoutCenterY * LayoutScale,
            radius);
    }

    private Obstacle AddCircleObstacleWorld(
        string imageFileName,
        float centerX,
        float centerY,
        float radius,
        float? renderWidth = null,
        float? renderHeight = null,
        float renderOffsetX = 0f,
        float renderOffsetY = 0f,
        bool visible = false,
        bool blocksMovement = true,
        bool blocksVision = true)
    {
        centerX *= CanonicalCoordinateScale;
        centerY *= CanonicalCoordinateScale;
        radius *= CanonicalCoordinateScale;
        renderWidth = (renderWidth ?? radius * 2f / CanonicalCoordinateScale) * CanonicalCoordinateScale;
        renderHeight = (renderHeight ?? radius * 2f / CanonicalCoordinateScale) * CanonicalCoordinateScale;
        renderOffsetX *= CanonicalCoordinateScale;
        renderOffsetY *= CanonicalCoordinateScale;

        var obstacle = new Obstacle
        {
            Type = "Circle",
            ImageFileName = string.Empty,
            CenterX = new PointF(centerX, centerY),
            Radius = radius,
            RenderWidth = renderWidth.Value,
            RenderHeight = renderHeight.Value,
            RenderOffsetX = renderOffsetX,
            RenderOffsetY = renderOffsetY,
            IsVisible = visible,
            BlocksMovement = blocksMovement,
            BlocksVision = blocksVision
        };

        Obstacles.Add(obstacle);
        return obstacle;
    }
}

public readonly record struct MapPropLayout(
    string AssetPath,
    float CenterX,
    float CenterY,
    float Width,
    float Height,
    bool IsTriangular = false,
    string CollisionShape = "Rect",
    float CollisionWidth = 0f,
    float CollisionHeight = 0f,
    float CollisionOffsetX = 0f,
    float CollisionOffsetY = 0f,
    float CollisionRadius = 0f,
    string? BuildingType = null,
    bool BlocksMovement = true,
    float CollisionRadiusY = 0f,
    PointF[]? CollisionPolygon = null);

public class MapBuilding
{
    public string Type { get; set; } = string.Empty;
    public string ImageFileName { get; set; } = string.Empty;
    public PointF LeftTop { get; set; }
    public PointF RightBottom { get; set; }
    public float RotationDegrees { get; set; }
    public float CollisionWidth { get; set; }
    public float CollisionHeight { get; set; }
    public float CollisionOffsetX { get; set; }
    public float CollisionOffsetY { get; set; }
    public PointF[] CollisionPolygon { get; set; } = [];
    public bool IsVisible { get; set; } = true;
    public bool BlocksMovement { get; set; } = true;
    public bool BlocksVision { get; set; } = true;
    public float Width => RightBottom.X - LeftTop.X;
    public float Height => RightBottom.Y - LeftTop.Y;
    public float EffectiveCollisionWidth => CollisionWidth > 0f ? CollisionWidth : Width;
    public float EffectiveCollisionHeight => CollisionHeight > 0f ? CollisionHeight : Height;
    public PointF Center => new((LeftTop.X + RightBottom.X) / 2f, (LeftTop.Y + RightBottom.Y) / 2f);
    public PointF CollisionCenter
    {
        get
        {
            if (CollisionPolygon.Length >= 3)
            {
                var x = 0f;
                var y = 0f;
                foreach (var point in CollisionPolygon)
                {
                    x += point.X;
                    y += point.Y;
                }

                return new PointF(x / CollisionPolygon.Length, y / CollisionPolygon.Length);
            }

            var angle = RotationDegrees * MathF.PI / 180f;
            var cos = MathF.Cos(angle);
            var sin = MathF.Sin(angle);
            return new PointF(
                Center.X + CollisionOffsetX * cos - CollisionOffsetY * sin,
                Center.Y + CollisionOffsetX * sin + CollisionOffsetY * cos);
        }
    }
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
    public PointF[] PolygonPoints { get; set; } = [];
    public float Radius { get; set; }
    public float RadiusY { get; set; }
    public float EffectiveRadiusY => RadiusY > 0f ? RadiusY : Radius;
    public float RenderWidth { get; set; }
    public float RenderHeight { get; set; }
    public float RenderOffsetX { get; set; }
    public float RenderOffsetY { get; set; }
    public bool IsVisible { get; set; } = true;
    public bool BlocksMovement { get; set; } = true;
    public bool BlocksVision { get; set; } = true;
    public float Width => RightBottom.X - LeftTop.X;
    public float Height => RightBottom.Y - LeftTop.Y;
    public PointF Center => Type == "Circle"
        ? CenterX
        : new PointF((LeftTop.X + RightBottom.X) / 2f, (LeftTop.Y + RightBottom.Y) / 2f);
    public PointF RenderCenter => new(Center.X + RenderOffsetX, Center.Y + RenderOffsetY);
}
