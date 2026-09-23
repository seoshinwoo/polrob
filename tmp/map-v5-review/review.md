# 기존 건물 에셋을 반영한 맵 시안 v5

## 결과

- final-map.png: 전체 맵 시안 (1024 × 1536).
- shops-with-actual-characters.png: 중앙 상가 확대 + 실제 char_police.png, char_robber.png 직접 합성.
- existing-building-references.png: 실제 기존 건물 PNG 7종을 모은 참고 이미지.

## 반영한 특징과 배치

- 경찰서: 상단 중앙, 파란 지붕·금색 방패·작은 환기구.
- 감옥: 상단 오른쪽, 낮은 철창과 열린 안뜰.
- 카페: 중앙 왼쪽 블록 위쪽, 갈색 테두리·커피잔 표식·줄무늬 차양.
- 도넛집: 같은 블록 아래쪽, 분홍 테두리·도넛 표식.
- 버거집: 중앙 오른쪽 블록 위쪽, 빨간 테두리·버거 표식.
- 창고: 중앙 오른쪽 작은 창고와 남서쪽 큰 창고, 청회색 지붕·채광창·주황 셔터.
- 주택: 나머지 주거 구역, 주황 지붕·작은 굴뚝·현관 덮개를 반복 사용.

참고한 원본은 polrob.Client/Resources/Raw/MapAssets 아래 police_station.png, jail.png, cafe.png, donut.png, burger.png, warehouse.png, house-orange.png입니다. 자세한 원본 렌더링을 그대로 붙이는 대신 건물의 식별 특징을 기존 v4 시안의 단순한 그림체로 다시 그렸습니다. 공통 외곽선과 지붕 중심 시점을 유지하고 원본의 복잡한 기계·화단·벽돌 질감은 줄였습니다. 지형·큰길·골목·수풀 배치도 대체로 유지했습니다. 상가 차양 등 일부 실루엣 경계는 v4와 조금 달라졌으므로 실제 제작 시 좁은 길의 충돌 여유를 다시 맞춰야 합니다.

맵은 내장 image_gen 도구로 수정했습니다. CLI/API 대체 경로를 사용하지 않았습니다. 참고 모음과 캐릭터 확대 비교는 AppKit으로 원본 PNG를 축소·합성했으며 캐릭터를 다시 생성하지 않았습니다. 게임 코드와 현재 게임 에셋은 수정하지 않았습니다.

캐릭터 비교는 기존과 동일하게 월드 2560 × 3840, 충돌 지름 50, 몸통 표시 비율 0.86 및 원본 몸통 폭 512를 적용했습니다. 확대 화면에서 캐릭터만 별도로 크게 하지 않았습니다.

## 최종 생성 프롬프트

```text
Use case: precise-object-edit.
Asset type: updated simple 2D overhead cops-and-robbers game map, same 1024x1536 portrait composition.
INPUT ROLES: Image 1 is the EDIT TARGET and the ABSOLUTE STYLE reference: this current simple flat game map. Image 2 is a contact sheet of the existing building DESIGN/IDENTITY references ONLY, NOT rendering style or camera references. Its top row from left is police station, jail, café, donut shop; bottom row is burger shop, warehouse, orange-roof house. Its labels are for identification only. Images 3 and 4 are the existing police and robber character STYLE calibration references, not inserts; do not redraw or add characters.
User request: buildings currently all look alike. Redesign and place buildings inspired by the existing asset identities, but render them in EXACTLY the restrained simple drawing style of Image 1. The original reference assets are much more detailed and frontal; do NOT import their detail, gloss, proportions, deep facades or perspective. Keep orthographic 80–85-degree overhead camera with roof filling 85–90% of each building. Only a short facade lip, simple flat colors and broad two-tone shading, thin navy/charcoal outlines and tiny contact shadows, no realistic lighting.
STRICT INVARIANTS: keep all roads, alleys, ground shapes/colors, walls, trees, bushes and crates in their existing positions. Preserve building footprints and clearances so the chase paths remain open. Every replacement must fit ENTIRELY inside its old footprint, including its little awning. No widening onto sidewalks or alleys. No new street props or characters.
BUILDING KIT, seven reusable families:
- Police: recognizable rounded dark-blue flat roof, gold shield over central entry, cream edge, one tiny simple roof vent. Strip all fussy rooftop machinery and garden platforms.
- Jail: open gray holding courtyard with simple low dark-gray barred fence and tiny gate, almost directly overhead. It is not a house.
- Café: cocoa-brown flat roof rim around a cream roof, one SIMPLE FLAT coffee-cup pictogram painted on the roof, short brown/cream awning edge. No latte artwork, plants or windows full of furniture.
- Donut shop: cream flat roof with muted-pink trim, one simple flat pink donut ring pictogram with only 3 sprinkles, short pink/cream awning.
- Burger shop: muted-red flat roof rim, one simple flat burger pictogram, short red/cream awning. The food signs are SMALL painted flat graphics, NOT giant 3D sculptures.
- Warehouse: slate-blue simple flat roof with two broad pale-blue skylights and a small square vent, thin cream parapet, short visible orange rolling loading door. Large warehouse gets two loading doors; small warehouse one. No pipes, bolts, grids of machinery or complex panels.
- Houses: reusable orange roof with just two broad roof planes, a tiny cream chimney, a tiny triangular entry canopy within the footprint; no brick/tile textures or detailed window boxes. Remaining houses use this same reusable design, with occasional desaturated terracotta variation.
EXACT PLACEMENT in reference 1024x1536 coordinates:
Keep top-center police station in its current footprint (~x473–722,y34–199), but use simplified existing police identity.
Replace ONLY the top-right corner house (~x880–1008,y102–206) with the jail courtyard. Keep the upper-left house a house.
Central LEFT block: upper-left building (~x205–320,y395–497) becomes café; upper-right remains house; lower-left (~x207–326,y589–692) becomes donut shop; lower-right remains house.
Central RIGHT block: upper-left (~x563–678,y396–496) becomes burger shop; upper-right remains house; lower-left remains house; lower-right (~x702–816,y619–723) becomes small warehouse.
The large existing lower-left teal warehouse (~x215–502,y1063–1234) becomes the simplified blue warehouse design with two orange doors, preserving size.
All other perimeter houses get the simplified existing orange-house design in the same footprints.
Aim for recognizable variety in roof silhouettes, color blocks and tiny pictograms, with FEW reusable assets and the SAME low visual complexity as Image 1. Shops should be charming local landmarks that help players say 'behind the donut shop' or 'past the café'. Avoid making each individual house unique. No text, labels, title, UI, characters, realistic textures or increased 3D depth. Return only the complete revised map.
```

