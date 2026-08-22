# Floor tile generation prompts

Generated on 2026-08-20 with the built-in `image_gen` tool. The two user-supplied images were used only as visual-style references. Final source assets are 1254 x 1254 opaque RGB PNGs; runtime exports are 256 x 256 opaque RGB PNGs.

## Gray brick

```text
Use case: stylized-concept
Asset type: seamless tileable 2D game floor texture
Primary request: Create a cool slate-gray rectangular brick floor in a regular staggered running-bond pattern, engineered as a clean repeat tile.
Construction grid — highest priority: exactly TEN complete horizontal brick courses fill the square canvas, no more and no fewer. Each course is fully visible from its upper mortar joint to its lower mortar joint. The TOP canvas edge and BOTTOM canvas edge must both pass precisely through the CENTER of equivalent thin horizontal mortar joints, so neither boundary crops through a brick and no partial brick course appears at the top or bottom. Because there are exactly ten courses, the alternating running-bond phase at the bottom returns to the same vertical-joint phase as the top.
Horizontal wrap — highest priority: use a mathematically regular running bond. On alternating courses, any partial brick cut by the LEFT edge must be the reciprocal continuation of the partial brick at the RIGHT edge; together those two edge fragments form one normal full-width brick when the image repeats. Other alternating courses may place equivalent vertical mortar-joint centers at both left and right boundaries. Keep brick widths and joint spacing uniform enough that every left/right row continuation is obvious and clean.
Scene/backdrop: edge-to-edge masonry floor surface only.
Subject: medium-scale cool slate-gray rectangular pavers with subtly chipped, slightly irregular rounded corners; thin dark charcoal mortar; restrained gray-blue value variation.
Style/medium: polished hand-painted cartoon 2D game art; soft rounded slightly irregular shapes; chunky clean dark outlines; restrained cel-shaded highlights; medium detail; lightly worn but not photorealistic.
Composition/framing: square canvas; exact top-down orthographic view; flat game-map texture; exactly ten equal-height courses; uniform density; no perspective and no focal point.
Lighting/mood: perfectly even diffuse neutral lighting over the entire surface. Identical brightness and contrast at the center, all four edges, and all four corners. Absolutely no edge lighting, darkening, brightening, vignette, border shadow, gradient, spotlight, or ambient-occlusion band around the canvas.
Color palette: cool slate gray, charcoal, restrained muted blue-gray highlights.
Seam test intent: the image must look continuous when copied into a 2x2 grid; the central horizontal and vertical joins in that grid must be indistinguishable from ordinary interior mortar joints and brick continuations.
Constraints: exactly ten complete horizontal brick courses; edge-to-edge; reciprocal left/right brick fragments; top and bottom on equivalent mortar-joint centers; no cropped partial top or bottom course; no border, gutter, margin, empty background, objects, debris, vegetation, text, labels, logo, signature, watermark, stock marks, diagonal lines, vignette, edge shading, or obvious center motif.
```

## Red brick

```text
Use case: stylized-concept
Asset type: seamless tileable 2D game floor texture
Primary request: Create a warm red and terracotta brick floor made of rectangular bricks in a staggered running-bond pattern.
Scene/backdrop: edge-to-edge masonry floor surface only, with no surrounding scene.
Subject: consistent medium-scale rectangular bricks with subtly chipped, slightly irregular rounded corners; thin deep burgundy-brown mortar; restrained rust, brick-red, and muted coral value variation from brick to brick.
Style/medium: polished hand-painted cartoon game art matching the supplied references' visual language: soft rounded slightly irregular shapes, chunky clean dark outlines, restrained cel-shaded highlights, medium detail.
Composition/framing: square canvas, exact top-down orthographic view, flat 2D game-map texture, uniform visual density and the same apparent brick scale as the companion gray brick tile, no perspective and no central focal point.
Lighting/mood: even diffuse neutral light; no directional cast shadow, vignette, glow, or dramatic lighting.
Seam requirements: genuinely seamless and periodically tileable on both axes; pixels and pattern continuation at the left edge must match the right edge, and the top edge must match the bottom edge; bricks and mortar crossing an edge must continue naturally on the opposite edge; avoid a visible perimeter seam or framed border.
Color palette: warm red, terracotta, muted rust, subdued coral highlights, deep burgundy-brown mortar; restrained contrast.
Materials/textures: lightly worn fired-clay brick surface with sparse tiny chips and shallow painted scuffs, never photorealistic.
Constraints: one single texture only; edge-to-edge; consistent brick size; running-bond layout; no border, gutter, margin, empty background, objects, debris, vegetation, text, labels, logo, signature, watermark, stock-photo marks, diagonal lines, or obvious repeating center motif.
```

