# New civic sprites — built-in ImageGen

## Successful alpha extraction

Each final alpha extraction used the newly generated source as the sole edit target. The original style references were not supplied during cutout editing. Every final file has an RGBA alpha range of 0–255 verified after mechanical `sips -Z` downscaling. No manual background cutout or Python image editing was used.

### police_station

Cut out this police-station building. Make everything outside its dark outline transparent. Deliver a transparent PNG sticker with real alpha transparency. Preserve the police-station building. Transparent background.

Original alpha result: `sources/police_station.alpha-final.png`. Runtime: `../../polrob.Client/Resources/Raw/TownMapV2/props/police_station.png`.

### bank

Cut out this bank building. Everything outside its dark outline transparent. Transparent PNG sticker with real alpha. Preserve the bank building. Transparent background.

Original alpha result: `sources/bank.alpha-final.png`. Runtime: `../../polrob.Client/Resources/Raw/TownMapV2/props/bank.png`.

### townhouse

Cut out this townhouse building. Make everything outside its dark outline transparent. Deliver a transparent PNG sticker with real alpha transparency. Preserve the townhouse building. Transparent background.

Original alpha result: `sources/townhouse.alpha-final.png`. Runtime: `../../polrob.Client/Resources/Raw/TownMapV2/props/townhouse.png`.

### jail

Cut out this jail cage and its opaque interior floor. Everything outside its dark outline transparent. Transparent PNG sticker with real alpha. Preserve the jail cage and its opaque interior floor. Transparent background.

Original alpha result: `sources/jail.alpha-final.png`. Runtime: `../../polrob.Client/Resources/Raw/TownMapV2/props/jail.png`.

See `civic-alpha-report.json` for final dimensions, alpha bounds, and visible silhouette aspect ratios. Police/bank use a landscape canvas with margins; renderer should use the alpha >= 16 bounds as recorded.


## Initial output verification

- All initial generated files are 1254×1254 RGB PNGs with no alpha channel despite requesting actual transparency.
- Police station background-repair output also has no alpha channel: ImageGen replaced the checkerboard with white.
- Preserved unmodified sources: `sources/police_station.generated.png`, `sources/police_station.background-repair.png`, `sources/bank.generated.png`, `sources/townhouse.generated.png`, `sources/jail.generated.png`.
- These source files are not yet transparent runtime sprites. No external pixel editing has been performed.

## jail

Use case: stylized-concept. Asset type: entirely NEW standalone 2D game sprite, an open-top jail holding cage for a front-elevated top-down mobile game. Original input police_station image is art-style/camera reference ONLY; police character is outline/shading reference ONLY. Generate new art, no copying or reuse. Camera looks straight at FRONT from above, exactly parallel horizontal rear/front edges, vertical left/right edges in the ground-plane projection, no isometric yaw, no side-facade perspective. A compact rectangular roofless holding yard: a clearly visible large EMPTY opaque dark-gray paving interior floor occupying 70% of sprite, enclosed by low chunky graphite metal fence on all four sides, rounded-corner square posts and simple widely spaced vertical bars, navy-blue narrow rear service wall with one very small gold shield. Front low gate centered, front fence low enough detained game characters remain visible when rendered on top of interior. Show upper surfaces of fence rails and the full interior floor. The full footprint is a rectangle, not a diamond or tilted quadrilateral. Same clean dark outline, smooth restrained shaded 2D illustration style as references. ONE centered sprite, genuinely TRANSPARENT alpha background outside the outermost cage perimeter; inside paving floor opaque. No checkerboard pixels, white backdrop, outside shadow, surrounding pavement, yard beyond cage, vegetation, roof, giant badge, signage, people, prisoners or other objects. Whole cage in frame with 3% transparent margins, square PNG approx1024.

## police_station alpha repair

Preserve the newly generated police-station silhouette and illustration exactly. Remove the painted checkerboard completely and output a real transparent PNG cutout with actual alpha zero outside the station; no replacement background and no checkerboard pixels. Do not change the building, framing, shading, outlines or details.

References are the user's original police_station.png and police.png from the iCloud PolRob Assets folder. They are style/camera references only. Every delivered sprite is newly generated; no prior map art is reused.

## police_station

