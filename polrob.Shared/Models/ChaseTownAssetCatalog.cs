using System.Drawing;

namespace polrob.Shared;

/// <summary>
/// Reusable v7 art/physics pack. Local pixels have a TOP-LEFT origin, +Y down.
/// Used by the active ChaseTownLayout. Width/Height are logical design pixels,
/// not texture pixels. Draw the WHOLE texture into the original logical rectangle.
/// Texture density never changes world size, pivots or physics.
/// </summary>
public static class ChaseTownAssetCatalog
{
    public const float DefaultWorldScale = 2.5f;
    // A shallow roof overhang for the ~80-degree view: 8 source pixels = 20 world units.
    // The sprite stays whole; only the rear edge of the solid footprint is clipped.
    public const float BuildingRearInsetPixels = 8f;
    public const string AssetRoot = "ChaseTownV7";
    public const int PropTextureScale = 6;
    public static IReadOnlyList<ChaseTownAsset> Assets { get; } = Create();

    public static ChaseTownAsset Get(string id) => Assets.Single(a => a.Id == id);

    /// <summary>Builds movement/vision obstacles or non-solid trigger/occlusion regions.</summary>
    public static IReadOnlyList<ChaseTownRegion> Place(string id, PointF center,
        float scale = DefaultWorldScale, float rotationDegrees = 0, bool gateOpen = false)
    {
        if (!float.IsFinite(scale) || scale <= 0 || !float.IsFinite(center.X) ||
            !float.IsFinite(center.Y) || !float.IsFinite(rotationDegrees))
            throw new ArgumentOutOfRangeException(nameof(scale), "Placement must be finite with positive uniform scale.");
        var asset = Get(id);
        var radians = rotationDegrees * MathF.PI / 180;
        PointF Transform(AssetPoint p)
        {
            var x = (p.X - asset.Width / 2f) * scale;
            var y = (p.Y - asset.Height / 2f) * scale;
            return new(center.X + x * MathF.Cos(radians) - y * MathF.Sin(radians),
                center.Y + x * MathF.Sin(radians) + y * MathF.Cos(radians));
        }
        return asset.Regions.Where(r => !gateOpen || r.Kind != "gate").Select(r =>
        {
            var points = r.Points.Select(Transform).ToArray();
            var c = Transform(r.Center);
            var radius = r.Radius * scale;
            var left = r.Shape == "circle" ? c.X - radius : points.Min(p => p.X);
            var right = r.Shape == "circle" ? c.X + radius : points.Max(p => p.X);
            var top = r.Shape == "circle" ? c.Y - radius : points.Min(p => p.Y);
            var bottom = r.Shape == "circle" ? c.Y + radius : points.Max(p => p.Y);
            var obstacle = new Obstacle
            {
                ImageFileName = asset.AssetPath, Type = r.Shape == "circle" ? "Circle" : "Polygon",
                CenterX = c, Radius = radius, PolygonPoints = points,
                LeftTop = new(left, top), RightBottom = new(right, bottom),
                RightTop = new(right, top), LeftBottom = new(left, bottom),
                BlocksMovement = r.BlocksMovement, BlocksVision = r.BlocksVision, IsVisible = false
            };
            return new ChaseTownRegion(r.Name, r.Kind, obstacle);
        }).ToArray();
    }

