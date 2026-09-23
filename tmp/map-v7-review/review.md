# 건물·상가 개선 시안 v7

## 결과

- final-map.png: 개선한 전체 맵 (1024 × 1536).
- shop-details-with-actual-characters.png: 카페·도넛집·버거집 확대. 실제 캐릭터 PNG를 현재 게임 비율로 직접 합성했습니다.

## 변경

카페와 도넛집에 각각 갈색·분홍색 곡선 간판, 짧은 차양, 양측 진열창과 중앙 입구를 정리했습니다. 버거집은 흰 글자의 빨간 직선 간판으로 구분했습니다. 추가로 생긴 가게 앞 화단은 제거해 모퉁이 주변의 시각적 복잡함을 줄였습니다. 주택에는 작은 창문과 굴뚝 구멍을, 창고에는 셔터 주변 하역장 표시를 더했습니다. 사용자가 승인한 경찰서 디자인은 유지하도록 요청했습니다.

기존 MapAssets 에셋을 디자인 참고로 사용하고 v6 맵의 단순한 그림체·높은 시점·동선을 기준으로 수정했습니다. 내장 image_gen 도구를 사용했으며 CLI/API 대체 경로는 사용하지 않았습니다. 고해상도 렌더를 요청했지만 실제 반환 해상도는 1024 × 1536입니다. 생성 결과의 국소적인 식물/건물 경계는 조금 달라질 수 있어 실제 충돌 배치는 이 시안과 별도로 맞춰야 합니다. 게임 코드나 사용 중인 에셋은 교체하지 않았습니다.

캐릭터는 AppKit으로 char_police.png와 char_robber.png를 직접 합성했습니다. 월드 폭 2560, 충돌 지름 50, 몸통 표시 비율 0.86, 원본 몸통 폭 512를 적용했고 각 맵 구역과 함께 같은 비율로 확대했습니다.

## 생성 프롬프트

```text
Use case: precise-object-edit.
Asset: refined simple overhead 2D cops-and-robbers map, portrait 2:3. Please render at 2048x3072 pixels if possible, maintaining EXACT same composition scaled uniformly for sharper inspection.
INPUTS: Image 1 is the EDIT TARGET and authoritative map style/layout. Its newly approved blue POLICE STATION MUST stay exactly as it is. Image 2 is a contact sheet of the game's original assets: top row police station, jail, café, donut shop; bottom row burger shop, warehouse, orange house. It supplies recognizable DESIGN DETAILS only, NOT its rich rendering or lower camera. Images 3 and 4 are the existing unchangeable character sprites, for checking compatible simplicity; do NOT draw characters.
Goal: improve the other buildings, especially the 3 little shops, to the same appealing purposeful design quality as the approved police station. They should look like coherent little storefronts, not merely identical boxes with different stickers. But still a VERY SIMPLE, light, reusable game-asset kit.
PRESERVE: all roads, walkable gaps, roofs' collision footprints, walls, bushes, trees, crates, landscape colors and the complete police station and jail. Keep every building within its existing total silhouette envelope. Tiny awnings/steps must not protrude farther into the narrow alleys. Keep the current steep almost overhead orthographic view, with roof the dominant plane. Do not lower camera to show more shopfront. Match the approved station's simple dark outlines, rounded corners, two-tone shading and SHORT facade. No realistic 3D, glossy plastics, long cast shadows, brick/tile grain, ornamental clutter or new standalone props.
REDESIGN THREE SHOPS (positions below use the original 1024x1536 coordinate frame; double them at 2048x3072):
1 CAFE at x205–320,y395–516: cozy walnut-brown rounded roof rim, quiet cream inset roof with a clean overhead coffee-cup-and-saucer motif in 3 flat colors (no latte art or photorealism). Short warm cream/brown facade, a small curved sign integrated above the entry reading exactly 'CAFE', three or four broad brown/cream awning panels, a visibly glazed double entry and one SIMPLE dark-blue display window on either side. Distinct cozy coffeehouse character with very few shapes.
2 DONUT SHOP at x208–329,y592–714: soft rose-pink curved roof cornice, ivory flat roof with a clean chunky pink donut ring and 3–4 sprinkles, slightly rounded sign reading exactly 'DONUTS'. Short pink/cream scalloped awning with only a few broad panels; small central glass door and two blue display windows with extremely simple donut-circle decals, no detailed interiors. A bakery silhouette, rounded and playful, no gloss.
3 BURGER SHOP at x562–676,y394–516: firmer rectangular red roof border, warm pale roof inset and one simplified burger sign in 4–5 flat color layers. Straight horizontal red sign reading exactly 'BURGER', distinct from the two curved café/bakery signs. Broad red/cream awning, a clear central entry and wider square blue service windows. A tidy compact diner. Not a giant 3D food sculpture.
The rooftop motifs may be modestly raised graphic signs with only a tiny flat shadow; no oversized ornaments or tiny decorative fragments. Signs should read at game scale. Typography bold and plain like the approved POLICE sign. Roof geometry and storefront silhouette should distinguish the shops even without text.
LIGHT REFINE OTHER BUILDINGS, don't overcomplicate:
- Keep the repeated orange houses as the SAME common asset: add one simple blue window on either side of the front entry where the shallow facade permits, and give the existing small chimney a dark square opening. Preserve simple broad orange roof planes, tiny entry canopy, size and placement.
- Warehouses: retain blue flat roof and the existing 1–2 pale-blue skylights and vent. Make the orange loading shutters read as a loading bay with a slim gray canopy/edge, a dark threshold, and just two small yellow/charcoal safety marks at the jambs; no new machinery or pipes.
Do not change the approved police station, jail, map paths or overall palette. Still the same simple 2D game world, just more deliberately designed buildings. No new props, characters, UI, frame, title or labels outside the actual storefront signs. Return only the complete revised map.
```

## 정리 프롬프트

```text
Use case: precise-object-edit. The input image is the edit target. Refine ONLY three central shops with a small simplification pass. Preserve every other pixel/layout as closely as possible, especially the approved police station, houses, warehouses, jail, original trees, bushes, walls, roads and path gaps.
1) Remove the NEW tiny flower/hedge planter boxes attached to the FRONT facade of the CAFE, DONUTS and BURGER shops. Fill those small planter pixels with the facade or existing paving. These are the two small green rectangles at the bottom of each storefront, NOT the original freestanding round bushes outside the buildings. Keep the original round bushes exactly.
2) Keep the CAFE and DONUTS shops' curved cream signs, clear central glass doors and simple blue shop windows. Reduce their front facade height slightly so that the cafe and burger shop end at y516 and the donut shop ends at y714 on the 1024x1536 canvas. No silhouette protrusions beyond those lines. Preserve their current roof sizes as much as possible. This keeps the alleys uncluttered. No new props.
3) The BURGER shop should have a different architectural identity: replace ONLY its cream semicircular shop sign with a simple straight dark-red rectangular sign reading 'BURGER' in WHITE bold letters, integrated with the red/cream shallow awning. Keep the same footprint. Retain the roof burger emblem but simplify it to broad flat-color bun/lettuce/patty/cheese layers and only 3 sesame dots.
4) Simplify the pink rooftop donut emblem to a clean matte pink ring with tan edge and only 3–4 sprinkles, no bright glossy highlights. The cafe's simple cup pictogram stays unchanged.
Maintain the same almost-overhead angle and clean flat 2D drawing style with modest dark outlines. Do not make the buildings more 3D, front-facing, realistic, complex or shiny. Return the complete portrait map, no characters, no new labels except the storefront signs.
```

