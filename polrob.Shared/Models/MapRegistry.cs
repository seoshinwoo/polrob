namespace polrob.Shared;

/// <summary>Stable map IDs are shared by room creation, physics and rendering.</summary>
public static class MapRegistry
{
    public const string ClassicTown = "canva-town-v1";
    public const string ChaseTown = "chase-town-v1";
    public const string DefaultId = ChaseTown;

    public static IReadOnlyList<MapDefinition> All { get; } = Array.AsReadOnly(new[]
    {
        new MapDefinition(ChaseTown, "추격 마을 (새 맵)", ChaseTownLayout.Props,
            ["ChaseTownV7/tiles/grass.png", "ChaseTownV7/tiles/road.png", "ChaseTownV7/tiles/paving.png"]),
        new MapDefinition(ClassicTown, "기존 마을", CanvaMapLayout.Props,
            ["TownMap/tiles/grass.png", "TownMap/tiles/asphalt.png", "TownMap/tiles/paving.png"])
    });

    public static bool Contains(string? id) => All.Any(map => map.Id == id);

    public static MapDefinition Get(string id) => All.FirstOrDefault(map => map.Id == id)
        ?? throw new ArgumentException($"Unknown map: {id}", nameof(id));
}

public sealed record MapDefinition(string Id, string DisplayName, MapPropLayout[] Props, string[] TileAssets);
