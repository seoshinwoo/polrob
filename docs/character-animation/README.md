# 모든 캐릭터 동작의 크기 고정

2026-09-12. 경찰 10개(기본, 달리기 8, 체포), 도둑 11개(기본, 달리기 8,
항복, 탈옥)를 같은 규격으로 다시 합성했다. 실제 앱 에셋은
`polrob.Client/Resources/Raw/char_*.png`에 적용했다.

## 고정 규격

| 항목 | 값 |
| --- | --- |
| 전체 PNG 캔버스 | 1024×1024 RGBA |
| 공통 머리·몸체 레이어의 최대 너비 | 512px |
| 공통 몸체 중심점 | (512, 512) |
| 게임의 몸체 너비 | 플레이어 지름 × 0.86 |
| 기본 충돌 지름 100일 때 몸체 너비 | 86 월드 단위 |
| 달리기 프레임 간격 | 기존과 같은 100ms |

역할별로 하나의 머리·몸체 레이어를 모든 동작에 반복 사용한다. 달리기는 한 벌의
팔 레이어를 좌우 어깨에서 반대 위상으로 회전하고 원근에 따른 팔 길이만 조절한다.
몸체는 이동·회전·크기 변경을 하지 않는다. 특수 동작에서도 같은 몸체를 사용하며,
앞으로 뻗은 팔·손·소품이 하단 몸체를 가릴 수 있다. 도둑의 눈은 투명 구멍이다.

팔과 소품에 필요한 여백을 모든 PNG에 동일하게 확보했다. 따라서 전체 이미지로
애니메이션을 만들 때에도 크기·중심점 보정이 필요 없다. 게임에서는 모든 상태가
하나의 `PlayerSpriteProfile(512, 512, 512)`을 사용한다. 투명 여백을 건너뛰는
렌더링 최적화는 배율에 영향을 주지 않는다.

탈옥 에셋 이름은 사용자가 변경한 `char_robber_prison_break.png`를 유지하고
로딩 코드도 같은 이름으로 수정했다.

## 확인 자료

- [전체 동작 GIF](animation.gif): 기본 → 달리기 → 체포/항복 → 탈옥 → 달리기.
- [인터랙티브 미리보기](../character-run-edges/preview.html): 현재 앱 에셋 21개를
  직접 로드하며, 전체 동작/달리기 선택·일시 정지·프레임 이동·배경·중심 가이드를 제공한다.
- [경찰 전체 프레임](result/police-all-states.png), [도둑 전체 프레임](result/robber-all-states.png).
- [검사 기록](result/audit.json): 캔버스, 몸체 규격, 보호 영역 픽셀 비교, 연결 성분, 여백.

## 제작 및 재현

ImageGen 스킬의 내장 이미지 도구를 사용해 경찰·도둑의 공통 몸체와 탈옥용 팔/소품
레이어를 제작했다. 생성 입력은 `source/`, 선택한 생성 결과는 `generated/`,
정확한 프롬프트는 [prompts.json](prompts.json)에 보관했다. 생성 결과에 포함된
체크무늬는 실제 투명도로 정리한 뒤 사용했다. 기본/달리기 팔과 체포/항복 동작은
기존 원본에서 추출했다. 합성은 SkiaSharp 도구에서 결정적으로 수행한다.

```sh
dotnet run --project tools/PolRob.CharacterAnimation -- \
  docs/character-animation docs/character-animation/result

env DEVELOPER_DIR=/Library/Developer/CommandLineTools \
  /Library/Developer/CommandLineTools/usr/bin/swift \
  tools/PolRob.CharacterAnimation/make-preview.swift \
  docs/character-animation/result docs/character-animation/animation.gif
```

결과의 `char_*.png` 21개만 앱 `Resources/Raw/`의 같은 이름으로 복사한다.
`source/`는 재현과 복구용 원본이며 덮어쓰지 않는다. 이전 외곽 정리 도구는
627px 원본 전용이므로 새 1024px 프레임에 적용하지 않는다.

## 검증

- 21개 모두 1024×1024이며, 머리·눈의 비교 영역 RGBA 차이 0픽셀.
- 기본·달리기·항복의 불투명한 공통 몸체는 기준 레이어와 차이 0픽셀.
- 체포·탈옥은 팔과 소품이 몸체 일부를 가리는 합성이므로 머리 보호 영역을 비교했다.
- 보이는 영역(알파 32 이상)의 연결 성분은 프레임마다 1개이며, 잘린 동작이나
  떨어진 조각 없이 캔버스 내부 여백을 확보했다.
- 900×600 GIF의 27개 프레임 및 프레임별 시간 정보를 인코딩 후 다시 읽어 확인했다.
- Android 빌드 통과. iOS는 현재 설정된 Xcode 경로가 없어 실행하지 못했다.
  실제 게임의 기기 실행 검증은 수행하지 않았다.
