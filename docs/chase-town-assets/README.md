# Chase Town V7 — 에셋 및 물리 판정 패키지

승인한 맵의 **투명 스프라이트 16종 + 반복 바닥 타일 3종**입니다. 2026-09-24에 소품 16종을 개별 고해상도 이미지로 다시 제작했습니다. `ChaseTownLayout`을 통해 기본 플레이 맵에 연결되어 있습니다. [HD 전환 안내·전후 비교](hd/README.md), [타일 맵 구현·검증 안내](../chase-town-map/README.md)를 참고하세요.

- 게임 리소스: `polrob.Client/Resources/Raw/ChaseTownV7/`
- 엔진 공통 정의: `polrob.Shared/Models/ChaseTownAssetCatalog.cs`
- 내보낸 정의: `polrob.Client/Resources/Raw/ChaseTownV7/manifest.json`
- 재생성 도구: `tools/PolRob.ChaseTownAssets/`
- [전체 에셋](asset-catalog.png), [전체 물리 영역](physics-catalog.png), [나무·수풀·담장·감옥 확대](physics-detail.png)
- [파일별 투명도·판정 수 감사 결과](asset-audit.json)

## 구성

| 종류 | 에셋 | 판정 |
|---|---|---|
| 건물 7종 | 경찰서, 카페, 도넛집, 버거집, 주택, 대형·소형 창고 | 외곽 다각형에서 뒤쪽(위쪽) 8px를 추가로 제외. 이동·시야 차단. 앞·옆쪽과 돌출 입구의 빈 공간은 기존 형태 유지 |
| 감옥 2종 | 닫힌 문 / 열린 문 | 철창을 여러 개의 좁은 영역으로 분리. 내부는 통과 가능. 앞문만 열고 닫기 가능. 철창은 시야를 차단하지 않음 |
| 나무 | tree | 줄기 반지름 9px만 이동·시야 차단. 수관 반지름 35px는 그리기 순서/가림용 영역 |
| 수풀 | bush | 이동·시야 차단 없음. 중심 반지름 17px의 은신 트리거 |
| 상자 | crate | 바깥 그림자보다 작은 사각형. 이동·시야 차단 |
| 담장 4종 | 가로, 세로, ㄱ자, 기둥 | 이동·시야 차단. ㄱ자 내부를 큰 사각형으로 막지 않음 |
| 지면 3종 | grass, road, paving | 이동 충돌 없음. 단색 64×64 반복 타일 |

수풀 군집은 `bush`를 여러 번 배치합니다. 현재 플레이 맵은 세 가지 바닥 타일을 32×48 격자에 배치하며, 도로 타일은 이웃 타일에 따라 바깥쪽 연석만 그립니다. 큰 배경 이미지나 타일별 별도 도로 PNG를 사용하지 않습니다.

바닥 타일은 현재 승인한 회색 팔레트를 `ChaseTownLayout`의 색상 상수에서 생성합니다. 원본 시안의 밝은 바닥색을 다시 추출하지 않습니다. HD 전환에서 바닥과 캐릭터 파일은 바꾸지 않았습니다.

## 좌표와 표시 규칙

### 건물 뒤쪽 가림

약 80도 시점에서 지붕 뒤로 지나가는 캐릭터가 가려지도록 일반 건물 7종의 기존 충돌 다각형 최상단에서 **원본 기준 8px**를 제외합니다. 기본 배율 2.5에서 **20 월드 단위**, 캐릭터 충돌 지름 50의 40% 깊이입니다. 4px 판정 미리보기를 확인한 사용자의 요청에 따라 같은 양인 4px를 더 제외한 값입니다. 이는 카메라 투영으로 자동 계산한 수치가 아니라 게임 표현을 위한 조정값입니다.

`BuildingRearInsetPixels`가 공통 조정값이며, 내보내기 정의의 `RearInsetPixels`에도 기록됩니다. 다각형을 수평선으로 잘라 위쪽만 줄이므로 밑변·옆변·입구는 이동하지 않습니다. 렌더러는 기존처럼 캐릭터를 그린 뒤 건물을 덧그려 실제 가림을 만듭니다. PNG를 잘라내거나 축소하지 않습니다.