Use case: stylized-concept. Asset type: standalone 2D sprite for a front-elevated top-down mobile cops-and-robbers game. Input image 1 is ONLY an art-style and camera-angle reference (the original police station); input image 2 is ONLY an outline, color and shading reference (the original police character). Create wholly NEW artwork, do not extract, trace, copy, crop or reuse the reference building. Match their clean illustrated outlines, restrained smooth cel-like gradients, mildly rounded chunky edges and rich but controlled colors. Camera looks straight toward the building FRONT from above; roof plane dominates approximately 60% of silhouette, shallow FRONT facade occupies bottom approximately 35%; horizontal roof edges perfectly horizontal, bilateral composition; NO visible LEFT or RIGHT SIDE FACADE, NO isometric rotation, NO diagonal building orientation, NO vanishing-point side perspective. The view angle must match original reference. Exactly ONE centered sprite, full object entirely in frame, minimal 2-4% transparent margin. Output PNG with GENUINE ALPHA TRANSPARENCY around the silhouette; no background color, no checkerboard painted into pixels, no scenery, no floor tile, no ground, no yard, no landscaping, no vegetation, no outside cast shadow, no lettering or watermark. Around 1024x1024 square. Subject: a new compact municipal police station, broad low rounded-rectangle deep navy-blue roof, ivory trim, front ivory masonry wall. A small single rooftop air-conditioning unit, centered slightly rearward. Front central navy double doorway and two paired small blue windows. Discreet golden shield emblem immediately above the door, no written POLICE sign. Bottom is just the front wall and a shallow doorstep, without any surrounding pavement. Newly designed facade and roof proportions clearly different from reference, preserving the reference illustration style.

## bank

Use case: stylized-concept. Asset type: standalone 2D sprite for a front-elevated top-down mobile cops-and-robbers game. Input image 1 is ONLY an art-style and camera-angle reference (the original police station); input image 2 is ONLY an outline, color and shading reference (the original police character). Create wholly NEW artwork, do not extract, trace, copy, crop or reuse the reference building. Match their clean illustrated outlines, restrained smooth cel-like gradients, mildly rounded chunky edges and rich but controlled colors. Camera looks straight toward the building FRONT from above; roof plane dominates approximately 60% of silhouette, shallow FRONT facade occupies bottom approximately 35%; horizontal roof edges perfectly horizontal, bilateral composition; NO visible LEFT or RIGHT SIDE FACADE, NO isometric rotation, NO diagonal building orientation, NO vanishing-point side perspective. The view angle must match original reference. Exactly ONE centered sprite, full object entirely in frame, minimal 2-4% transparent margin. Output PNG with GENUINE ALPHA TRANSPARENCY around the silhouette; no background color, no checkerboard painted into pixels, no scenery, no floor tile, no ground, no yard, no landscaping, no vegetation, no outside cast shadow, no lettering or watermark. Around 1024x1024 square. Subject: a new compact bank with broad low rounded-rectangle forest-green roof and light cream masonry front wall, ivory roof coping. Front center dark teal double entrance with one small circular gold coin emblem just above its lintel, two symmetrical paired rectangular blue windows. A very small discreet rooftop ventilation unit near back center. Restrained thick near-black contours, simple cream stone courses. NO giant dollar sign, NO giant roof-mounted symbol, no columns that extend the footprint, no yard.

## townhouse

Use case: stylized-concept. Asset type: standalone 2D sprite for a front-elevated top-down mobile cops-and-robbers game. Input image 1 is ONLY an art-style and camera-angle reference (the original police station); input image 2 is ONLY an outline, color and shading reference (the original police character). Create wholly NEW artwork, do not extract, trace, copy, crop or reuse the reference building. Match their clean illustrated outlines, restrained smooth cel-like gradients, mildly rounded chunky edges and rich but controlled colors. Camera looks straight toward the building FRONT from above; roof plane dominates approximately 60% of silhouette, shallow FRONT facade occupies bottom approximately 35%; horizontal roof edges perfectly horizontal, bilateral composition; NO visible LEFT or RIGHT SIDE FACADE, NO isometric rotation, NO diagonal building orientation, NO vanishing-point side perspective. The view angle must match original reference. Exactly ONE centered sprite, full object entirely in frame, minimal 2-4% transparent margin. Output PNG with GENUINE ALPHA TRANSPARENCY around the silhouette; no background color, no checkerboard painted into pixels, no scenery, no floor tile, no ground, no yard, no landscaping, no vegetation, no outside cast shadow, no lettering or watermark. Around 1024x1024 square. Subject: a new modest compact two-storey townhouse with a broad warm terracotta red-brown tiled roof, cream brick FRONT facade. The two storeys are compressed into the shallow bottom front facade to preserve the elevated game camera, not a tall front elevation. One dark wooden centered doorway at the bottom, two simple small blue windows above/alongside in a balanced arrangement; dark warm-brown eaves and clean ivory corners. Tile pattern broad and simple, not finely textured. No chimney needed, no yard, no garden, no furniture.
