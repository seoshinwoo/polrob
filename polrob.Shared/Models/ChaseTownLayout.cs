using System.Drawing;

namespace polrob.Shared;

/// <summary>Active map. Ground is a 32 × 48 tile grid, not a baked background.</summary>
public static class ChaseTownLayout
{
    public const int Columns = 32, Rows = 48, TileSize = 80;
    public const string GrassColor = "#646F63", RoadColor = "#484E57", PavingColor = "#898B88";
    public static readonly GroundTile[,] Ground = CreateGround();
    public static readonly ChaseTownPlacement[] Placements = CreatePlacements();
    public static readonly MapPropLayout[] Props = Placements.Select(p => new MapPropLayout(
        p.Asset.AssetPath, p.Center.X, p.Center.Y, p.Asset.Width * p.Scale, p.Asset.Height * p.Scale,
        BuildingType: p.BuildingType, BlocksMovement: p.Asset.Regions.Any(r => r.BlocksMovement))).ToArray();

    public static readonly PointF[] ChaseWaypoints = new (float X, float Y)[]
    {
        (512, 304), (342, 470), (350, 680), (698, 470), (694, 680),
        (512, 832), (368, 960), (570, 1180), (800, 1160), (672, 1312), (944, 260)
    }.Select(p => new PointF(p.X * 2.5f, p.Y * 2.5f)).ToArray();

    private static GroundTile[,] CreateGround()
    {
        var tiles = new GroundTile[Columns, Rows];
        void Fill(int left, int top, int right, int bottom, GroundTile tile)
        {
            for (var y = top; y <= bottom; y++)
            for (var x = left; x <= right; x++) tiles[x, y] = tile;
        }
        // Building blocks use one concrete surface even where trees are present.
        // Sage is reserved for forest-only blocks, including their walls and crates.
        Fill(0, 0, 31, 27, GroundTile.Paving);
        Fill(17, 27, 25, 29, GroundTile.Grass);
        Fill(4, 29, 14, 31, GroundTile.Paving);
        Fill(4, 32, 19, 40, GroundTile.Paving);
        Fill(6, 27, 14, 28, GroundTile.Paving);
        var roads = new (int L, int T, int R, int B)[]
        {
            (6,0,7,4), (6,4,8,5), (7,5,8,9),
            (25,1,26,9), (25,0,28,1),
            (1,9,31,10), (4,10,5,28), (0,14,4,15),
            (15,10,16,31), (26,10,27,29),
            (4,25,27,26), (2,28,16,29), (26,28,31,29),
            (2,29,3,43), (15,30,21,31), (20,30,21,41),
            (2,41,31,42), (2,43,10,44), (9,43,10,47)
        };
        // One shared road grid: adjoining tiles have no internal curb seams.
        foreach (var r in roads) Fill(r.L, r.T, r.R, r.B, GroundTile.Road);
        return tiles;
    }

    private static ChaseTownPlacement[] CreatePlacements()
    {
        var p = new List<ChaseTownPlacement>();
        void Add(string id, float x, float y, string? type = null, float scale = 2.5f) =>
            p.Add(new($"{id}-{p.Count}", id, new(x * 2.5f, y * 2.5f), scale, type));
        Add("police-station", 603, 152, "PoliceStation");
        Add("jail", 944, 153, "Jail");
        Add("cafe", 262, 455, "Cafe"); Add("donut-shop", 271, 656, "DonutShop");
        Add("burger-shop", 618, 455, "BurgerShop");
        foreach (var (x,y) in new (float,float)[] { (98,154),(417,475),(425,664),(773,473),
                     (618,644),(80,576),(87,749),(941,560),(941,747) })
            Add("house", x, y, $"House-{p.Count}");
        Add("warehouse-small", 770, 675, "Workshop");
        Add("warehouse-large", 323, 1141, "Warehouse");

        // Short, separated walls break sightlines without sealing the routes.
        foreach (var (x,y) in new (float,float)[] { (435,33),(752,33),(447,273),(744,277),
                     (260,775),(638,775),(190,1280),(815,1280) }) Add("wall-horizontal",x,y);
        foreach (var (x,y) in new (float,float)[] { (395,134),(796,135),(606,1137) })
            Add("wall-vertical",x,y);
        Add("wall-corner",250,561); Add("wall-corner",70,376); Add("wall-corner",890,1136);
        Add("wall-post",150,1058); Add("wall-post",150,1248);
        foreach (var (x,y) in new (float,float)[] { (450,151),(520,1070),(520,1154),
                     (516,1238),(870,1208),(416,765),(764,566) }) Add("crate",x,y);

        // Approved removals: police right pair, above jail, lower-left house's
        // right tree, donut frontage bush, and the tree between the east houses.
        foreach (var (x,y) in new (float,float)[] { (83,53),(305,58),(318,188),
                     (915,410),(39,853),
                     (196,995),(280,962),(576,902),(806,974),(767,1075),(967,1268),
                     (944,1049),(38,1030),(39,1295),(432,1415),(710,1415),(781,1455),
                     (48,1415),(570,1510) }) Add("tree",x,y);
        foreach (var (x,y) in new (float,float)[] { (41,222),(153,268),(444,90),(986,376),
                     (334,243),(70,422),(383,385),(720,385),(330,568),
                     (447,573),(578,554),(659,728),(791,767),(986,855),
                     (631,919),(648,922),(360,951),(415,997),(590,1283),(445,1283),
                     (751,1190),(774,1214),(988,1162),(70,1478),(99,1462),
                     (587,1418),(607,1443),(510,1475),(534,1485),(938,1429),(963,1450) })
            Add("bush",x,y);
        return p.ToArray();
    }
}

public enum GroundTile { Grass, Paving, Road }
public sealed record ChaseTownPlacement(string Id, string AssetId, PointF Center, float Scale, string? BuildingType)
{
    public ChaseTownAsset Asset => ChaseTownAssetCatalog.Get(AssetId);
    public IReadOnlyList<ChaseTownRegion> Regions => ChaseTownAssetCatalog.Place(AssetId, Center, Scale);
}