이동과 서버 시야 차단은 같은 줄어든 몸체를 사용합니다. 따라서 지붕의 시각적 가림만으로 별도의 은신 판정이 생기지는 않습니다. 감옥·담장·상자·나무에는 이 보정을 적용하지 않습니다.

[실제 캐릭터의 지붕 뒤 가림 / 판정 비교](../chase-town-map/rear-clearance.png)

### 공통 좌표

- **논리 에셋 전체 영역의 왼쪽 위가 (0, 0)** 입니다. 오른쪽이 +X, 아래쪽이 +Y입니다.
- 모든 판정은 **기존 저해상도 시안의 논리 픽셀 단위**입니다. 중심 피벗은 `(Width / 2, Height / 2)`입니다. HD PNG 실제 치수는 `TextureWidth / TextureHeight`이며 소품은 논리 치수의 6배, 바닥은 1배입니다. PNG 치수를 물리 좌표로 사용하지 마세요.
- 기본 월드 배율은 **2.5**입니다. 1024×1536 시안 → 기존 2560×3840 월드에 해당합니다.
- 기존 캐릭터 충돌 지름 50 월드 단위는 이 시안의 20px입니다. 충돌 영역 자체에 캐릭터 반지름을 더하지 않습니다. 기존 원-장애물 충돌 함수가 반지름을 적용합니다.
- 알파 여백을 자동 크롭해 늘려 그리면 그림과 판정이 어긋납니다. **PNG 전체를 그대로 그리거나**, 크롭/변환을 물리 데이터에도 동일하게 적용해야 합니다.
- 현재 `CanvaMapCollisions`의 왼쪽 아래 원점/알파 크롭 규약과 다릅니다. 새 정의를 그 규약으로 해석하지 마세요.
- `Place`는 이동, **양의 균일 배율**, 회전을 지원합니다. 비균일 배율은 지원하지 않습니다.

```csharp
var asset = ChaseTownAssetCatalog.Get("cafe");
var center = new PointF(660, 1130);
var scale = ChaseTownAssetCatalog.DefaultWorldScale;
var regions = ChaseTownAssetCatalog.Place(asset.Id, center, scale);

// Draw the whole PNG centered at center using these dimensions.
var drawWidth = asset.Width * scale;
var drawHeight = asset.Height * scale;

var colliders = regions.Where(r => r.Obstacle.BlocksMovement).ToArray();
bool blocked = colliders.Any(r =>
    GameMap.IsCircleCollidingWithObstacle(playerX, playerY, playerRadius, r.Obstacle));
```

`Place`가 반환하는 `Obstacle`은 기존 공통 충돌 함수에 바로 전달할 수 있습니다. 스프라이트 피벗, 배율, 회전을 바꾸면 같은 인수로 다시 생성하세요. `GameMap`이 현재 배치를 공간 인덱스에 등록하고, 클라이언트 렌더러는 같은 배치 데이터의 PNG 전체를 그립니다.

## 충돌과 트리거를 구분하기

`Kind` 값은 다음과 같습니다.

- `solid`: 고정 이동 장애물. `BlocksVision`으로 시야 차단 여부 구분.
- `gate`: 닫힌 감옥 문. `gateOpen: true`이면 반환하지 않음.
- `hiding`: 수풀 은신 영역. `GameMap.ContainsPoint(region.Obstacle, playerX, playerY)`로 중심 진입 확인.
- `holding`: 감옥 내부 수용 영역. 플레이어 전체 원이 영역 안에 들어가도록 배치.
- `interaction`: 감옥 앞 구조 접촉 영역. 닫힌 문 밖에서도 접근 가능하도록 **PNG 아래로 30px 연장**되어 있음.
- `occlusion`: 나무 수관의 시각적 가림 영역. 이동 장애물이 아니며 렌더 레이어/투명화용.

은신·가림은 `Kind`별로 연결됩니다. `hiding`은 `IsHidingArea`로 등록되어 기존 클라이언트 은신 표시와 서버 체포 검사에 반영됩니다. 자기 캐릭터가 들어간 수풀/수관은 반투명해집니다. 건물·담장 등의 `BlocksVision`은 서버 시야 차단 검사에 반영됩니다.

