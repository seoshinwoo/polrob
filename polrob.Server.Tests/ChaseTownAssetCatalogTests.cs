using System.Buffers.Binary;
using System.Drawing;
using System.Text.Json;
using NUnit.Framework;
using polrob.Shared;

namespace polrob.Server.Tests;

public sealed class ChaseTownAssetCatalogTests
{
    private static IReadOnlyList<ChaseTownRegion> AtNative(string id,bool open=false)
    {
        var a=ChaseTownAssetCatalog.Get(id);
        return ChaseTownAssetCatalog.Place(id,new(a.Width/2f,a.Height/2f),1,gateOpen:open);
    }
    private static bool Blocked(IEnumerable<ChaseTownRegion> regions,float x,float y,float radius=1) =>
        regions.Any(r=>r.Obstacle.BlocksMovement&&GameMap.IsCircleCollidingWithObstacle(x,y,radius,r.Obstacle));

    [Test]
    public void EveryProfileHasFiniteInBoundsGeometryAndUniqueIdentity()
    {
        Assert.That(ChaseTownAssetCatalog.Assets.Select(a=>a.Id).Distinct().Count(),Is.EqualTo(ChaseTownAssetCatalog.Assets.Count));
        foreach(var a in ChaseTownAssetCatalog.Assets)
        {
            Assert.That(a.Width,Is.GreaterThan(0));Assert.That(a.Height,Is.GreaterThan(0));
            Assert.That(a.Regions.Select(r=>r.Name).Distinct().Count(),Is.EqualTo(a.Regions.Length),a.Id);
            foreach(var r in a.Regions)
            {
                if(r.Shape=="circle")
                {
                    Assert.That(r.Radius,Is.GreaterThan(0),a.Id);
                    Assert.That(r.Center.X-r.Radius,Is.GreaterThanOrEqualTo(0),a.Id);
                    Assert.That(r.Center.Y-r.Radius,Is.GreaterThanOrEqualTo(0),a.Id);
                    Assert.That(r.Center.X+r.Radius,Is.LessThanOrEqualTo(a.Width),a.Id);
                    Assert.That(r.Center.Y+r.Radius,Is.LessThanOrEqualTo(a.Height),a.Id);
                }
                else
                {
                    Assert.That(r.Points.Length,Is.GreaterThanOrEqualTo(3),a.Id);
                    var area=0f;
                    for(var i=0;i<r.Points.Length;i++)
                    {
                        var p=r.Points[i];var q=r.Points[(i+1)%r.Points.Length];
                        Assert.That(float.IsFinite(p.X)&&float.IsFinite(p.Y),Is.True,a.Id);
                        Assert.That(p.X,Is.InRange(0,a.Width),a.Id);
                        Assert.That(p.Y,Is.InRange(0,a.Height+(r.Kind=="interaction"?40:0)),a.Id);
                        area+=p.X*q.Y-q.X*p.Y;
                    }
                    Assert.That(Math.Abs(area),Is.GreaterThan(1),a.Id);
                }
            }
        }
    }

    [Test]
    public void CornerWallKeepsTheInsideNotchWalkable()
    {
        var wall=AtNative("wall-corner");
        Assert.That(Blocked(wall,50,16,8),Is.False,"The AABB's empty corner must stay walkable.");
        Assert.That(Blocked(wall,15,20),Is.True);
        Assert.That(Blocked(wall,50,42),Is.True);
    }

    [Test]
    public void TreeBlocksOnlyTrunkAndKeepsCanopyAnOcclusionRegion()
    {
        var tree=AtNative("tree");
        Assert.That(Blocked(tree,42,48),Is.True);
        Assert.That(Blocked(tree,42,19,5),Is.False);
        var canopy=tree.Single(r=>r.Kind=="occlusion").Obstacle;
        Assert.That(GameMap.ContainsPoint(canopy,42,19),Is.True);
        Assert.That(canopy.BlocksMovement||canopy.BlocksVision,Is.False);
    }

    [Test]
    public void BushIsAnEnterableHideTrigger()
    {
        var bush=AtNative("bush");
        Assert.That(Blocked(bush,24,26,10),Is.False);
        var trigger=bush.Single(r=>r.Kind=="hiding").Obstacle;
        Assert.That(GameMap.ContainsPoint(trigger,24,26),Is.True);
        Assert.That(GameMap.ContainsPoint(trigger,1,1),Is.False);
        Assert.That(trigger.BlocksVision,Is.False);
    }

