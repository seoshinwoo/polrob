# Town map prop generation prompts

Created with the built-in `image_gen` tool. All final workspace PNG files have real alpha transparency. No external API/CLI fallback was used. Original generated outputs are retained under `$CODEX_HOME/generated_images`.

Style references used for every initial generation:

- `/Users/seoshinwoo/Library/Mobile Documents/com~apple~CloudDocs/Projects/PolRob/Assets/police_station.png`
- `/Users/seoshinwoo/Documents/code/projects/polrob/polrob.Client/Resources/Images/2D/bank.png`

References supply only visual style and front-elevated camera; the delivered warehouse, crate, and lamp are newly generated artwork.

## Alpha inspection

Measured with SkiaSharp. Bounds below are inclusive pixel coordinates, using alpha >= 16 to exclude isolated nearly transparent generation noise. Original alpha has been preserved in copied workspace assets.

| Asset | Original PNG dimensions | Visible alpha bounds | Visible aspect ratio |
|---|---|---|---|
| warehouse.png | 1312 × 1199 | (66,12)–(1245,1143), 1180 × 1132 | 1.0424 |
| crate.png | 1254 × 1254 | (40,33)–(1213,1200), 1174 × 1168 | 1.0051 |
| lamp.png | 1024 × 1536 | (151,135)–(873,1403), 723 × 1269 | 0.5697 |

These dimensions precede any later map asset builder mechanical trim/downsize. Source outputs remain unchanged.

## warehouse

First result had a painted checkerboard and no alpha; superseded.

Source output: `/Users/seoshinwoo/.codex/generated_images/01a0796f-ee8c-7b83-b6df-56e34db71785/exec-8705abfc-6d94-4c9e-a806-b86d58c86462.png`

Exact prompt:

```text
Use case: stylized-concept. Asset type: one transparent PNG building sprite for a 2D top-down game. Input images are STYLE REFERENCES ONLY, not edit targets. Create a NEW small warehouse building that belongs exactly to the illustrated police station and bank asset family: crisp black/dark brown outline, clean curved bevels, rich but controlled blue and cream colors, polished hand-painted cartoon shading, substantial simple shapes. Camera is elevated directly from the FRONT, near-orthographic, centered and symmetric: roof occupies about 65 percent of the height, short front facade about 35 percent. Horizontal roof and facade edges are horizontal; NO left or right side wall and NO rotated isometric view. Subject: compact square warehouse, blue flat metal roof with a few broad seams and a tiny simple roof vent; warm tan front facade, dark blue broad closed loading shutter in center, two small windows to either side. No signs or letters. Absolutely no courtyard, floor, pavement, grass, ground patch, trees, props, cast ground shadow, background or environment. A single isolated building on genuine transparent alpha, not checkerboard painted into art. Full sprite visible with only a narrow transparent safety margin. No photorealistic texture, no grit, no tiny clutter, no additional objects. Match the clean softly dimensional 2D illustration and camera angle of the references faithfully.
```

## warehouse alpha repair

Selected output, copied to `polrob.Client/Resources/Raw/TownMap/props/warehouse.png`.

Source output: `/Users/seoshinwoo/.codex/generated_images/01a0796f-ee8c-7b83-b6df-56e34db71785/exec-a2bcf70b-9fcb-45b3-be38-56b1c63fdff7.png`

Exact prompt:

```text
Use case: background-extraction. EDIT TARGET: attached warehouse sprite. Remove the pale painted checkerboard surrounding the building and replace it with real transparent alpha. Keep every pixel inside the dark building outline, the building dimensions, design, exact colors, front-elevated viewpoint, all shading and details unchanged. Do not redraw or restyle the warehouse. Output a genuine RGBA PNG with transparent pixels all around the full isolated building. This is a game sprite, it must have actual transparency, not a transparency illustration. No visible checkerboard, white, gray or black background. Preserve entire building and use a narrow transparent margin.
```

## crate

First result had a painted checkerboard and no alpha; superseded.

Source output: `/Users/seoshinwoo/.codex/generated_images/01a0796f-ee8c-7b83-b6df-56e34db71785/exec-68e79b05-b085-409b-bbf9-bfd22a166038.png`

Exact prompt:

