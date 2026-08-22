# PolRob 2D Map Assets v1

This folder contains the first separated 2D map-asset pass. It is a reviewable
asset pack and is not wired into the runtime map yet.

## Final assets

- `buildings/`: 8 transparent RGBA PNG sprites, 512 x 512, bottom-center anchor
- `obstacles/`: 8 transparent RGBA PNG sprites, 256 x 256, bottom-center anchor
- `tiles/`: 8 opaque RGB PNG ground tiles, 256 x 256
- `previews/`: contact sheets and repeated-tile seam checks
- `sources/`: original built-in ImageGen outputs retained non-destructively

## Buildings

- `police_station.png`
- `jail.png`
- `bank.png`
- `general_store.png`
- `cafe.png`
- `house_blue.png`
- `house_orange.png`
- `warehouse.png`

## Physical obstacles

- Trees: `tree_deciduous.png`, `tree_pine.png`, `tree_small.png`
- Crates: `crate_wooden.png`, `crates_stacked.png`
- Rocks: `rock_small.png`, `rock_large.png`, `rocks_cluster.png`

For tree collision, use a compact collider around the trunk rather than the
full canopy. Crate and rock colliders should follow their visible bottom
footprints.

## Ground tiles

- Base: `grass.png`, `dirt.png`, `gray_brick.png`, `asphalt.png`
- Grass north / dirt south: `grass_north_dirt_south.png`
- Dirt north / grass south: `dirt_north_grass_south.png`
- Grass west / dirt east: `grass_west_dirt_east.png`
- Dirt west / grass east: `dirt_west_grass_east.png`

The transition tiles are composed from the final grass and dirt textures so
their colors match the base tiles. They use a centered, slightly pixel-jagged
50/50 boundary and do not bake a trail shape into the ground.

## Art direction

The built-in ImageGen prompts use an original, colorful, fixed-view 2D RPG
tile-map language: chunky pixel-inspired shapes, clear silhouettes, shallow
screen-facing building facades, transparent object backgrounds, no characters,
no text, no logos, and no terrain baked into object sprites.