    [Test]
    public void JailInteriorIsFreeAndGateActuallyControlsTheExit()
    {
        var closed=AtNative("jail");var open=AtNative("jail",true);
        Assert.That(Blocked(closed,68,85,10),Is.False);
        Assert.That(Blocked(closed,68,135,10),Is.True);
        Assert.That(closed.Where(r=>r.Obstacle.BlocksMovement).All(r=>!r.Obstacle.BlocksVision),Is.True);
        // 20px diameter in source map == current 50-world-pixel player.
        for(var y=90;y<=165;y++)Assert.That(Blocked(open,68,y,10),Is.False,$"Gate exit at y={y}");
        Assert.That(Blocked(open,14,85,5),Is.True,"Side bars remain solid.");
        Assert.That(AtNative("jail-open").Any(r=>r.Kind=="gate"),Is.False);
        Assert.That(Blocked(closed,68,158,10),Is.False,"Rescuer can reach the contact trigger.");
        Assert.That(GameMap.ContainsPoint(closed.Single(r=>r.Kind=="interaction").Obstacle,68,158),Is.True);
    }

    [TestCase("police-station",20,176)]
    [TestCase("cafe",20,140)]
    [TestCase("burger-shop",20,140)]
    public void EntranceShouldersDoNotBecomeInvisibleSolidCorners(string id,int x,int y)
    {
        Assert.That(Blocked(AtNative(id),x,y,.5f),Is.False);
    }

    [Test]
    public void PlacementMovesRotatesAndScalesTheColliderWithTheWholeSprite()
    {
        var a=ChaseTownAssetCatalog.Get("wall-corner");
        var center=new PointF(600,800);const float scale=2.5f;
        PointF Transform(float x,float y)=>new(center.X-(y-a.Height/2f)*scale,center.Y+(x-a.Width/2f)*scale);
        var placed=ChaseTownAssetCatalog.Place(a.Id,center,scale,90);
        var solid=Transform(15,20);var empty=Transform(50,16);
        Assert.That(Blocked(placed,solid.X,solid.Y),Is.True);
        Assert.That(Blocked(placed,empty.X,empty.Y,20),Is.False);
    }

    [TestCase("police-station",11,11,223,182)]
    [TestCase("cafe",12,12,123,140)]
    [TestCase("donut-shop",14,11,118,131)]
    [TestCase("burger-shop",12,12,123,140)]
    [TestCase("house",9,8,113,111)]
    [TestCase("warehouse-large",10,10,287,185)]
    [TestCase("warehouse-small",10,8,113,126)]
    public void BuildingRearAllowsPartialOcclusionWithoutMovingSidesOrFront(
        string id,float oldTop,float left,float right,float bottom)
    {
        var asset = ChaseTownAssetCatalog.Get(id);
        var regions = AtNative(id);
        var body = regions.Single(r => r.Name == "body").Obstacle;
        var bounds = GameMap.GetObstacleBounds(body);
        var top = oldTop + ChaseTownAssetCatalog.BuildingRearInsetPixels;
        Assert.Multiple(() =>
        {
            Assert.That(asset.RearInsetPixels, Is.EqualTo(8));
            Assert.That(bounds.Top, Is.EqualTo(top));
            // Cafe/burger have a slightly slanted left side (12,12) -> (14,134).
            // Clipping truncates that segment; it must not translate the remaining edge.
            var clippedLeft = id is "cafe" or "burger-shop"
                ? left + asset.RearInsetPixels * 2 / 122 : left;
            Assert.That(bounds.Left, Is.EqualTo(clippedLeft).Within(.0001f));
            Assert.That(bounds.Right, Is.EqualTo(right));
            Assert.That(bounds.Bottom, Is.EqualTo(bottom));
            Assert.That(body.BlocksVision, Is.True);
        });
        // Current 25-world-unit player radius is 10 pixels in the native sprite.
        const float radius = 10;
        var x = asset.Width / 2f;
        Assert.That(Blocked(regions,x,top-radius-.5f,radius), Is.False);
        Assert.That(Blocked(regions,x,top-radius+.5f,radius), Is.True);
        Assert.That(top-radius-.5f+radius, Is.GreaterThan(oldTop),
            "The character circle now overlaps the old roof footprint without entering the solid body.");
        Assert.That(GameMap.ContainsPoint(body,x,top-.5f), Is.False);
        Assert.That(GameMap.ContainsPoint(body,x,top+.5f), Is.True);
        Assert.That(Blocked(regions,left+1,asset.Height/2f,.5f), Is.True);
        Assert.That(Blocked(regions,left-1,asset.Height/2f,.5f), Is.False);
        Assert.That(Blocked(regions,right-1,asset.Height/2f,.5f), Is.True);
        Assert.That(Blocked(regions,right+1,asset.Height/2f,.5f), Is.False);
        var frontEdge = body.PolygonPoints.Where(p => p.Y == bottom).Take(2).ToArray();
        var frontX = frontEdge.Average(p => p.X);
        Assert.That(Blocked(regions,frontX,bottom-.5f,.25f), Is.True);
        Assert.That(Blocked(regions,frontX,bottom+.5f,.25f), Is.False);
    }