감옥 문을 열 때는 이미지도 `jail-open.png`로 바꾸세요. 해당 에셋의 프로필에는 `gate`가 처음부터 없습니다. 또는 `Place("jail", ..., gateOpen: true)`와 열린 이미지 조합을 사용할 수 있습니다. 닫힌 철창을 보이게 둔 채 판정만 없애면 안 됩니다. 이 열린 문 상태는 패키지에서 제공하는 기능이며 현재 게임의 구조/석방 규칙을 변경한 것은 아닙니다.

## 추출 이력과 현재 HD 해상도

최초 버전은 전체 시안에서 작은 에셋을 추출했으므로 실제 게임의 확대 배율에서 흐림이 생겼습니다. 이 저해상도 버전은 `hd/original-props/`에 보존합니다.

현재는 내장 `image_gen`으로 원래 에셋과 실제 캐릭터를 참고해 소품별로 새 고해상도 PNG를 만들었습니다. 단순한 색면·또렷한 외곽선·기존 시점과 실루엣을 유지하며, 디테일을 늘리는 사실적 3D 스타일은 피했습니다. 감옥의 빈 내부와 철창 사이, 열린 문의 출구는 실제 알파 투명도입니다.

원본 생성물은 `hd/sources/`에 보관하며, 기존 투명 여백·전체 크기에 정렬해서 **논리 치수의 가로·세로 6배 PNG**로 패키징합니다. 경찰서는 1380×1134, 대형 창고는 1782×1152입니다. 월드 크기·배치·판정은 그대로이고, 단순히 옛 PNG를 확대 저장한 것이 아닙니다.

## 재생성·검증

저장소 루트에서 실행합니다.

```sh
dotnet run --project tools/PolRob.ChaseTownAssets -- build
dotnet test polrob.Server.Tests --no-restore
```

`hd/sources/`, `hd/original-props/`, 공통 C# 정의만 있으면 재패키징됩니다. 외부 API, 새 이미지 생성 호출, `tmp` 폴더의 시안은 필요하지 않습니다. HD 원본이 없으면 실패하며 옛 저해상도 에셋으로 되돌리지 않습니다. `references` 명령은 최초 참고 크롭 준비용입니다.

검증 완료:

- 16개 스프라이트의 알파 투명도·비어 있는 모서리, 19개 PNG 치수와 manifest 일치.
- ㄱ자 담장 안쪽 통과, 나무 수관과 줄기의 다른 판정, 수풀 진입.
- 감옥 내부 통과, 닫힌 문 차단, 열린 문으로 지름 20px 캐릭터 통과, 닫힌 문 밖 구조 영역 접근.
- 건물 입구 양옆의 빈 공간, 피벗 기준 배율·회전 일치.
- 새 물리 테스트 14개 포함 **서버 테스트 91개 통과**.

위 목록은 최초 에셋 단위 검증 기록입니다. 후속 맵 배치·공간 인덱스·경로 검증 결과는 [맵 안내](../chase-town-map/README.md)에 있습니다. 실제 단말에서의 네트워크 플레이 체감은 별도 확인이 필요합니다.

## 감옥 복원 프롬프트

내장 `image_gen`에 `references/jail.png`를 편집 대상으로 전달했습니다. CLI/API 대체 경로는 사용하지 않았습니다.

```text
Use case: background-extraction. Extract ONLY the gray jail fence as ONE reusable 2D game sprite from this reference, preserving its simple charcoal outlines, restrained gray-blue two-tone colors, almost overhead 80-degree camera and shape. Restore the small lower fence sections hidden by the two green bushes. Remove every tree, bush, beige ground and green background. The inside of the rectangular cage and the gaps BETWEEN EVERY bar must be true alpha transparency, same as outside. No floor. Four short corner posts, barred back and side fences, front fence and clearly centered front locked gate. Keep extremely SIMPLE like the input, not a 3D realistic cage, no texture, no added details, no lettering. Return PNG with actual RGBA transparent background, tight transparent margin. No checkerboard illustration. Entire isolated fence visible.
```