## Wood

```text
Use case: stylized-concept
Asset type: seamless tileable top-down 2D game floor texture
Input images: Image 1 and Image 2 are visual-style references only; do not reproduce their layouts, source imagery, text, logos, or watermarks.
Primary request: a weathered warm-brown wooden plank floor tile for constructing a game map
Subject: long wooden floorboards arranged in a straight parallel plank pattern with naturally staggered end seams, narrow dark gaps, a few restrained knots, and flowing carved wood grain
Style/medium: polished hand-painted cartoon game art matching the references' broad shapes, chunky clean dark linework, rounded irregularities, and restrained cel-shaded highlights; original artwork
Composition/framing: perfectly top-down orthographic square texture; flat surface fills the entire canvas edge-to-edge; consistent board scale; no perspective; no frame or margin
Lighting/mood: soft even diffuse light with no cast shadows and no directional vignette
Color palette: warm chestnut, walnut, muted caramel highlights, dark espresso gaps
Materials/textures: readable stylized wood grain and gentle wear; no splinters or dramatic damage
Constraints: genuinely seamless repeat on left-right and top-bottom edges; pattern continues across every boundary; no obvious center focal point; no isolated object; no text, labels, numbers, logo, watermark, stock-photo markings, diagonal lines, border, gutter, transparent margin, nails, furniture, leaves, or debris
Avoid: photorealism, 3D perspective, harsh lighting, glossy varnish, oversized knots, strong shadows
```

## Dirt

```text
Use case: stylized-concept
Asset type: seamless tileable 2D game floor texture
Primary request: compacted warm umber-brown dirt soil for building a game map floor
Scene/backdrop: dirt surface fills the entire square canvas edge to edge
Subject: continuous compacted soil with soft irregular tonal patches, sparse fine pebbles, and a few thin shallow cracks; distribute detail evenly with no center subject
Style/medium: polished hand-painted cartoon game art; soft rounded organic marks; clean dark accents; restrained cel-shaded tonal variation; medium detail
Composition/framing: square, perfectly straight top-down orthographic view, flat surface, no perspective; same small environmental-texture scale throughout
Lighting/mood: even diffuse neutral light; no directional shadow; no vignette
Color palette: warm umber-brown and muted terracotta-brown tonal patches with restrained dark brown accents
Materials/textures: compacted dry soil, subtle shallow mottling, tiny rounded pebble flecks only
Constraints: create a genuinely seamless repeating texture; left edge must continue exactly into right edge and top edge exactly into bottom edge; features crossing an edge must resume naturally on the opposite edge; edge density and color must match; floor texture must reach every edge; no border, no gutter, no empty margin; no obvious repeated stamp and no focal point; no text, labels, logo, signature, watermark, stock marks, or diagonal lines
Avoid: large rocks, grass, plants, roots, leaves, footprints, tire tracks, paths, puddles, objects, horizon, perspective, strong highlights, cast shadows, photorealism
```

## Grass

