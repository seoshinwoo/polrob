# Original-character preview compositor

Run `swift compose-character-preview.swift preview-config.json` from this directory.
Relative paths are resolved beside the config JSON, not the shell's working directory.
Only preview outputs are written. Map inputs and runtime character PNGs remain untouched.

The full PNG draws the map and exact original `char_police.png` / `char_robber.png`
with their existing alpha. Crops redraw the source map and original sprites directly
at the chosen scale so that the character stays sharp. Sprites are not regenerated.

## Config

- `mapPath`, `outputPath`: required input map and full composite output.
- `characters`: objects containing `kind` (`police` or `robber`), `x`, `y`.
  Coordinates are pixel positions on the original map with origin at its top left.
- Optional character `angleDegrees`: clockwise rotation, as in the game.
- Optional `worldWidth`: default 2560. The input map's full width represents this
  many world units. It determines map pixels per world unit.
- Optional `pixelsPerWorldUnit`: explicit uniform map/world scale; overrides
  the value inferred from `worldWidth`.
- Optional `outputScale`: full-preview scale, default 1. This enlarges the map and
  sprites together, preserving their relative scale.
- Optional `crop` or `crops`: a rectangle `x`, `y`, `width`, `height` in original
  map pixels, its `outputPath`, and optional enlargement `scale`. The default
  enlargement reproduces the live renderer's `CameraZoom=2`.
- Optional `spriteDirectory`: location of the two existing character PNGs.
- Optional per-character `radiusWorldUnits`: default 25, matching current live
  client and server. Only override for an explicitly labeled scale experiment.

## Verified runtime scale (2026-10-01)

- `polrob.Shared/Models/Map.cs`: current world width 2560, height 3840.
- `polrob.Client/GamePlay.xaml.cs`: local radius 25, body-width ratio 0.86,
  normalized body width 512, source pivot (544,544), camera zoom 2.
- `polrob.Server/Network/GameNetworkServer.cs`: server radius 25.
- `Player.cs` has a default radius 50, but live client/server override it to 25.
- Both original PNGs have a 1088 × 1088 canvas. Their transparent canvas is not
  the body width and must not be used as the visible body-width measurement.

The source-pixel-to-world scale is `50 × 0.86 / 512`, so the character body is
43 world units wide. A 1024-pixel-wide full-map preview uses 0.4 pixels per
world unit: body width 17.2px and full transparent canvas width 36.55px.
At the current game camera zoom the body is 86 render pixels wide. Therefore,
a 5× crop from a 1024-pixel map matches the live render scale. At 1536 pixels
wide the corresponding crop scale is 3⅓×; at 2048 pixels wide it is 2.5×.

Existing `tmp/map_v7_preview.swift` used 3× on a 1024-pixel map, yielding
51.6px body width; useful for an overview but smaller than current camera scale.
For honest comparison, use the runtime-relative size on the full map and a
separate crop with the default enlargement. Do not enlarge sprites alone.
PNG pixel sizes reflect renderer pixels, not a promise about device CSS points
or physical screen size. These are art previews, without fog, name labels or UI.
