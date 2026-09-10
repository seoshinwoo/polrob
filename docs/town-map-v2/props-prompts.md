# TownMapV2 — newly generated prop assets

All seven sprites were generated from scratch with the built-in `image_gen` tool. No TownMapV1 prop art or repository prop art was reused. The original user police and police-station images supplied only the art style and front-elevated camera angle. Actual alpha channels were verified after generation and after `sips` downscaling. No Python image editing or external image API was used.

References:

- `/Users/seoshinwoo/Library/Mobile Documents/com~apple~CloudDocs/Projects/PolRob/Assets/police_station.png`
- `/Users/seoshinwoo/Library/Mobile Documents/com~apple~CloudDocs/Projects/PolRob/Assets/police.png`

Selected full-resolution generated sources are retained in `docs/town-map-v2/sources/{oak,birch,bush,crates,lamp,rocks,fountain}.png`. Runtime sprites are in `polrob.Client/Resources/Raw/TownMapV2/props/` under the same names. Mechanical downscale: `sips -Z 512` (lamp: `sips -Z 256`), preserving alpha and aspect ratio. No cropping or palette modification was performed outside imagegen.

Initial outputs with false painted transparency were repaired with imagegen. Oak required an additional imagegen palette correction to restore muted moss colors after an alpha repair raised its saturation. All prompts below are exact.

## Runtime alpha verification

SkiaSharp decoded every runtime file as premultiplied RGBA, with transparent surrounding pixels. Inclusive visible bounds use alpha >= 16 to exclude near-invisible generation noise. The runtime renderer may use these bounds while preserving the original alpha channel.

| Sprite | PNG size | Visible bounds | Visible size | Visible width / height |
|---|---|---|---|---|
| oak | 512 × 499 | (9,6)–(505,475) | 497 × 470 | 1.0574 |
| birch | 394 × 512 | (37,4)–(364,487) | 328 × 484 | 0.6777 |
| bush | 512 × 341 | (20,36)–(490,286) | 471 × 251 | 1.8765 |
| crates | 512 × 468 | (66,11)–(446,442) | 381 × 432 | 0.8819 |
| lamp | 192 × 256 | (18,3)–(173,246) | 156 × 244 | 0.6393 |
| rocks | 512 × 341 | (27,5)–(485,331) | 459 × 327 | 1.4037 |
| fountain | 512 × 341 | (12,23)–(497,316) | 486 × 294 | 1.6531 |

## oak

Selected source: `/Users/seoshinwoo/.codex/generated_images/01a0796f-ee8c-7b83-b6df-56e34db71785/exec-aab9d45a-ddfa-4838-a785-63a7f60ce64c.png`

Initial source: `/Users/seoshinwoo/.codex/generated_images/01a0796f-ee8c-7b83-b6df-56e34db71785/exec-c819417c-d8f9-4a79-bbfc-75c454a2c738.png`

Initial prompt:

```text
Use case: stylized-concept. Create one brand-new transparent PNG mature oak tree game sprite. The two attached images are STYLE and CAMERA references only, not subjects to copy. Match their clean dark outlines, controlled smooth cartoon shading, dimensional 2D forms and directly frontal elevated high-angle perspective. The oak has a broad naturally irregular ASYMMETRICAL crown made of six or seven overlapping chunky rounded foliage masses, moss and muted emerald green, subdued olive highlights, darker forest-green lower folds. Large simple readable leaf clumps, no individually intricate leaves, no speckled texture, not neon lime. The canopy dominates the sprite, and only a tiny brown trunk peeks out centrally beneath it. A natural mature tree, not a perfect ball or ornamental topiary. Camera looks down from the front, top surfaces very visible, no isometric tilt or long side trunk. Entire single tree isolated, no grass, planter, soil, flowers, surrounding objects or cast ground shadow. Genuine transparent background, real RGBA alpha pixels outside the outline. No ground plane. Center sprite with narrow padding, no clipping. No text, badge or emblem.
```

Alpha repair prompt:

```text
Cut out this oak tree. Make everything outside its dark outline transparent. Deliver the tree as a transparent PNG sticker with real alpha transparency. Preserve the tree shape, foliage colors and drawing style. Transparent background.
```

Palette correction source: `/Users/seoshinwoo/.codex/generated_images/01a0796f-ee8c-7b83-b6df-56e34db71785/exec-987c56dc-ca99-47df-910e-3af4d7ced04f.png`

Palette correction prompt:

```text
Edit ONLY the foliage COLORS of this transparent oak-tree game sprite. Strongly reduce saturation and brightness of the yellow-lime highlights, changing them to subdued medium moss green and soft grayish olive green. Main foliage should be natural muted moss/emerald, dark forest-green shadows. No fluorescent chartreuse or bright neon lime anywhere. Keep the exact irregular canopy silhouette, leaf clumps, small trunk, elevated frontal angle, clean 2D cartoon linework and shading structure. Do not add/remove objects. Preserve the existing true transparent alpha background and keep the whole tree as a transparent PNG cutout.
```

