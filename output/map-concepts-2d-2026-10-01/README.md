# 정돈된 도시 블록 — 초기 시안

사용자가 선택한 초기 2D 맵 시안입니다. 경찰서는 좌측 상단, 감옥은 바로 오른쪽에 있습니다. 길과 도로는 직선·직각을 기본으로 구성했습니다.

- `02-clean-blocks.png`: 캐릭터 없는 맵 원본 (1024 × 1536).
- `02-clean-blocks-with-characters.png`: 기존 경찰 2명·도둑 4명을 실제 게임 비율로 합성한 전체 맵.
- `02-clean-blocks-character-detail.png`: 캐릭터 주변을 맵과 함께 5배 확대해 비교한 이미지.
- `prompts.md`: 이 초기 시안의 원본 생성 프롬프트.
- `02-clean-blocks-initial-preview.json`, `compose-character-preview.swift`, `preview-scaling.md`: 캐릭터 합성 설정·도구·배율 근거.

합성 재현: 저장소 루트에서 `swift output/map-concepts-2d-2026-10-01/compose-character-preview.swift output/map-concepts-2d-2026-10-01/02-clean-blocks-initial-preview.json` 실행.

현재 이미지는 맵 아트 시안입니다. 실제 게임에 적용하려면 타일 간격을 일정하게 맞추고 건물·나무·장애물 에셋 및 충돌 판정을 따로 구성해야 합니다. 게임에 사용 중인 에셋과 코드는 변경하지 않았습니다.
