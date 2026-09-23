# Chase Town — HD 소품 교체 (2026-09-24)

경찰서 230×189, 상자 45×51 등 전체 시안에서 잘라낸 작은 이미지를 게임에서 약 5배로 확대해 흐리게 보이던 문제를 수정했습니다. 건물·상점·창고·감옥 9종, 나무·수풀·상자·담장 7종, 총 **16종**을 개별 고해상도 투명 이미지로 다시 제작했습니다.

이미지 생성 스킬의 내장 `image_gen`을 사용했고 CLI/API 대체 경로는 사용하지 않았습니다. 각 원본 소품과 실제 `char_police.png`를 입력해 단순한 색면, 선명한 윤곽선, 기존 시점·팔레트·실루엣을 유지하도록 요청했습니다. 캐릭터 자체는 변경하지 않았습니다. [전체 생성 프롬프트](prompts.json).

## 결과

- [동일 게임 배율 전후 비교](../../chase-town-map/hd-before-after.png)
- [경찰서와 실제 캐릭터](../../chase-town-map/play-police-hd.png)
- [상점·담장·수풀과 실제 캐릭터](../../chase-town-map/play-market.png)
- [전체 맵](../../chase-town-map/map-overview.png)
- [에셋별 판정 오버레이](../physics-catalog.png)

미리보기는 실제 게임 렌더러와 원래 캐릭터 PNG를 사용한 오프라인 렌더입니다. 단말 스크린샷은 아니며 HUD·네트워크 플레이를 재현하지 않습니다.

## 해상도와 판정 분리

`ChaseTownAsset.Width/Height`와 모든 충돌점은 **원래 논리 픽셀 좌표**입니다. 새 `TextureScale/TextureWidth/TextureHeight`가 실제 PNG 해상도를 표시합니다. 소품은 6배, 바닥 타일은 1배입니다. 예:

| 에셋 | 이전 PNG / 논리 크기 | 새 PNG | 게임 속 크기 |
|---|---|---|---|
| 경찰서 | 230×189 | 1380×1134 | 575×472.5 유지 |
| 대형 창고 | 297×192 | 1782×1152 | 742.5×480 유지 |
| 나무 | 82×86 | 492×516 | 205×215 유지 |
| 상자 | 45×51 | 270×306 | 112.5×127.5 유지 |

게임의 2배 카메라에서 논리 픽셀 1개가 화면 5픽셀로 표시되는 데 비해, 이제 텍스처 6픽셀을 공급합니다. 원래 저해상도 이미지를 단순 확대하는 대신 별도 생성된 고해상도 원본을 사용합니다. PNG 전체를 동일한 월드 사각형에 그리므로 소품 크기·피벗·통로 폭·건물 뒤쪽 판정 8논리px가 바뀌지 않습니다.

## 파일 및 재생성

- 런타임 파일: `polrob.Client/Resources/Raw/ChaseTownV7/props/` — 16개 PNG, 약 6.8MiB.
- `sources/`: 생성된 HD 원본(실제 알파 포함).
- `original-props/`: 교체 전 16개 저해상도 PNG. 정렬 기준과 전후 비교용이며 앱에 추가로 포함하지 않음.
- `physics-before.json`: 교체 전 판정 스냅샷. 새 프로필의 논리 치수·판정이 같은지 회귀 테스트.
- `prompts.json`: 에셋별 프롬프트와 생성 결과 추적.

```sh
dotnet run --project tools/PolRob.ChaseTownAssets -- build
dotnet run --project tools/PolRob.TownMapPreview
dotnet test polrob.Server.Tests --no-restore
dotnet build polrob.Client -f net10.0-android --no-restore -p:RuntimeIdentifier=android-arm64
```

빌더는 HD 원본의 알파 경계를 기존 여백에 맞추고 RGBA PNG로 패키징합니다. 이미지 파일 크기로 물리 크기를 계산하지 않습니다. 전체 원본을 검증한 다음 저장하므로 누락/불투명 배경 에셋이 있으면 기존 패키지를 저해상도 이미지로 덮어쓰지 않고 오류를 냅니다.

114개 서버 테스트와 Android arm64 Debug 빌드 통과(기존 GameCreate 경고 8개). iOS arm64 시뮬레이터 Debug 빌드도 코드 서명 없이 통과했습니다(기존 GameCreate 경고와 RuntimeIdentifier 안내 포함 9개, 오류 0개). 새 PNG 크기·manifest, 기존 판정 불변, 경로·스폰·감옥 동작을 검사했습니다. 캐릭터 21개 PNG·바닥 타일 3개·맵 배치 파일의 SHA-256이 작업 전후 동일함을 확인했습니다. 기존 마을 맵의 에셋도 수정하지 않았습니다. 실제 단말에 설치하지 않았으며 로딩 메모리·프레임률은 별도 측정이 필요합니다.
