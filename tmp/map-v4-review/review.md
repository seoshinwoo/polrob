# 캐릭터 기준 맵 시안 v4

이 결과는 검토용 이미지이며 게임 코드나 현재 맵 에셋을 교체하지 않았습니다.

## 최종 파일

- final-map.png: 전체 맵, 1024 × 1536.
- final-01-alley.png: 중앙 골목에 실제 경찰·도둑 PNG를 합성.
- final-02-trees.png: 나무·수풀 구역에 실제 PNG를 합성.
- final-03-yard.png: 창고·상자 구역에 실제 PNG를 합성.
- ../map_v4_preview.swift: 확대 비교 이미지를 재생성하는 합성 스크립트.

## 제작 방법과 크기

맵 제작 및 수정에는 내장 image_gen 도구를 사용했습니다. CLI/API 대체 경로는 사용하지 않았습니다. 캐릭터 비교는 macOS AppKit으로 실제 Resources/Raw/char_police.png와 char_robber.png를 읽어 알파 합성했습니다. 캐릭터는 생성 도구로 재해석하지 않았으며 디자인, 색, 모양을 수정하지 않았습니다.

현재 코드의 월드 2560 × 3840, 캐릭터 충돌 지름 50, 몸통 표시 비율 0.86, 원본 몸통 폭 512px를 적용했습니다. 맵 원본에서 충돌 지름은 20px, 기준 몸통 폭은 17.2px입니다. 비교 이미지는 맵의 300 × 240 구역을 4배 확대했으며 캐릭터에도 동일한 배율을 적용했습니다. 실제 화면의 시야 크기는 기기 해상도에 따라 달라집니다.

## 디자인 판단

원래 시안보다 작은 앞면, 지붕 중심 시점, 단순한 명암과 얇아진 외곽선이 실제 캐릭터와 더 자연스럽게 맞습니다. 긴 그림자와 바닥 잔질감을 줄여 캐릭터와 배경의 질감 차이를 줄였습니다. 밝은 길과 잔디에서 검은 도둑의 실루엣도 잘 구분됩니다. 확대된 맵은 1024 × 1536 래스터 원본이라 원본 고해상도 캐릭터보다 부드럽게 보입니다. 이것은 최종 개별 에셋 제작 시 해결할 해상도 차이입니다.

주택은 같은 형태의 색상 변형, 나무·수풀·상자·담장은 반복 배치하는 구성입니다. 생성 시안의 반복물은 픽셀 단위로 동일한 타일/스프라이트가 아니며, 실제 제작에서는 이를 소수의 공통 에셋으로 정규화할 수 있습니다.

## 게임 구조 판단

1. 중앙 주택 골목: 큰 도로에서 골목으로 진입한 뒤 건물 모퉁이로 직선 시야를 끊는 장면을 만들기 좋습니다. 비교 이미지의 두 캐릭터도 오른쪽 위 주택 모퉁이를 사이에 두고 있습니다.
2. 큰길과 내부 골목: 도둑이 안쪽으로 빠질 때 경찰이 바깥 경로를 택해 출구를 막는 선택지를 제공합니다. 두 주택 블록이 각각 남북으로 연결되고 주변 길로 빠질 수 있습니다.
3. 은신과 노출: 수풀은 몇몇 코너·출구 옆에 있고 큰길은 노출되어 있어, 숨어서 기다릴지 이동할지 선택하게 합니다. 수풀을 지나갈 수 있고 은신 판정이 작동한다는 설계 가정입니다.
4. 공간 차이: 주택 구역은 짧은 코너, 창고 구역은 큰 건물 우회와 상자, 나무 구역은 여러 방향으로 빠질 수 있는 열린 공간입니다.
5. 경찰서: 정면 입구 외에 두 측면 틈을 두어 정면 하나만 지키는 구조를 완화했습니다.

수정 과정에서 중앙 담장이 캐릭터 통로를 막는 문제를 찾아 이동·삭제했습니다. 최종 이미지의 건물/담장 경계를 수동으로 근사한 사각형 모델에서 충돌 반지름 10px로 두 중앙 블록의 남북 통과를 별도로 확인했습니다. 두 경로 모두 연결되어 있었습니다. 이는 이미지 기반의 개략 확인이며 실제 엔진 충돌 검증은 아닙니다.

재미와 밸런스는 아직 실플레이로 검증하지 않았습니다. 오른쪽 상단 내부 골목은 좁은 편이라 충돌 여유가 특히 중요합니다. 은신 판정 범위, 시야 차단 범위, 양측 이동 속도에 따라 수풀 구역과 큰 건물 순환로가 지나치게 유리해질 수 있으므로 실제 구현 후 확인해야 합니다. 제 판단은 '현재 캐릭터에 맞는 그래픽 방향이며, 추격·우회·은신을 시험하기 좋은 맵 시안'입니다.