    private static ChaseTownAsset[] Create()
    {
        // Bounding silhouettes exclude soft shadows; small front canopies do not
        // turn empty front corners into solid rectangles.
        var a = new List<ChaseTownAsset>();
        void Building(string id, int x, int y, int w, int h, params float[] xy)
        {
            var body = Polygon("body", "solid", true, true, xy);
            a.Add(new(id, x, y, w, h,
                [body with { Points = ClipRear(body.Points, BuildingRearInsetPixels) }],
                RearInsetPixels: BuildingRearInsetPixels));
        }
        Building("police-station",478,34,230,189,
            14,11,218,11,223,20,223,163,160,163,160,182,73,182,73,163,11,163,11,20);
        Building("cafe",196,382,132,147,
            12,12,121,12,123,109,119,109,119,134,88,134,88,140,48,140,48,134,14,134);
        Building("donut-shop",208,585,126,140,
            11,14,118,14,118,101,108,101,108,124,84,124,84,131,45,131,45,124,18,124,18,102,11,102);
        Building("burger-shop",552,382,132,147,
            12,12,121,12,123,109,119,109,119,134,88,134,88,140,48,140,48,134,14,134);
        Building("house",698,416,120,114,
            8,9,112,9,113,108,77,108,77,111,47,111,47,108,8,108);
        Building("warehouse-large",209,1051,297,192,
            10,10,287,10,287,174,234,174,234,185,160,185,160,176,128,176,128,185,50,185,50,175,10,175);
        Building("warehouse-small",695,608,121,130,
            8,10,113,10,113,116,86,116,86,126,30,126,30,116,8,116);
        // Jail has separate wall pieces, switchable front gate and a walkable interior.
        a.Add(new("jail",874,93,137,150,
        [
            Rect("back-bars","solid",true,false,15,34,122,41),
            Rect("left-bars","solid",true,false,10,35,20,136),
            Rect("right-bars","solid",true,false,117,35,127,136),
            Rect("front-left","solid",true,false,18,129,43,140),
            Rect("front-right","solid",true,false,94,129,119,140),
            Rect("locked-gate","gate",true,false,43,129,94,140),
            Rect("holding-area","holding",false,false,25,46,112,123),
            // Contact must be reachable by a player whose center cannot approach
            // closer than its radius to the closed gate; intentionally outside PNG.
            Rect("rescue-area","interaction",false,false,37,145,100,180)
        ]));
        a.Add(a[^1] with { Id="jail-open", Regions=a[^1].Regions.Where(r=>r.Kind!="gate").ToArray() });
        a.Add(new("tree",392,1371,82,86,
        [Circle("trunk","solid",true,true,42,48,9), Circle("canopy","occlusion",false,false,42,42,35)]));
        a.Add(new("bush",326,548,49,49,
        [Circle("hide","hiding",false,false,24,26,17)]));
        a.Add(new("crate",519,1088,45,51,
        [Rect("box","solid",true,true,8,8,39,45)]));
        a.Add(new("wall-horizontal",584,752,107,44,
        [Rect("wall","solid",true,true,9,14,102,37)]));
        a.Add(new("wall-vertical",586,1033,30,209,
        [Rect("wall","solid",true,true,8,10,24,198)]));
        a.Add(new("wall-corner",216,532,80,62,
        [Polygon("wall","solid",true,true,9,11,22,11,22,29,75,29,75,53,9,53)]));
        a.Add(new("wall-post",155,1028,29,46,
        [Rect("post","solid",true,true,8,12,24,39)]));
        // Flat base tiles, repeatable on every edge. Borders/paths are renderer geometry.
        foreach (var (id,x,y) in new[]{("grass",840,1050),("road",480,330),("paving",580,245)})
            a.Add(new(id,x,y,64,64,[],true));
        return a.ToArray();
    }

    private static AssetPoint[] ClipRear(AssetPoint[] points, float inset)
    {
        var top = points.Min(p => p.Y) + inset;
        var clipped = new List<AssetPoint>();
        var previous = points[^1];
        foreach (var current in points)
        {
            var previousInside = previous.Y >= top;
            var currentInside = current.Y >= top;
            if (previousInside != currentInside)
            {
                var t = (top - previous.Y) / (current.Y - previous.Y);
                clipped.Add(new(previous.X + (current.X - previous.X) * t, top));
            }
            if (currentInside) clipped.Add(current);
            previous = current;
        }
        return clipped.ToArray();
    }

    private static AssetRegion Polygon(string name,string kind,bool movement,bool vision,params float[] xy) =>
        new(name,kind,"polygon",movement,vision,Enumerable.Range(0,xy.Length/2)
            .Select(i => new AssetPoint(xy[i*2],xy[i*2+1])).ToArray(),new(0,0),0);
    private static AssetRegion Rect(string n,string k,bool m,bool v,float l,float t,float r,float b) =>
        Polygon(n,k,m,v,l,t,r,t,r,b,l,b);
    private static AssetRegion Circle(string n,string k,bool m,bool v,float x,float y,float radius) =>
        new(n,k,"circle",m,v,[],new(x,y),radius);
}

public sealed record ChaseTownAsset(string Id,int SourceX,int SourceY,int Width,int Height,
    AssetRegion[] Regions,bool IsTile=false,float RearInsetPixels=0)
{
    public string AssetPath => $"{ChaseTownAssetCatalog.AssetRoot}/{(IsTile ? "tiles" : "props")}/{Id}.png";
    public int TextureScale => IsTile ? 1 : ChaseTownAssetCatalog.PropTextureScale;
    public int TextureWidth => Width * TextureScale;
    public int TextureHeight => Height * TextureScale;
    public AssetPoint Pivot => new(Width/2f,Height/2f);
    public float WorldWidth => Width*ChaseTownAssetCatalog.DefaultWorldScale;
    public float WorldHeight => Height*ChaseTownAssetCatalog.DefaultWorldScale;
}
public readonly record struct AssetPoint(float X,float Y);
public sealed record AssetRegion(string Name,string Kind,string Shape,bool BlocksMovement,
    bool BlocksVision,AssetPoint[] Points,AssetPoint Center,float Radius);
public sealed record ChaseTownRegion(string Name,string Kind,Obstacle Obstacle);