Final alpha repair prompt:

```text
Cut out this dark moss-green oak tree. Make everything outside its dark silhouette fully transparent. Deliver the whole tree as a transparent PNG sticker with true RGBA alpha. Preserve its muted DARK desaturated grayish moss and forest-green leaf colors EXACTLY. Do not brighten foliage and do not raise saturation. Keep the exact tree, trunk, drawing and shape. Only remove the background. Transparent background.
```

## birch

Selected source: `/Users/seoshinwoo/.codex/generated_images/01a0796f-ee8c-7b83-b6df-56e34db71785/exec-7022b146-0aec-4c37-9658-cc3d402fd3a2.png`

Initial source: `/Users/seoshinwoo/.codex/generated_images/01a0796f-ee8c-7b83-b6df-56e34db71785/exec-3a8d66d5-f523-4ac1-8888-6d5b7bd551f5.png`

Initial prompt:

```text
Create a transparent PNG sticker of one NEW oval-canopy birch tree for a 2D game. Real alpha transparency outside the single tree, exactly like the transparent cutout reference assets. Reference images supply only the consistent art style and elevated straight-from-front camera. Clean dark outlines, smoothly controlled cartoon shading, simple chunky natural foliage masses. A naturally asymmetric upright oval crown in soft light olive greens and muted sage highlights, darker forest-green undersides; wide upper left lobe and slightly smaller right lobes. Leaf texture simplified, canopy occupies nearly all the height, tiny pale birch trunk with two dark marks barely visible at its bottom. Top surfaces are prominently visible, the camera looks down from the FRONT at a high angle, no isometric side view. Distinct tree silhouette, not a sphere, not a hedge, no neon lime. Preserve this game's polished dimensional 2D drawing style. Isolated full tree, narrow transparent margins. No grass, planter, soil disk, cast ground shadow, environment, additional objects or text.
```

Alpha repair prompt:

```text
Cut out this birch tree. Make everything outside its dark outline transparent. Deliver the tree as a transparent PNG sticker with real alpha transparency. Preserve its naturally irregular oval canopy, small pale trunk, muted soft olive green foliage, low saturation and controlled shading exactly. Do not brighten or increase saturation. Transparent background.
```

## bush

Selected source: `/Users/seoshinwoo/.codex/generated_images/01a0796f-ee8c-7b83-b6df-56e34db71785/exec-179c60f1-2f57-4bc6-89a6-a76e3512c345.png`

Initial source: `/Users/seoshinwoo/.codex/generated_images/01a0796f-ee8c-7b83-b6df-56e34db71785/exec-179c60f1-2f57-4bc6-89a6-a76e3512c345.png`

Initial prompt:

```text
Create ONE brand-new low natural shrub transparent PNG game sprite. Use the two references ONLY for their clean dark linework, smooth controlled dimensional 2D cartoon shading and high-angle camera directly from the FRONT. Subject is a dense uneven bush wider than tall, about 1.5 times wide, formed from five overlapping chunky foliage clumps of different sizes with a low irregular silhouette. Muted moss green, soft olive highlights and forest-green shaded lower folds. Not neon green, not geometric topiary, no single round ball. Top surfaces clearly visible. No exposed trunk. No separate leaves, flowers, planter, grass, soil, ground plane, drop shadow, environment or surrounding objects. One isolated connected shrub; no text or emblem. Deliver a transparent PNG sticker cutout with real alpha, generous clear pixels all around the silhouette. Full object visible and centered.
```

## crates

Selected source: `/Users/seoshinwoo/.codex/generated_images/01a0796f-ee8c-7b83-b6df-56e34db71785/exec-84a20f94-21e2-4145-b059-6562262e75d0.png`

Initial source: `/Users/seoshinwoo/.codex/generated_images/01a0796f-ee8c-7b83-b6df-56e34db71785/exec-84a20f94-21e2-4145-b059-6562262e75d0.png`

Initial prompt:

```text
Create ONE brand-new transparent PNG game obstacle sprite: one connected L-shaped stack of exactly THREE stout wooden shipping crates. Two crates sit next to one another and the third sits directly on the left crate. Directly front-facing high-angle camera matching the supplied police station and police character, with broad crate TOP planes clearly visible and short FRONT faces. All crate front edges horizontal, vertical edges upright, NO left/right isometric side facade and no diagonal rotation. Style ONLY from the references: clean dark outlines, rounded bevels, smooth dimensional 2D cartoon shading, substantial simple forms. Honey-golden timber, thick squared wooden frame, broad simple planks, large X braces on front faces and very few dark nail dots. Medium warm color, restrained highlights. No letters or labels. One connected cluster, no additional props, ground, pavement, grass, ground shadow or environment. Deliver full isolated sprite as transparent PNG sticker with genuine RGBA alpha transparency and a narrow clear margin.
```

## lamp

