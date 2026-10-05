# 연못·공사 구역·회색 도로 수정 프롬프트

내장 image_gen으로 비대칭 추격 마을 시안을 편집합니다.

```text
Use case: precise-object-edit.
Image1 is the selected dynamic2D PolRob map EDIT TARGET. Make THREE requested changes only while keeping its approved simple flat2D art style, source asset designs, orthographic overhead camera, line weight, portrait1024x1536 framing and the rest of its asymmetric layout.
No characters: the exact original sprites will be composited later.

1. A POND IN THE MIDDLE-RIGHT PLAZA:
Replace the solitary large central tree in the right-middle paved square (tree centered roughly x744,y660 in the1024x1536 source) with one attractive small flat overhead POND. Remove that tree, its little grass island and small bush directly below/right. Remove the short upright wall immediately touching that island if needed for the pond's clear outline. Place the pond roughly within x686–864,y596–744, centered in the plaza with generously walkable paving on ALL sides. A modest soft irregular rounded rectangular water silhouette, narrow simple light bank, muted pale blue-green water, one darker flat edge shape and two tiny flat ripple marks. At most two small simple lily-pad disks. No fountain, pool tiles, raised stone bowl, bridge, reeds, rocks or decorative clutter. It should read as a pond and a clear dodge-around obstacle, not a3D object. The surrounding other plaza walls and corner greenery stay, with comfortable passages. The pond is an extractable obstacle sprite; roads and paving remain square-tile terrain.

2. NEUTRAL GRAY ROADS:
Recolor ALL existing blue road surface to a restrained medium-light neutral asphalt GRAY, approximately #939795, with very low saturation and only a tiny cool bias. NO visibly blue road. Keep the EXACT path shape, road widths and square right-angle junctions. Keep warm cream sidewalks and green lawns. Preserve blue/navy roof colors and police building colors; ONLY the road color changes. Clean flat ground color without texture or vignette.

3. A DISTINCT VACANT CONSTRUCTION LOT AT LOWER RIGHT:
Convert the former lower-right depot yard (roughly x564–980,y932–1338) into a modest unfinished CONSTRUCTION / VACANT LOT so this area feels different from the residential streets and green park.
- Replace its pale paved interior with a flat dusty warm sand/tan dirt tile color, clearly different from sidewalks. Rectangular/stepped90-degree boundaries that fit a square tile grid.
- Remove the large L-shaped blue warehouse at x588–778,y1006–1244. In part of its former footprint place a VERY LOW simple gray concrete foundation outline, an incomplete rectangle or L, with one wide open gap. No high walls, room detail, scaffolding or rebar.
- Keep the smaller blue-roof building toward the right edge, interpreting it as a compact site office. Same source style, not a new rendering style.
- Retain/reposition a few source wooden crates as materials. Add only two simple short orange-and-cream striped temporary barrier modules near two edges and one very small neat stack of3 gray planks/concrete beams. No machinery, excavators, trucks, cranes, cones everywhere, signs or rubble.
- At least60 percent of the lot should remain EMPTY walkable dirt so it reads as a vacant work site. Keep broad open access from north, west and south; no sealed fence. Obstacles provide a few new turn choices without clutter.
- Remove tiny lawn patches inside this worksite as appropriate, retaining greenery outside its boundary and the park to the left. Keep the surrounding road network unchanged.

STYLE LOCK: use exactly the same crisp simple illustrated2D rendering as Image1. Few broad color planes, thin dark colored outlines, minimal two-tone shadows only. No extra dimensionality, realistic water, strong cast shadows, gradients, grain or busy texture. Existing buildings, market cluster, park and civic compound remain as close to source as possible. Police stays upper-left, jail directly adjacent. No characters or UI. Only existing POLICE text; no new labels or arrows. Return the complete edited map.
```
