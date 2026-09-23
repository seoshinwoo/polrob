# 두 맵 관리

## 사용 방법

메인 화면의 **방 만들기 위 맵 선택**에서 선택한 뒤 방을 만듭니다.

- **추격 마을 (새 맵)** — 기본값. 회색 타일 배경, 승인한 에셋과 통로 배치.
- **기존 마을** — 이전 Canva 배치, 기존 이미지·바닥·물리 판정으로 실행.

참가자는 방장의 맵을 자동으로 사용하며 로비에도 맵 이름이 표시됩니다. 방 안에서 맵을 바꾸지는 않습니다. 재경기는 같은 맵을 유지하고, 다른 맵을 쓰려면 새 방을 만듭니다. 랜덤 매칭은 새 맵이 기본입니다.

## 구성

| 역할 | 기존 마을 | 추격 마을 |
|---|---|---|
| 고정 ID | `canva-town-v1` | `chase-town-v1` |
| 배치 | `CanvaMapLayout` | `ChaseTownLayout` |
| 물리 프로필 | `CanvaMapCollisions` | `ChaseTownAssetCatalog` |
| 렌더러 | `ClassicTownMapRenderer` | `TownMapRenderer` |
| 리소스 폴더 | `MapAssets/`, `TownMap/tiles/` | `ChaseTownV7/props/`, `ChaseTownV7/tiles/` |

공통 목록·이름·기본값은 `polrob.Shared/Models/MapRegistry.cs`에서 관리합니다. 에셋 폴더를 덮어쓰거나 Git 브랜치를 바꿔서 맵을 교체하지 않습니다. `new GameMap(mapId)`로 해당 맵의 판정·스폰·감옥을 함께 구성합니다.

서버는 `Game.MapId`를 방 생성 시 고정하고, 방별 `GameSession.Map`을 갖습니다. 동시에 서로 다른 맵으로 플레이해도 판정이 섞이지 않습니다. 기존 맵의 감옥 근접 구조 규칙과 새 맵의 감옥 앞 interaction 영역을 각각 유지합니다.

클라이언트는 인증된 `GET /game/{roomId}/status`로 서버의 맵 ID를 확인한 후 해당 맵의 리소스와 렌더러만 로드합니다. TCP 입장에도 로드한 `MapId`를 보내 서버와 비교합니다. 누락·알 수 없는 ID·불일치는 기본 맵으로 대체하지 않고 입장을 거절합니다. **클라이언트와 서버를 함께 빌드/업데이트해야 합니다.** 현재 변경은 소스와 로컬 빌드에 적용했으며 운영 서버 배포는 수행하지 않았습니다.

API에서 기존 맵 방을 만들 때는 `POST /game/create`의 `mapId`에 `canva-town-v1`을 전달합니다. 생략 시 새 맵입니다. 게임 내 선택 UI도 이 API를 사용합니다.

## 수정과 검증

새 맵 배치는 `ChaseTownLayout`, 새 에셋 판정은 `ChaseTownAssetCatalog`에서 수정합니다. 이전 맵의 배치·프로필·PNG는 수정하지 않습니다. 추후 기존 버전까지 계속 재사용해야 하는 대규모 변경은 새 ID로 등록하고 이전 정의/리소스를 남겨야 합니다.

```sh
dotnet run --project tools/PolRob.ChaseTownAssets -- build
dotnet run --project tools/PolRob.TownMapPreview
dotnet test polrob.Server.Tests --no-restore
dotnet build polrob.Client -f net10.0-android --no-restore -p:RuntimeIdentifier=android-arm64
dotnet build polrob.Test --no-restore
```

미리보기 도구는 두 맵 모두 실제 게임 렌더러로 내보내고, 카메라 범위만 그린 결과가 전체 렌더와 같은지 검사합니다. 새 바닥 타일 3장의 색상/크기도 검사합니다.

- [새 맵 미리보기](chase-town-map/map-overview.png)
- [기존 맵 미리보기](chase-town-map/classic-map-overview.png)
- [새 맵 물리 판정](chase-town-map/collisions-overview.png)

테스트는 두 맵의 방 생성·참가·재경기·맵 조회 권한, 독립된 런타임 판정, 안전 스폰, 감옥 구조/석방, 새 통로 연결, 나무 제거, 건물 뒤쪽 판정과 기존 맵 회귀 검사를 포함합니다. 단말 UI와 실제 여러 기기의 네트워크 플레이는 별도 확인이 필요합니다.

2026-09-22 검증: 서버 테스트 **113개 통과**, Android arm64 Debug 빌드 성공(기존 GameCreate 경고 8개), 봇 클라이언트 빌드 성공. 기존 Canva 배치·프로필·리소스는 변경하지 않았습니다.
