# 달리기 캐릭터 외곽 정리

> 이 문서는 627px 달리기 에셋의 이전 외곽 정리 기록이다. 현재 기본·달리기·특수 동작은
> [공통 몸체를 사용하는 1088px 규격](../character-animation/README.md)으로 교체됐다.
> `preview.html`도 현재 규격의 전체 동작을 확인하도록 갱신했다.

2026-09-11. `Resources/Raw/char_police_run_1.png`–`8.png` 및
`char_robber_run_1.png`–`8.png` 총 16개를 교체했다.

기존 발광 제거 후 남은 회색/갈색 띠, 경찰의 파란 후광, 버클 아래의 노란 돌출부를
정리했다. 캐릭터를 새로 그리지 않고 원본의 색상·포즈·627×627 캔버스를 보존한 채
외곽 투명도만 재구성했다. 눈의 투명 구멍도 보존했다.

## 결과 확인

- `before-after.png`: 밝은 배경과 어두운 배경의 수정 전·후 비교.
- `preview.html`: 현재 최종 게임 에셋 21개를 직접 로드하는 로컬 애니메이션 미리보기.
  브라우저에서 파일을 직접 열면 된다. 100ms 간격과 고정 몸체 중심점·배율을 사용한다.
- 정지·특수동작 에셋과 게임 코드는 이번 작업에서 변경하지 않았다.

## 제작 방식 및 프롬프트 세트

ImageGen 스킬의 **내장 이미지 도구 모드**를 사용했다. 각 역할의 고유 포즈 4개씩,
총 8개의 흑백 실루엣 가이드를 생성했으며 `masks/`에 보관했다.
생성된 가이드의 미세한 위치 차이를 원본에 맞춰 보정한 뒤, 원본의 고불투명도 몸체에서
연속 외곽선을 추출했다. 외곽선을 따라 가우시안 평활화하고 곡선으로 연결해
4배 슈퍼샘플링으로 투명 경계를 만들었다. 버클 하단의 고불투명도 발광은
별도의 둥근 경계로 제한했다. 최종 RGB는 원본 픽셀을 사용한다.

사용한 프롬프트의 공통 명세:

> Use case: background-extraction. Produce only an exact monochrome external
> silhouette clipping mask for precision compositing the supplied animation
> sprite. White solid silhouette on pure black; preserve the square canvas,
> source-relative position, scale, pose, anatomy, framing and margins. Trace
> the actual solid character contour including rounded hands, head/hat, arms
> and body. Exclude diffuse glow, colored halo, ragged fringe and external
> shadows. Use smooth natural curves and fine antialiasing. Fill internal
> details completely white. No checkerboard, text, scenery, redesign or reshaping.

경찰 추가 지시: 벨트 버클은 포함하되 아래의 흐릿한 노란 발광 덩어리는 제외.
도둑 추가 지시: 마스크에서는 눈 구멍도 흰색으로 채움. 실제 눈 구멍은 합성 시
원본 알파로 보존.

## 재현

원본 스냅샷을 입력으로 실행한다. 이미 정리한 결과를 재입력하면 외곽이 더 줄어들 수
있으므로 반복 적용하지 않는다. 작업 시작 시 스냅샷은
`tmp/character-run-edges-source/`에 보관했다.

```sh
dotnet run --project tools/PolRob.SpriteEdges -- \
  tmp/character-run-edges-source \
  docs/character-run-edges/masks \
  tmp/character-run-edges-result
```

이 도구는 결과 PNG 및 밝은/어두운/체크무늬 배경 검사 시트를 생성한다.
입력과 다른 출력 디렉터리를 사용해야 한다. 앱 실행 시에는 도구나 마스크를 사용하지 않는다.

## 검증 결과

- 16/16: RGBA, 627×627, 기존 중심점 좌표 및 프레임 순서 유지.
- 반복 포즈 `1=7`, `2=6=8`, `3=5`는 역할별로 파일 바이트까지 동일.
- 알파 임계값 1, 32, 128에서 각 프레임의 연결된 몸체는 1개: 떨어진 잔여 픽셀 없음.
- 경찰 내부 구멍 없음. 도둑의 눈 구멍 2개 유지.
- 외곽에서 4픽셀 안쪽 몸체는 원본 대비 RGB·알파 변경 픽셀 0개.
- 모든 보이는 픽셀의 RGB가 원본과 동일하고, 새 불투명도를 추가하지 않음을 도구에서 검사.
- 확대 검사 시 손·버클·모자·몸체의 잔여 후광 및 거친 외곽이 해소됨을
  밝은/어두운/체크무늬 배경에서 확인.
- MAUI 앱이나 시뮬레이터를 직접 실행한 검증은 수행하지 않았다.
  로컬 브라우저 자동 검증은 파일 URL 정책으로 실행되지 않았다.
