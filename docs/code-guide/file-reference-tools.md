# 파일별 메서드 사전: 개발 도구 C# 11개

[파일별 사전 목차](file-reference.md) · [도구의 입력·출력과 실행 맥락](06-tests-and-tools.md#5-맵에셋-개발-도구-전체)

이 폴더의 C#은 게임 실행 중의 서비스가 아니라 제작·검사용 독립 프로그램이다. `Program.cs`의 top-level statements가 진입점이므로 별도의 `Main` 선언이 없어도 실행된다. 같은 파일의 지역 함수는 그 프로그램 한 번의 실행 안에서 호출된다. 실제 앱 빌드에 연결된 `AssetBounds`와 수동 에셋 제작 도구의 차이는 6장에서 설명한다.

## PolRob.AssetBounds/Program.cs

[소스](../../tools/PolRob.AssetBounds/Program.cs#L1). **역할:** PNG의 보이는 픽셀 경계를 빌드 시 계산해 앱에서 쓰는 C# 자료형을 생성한다. 인자는 입력 Raw 디렉터리와 출력 `.cs` 경로다.

- [`FindAssets(directory, pattern, searchOption, minimumAlpha)`](../../tools/PolRob.AssetBounds/Program.cs#L25): 지정한 이미지 파일들을 정렬해 순회하고 각각 `Analyze`한 메타데이터 목록을 돌려준다. 지도와 캐릭터의 alpha 기준을 다르게 넘긴다.
- [`Analyze(path, minimumAlpha)`](../../tools/PolRob.AssetBounds/Program.cs#L38): SkiaSharp bitmap의 픽셀을 순회해 alpha가 임계값 이상인 최소 반열린 사각형과 원본 Width/Height를 구한다. 완전히 비어 있으면 전체 bounds를 사용한다.
- [`GenerateSource(...)`](../../tools/PolRob.AssetBounds/Program.cs#L78): 분석 결과를 `GeneratedAssetBounds` C# 소스 텍스트로 조합한다. 런타임 조회 메서드와 원본 크기 검증도 포함한다.
- [`WriteDictionary(...)`](../../tools/PolRob.AssetBounds/Program.cs#L123): 파일별 bounds를 딕셔너리 초기화 코드로 출력한다. 경로 문자열을 C# 문법에 맞게 escape한다.
- `AssetMetadata`: 파일명, 원본 크기, 보이는 영역을 생성 단계 사이로 넘기는 record다. 빌드 연결은 [Client csproj](../../polrob.Client/polrob.Client.csproj#L117)에 있다.

## PolRob.ChaseTownAssets/Program.cs

[소스](../../tools/PolRob.ChaseTownAssets/Program.cs#L1). **역할:** Chase 에셋 제작 명령의 진입점. 상위 폴더에서 `polrob.slnx`를 찾아 repo 위치를 정한다.

- 인자가 없거나 `references`면 기존 지도에서 사전에 정한 위치를 잘라 문서용 참고 PNG를 만든다. 소스 이미지가 없으면 허용된 이전 지도 경로에서 준비한다.
- `build`면 [`AssetPackBuilder.Build`](../../tools/PolRob.ChaseTownAssets/AssetPackBuilder.cs#L7)를 호출해 앱에서 사용하는 팩을 생성한다.
- 다른 명령은 예외다. 참조 이미지 생성과 팩 생성은 같은 단계가 아니다.

## PolRob.ChaseTownAssets/AssetPackBuilder.cs

[소스](../../tools/PolRob.ChaseTownAssets/AssetPackBuilder.cs#L5). **역할:** Shared 카탈로그를 기준으로 타일·소품 PNG와 manifest·검사 이미지를 만든다.

- [`Build(repo, docs)`](../../tools/PolRob.ChaseTownAssets/AssetPackBuilder.cs#L7): `ChaseTownAssetCatalog.Assets` 전체를 읽는다. 타일은 지정된 색으로 만들고 소품은 고해상도 loader를 사용한다. 소품 alpha 조건을 먼저 검사한 뒤 팩의 PNG/manifest와 audit·미리보기를 출력한다.
- [`RenderCatalog(images, path, physics)`](../../tools/PolRob.ChaseTownAssets/AssetPackBuilder.cs#L75): 에셋을 한 장의 목록 이미지로 만들고 `physics`가 참이면 충돌/trigger 영역을 함께 그린다.
- [`RenderDetail(images, path)`](../../tools/PolRob.ChaseTownAssets/AssetPackBuilder.cs#L96): 나무·수풀·벽·감옥 등 판정이 중요한 소품을 확대해 검사할 이미지를 만든다.
- `DrawSprite`, `DrawRegions`, `PolygonPath`: 원본 소품과 원/다각형 물리 모양을 같은 좌표로 표시한다. 실제 게임의 충돌 데이터를 수정하는 함수는 아니다.
- `Checker`, `Label`, 두 `Save` overload: 투명 배경을 보기 쉽게 하고 제목·PNG 저장을 수행한다.

## PolRob.ChaseTownAssets/HighResolutionAssets.cs

[소스](../../tools/PolRob.ChaseTownAssets/HighResolutionAssets.cs#L5). **역할:** 고해상도 artwork를 기존 논리 크기에 맞춰 정렬한다.

- [`Load(docs, asset)`](../../tools/PolRob.ChaseTownAssets/HighResolutionAssets.cs#L7): 기준 PNG와 HD PNG를 읽고 각 visible bounds를 비교한다. HD의 보이는 부분을 `TextureScale`에 맞춰 투명 출력 캔버스에 그려 bitmap을 반환한다. 원본·HD 파일이 없거나 유효한 alpha가 없으면 실패한다.
- [`Bounds(bitmap)`](../../tools/PolRob.ChaseTownAssets/HighResolutionAssets.cs#L35): alpha 기준을 만족하는 최소 영역을 구한다. 보이는 픽셀이 없으면 예외다.

## PolRob.TownMapPreview/Program.cs

[소스](../../tools/PolRob.TownMapPreview/Program.cs#L1). **역할:** 실제 앱의 맵 renderer 코드를 연결해 지도·충돌·가림·culling 결과를 PNG/JSON으로 내보내는 진입점이다.

- top-level 실행부는 repo와 입력 에셋을 찾고 타일 크기·색을 검사한 뒤 여러 배율/구역에서 `TownMapRenderer`와 `ClassicTownMapRenderer`를 호출한다. culling 적용 전후의 픽셀 일치도 확인한다.
- [`DrawCharacter(canvas, name, x, y)`](../../tools/PolRob.TownMapPreview/Program.cs#L181): 검사 장면에 실제 캐릭터 bitmap을 배치해 건물 앞뒤 가림을 비교한다.
- [`Save(surface, filename)`](../../tools/PolRob.TownMapPreview/Program.cs#L189): surface snapshot을 지정 이름의 PNG로 인코딩한다.
- 같은 폴더의 `ArchivedTownMapPreview.Export`와 `CollisionProfilePreview.Export`는 현재 Program에서 호출되지 않는다. 아래 두 절은 helper 구현의 역할 설명이다.

## PolRob.TownMapPreview/PreviewAssetAnalysis.cs

[소스](../../tools/PolRob.TownMapPreview/PreviewAssetAnalysis.cs#L3). **역할:** 미리보기에서 PNG의 투명 여백을 계산한다.

- [`VisibleBounds(bitmap)`](../../tools/PolRob.TownMapPreview/PreviewAssetAnalysis.cs#L5): alpha 임계값 이상의 최소 SKRect를 반환한다. 현재 Program의 Classic 이미지와 캐릭터 보이는 범위 분석에서 호출된다. 완전히 빈 이미지는 전체 경계로 대체한다.

## PolRob.TownMapPreview/CollisionProfilePreview.cs

[소스](../../tools/PolRob.TownMapPreview/CollisionProfilePreview.cs#L4). **역할:** 이전 Canva 원본 소품에 물리 영역을 덧그리는 검사 helper다.

- [`Export(output, assets)`](../../tools/PolRob.TownMapPreview/CollisionProfilePreview.cs#L6): 각 소품의 사각형·원·다각형 프로필과 원본 이미지 좌표계를 비교하는 PNG를 쓴다. 현재 미리보기 Program의 직접 호출자는 없다.

## PolRob.TownMapPreview/ArchivedTownMapPreview.cs

[소스](../../tools/PolRob.TownMapPreview/ArchivedTownMapPreview.cs#L8). **역할:** 과거 `docs/town-map/layout.json`과 옛 소품 에셋을 기반으로 이전 지도를 재현한다.

- [`Export(repo)`](../../tools/PolRob.TownMapPreview/ArchivedTownMapPreview.cs#L10): repo의 예전 레이아웃·이미지를 읽고 출력 preview를 만든다. 현재 기본 Chase 맵 생성/게임 실행 경로에서는 호출되지 않는다.

## PolRob.MapAssetBuilder/Program.cs

[소스](../../tools/PolRob.MapAssetBuilder/Program.cs#L1). **역할:** 과거 5120×7680 지도 PNG와 LabelMe 주석에서 exact base/foreground 타일·검사 이미지를 만드는 top-level 프로그램. 현재 기본 2560×3840 Chase 맵과 요구 크기가 다르다.

- [`RenderPreview(outputPath, includePlayers, showMask)`](../../tools/PolRob.MapAssetBuilder/Program.cs#L191): 저장된 타일을 합성하고 옵션에 따라 캐릭터와 마스크를 덧그려 preview를 만든다.
- [`DrawPersistedTiles(canvas, tileDirectory)`](../../tools/PolRob.MapAssetBuilder/Program.cs#L235): 저장된 512px base 타일을 행·열 좌표에 맞춰 다시 그린다.
- [`CountRuntimePreviewPixelDifferences(runtimePreviewPath)`](../../tools/PolRob.MapAssetBuilder/Program.cs#L253): 생성된 runtime preview와 기대 이미지의 픽셀 차이를 세어 exact 재현 여부를 확인한다.
- [`RenderCollisionPreview(outputPath)`](../../tools/PolRob.MapAssetBuilder/Program.cs#L281): LabelMe 물리 형상을 지도 위에 그려 통행 판정을 눈으로 검사한다.
- [`ValidatePhysicsGeometry()`](../../tools/PolRob.MapAssetBuilder/Program.cs#L392): `new GameMap()` 결과가 과거 크기·건물·다각형 개수와 맞는지 assert한다. 현재 기본 맵과 불일치하므로 이 프로그램은 현행 맵 재생성 절차가 아니다. 검증 전에 일부 출력이 발생할 수 있다.
- `DrawPlayer`, `CreateJailDetailMask`, `GetTilePath`, `CreateClosedPath`, `CreateRectPath`, `SavePng`, `FindRepositoryRoot`: preview 캐릭터/감옥 마스크/좌표 path/파일 저장·repo 탐색 helper다.
- `LabelMeDocument`, `LabelMeShape`: JSON 주석의 레이블과 좌표를 역직렬화하는 내부 자료형이다.

## PolRob.SpriteEdges/Program.cs

[소스](../../tools/PolRob.SpriteEdges/Program.cs#L1). **역할:** 이전 627×627 경찰·도둑 달리기 프레임의 외곽 alpha를 정리한다. 원본/마스크/출력 디렉터리를 받으며 원본과 출력이 같으면 실패한다.

- [`Clean(source, guide, role, pose)`](../../tools/PolRob.SpriteEdges/Program.cs#L35): guide mask와 alpha를 결합해 몸체 후보를 만든다. 연결된 큰 영역과 구멍 메우기·contour 평활을 거쳐 원본 색을 유지한 출력 bitmap을 돌려준다.
- [`Validate(source, result, name)`](../../tools/PolRob.SpriteEdges/Program.cs#L144): 출력 alpha가 원본보다 늘거나 남은 픽셀의 RGB가 변한 경우 거부한다.
- `BuckleCoverage`: 경찰 버클 주변의 자세별 경계 보정값. `Neighbors`와 `FillHoles`는 연결된 픽셀 영역을 탐색한다.
- `Load`, `Save`, `RenderSheet`, `RenderComparison`: PNG 입출력과 결과 시트/전후 비교를 만든다.

## PolRob.CharacterAnimation/Program.cs

[소스](../../tools/PolRob.CharacterAnimation/Program.cs#L1). **역할:** 원본 캐릭터의 얼굴·몸체 식별 부분을 보존하면서 달리기와 특수 동작 PNG를 합성한다. 1088×1088 출력 캔버스를 사용한다.

이 파일은 약 1,000줄로 helper가 많다. 입력과 반환 bitmap, 원본 식별 픽셀을 보호하는지에 따라 묶어 읽으면 역할이 분명해진다.

| 주요 메서드 | 입력 → 처리 → 출력/부작용 |
|---|---|
| [`ComposeSpecial`](../../tools/PolRob.CharacterAnimation/Program.cs#L225) | 팔·몸체·참조 bitmap과 포즈 이름을 받아 체포·항복·탈옥의 특수 프레임을 합성한다. |
| [`ExtendBodySides`](../../tools/PolRob.CharacterAnimation/Program.cs#L258), [`CompleteSideWarp`](../../tools/PolRob.CharacterAnimation/Program.cs#L437), [`CompleteSide`](../../tools/PolRob.CharacterAnimation/Program.cs#L472) | 팔을 벌릴 때 드러날 몸체 옆면을 같은 재질로 채운다. |
| [`SmoothBoundary`](../../tools/PolRob.CharacterAnimation/Program.cs#L373), [`EnforceRoundBoundary`](../../tools/PolRob.CharacterAnimation/Program.cs#L397), [`SmoothColorProfile`](../../tools/PolRob.CharacterAnimation/Program.cs#L410) | 경계·색 배열을 부드럽게 하되 round silhouette를 지킨다. |
| [`FindSideMaterialColor`](../../tools/PolRob.CharacterAnimation/Program.cs#L527), [`MixRgb`](../../tools/PolRob.CharacterAnimation/Program.cs#L567), [`SourceOver`](../../tools/PolRob.CharacterAnimation/Program.cs#L577), [`SampleHorizontalPremultiplied`](../../tools/PolRob.CharacterAnimation/Program.cs#L615) | 원본의 옆면 색을 찾고 alpha를 고려해 보간·합성한다. |
| [`ClipByGuide`](../../tools/PolRob.CharacterAnimation/Program.cs#L642), [`RemoveSmallComponents`](../../tools/PolRob.CharacterAnimation/Program.cs#L707) | guide로 필요한 부분을 분리하고 혼자 떨어진 작은 pixel 조각을 없앤다. |
| [`ReferenceBodyPath`](../../tools/PolRob.CharacterAnimation/Program.cs#L741), `ReferenceHeadPath`, `ReferenceFacePath`, `ReferenceHeadbandPath`, `ReferenceArm` | 원본에서 보존/분리할 몸·머리·얼굴·팔 경계를 정의한다. |
| [`PreserveIdentity`](../../tools/PolRob.CharacterAnimation/Program.cs#L935), [`IdentitySilhouette`](../../tools/PolRob.CharacterAnimation/Program.cs#L915), [`ExcludeReconstructedPixels`](../../tools/PolRob.CharacterAnimation/Program.cs#L590) | 참조 픽셀 중 생성된 부분을 제외하고 보호 대상의 원래 모습을 합성 뒤 복원한다. |
| [`SaveAndValidate`](../../tools/PolRob.CharacterAnimation/Program.cs#L950), [`CountComponents`](../../tools/PolRob.CharacterAnimation/Program.cs#L990) | 프레임 저장 전 보호 픽셀 불변·가장자리 여백·alpha 연결 요소를 검사한다. |
| `Load`, `Bounds`, `NewBitmap`, `Draw`, `Save`, `RenderSheet` | 이미지 입출력과 최종 시트를 만든다. |

상세 출력 파일과 두 세대의 스프라이트 도구 차이는 [6장 5.8~5.10](06-tests-and-tools.md#58-polrobcharacteranimationprogramcs-원본-캐릭터-기반-프레임-구성)에 이어진다. 이 프로그램의 결과는 게임 클라이언트에 포함된 정적 PNG이며 실행 중 실시간 애니메이션을 합성하지 않는다.