## 사용한 프롬프트

### 1

```text
Use case: stylized-concept.
Asset type: a clean production-minded 2D top-down cops-and-robbers game MAP CONCEPT, portrait 2:3, preferably 1536x2304. Full playable map, edge to edge, no border, text, labels, legend, UI or characters.
Input images 1 and 2: STYLE REFERENCES ONLY, the actual unchangeable police and robber sprites. Match their restrained navy/charcoal outlines, simple rounded silhouettes, restrained broad cel shading and almost directly overhead camera. Do NOT draw the characters; they will be composited separately from their original PNGs.
Primary request: a charming but extremely SIMPLE lightweight neighborhood map belonging to exactly the same casual 2D sprite game. Camera orthographic 80–85 degrees down from horizontal, roof tops occupy almost all of each building, at most a very thin front lip. No isometric angles. Flat vector-like raster art, sharp restrained dark outlines, flat quiet ground, only two tones per prop and tiny subtle contact shadows. Matte, not glossy. No gradients on terrain, textures, bevels, ambient occlusion or realistic 3D rendering. Props must be restrained enough that the tiny actual character sprites will be the visual focus.
Palette: muted sage grass, light warm-gray paved courtyards, light-medium desaturated blue-gray roads so black robber sprites remain visible. Muted terracotta house roofs, one navy-blue police building, a muted teal warehouse. Controlled color, not neon.
Limited reusable kit: only three plain terrain types, one identical small rectangular house repeated in TWO roof colors, one double-width version for a warehouse/police landmark, ONE repeated round tree canopy with nearly invisible trunk, ONE repeated simple bush tuft grouped in twos/threes, ONE square wooden crate repeated, ONE short wall segment repeated. Same size and design for every repeat of a type. No lamps, fountains, cars, benches, rooftop machinery, flowers or tiny decorative clutter. Buildings closed/unenterable.
Gameplay layout is the priority, not a decorative city grid. About 12 repeated buildings and 18–24 small tree/bush groups, composed into three interconnected neighborhoods. Each neighborhood has a loop around two or three buildings plus a shorter narrow cut-through: police can cut across while a robber breaks sight around corners. Roads take several offset 90-degree turns and T-junctions. Avoid a straight full-map sightline or a continuous unobstructed perimeter racetrack. At least TWO independent connections between every neighborhood and the others, no single choke point controls the whole map. Place a modest blue police station and compact fenced holding yard in the upper district, approachable by front street and BOTH side paths. Put a clustered residential alley network through the middle with staggered buildings making blind corners. Place a sparse tree/bush courtyard and modest warehouse crate yard in the lower half with at least two ways out each. Arrange isolated short L-shaped walls and crates to interrupt sightlines, but keep generous clear gaps. Bush hiding patches beside corners and side exits, not on every route. Balance exposed crossings for police with sheltered detours for robbers. Every little courtyard has two obvious exits; no sealed pretty gardens.
Scale guide, if canvas is 1024x1536: player collision diameter 20px, visible character body about 17px, houses approximately 112x88px (all identical), trees 45–50px canopy diameter, bush modules 28px, crates 24px. Main routes 70–90px wide, secondary routes 45–60px wide, deliberate short alleys 32–40px wide and visibly passable. Scale all dimensions together for higher resolution.
Keep layout legible and modest in asset count, with clear walkable negative space, memorable three-color districts and reusable silhouettes. The result should look like an actual lightweight 2D game map, not a toy diorama, illustration poster, blueprint or complicated painterly map.
```

### 2