```text
Use case: stylized-concept
Asset type: seamless tileable 2D game floor texture
Primary request: dense short green lawn for building a game map floor
Scene/backdrop: grass surface fills the entire square canvas edge to edge
Subject: continuous dense short lawn with layered simplified small blade and clump marks plus subtle olive-green and emerald-green tonal patches; distribute detail evenly with no center subject
Style/medium: polished hand-painted cartoon game art; soft rounded organic marks; clean dark accents; restrained cel-shaded tonal variation; medium detail
Composition/framing: square, perfectly straight top-down orthographic view, flat surface, no perspective; same small environmental-texture scale throughout
Lighting/mood: even diffuse neutral light; no directional shadow; no vignette
Color palette: balanced natural mid green, muted olive patches, restrained emerald highlights, and limited deep forest-green accents
Materials/textures: dense fine short grass; overlapping tiny curved blade strokes and compact rounded clumps; subtle broad patch variation beneath the marks
Constraints: create a genuinely seamless repeating texture; left edge must continue exactly into right edge and top edge exactly into bottom edge; features crossing an edge must resume naturally on the opposite edge; edge density and color must match; floor texture must reach every edge; no border, no gutter, no empty margin; no obvious repeated stamp and no focal point; no text, labels, logo, signature, watermark, stock marks, or diagonal lines
Avoid: flowers, seed heads, rocks, pebbles, bare dirt, paths, roots, leaves, weeds taller than the lawn, objects, horizon, perspective, strong highlights, cast shadows, photorealism
```

## Sand

```text
Use case: stylized-concept
Asset type: seamless tileable 2D game floor texture
Primary request: warm golden-beige sand for building a game map floor
Scene/backdrop: sand surface fills the entire square canvas edge to edge
Subject: continuous fine sand with broad shallow wind ripples, restrained fine speckles, and only a few tiny rounded pebble dots; distribute detail evenly with no center subject
Style/medium: polished hand-painted cartoon game art; soft rounded organic marks; clean dark accents; restrained cel-shaded tonal variation; medium detail
Composition/framing: square, perfectly straight top-down orthographic view, flat surface, no perspective; same small environmental-texture scale throughout
Lighting/mood: even diffuse neutral light; no directional shadow; no vignette
Color palette: warm golden beige, pale honey-tan highlights, muted amber-tan ripple shadows, and sparse soft brown dots
Materials/textures: smooth dry fine sand; wide gentle curving ripple bands with low contrast; tiny scattered grains and very small pebble dots
Constraints: create a genuinely seamless repeating texture; left edge must continue exactly into right edge and top edge exactly into bottom edge; ripples and marks crossing an edge must resume naturally on the opposite edge; edge density and color must match; floor texture must reach every edge; no border, no gutter, no empty margin; no obvious repeated stamp and no focal point; no text, labels, logo, signature, watermark, stock marks, or diagonal lines
Avoid: large rocks, stones, water, foam, seashells, plants, grass, footprints, tire tracks, paths, objects, horizon, beach scene, large dunes, perspective, strong highlights, cast shadows, photorealism
```

## Asphalt

```text
Use case: stylized-concept
Asset type: seamless tileable top-down 2D game floor texture
Input images: Image 1 and Image 2 are visual-style references only; do not reproduce their layouts, source imagery, text, logos, or watermarks.
Primary request: a dark asphalt floor tile for constructing roads and paved areas in a game map
Subject: continuous charcoal-gray asphalt with fine simplified aggregate flecks, softly mottled tonal patches, a few sparse shallow hairline cracks, and very subtle worn patching; it must still read as asphalt at small game scale
Style/medium: polished hand-painted cartoon game art matching the references' broad simplified marks, clean dark accents, rounded irregularities, and restrained cel-shaded tonal variation; original artwork
Composition/framing: perfectly top-down orthographic square texture; flat asphalt fills the entire canvas edge-to-edge; uniform visual scale; no perspective; no frame or margin
Lighting/mood: soft even diffuse light with no cast shadows and no directional vignette
Color palette: dark charcoal, graphite gray, muted cool slate, sparse soft medium-gray aggregate accents
Materials/textures: dry matte asphalt, understated grain, gentle wear
Constraints: genuinely seamless repeat on left-right and top-bottom edges; texture continues across every boundary; no obvious center focal point; no isolated object; no text, labels, numbers, logo, watermark, stock-photo markings, diagonal stock lines, border, gutter, transparent margin, lane markings, arrows, curbs, manholes, drains, potholes, tire tracks, oil stains, weeds, litter, or debris
Avoid: photorealism, 3D perspective, harsh lighting, shiny wet surface, large rocks, dramatic cracks, strong shadows
```
