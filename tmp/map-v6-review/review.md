# 경찰서 수정 v6

맵 상단 경찰서의 주택형 경사 지붕을 평평한 파란 옥상으로 바꾸고, 기존 경찰서 에셋의 금색 방패·POLICE 간판·넓은 양문 출입구·양측 창을 단순화해 반영했습니다. 내장 image_gen 도구로 맵의 경찰서 부분을 수정했으며 다른 건물과 동선은 유지하도록 요청했습니다. 실제 게임에는 적용하지 않은 이미지 시안입니다.

- final-map.png: 수정한 전체 맵.
- police-station-with-actual-character.png: 경찰서 확대. 원본 char_police.png를 AppKit으로 직접 합성했고, 현재 게임의 몸통 표시 비율을 적용했습니다.

## 최종 프롬프트

```text
Use case: precise-object-edit.
Image 1 is the EDIT TARGET: the complete simple 2D game map. Image 2 is the EXISTING POLICE STATION design reference. Redesign ONLY the blue police building in the top-center courtyard, around x473–724,y34–202 on this 1024x1536 map. All other pixels, buildings, roads, plants, courtyard walls, crates and pathways must remain unchanged. No characters.
PROBLEM: current police station is a generic pitched-roof HOUSE with a badge. It must read immediately as a small municipal POLICE STATION, clearly different from the houses and warehouses.
REPLACE THE BUILDING SILHOUETTE, not merely the badge:
- ONE broad rounded-rectangular dark/navy blue FLAT ROOF enclosed by a simple narrow raised blue parapet. The entire roof top is one flat plane. ABSOLUTELY NO sloping roof planes, gable, hip roof, peak, triangular roof facets or house-style porch. Remove the two old house-like side roof projections.
- A restrained pale-cream institutional facade lip along the front, with a continuous dark-blue belt and TWO broad horizontal blue window groups flanking a wide centered double glass entrance. The entrance should be distinctly broad and public, not a small domestic door.
- A flat projecting navy entrance canopy, rounded at the corners but with a LEVEL top. Place a modest gold police shield on its upper surface, and a wide crisp sign reading exactly 'POLICE' in white block letters on dark navy. The sign is integrated into the shallow entrance canopy, not floating in the air or towering vertically. A tiny red/blue light strip can sit at the canopy edge for instant police recognition.
- One small simple rooftop ventilation box only, placed toward the rear. No rooftop clutter.
Take the blue/cream palette, broad formal entrance, shield and POLICE sign from reference image 2, but simplify it severely to the FLAT RESTRAINED MAP STYLE in image 1. No landscaping platforms imported from the reference.
Critical camera/style: keep the current 80–85 degree nearly overhead orthographic game camera. Roof is about 80–85 percent of the visible building, front facade a SHORT 15–20 percent band. No deep front elevation, isometric sides, 3D bevel/gloss, detailed texture, elaborate stairs or thick shadows. Crisp modest outline and simple broad color fills like the other map buildings.
Keep the same overall footprint and center, approximately 250px wide and 170px tall; fit canopy and entrance within it. Preserve front courtyard clearance and side paths. Only police station changes. Return the COMPLETE portrait map with this redesigned station.
```