```text
Use case: precise-object-edit.
Image 1 is the EDIT TARGET: the complete portrait 2D cops-and-robbers town map. Images 2 and 3 are immutable character STYLE references only. Do not insert or redraw any characters.
Change only gameplay GEOMETRY in the middle residential district and access gaps around the blue police station. Keep image size/aspect, restrained palette, overhead angle, ground style, exact repeated house/tree/bush/crate/wall designs, modest detail, upper and lower landmarks. This must stay a simple lightweight sprite game map.

Critical problem to fix: isolated single houses sitting in very broad roads make this a running track, not an interesting hide-and-chase map.
Replace the central four isolated house islands (approximately x180–845, y365–820 on the 1024x1536 map) with TWO compact residential BLOCKS made from EIGHT copies of the SAME current small house, four per block. Stagger each block's four houses to form a zigzag internal passage, with 32–40px CLEAR PASSABLE alley gaps between rooftops, calibrated for a 20px player diameter. Interleaved short 60–80px wall segments should create one or two real blind corners and break long sightlines, never seal a route. These passages need different exits on north/south/side leading to external streets. Keep a 65–80px-wide central spine between the two blocks and cross-connect it to BOTH outer streets above, between, and below the blocks. A player can pursue through the inner alley while another takes a distinct outer route to intercept. All grass/paving is walkable, so actual building and wall placements—not decorative road colors—must make the corner/cover structure. Mix broad exposed cross-streets with short narrow covered passages, not an even maze. Place about six identical bush tufts just after alternate corners and near secondary exits; leave the other corners exposed. Reuse current house and bush assets; no new types.

At the police station courtyard, make clear 40px-wide gaps in BOTH left and right wall halfway down as well as keeping the broad front entrance, so the yard is not protected by a single bottleneck.

Retain the lower warehouse and lower-right tree courtyard as the second type of chase space, with multiple exits. Keep everything graphically consistent: simple closed roofs, nearly 80–85-degree overhead camera, small flat facade lip, quiet colors, restrained dark outline, no 3D shine or rich details. All eight central houses identical footprint and style, only the existing two roof colors. No cars, streetlights, fountain, furniture, characters, labels, arrows, diagram markers or text. Output only the full revised map.
```

### 3

```text
Use case: precise-object-edit.
Image 1 is the exact edit target, a 1024x1536 simple overhead cops-and-robbers map. Preserve all buildings, all roads and paving, all trees and bushes, palette, overhead camera, illustration style and image dimensions. No characters or labels. Make ONLY these three small WALL changes, essential for player collision clearance:
1. In the LEFT central residential block, the middle L-shaped wall currently at approximately x322–385, y507–589 pinches the passage between the two upper houses shut. Remove that wall from that location. Put the SAME small L wall farther DOWN and LEFT, entirely inside rectangle x268–316, y534–568. Its vertical leg is on the left and horizontal leg points right. Keep its thin footprint. This leaves the important north-to-south alley at x328–343 completely open, and gives at least 32px gap under the upper houses.
2. In the RIGHT central residential block, remove the middle L wall at approximately x673–739,y508–592. Relocate the same small L wall down-left into rectangle x617–662,y534–568, vertical leg on left, horizontal points right. Keep the through-alley at x686–696 unobstructed. This prevents a dead end for the 20px-diameter player.
3. At the bottom of the right residential block, the wall attached to the bottom of the left house looks like a blocked doorway. Remove its tall vertical leg currently around x609–628,y690–770. Retain only a short horizontal freestanding wall around x615–680,y770–783, with its small end posts. Leave at least 35px clear gap from the house.
All other pixels/composition must be preserved as closely as possible. Do not add detail, texture or shading. The output remains only the full portrait map.
```

### 4

```text
Use case: precise-object-edit. The input is the edit target. Make ONE tiny removal only: remove the freestanding L-shaped gray wall in the MIDDLE of the RIGHT central group of four houses, at approximately x631–700, y539–586 on the 1024x1536 image. This is the elbow immediately below the upper brown-roof house and above the lower red-roof house. Replace only those wall and shadow pixels with the surrounding quiet grass/paving so the alley is visibly unobstructed. Do not remove the short horizontal wall at the bottom of that block (near y770). Keep EVERY other building, bush, tree, wall, road, image size, palette, overhead viewpoint and detail unchanged. Do not add anything. No characters, text or labels. Output the complete map.
```

### 5

```text
Use case: style-transfer.
Image 1 is the edit target: the final map with approved gameplay geometry. Images 2 and 3 are STYLE references of the actual immutable 2D characters, NOT inserts. No characters in the output.
One final graphics-only simplification to harmonize the environment with these small clean sprites: remove the long dark ground-cast shadows from buildings, walls and tree canopies, replacing them with at most a very short subtle 1–2 pixel contact shadow; reduce the map props' heavy dark outline thickness by about one third; remove the tiny painted flecks, grain and wisps from all ground tiles, making grass, roads and paving uniform quiet flat color. Preserve broad simple two-tone foliage and the existing plain roof planes; don't add highlights or gloss.
STRICT INVARIANTS: preserve the entire map layout EXACTLY, every house footprint, wall location, bush/tree position, alley clearance, doorway and road boundary unchanged. Preserve existing colors and object shapes. Maintain near-vertical 80–85 degree top-down orthographic camera. No richer detail or 3D rendering. Don't turn buildings into icons; they remain the same buildings. No characters, labels or UI. Full portrait map only.
```

