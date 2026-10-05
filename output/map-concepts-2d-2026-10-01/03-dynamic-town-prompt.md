# 비대칭 추격 마을 — 배치 재구성 프롬프트

내장 image_gen. v2 이미지는 그림체와 에셋 참고이며, 새 구조로 재배치합니다.

```text
Use case: compositing / level-layout redesign.
Asset: a NEW, much more dynamic playable 2D cops-and-robbers town layout, portrait2:3.
Image1 is the APPROVED ART STYLE AND ASSET KIT reference. Treat it as a box of reusable sprites to REARRANGE. Keep its exact simple roof designs, illustrated tree silhouettes, short cream walls, wood crates, police station, jail, outline weight, muted blue/coral/teal palette, shallow top-down camera and minimal2D rendering. The source LAYOUT must be replaced radically: user finds its repeated grid blocks boring. This is a LEVEL DESIGN change, not an art-style change.
No characters in this output: original game sprites will be overlaid later.

MANDATORY: police building in UPPER-LEFT corner zone; jail immediately beside it to the RIGHT. Both are part of the same broad open civic forecourt. Keep them recognizable and same relative scale as source.
ALL GROUND MADE FROM SQUARE TILES: only horizontal/vertical roads and paths, hard90-degree ground corners, no curved roads, no diagonal ground edges, no circular plazas. Foreground trees may have rounded silhouettes exactly as source. Use the source's blue-gray roadway, pale stone paving and sage lawn. Clean broad color fields, subtle sparse paving joints only. Work on a32px conceptual module at1024 width.
Replace the old regular street grid with an ASYMMETRIC arrangement of districts. No continuous straight central spine, no equally spaced cross streets, no mirrored left/right paired buildings, no individual house sitting alone in every identical city block.

NEW SPATIAL COMPOSITION, top-to-bottom:
- UPPER LEFT: police/jail civic compound, about the upper-left half of top quarter. Broad paved forecourt below, access from both east and south, short broken wall sections with generous gaps.
- UPPER RIGHT: two modest offset homes and a few trees in a single stepped garden block, different from the civic compound; small through-alley between them. Keep the source assets.
- MIDDLE LEFT: a compact STAGGERED MARKET CLUSTER of THREE source shops/houses in an irregular L-shaped GROUP (separate buildings with clear gaps, NOT a solid merged roof). Form a small pocket courtyard opening in THREE directions, one short rear alley, and a little gap through the cluster. Shops are not in straight rows. This is the close-range chase area with several fast corner choices. Approximate widths: small alleys48-64px in a1024px image, spacious enough for two player bodies.
- MIDDLE RIGHT: a contrasting broad open RECTANGULAR SQUARE, shifted right of center, about200-260px wide. It is paved pale stone, NOT enclosed by a road ring. Two staggered detached short L-wall segments and a single source tree island break long sight lines while leaving generous passing room on all sides. It has four offset entries and connects the market, upper garden and lower warehouse yard. It must visibly read as a large empty tactical space, not another two-building city block.
- LOWER LEFT: a long narrow STEPPED rectangular green park, different proportions from other districts, with a straight paved through-path and two side exit paths. Four source canopy trees and1-2 short hedge modules provide cover; complete separate silhouettes. One little source home near an end rather than one home per island.
- LOWER RIGHT: a compact DEPOT YARD. One larger L-shaped low warehouse and one smaller rectangular source-blue-roof warehouse, with a few detached source crates and short wall pieces making a loading courtyard that has THREE open exits. The L-shaped building is the only new footprint variant and must be built from the exact same flat roof/facade styling as the existing blue buildings. No realistic industrial equipment.
- LOWER EDGE: a broad return route and open connection back up the left side, with occasional cover, not a third repeated residential row.

ROUTE TOPOLOGY:
One broad main blue-gray road links the civic forecourt to the square via an off-center right-angle jog, then connects the depot and park edge. Roads turn at different positions and have T-junctions, not a plaid grid. Add a second smaller loop through the market and a larger southern loop around park/depot; they share the square as one meeting place but ALSO have a bypass so the square is not a single bottleneck. Add a short market-back passage and a straight park-cutting shortcut, clearly connected at both ends. All major spaces have at least two exits. Do not make the entire map a long maze, racetrack ring, or one zigzag corridor.
Alternate wide90-128px main routes with short48-64px alleys at1024 width. At least50 percent of the arena is clear walkable terrain. Terrain color does NOT by itself block movement. Buildings, low walls, trees and crates physically create corners. All gaps must be visibly useful, no tiny accidental1-pixel slits. Long routes are interrupted by corners or staggered cover without being sealed off.

STYLE LOCK:
Imagine moving copies of the SAME approved sprites in a level editor, not drawing a different game. Flat colored roofs with only thin lower eaves, very shallow cream facade, plain blue windows and brown doors, minimal striped awnings. Overhead2-tone lobed tree canopy, no trunks. Thin clean dark outlines, barely any contact shading, no bulky sides or bevels. Keep source colors, detail level and simplicity. No added realism, richer decoration, 3D shadows, perspective, grain, gradients or new ornamental objects. Keep scale consistent and individual assets separable with complete outlines. Do not overlap sprites.
Allowed asset count/type adjustments for this new layout; total roughly9-11 buildings including police, one jail,10-14 trees, some short walls and crates. No extra cars, lamps, benches, flowers, fountains or water.
Only allowed text: POLICE on station. No route arrows, colored diagrams, labels, title, border or UI. Deliver only the complete new map image. The new layout should be UNMISTAKABLY asymmetrical and different from the reference at thumbnail size.
```
