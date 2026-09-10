# Canva 맵 — 2560 × 3840

첨부한 `polrob-new-map-example.png`를 기준으로 `polrob.Client/Resources/Raw/MapAssets/`의 원본 에셋 14종을 70개 배치했습니다. 현재 게임 플레이 화면과 이 폴더의 미리보기가 같은 `TownMapRenderer`를 사용합니다.

## 배치

- 건물 9개: 경찰서, 감옥, 창고, 도넛 가게, 버거 가게, 카페, 주택 3개
- 나무 6개, 덤불 14개, 돌 4개, 연못 1개, 가로등 5개
- 상자 더미 에셋 1개와 개별 상자 30개: 왼쪽 아래 ㄷ자 배열과 가운데 묶음, 주택 옆 상자 포함

이미지와 각 에셋의 투명 영역을 대조해 보이는 그림의 위치·크기를 측정했습니다. Canva 편집 문서의 수치가 아닌 PNG 기준 측정값이므로 몇 픽셀 정도 차이는 있을 수 있습니다. 위치 원본은 `polrob.Shared/Models/CanvaMapLayout.cs`이며 좌측 상단이 (0, 0)입니다. 모든 크기는 투명 여백을 제외한 그림 기준입니다.

## 렌더링과 충돌

잔디·아스팔트·벽돌은 반복 타일, 도로는 곡선 경로, 에셋은 각각의 원본 PNG로 렌더링합니다. 벽돌은 요청한 V2의 `paving-generated.png`에서 만든 타일을 240 월드 단위마다 반복합니다. 확대·축소 시 에셋에 이미지 필터링을 적용합니다.

요청한 원본 이미지 좌표를 기준으로 **70개 배치 모두에 이동 충돌을 적용했습니다.** 원점은 투명 여백을 포함한 원본 PNG의 좌측 하단입니다. 사각형은 `(0, 0)`부터 원본 가로 전체와 지정한 높이까지입니다. `house.png` 규칙은 실제 에셋인 `house-orange.png`의 주택 3채에 적용했습니다.

| 에셋 | 원본 이미지 기준 충돌 영역 |
|---|---|
| police_station | 764 × 565 사각형 |
| donut | 880 × 700 사각형 |
| cafe | 926 × 875 사각형 |
| house-orange | 975 × 825 사각형 |
| burger | 1053 × 885 사각형 |
| jail | 979 × 600 사각형 |
| warehouse | 951 × 900 사각형 |
| box | 1044 × 1199 사각형 |
| tree | 중심 (504.5, 150), 반지름 150 |
| streetlamp | 중심 (303, 131), 반지름 131 |
| rock | 중심 (625.5, 560.5), 반지름 560 |
| bush | 중심 (395.5, 388.5), 반지름 390 |
| boxes | 알파 외곽선을 측정한 14점 오목 다각형. 오른쪽 위 빈 공간 제외 |
| pond | 돌 테두리를 측정한 타원형 다각형. 원본 범위 x=5..1381, y=1..831, 약 1376 × 830 |

`polrob.Shared/Models/CanvaMapCollisions.cs`가 원본 규칙과 변환을 관리합니다. 렌더러가 투명 여백을 제외해서 표시하므로 충돌 계산도 같은 여백과 배율을 반영합니다. 원본 좌표 `(u, v)`는 맵에서 `x = 이미지 왼쪽 + (u - 잘린 왼쪽 여백) × X배율`, `y = 이미지 위쪽 + (원본 높이 - v - 잘린 위쪽 여백) × Y배율`로 변환합니다.

원의 X/Y 배율도 실제 스프라이트와 일치시켰습니다. 정수 크기 배치로 두 배율이 조금 다른 경우에는 해당 타원과의 거리를 계산하므로 원본 반지름과 맵의 그림이 어긋나지 않습니다. 연못 외곽은 돌 테두리를 따라 측정했고 위로 솟은 갈대는 제외했습니다. 상자 더미의 오목한 부분은 직사각형이나 볼록 껍질로 메우지 않습니다.

클라이언트·서버는 같은 충돌 배치를 사용합니다. 기존 월드 경계와 캐릭터 간 게임 규칙은 유지합니다.

## 결과 파일

- `map-2560x3840.png`, `map-canva-2560x3840.png`: 전체 맵
- `map-overview.png`, `map-canva-overview.png`: 1024 × 1536 미리보기
- `background-2560x3840.png`: 타일·도로 배경
- `props-2560x3840.png`: 투명 에셋 레이어
- `layout.json`: 배치·도로·활성 충돌 설정
- `asset-audit.json`: 에셋 경로·해상도·투명도·실루엣 경계
- `collisions-2560x3840.png`, `collisions-source-profiles-2560x3840.png`: 실제 충돌 영역 표시. 빨강 사각형, 하늘색 원, 보라색 외곽 다각형
- `collisions-overview.png`: 충돌 지도 미리보기
- `collision-original-assets.png`: 각 원본 이미지에 지정 영역과 좌측 하단 원점을 표시한 확인표
- `collision-source-profiles.json`: 원본 해상도, 여백, 충돌 중심·높이·반지름·다각형 좌표
- `detail-50px-characters.png`: 50 × 50 캐릭터를 덧붙인 상세 크기 확인용 이미지

`docs/town-map-v2`와 이 폴더의 예전 생성 프롬프트·sources는 이전 제작 기록입니다. 활성 맵의 에셋은 MapAssets 폴더를 사용합니다.

## 다시 생성하기

```sh
dotnet run --project tools/PolRob.TownMapPreview --no-restore
dotnet test polrob.Server.Tests/polrob.Server.Tests.csproj --no-restore
dotnet build polrob.Client/polrob.Client.csproj -f net10.0-ios -r iossimulator-arm64 -t:Compile --no-restore
```

기존 `--town-map` 인자도 같은 현재 맵을 내보냅니다.

2026-09-09 검증: 테스트 35개 통과. 원본 좌측 하단 변환, 투명 여백, 모든 반복 배치의 크기·반지름, 상자 더미 빈 공간, 연못 모서리, 타원 접선 거리, 공간 검색, 스폰·감옥 접근·주요 경로를 검사했습니다. 미리보기 생성 시 실제 PNG 해상도와 알파 경계가 충돌 메타데이터와 일치하는지도 확인합니다. iOS Compile 오류 0개이며 기존 XAML 경고 8개가 남아 있습니다. 실기기 실행은 수행하지 않았습니다.