```text
Use case: stylized-concept. Asset type: one transparent PNG obstacle sprite for a 2D top-down game. Input images are STYLE REFERENCES ONLY, not edit targets. Create a NEW single large wooden shipping crate that belongs exactly to the illustrated police station and bank asset family: crisp dark brown outline, clean curved bevels, honey golden brown wood, polished cartoon cel shading, substantial very simple shapes. Camera is elevated directly from the FRONT, near-orthographic, centered and symmetric: square lid/top plane occupies about 60 percent of sprite height, a short rectangular front plane about 40 percent. Horizontal crate edges are horizontal; NO left or right side plane and NO rotated isometric view. Subject: one stout closed wooden shipping crate, broad plank lid bounded by raised simple timber rim, X-braced front face, only a few tiny dark nails, rounded beveled corners. No labels or letters. Absolutely no floor, pavement, grass, ground patch, props, cast ground shadow, background or environment. Single isolated crate on a genuine TRANSPARENT BACKGROUND with alpha channel. Please return actual RGBA pixels and no white or checkerboard backdrop. Full sprite visible with a narrow transparent safety margin. No photorealistic woodgrain, no grit, no tiny clutter, no additional objects. Match the clean softly dimensional 2D illustration and camera angle of the references faithfully.
```

## crate alpha repair 1

First repair still had a painted checkerboard and no alpha; superseded.

Source output: `/Users/seoshinwoo/.codex/generated_images/01a0796f-ee8c-7b83-b6df-56e34db71785/exec-23bd74d7-9952-4e79-a577-f725b2aa7bc2.png`

Exact prompt:

```text
Use case: background-extraction. EDIT TARGET: attached wooden shipping crate sprite. Remove the pale painted checkerboard surrounding the crate and replace it with real transparent alpha. Keep the crate design, colors, front-elevated viewpoint, bold clean outline, shading, wooden plank lid, X brace, and proportions unchanged. Output a genuine RGBA PNG with transparent pixels all around the full isolated crate. This is a game sprite, it must have actual transparency, not a transparency illustration. No visible checkerboard, white, gray or black background. Preserve entire crate and use a narrow transparent margin. No cast shadow or outer glow. Change only the background.
```

## crate alpha repair 2

Selected output, copied to `polrob.Client/Resources/Raw/TownMap/props/crate.png`.

Source output: `/Users/seoshinwoo/.codex/generated_images/01a0796f-ee8c-7b83-b6df-56e34db71785/exec-21a46a20-1b10-4722-85bc-f852bb28ca96.png`

Exact prompt:

```text
Cut out this wooden box. Make everything outside its black outline transparent. Deliver the box as a transparent PNG sticker with real alpha transparency. Preserve the wooden box. Transparent background.
```

## lamp

Selected output, copied to `polrob.Client/Resources/Raw/TownMap/props/lamp.png`. Real alpha was present in the first result.

Source output: `/Users/seoshinwoo/.codex/generated_images/01a0796f-ee8c-7b83-b6df-56e34db71785/exec-65de6e33-cb9f-4557-bcd7-74907a396c6d.png`

Exact prompt:

```text
Use case: stylized-concept. Asset type: one isolated obstacle sprite for a 2D top-down game. Input images are STYLE REFERENCES ONLY. Create a NEW short stout park lamp that belongs exactly to the police station and bank illustration family: crisp dark outline, smoothly beveled chunky forms, controlled deep navy metal and warm pale golden glass, polished 2D cartoon shading. Camera elevated directly from the FRONT, near-orthographic, perfectly centered and symmetric, same downward angle as buildings. Subject: one park lamp with a broad square navy cap viewed from above, a small simple warm glass lantern underneath it, short thick navy metal pole and a rounded broad weighted foot. Top surface of the cap clearly visible. Proportions squat and robust, total height only about three times width. Soft unlit daylight glass, no radiating beams, no glow beyond the outline. NO left/right isometric rotation. Absolutely no floor, pavement, ground shadow, ground patch, grass, environment, additional props, text or labels. Entire sprite visible with narrow transparent safety margin. A genuine TRANSPARENT BACKGROUND with alpha channel is essential. No painted white backdrop and no checkerboard squares. No photographic texture, no grit, no tiny ornate details. Match clean softly dimensional 2D illustration camera angle of the references.
```

