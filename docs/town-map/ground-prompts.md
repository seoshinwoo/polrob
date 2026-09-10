# Town map ground tiles

Generated with the built-in image_gen tool on 2026-09-07. The supplied police_station.png was inspected and used as a visual style reference. No assets were extracted from the reference map.

All three source outputs are 1254 × 1254 PNG images preserved in sources/. Runtime textures were mechanically resized with macOS sips to 256 × 256 pixels in polrob.Client/Resources/Raw/TownMap/tiles/. No Python image processing was used.

Tiles are intended to repeat at 256 game units. Paving contains four slabs per axis, so each slab is approximately 64 game units wide. Road contours and road markings are drawn by game code over the asphalt texture.

## grass.png

Source: sources/grass-generated.png

```text
Use case: stylized-concept
Asset type: seamless repeating 2D mobile game ground texture, square PNG.
Primary request: Generate ONE grass ground texture tile, exact orthographic overhead ground view, full square edge to edge, seamless repeating along both axes.
Style/medium: Clean hand-painted mobile cartoon finish matching the provided police station reference image: polished soft local shading, simplified readable forms, restrained dark fine edges. This is only a ground texture so keep detail extremely understated.
Subject: A uniform muted moss meadow green ground with a few tiny sparse stylized short grass blades, very subtle fine mottled texture. Broad average color around #647C39, small restrained natural variation.
Composition/framing: Entire image is flat grass, no perspective, no horizon, no depth plane. Each edge must continue naturally into the opposite edge for a perfect repeating texture. Flat overall illumination and uniform brightness throughout.
Constraints: Output square 1024x1024; no vignette, no gradient, no dramatic highlights, no lighting direction, no shadows, no objects, no rocks, no flowers, no bushes, no bare soil, no paths, no border, no text, no watermark. Grass blades must stay very small and sparse; maintain calm negative space to let game characters and buildings stand out.
```

## asphalt.png

Source: sources/asphalt-generated.png

```text
Use case: stylized-concept
Asset type: seamless repeating 2D mobile game ground texture, square PNG.
Primary request: Generate ONE asphalt road ground texture tile, exact orthographic overhead ground view, full square edge to edge, seamless repeating along both axes.
Style/medium: Clean hand-painted mobile cartoon finish matching the provided police station reference image: polished soft local shading, simplified readable forms. Keep ground detail extremely understated.
Subject: Uniform dark desaturated blue gray asphalt, average color around #424B53, very subtle fine grain; calm solid surface with tiny restrained natural color variation.
Composition/framing: Entire image is flat asphalt only, no perspective, no horizon, no depth plane. Each edge continues naturally into the opposite edge for a perfect repeating texture. Flat overall illumination and uniform brightness throughout.
Constraints: Output square 1024x1024; absolutely no road lines or markings, no cracks, no pebbles, no objects, no curb, no drain, no border, no text, no watermark, no vignette, no gradient, no directional lighting, no shadows. This texture is a quiet background for colorful game sprites.
```

## paving.png

Source: sources/paving-generated.png

```text
Use case: stylized-concept
Asset type: seamless repeating 2D mobile game ground texture, square PNG.
Primary request: Generate ONE paved sidewalk texture tile, exact orthographic overhead ground view, full square edge to edge, seamless repeating along both axes.
Style/medium: Clean hand-painted mobile cartoon finish matching the provided police station reference: warm cream gray paving with thin softly inked grout and restrained beveled slab edges. Simple and polished.
Subject: A strictly regular 4 by 4 grid of equal square warm cream gray sidewalk slabs, average slab color around #B5AE93. Narrow muted gray grout lines only; subtle local bevel shading; very restrained variation between slabs. Paving grid aligned horizontally and vertically to image.
Composition/framing: Entire image is flat paving only, exact overhead, no perspective, no horizon. 4 equal columns by 4 equal rows of square slabs occupy all image with half-width grout on outer image edges so tiling creates consistent full grout. Every slab identical size. Each edge must continue perfectly into opposite edge for repeating.
Constraints: Output square 1024x1024; no vignette, no overall gradient, no drop shadows, no lighting falloff, no chips, no cracks, no moss, no dirt, no debris, no plants, no objects, no border, no text, no watermark. Avoid diagonal/isometric grid. Quiet texture for colorful game sprites.
```