    [Test]
    public void ActiveBuildingsUseTheInsetWhileNonBuildingsKeepTheirOriginalProfiles()
    {
        var map = new GameMap();
        foreach (var placement in ChaseTownLayout.Placements.Where(p => p.Asset.RearInsetPixels > 0))
        {
            var building = map.Buildings.Single(b => b.Type == placement.BuildingType);
            var localTop = placement.Asset.Regions.Single(r => r.Name == "body").Points.Min(p => p.Y);
            var top = placement.Center.Y + (localTop - placement.Asset.Height / 2f) * placement.Scale;
            var x = placement.Center.X;
            Assert.That(GameMap.GetBuildingCollisionBounds(building).Top, Is.EqualTo(top));
            Assert.That(GameMap.IsCircleCollidingWithBuilding(x,top-25.5f,25,building), Is.False);
            Assert.That(GameMap.IsCircleCollidingWithBuilding(x,top-24.5f,25,building), Is.True);
        }
        Assert.That(ChaseTownAssetCatalog.Assets.Where(a => a.RearInsetPixels > 0).Count(), Is.EqualTo(7));
        foreach (var id in new[] { "jail", "jail-open", "tree", "bush", "crate", "wall-corner" })
            Assert.That(ChaseTownAssetCatalog.Get(id).RearInsetPixels, Is.Zero);
    }

    [TestCase(0f)] [TestCase(-1f)] [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)]
    public void InvalidScaleIsRejected(float scale) =>
        Assert.Throws<ArgumentOutOfRangeException>(()=>ChaseTownAssetCatalog.Place("house",new(0,0),scale));

    [Test]
    public void HighResolutionTexturesDoNotChangeLogicalSizesOrPhysics()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "polrob.slnx"))) dir = dir.Parent;
        using var before = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName,
            "docs/chase-town-assets/hd/physics-before.json")));
        foreach (var asset in ChaseTownAssetCatalog.Assets)
        {
            var old = before.RootElement.GetProperty("Assets").EnumerateArray()
                .Single(a => a.GetProperty("Id").GetString() == asset.Id);
            Assert.That(asset.Width, Is.EqualTo(old.GetProperty("Width").GetInt32()), asset.Id);
            Assert.That(asset.Height, Is.EqualTo(old.GetProperty("Height").GetInt32()), asset.Id);
            Assert.That(JsonSerializer.Serialize(asset.Regions),
                Is.EqualTo(JsonSerializer.Serialize(old.GetProperty("Regions").Deserialize<AssetRegion[]>())), asset.Id);
            Assert.That(asset.TextureScale, Is.EqualTo(asset.IsTile ? 1 : 6));
            // Current camera uses 2 screen pixels/world unit and world scale is 2.5.
            // HD sprites provide 1.2 texture pixels per screen pixel without upscaling.
            if (!asset.IsTile) Assert.That(asset.TextureWidth / asset.WorldWidth, Is.GreaterThanOrEqualTo(2));
        }
    }

    [Test]
    public void PackagedPngDimensionsAndManifestMatchSharedPhysics()
    {
        var dir=new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while(dir!=null&&!File.Exists(Path.Combine(dir.FullName,"polrob.slnx")))dir=dir.Parent;
        Assert.That(dir,Is.Not.Null);
        var raw=Path.Combine(dir!.FullName,"polrob.Client/Resources/Raw");
        foreach(var a in ChaseTownAssetCatalog.Assets)
        {
            var bytes=File.ReadAllBytes(Path.Combine(raw,a.AssetPath));
            Assert.That(bytes.Take(8),Is.EqualTo(new byte[]{137,80,78,71,13,10,26,10}),a.Id);
            Assert.That(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16,4)),Is.EqualTo(a.TextureWidth),a.Id);
            Assert.That(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20,4)),Is.EqualTo(a.TextureHeight),a.Id);
            if(!a.IsTile)Assert.That(bytes[25],Is.EqualTo(6),$"{a.Id} must be RGBA.");
        }
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(raw,"ChaseTownV7/manifest.json")));
        var entries=manifest.RootElement.GetProperty("Assets").EnumerateArray().ToArray();
        Assert.That(entries.Length,Is.EqualTo(ChaseTownAssetCatalog.Assets.Count));
        foreach(var a in ChaseTownAssetCatalog.Assets)
        {
            var entry=entries.Single(e=>e.GetProperty("Id").GetString()==a.Id);
            Assert.That(entry.GetProperty("TextureWidth").GetInt32(), Is.EqualTo(a.TextureWidth));
            Assert.That(entry.GetProperty("TextureHeight").GetInt32(), Is.EqualTo(a.TextureHeight));
            Assert.That(entry.GetProperty("RearInsetPixels").GetSingle(),Is.EqualTo(a.RearInsetPixels));
            var packaged=entry.GetProperty("Regions").Deserialize<AssetRegion[]>();
            Assert.That(JsonSerializer.Serialize(packaged),Is.EqualTo(JsonSerializer.Serialize(a.Regions)));
        }
    }
}
