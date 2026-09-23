using System.Drawing;
using NUnit.Framework;
using polrob.Shared;

namespace polrob.Server.Tests;

public sealed class CanvaMapCollisionProfileTests
{
    [TestCase("police_station.png", 764, 700, 565, 2, 2, 762, 698)]
    [TestCase("donut.png", 880, 850, 700, 0, 0, 880, 850)]
    [TestCase("cafe.png", 926, 969, 875, 1, 0, 925, 967)]
    [TestCase("house-orange.png", 975, 960, 825, 2, 3, 975, 959)]
    [TestCase("burger.png", 1053, 1058, 885, 0, 0, 1053, 1055)]
    [TestCase("jail.png", 979, 858, 600, 0, 0, 979, 856)]
    [TestCase("warehouse.png", 951, 1000, 900, 4, 4, 948, 992)]
    [TestCase("box.png", 1044, 1199, 1199, 7, 8, 1037, 1190)]
    public void RectanglesStartAtOriginalBottomLeftIncludingTransparentMargins(
        string file, int sourceWidth, int sourceHeight, int height, int cropLeft, int cropTop, int cropRight, int cropBottom)
    {
        var map = new GameMap(useLegacyCanvaMap: true);
        foreach (var prop in CanvaMapLayout.Props.Where(p => p.AssetPath == "MapAssets/" + file))
        {
            var sx = prop.Width / (cropRight - cropLeft); var sy = prop.Height / (cropBottom - cropTop);
            var left = prop.CenterX - prop.Width / 2 - cropLeft * sx;
            var top = prop.CenterY - prop.Height / 2 + (sourceHeight - height - cropTop) * sy;
            var right = left + sourceWidth * sx;
            var bottom = prop.CenterY - prop.Height / 2 + (sourceHeight - cropTop) * sy;
            var actual = prop.BuildingType != null
                ? GameMap.GetBuildingCollisionBounds(map.Buildings.Single(b => b.Type == prop.BuildingType))
                : GameMap.GetObstacleBounds(map.Obstacles.Single(o => o.ImageFileName == prop.AssetPath &&
                    Math.Abs(o.Center.X - (left + right) / 2) < .01f && Math.Abs(o.Center.Y - (top + bottom) / 2) < .01f));
            Assert.Multiple(() =>
            {
                Assert.That(actual.Left, Is.EqualTo(left).Within(.001f));
                Assert.That(actual.Top, Is.EqualTo(top).Within(.001f));
                Assert.That(actual.Right, Is.EqualTo(right).Within(.001f));
                Assert.That(actual.Bottom, Is.EqualTo(bottom).Within(.001f));
            });
            if (prop.BuildingType != null)
            {
                var building = map.Buildings.Single(b => b.Type == prop.BuildingType);
                Assert.That(GameMap.IsCircleCollidingWithBuilding(prop.CenterX, top - 2, 1, building), Is.False);
                Assert.That(GameMap.IsCircleCollidingWithBuilding(prop.CenterX, bottom - 2, 1, building), Is.True);
                Assert.That(GameMap.IsCircleCollidingWithBuilding(prop.CenterX, bottom + 2, 1, building), Is.False);
            }
        }
    }

    [TestCase("tree.png", 1009, 1080, 150, 150, 10, 6, 1001, 1073)]
    [TestCase("streetlamp.png", 606, 1202, 131, 131, 4, 3, 601, 1192)]
    [TestCase("rock.png", 1251, 1121, 560.5f, 560, 8, 8, 1245, 1113)]
    [TestCase("bush.png", 791, 777, 388.5f, 390, 2, 0, 791, 777)]
    public void CirclesUseTheOriginalCenterAndBothSpriteScaleFactors(
        string file, int sourceWidth, int sourceHeight, float centerY, float radius,
        int cropLeft, int cropTop, int cropRight, int cropBottom)
    {
        var map = new GameMap(useLegacyCanvaMap: true);
        foreach (var prop in CanvaMapLayout.Props.Where(p => p.AssetPath == "MapAssets/" + file))
        {
            var sx = prop.Width / (cropRight - cropLeft); var sy = prop.Height / (cropBottom - cropTop);
            var x = prop.CenterX - prop.Width / 2 + (sourceWidth / 2f - cropLeft) * sx;
            var y = prop.CenterY - prop.Height / 2 + (sourceHeight - centerY - cropTop) * sy;
            var obstacle = map.Obstacles.Single(o => o.ImageFileName == prop.AssetPath &&
                Math.Abs(o.Center.X - x) < .01f && Math.Abs(o.Center.Y - y) < .01f);
            Assert.Multiple(() =>
            {
                Assert.That(obstacle.Type, Is.EqualTo("Circle"));
                Assert.That(obstacle.Radius, Is.EqualTo(radius * sx).Within(.001f));
                Assert.That(obstacle.EffectiveRadiusY, Is.EqualTo(radius * sy).Within(.001f));
                Assert.That(GameMap.IsCircleCollidingWithObstacle(x, y, 1, obstacle), Is.True);
                Assert.That(GameMap.IsCircleCollidingWithObstacle(x + radius * sx + 3, y, 1, obstacle), Is.False);
                Assert.That(GameMap.IsCircleCollidingWithObstacle(x, y - radius * sy - 3, 1, obstacle), Is.False);
            });
            var bounds = GameMap.GetObstacleBounds(obstacle);
            Assert.That(bounds.Bottom, Is.EqualTo(y + radius * sy).Within(.001f));
            Assert.That(bounds.Top, Is.EqualTo(y - radius * sy).Within(.001f));
        }
    }