Selected source: `/Users/seoshinwoo/.codex/generated_images/01a0796f-ee8c-7b83-b6df-56e34db71785/exec-bea01ab2-d420-4e4b-a8fe-fba3381030cf.png`

Initial source: `/Users/seoshinwoo/.codex/generated_images/01a0796f-ee8c-7b83-b6df-56e34db71785/exec-7fb064f2-fd00-46d9-835f-f210d0ecdd79.png`

Initial prompt:

```text
Create ONE brand-new transparent PNG game obstacle sprite: a simple squat park streetlamp, dark graphite charcoal metal and soft warm pale yellow glass. Use the supplied images ONLY to match the clean dark outline and smoothly shaded dimensional 2D cartoon illustration style and directly front-facing high-angle elevated camera. The broad square lamp roof is seen prominently from above, a short warm glass chamber under it, one short thick graphite post and small broad rounded foot. Front symmetry, clean simple chunky shapes, no isometric rotation. Overall width roughly 45 percent of height. Entire object visible. NO badge, shield, emblem, decorative gold trim, finial or lettering. Unlit daylight glass, absolutely NO glow, halo, rays, outer shading or ground shadow. No pavement, ground disk, grass or environment. A single isolated transparent PNG sticker with real alpha transparency all around the cutout silhouette, narrow clear margins.
```

Alpha repair prompt:

```text
Cut out this streetlamp. Make everything outside its dark outline transparent. Deliver the entire streetlamp as a transparent PNG sticker with real alpha transparency. Preserve the graphite metal, pale warm glass, simple design, drawing style and elevated front view. No glow or shadow outside the silhouette. Transparent background.
```

## rocks

Selected source: `/Users/seoshinwoo/.codex/generated_images/01a0796f-ee8c-7b83-b6df-56e34db71785/exec-afba4f1c-2a05-4b73-b0e5-56fe01d471e1.png`

Initial source: `/Users/seoshinwoo/.codex/generated_images/01a0796f-ee8c-7b83-b6df-56e34db71785/exec-fafaafe4-65f2-4300-910f-192a754de949.png`

Initial prompt:

```text
Create ONE brand-new transparent PNG game obstacle sprite, a connected small cluster of exactly three smooth rounded gray boulders of different sizes. One large irregular boulder back left, a medium one back right and a low small boulder in front, all touch and overlap into one readable natural group. Match the reference assets' crisp thin dark outlines, clean softly beveled contours and controlled smooth dimensional 2D cartoon shading. Camera is directly from the FRONT at a high downward angle: top surfaces dominate, very short front faces, no isometric diagonal composition. Cool slate gray rocks with softer warm gray highlights and darker lower facets, very few broad angular facets for shape, no intricate texture, no speckles, no moss. Full isolated connected stone group on actual transparent RGBA alpha background. No ground, shadow beyond the outline, grass, soil, flowers, landscape, lettering or extra stones. Generous transparent clear pixels around sprite.
```

Alpha repair prompt:

```text
Cut out this group of three gray rocks. Make everything outside the group's dark silhouette transparent. Deliver the entire three-rock group as a transparent PNG sticker with real alpha transparency. Preserve the gray rock shapes, controlled shading and drawing style. Do not add anything. Transparent background.
```

## fountain

Selected source: `/Users/seoshinwoo/.codex/generated_images/01a0796f-ee8c-7b83-b6df-56e34db71785/exec-93ec2cf9-ac3e-4a5f-938e-05e7a8679888.png`

Initial source: `/Users/seoshinwoo/.codex/generated_images/01a0796f-ee8c-7b83-b6df-56e34db71785/exec-8deebe75-d210-4146-a20a-71df65270730.png`

Initial prompt:

```text
Create ONE brand-new transparent PNG small town fountain game sprite. References supply ONLY the clean dark outline, rounded bevels, smooth controlled dimensional 2D cartoon shading and high-angle camera looking straight from the FRONT. A round pale warm-gray stone basin of modest height, turquoise water, a very low simple central stone nozzle with one short turquoise water jet and a few tiny rounded droplets falling inside the basin. The basin is shown mostly from above: water surface reads as a broad ellipse, nearly round, with only a narrow short front stone rim visible. Simple broad stone segments with restrained beige-gray highlights and dark lower edges. No tall tower or decorative sculpture. No isometric rotation. Full fountain isolated; actual RGBA transparent background with clear transparent pixels around the silhouette. No ground platform, surrounding pavement, grass, flowers, cast ground shadow, environment, benches, signs, text or additional objects. Crisp readable game cutout, not photorealistic.
```

Alpha repair prompt:

```text
Cut out this fountain. Make everything outside its dark outer stone basin outline transparent. Deliver the whole fountain as a transparent PNG sticker with real alpha transparency. Keep all water INSIDE the basin fully opaque smooth solid turquoise with soft shading and simple ripple arcs; remove the square pattern currently visible through the water. Preserve the stone rim, front-elevated viewpoint, short central nozzle and water jet, clean outlined cartoon drawing. No glow or shadow outside the outline. Transparent background.
```