    [TestCase("tree.png")]
    [TestCase("streetlamp.png")]
    public void CrownAndLampHeadAreOutsideTheBaseCollider(string file)
    {
        var map = new GameMap(useLegacyCanvaMap: true);
        foreach (var prop in CanvaMapLayout.Props.Where(p => p.AssetPath == "MapAssets/" + file))
        {
            var shape = map.Obstacles.Single(o => o.ImageFileName == prop.AssetPath &&
                Math.Abs(o.Center.X - prop.CenterX - prop.CollisionOffsetX) < .01f &&
                Math.Abs(o.Center.Y - prop.CenterY - prop.CollisionOffsetY) < .01f);
            Assert.That(GameMap.IsCircleCollidingWithObstacle(prop.CenterX, prop.CenterY - prop.Height * .25f, 5, shape), Is.False);
        }
    }

    [Test]
    public void TracedBoxesKeepTheUpperRightNotchOpen()
    {
        var prop = CanvaMapLayout.Props.Single(p => p.AssetPath == "MapAssets/boxes.png");
        var image = CanvaMapCollisions.Profiles["boxes.png"].Image;
        var obstacle = new GameMap(useLegacyCanvaMap: true).Obstacles.Single(o => o.ImageFileName == prop.AssetPath);
        var empty = image.ToWorld(prop, new(800, 900));
        var solid = image.ToWorld(prop, new(250, 850));
        Assert.That(obstacle.Type, Is.EqualTo("Polygon"));
        Assert.That(GameMap.IsCircleCollidingWithObstacle(empty.X, empty.Y, 3, obstacle), Is.False);
        Assert.That(GameMap.IsCircleCollidingWithObstacle(solid.X, solid.Y, 3, obstacle), Is.True);
        var nearby = new List<Obstacle>();
        var bounds = GameMap.GetObstacleBounds(obstacle);
        new GameMap(useLegacyCanvaMap: true).GetNearbyObstacles(bounds.Right, bounds.Bottom, 5, nearby);
        Assert.That(nearby.Any(o => o.ImageFileName == prop.AssetPath), Is.True);
    }

    [Test]
    public void PondBlocksWaterAndRimButNotTheImageCorners()
    {
        var prop = CanvaMapLayout.Props.Single(p => p.AssetPath == "MapAssets/pond.png");
        var profile = CanvaMapCollisions.Profiles["pond.png"];
        var obstacle = new GameMap(useLegacyCanvaMap: true).Obstacles.Single(o => o.ImageFileName == prop.AssetPath);
        foreach (var source in new[] { new PointF(694, 420), new PointF(680, 50) })
        {
            var point = profile.Image.ToWorld(prop, source);
            Assert.That(GameMap.IsCircleCollidingWithObstacle(point.X, point.Y, 1, obstacle), Is.True);
        }
        foreach (var source in new[] { new PointF(20, 822), new PointF(1369, 20) })
        {
            var point = profile.Image.ToWorld(prop, source);
            Assert.That(GameMap.IsCircleCollidingWithObstacle(point.X, point.Y, 1, obstacle), Is.False);
        }
    }

    [TestCase(0f)]
    [TestCase(30f)]
    [TestCase(60f)]
    [TestCase(90f)]
    [TestCase(210f)]
    public void NonUniformlyScaledCircleUsesTheEllipseEdgeNotAnExpandedRectangle(float angle)
    {
        const float rx = 80, ry = 20, playerRadius = 7;
        var a = angle * MathF.PI / 180;
        var edge = new PointF(rx * MathF.Cos(a), ry * MathF.Sin(a));
        var nx = MathF.Cos(a) / rx; var ny = MathF.Sin(a) / ry;
        var length = MathF.Sqrt(nx * nx + ny * ny); nx /= length; ny /= length;
        var ellipse = new Obstacle { Type = "Circle", CenterX = new(0, 0), Radius = rx, RadiusY = ry };
        Assert.That(GameMap.IsCircleCollidingWithObstacle(edge.X + nx * (playerRadius - .01f), edge.Y + ny * (playerRadius - .01f), playerRadius, ellipse), Is.True);
        Assert.That(GameMap.IsCircleCollidingWithObstacle(edge.X + nx * (playerRadius + .01f), edge.Y + ny * (playerRadius + .01f), playerRadius, ellipse), Is.False);
        Assert.That(GameMap.GetDistanceSquaredToEllipse(edge.X + nx * 10, edge.Y + ny * 10, new(0, 0), rx, ry), Is.EqualTo(100).Within(.005f));
    }
}
