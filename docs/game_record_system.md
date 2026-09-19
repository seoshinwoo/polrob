# 게임 전적 시스템

서버가 실제 게임 시작과 종료를 판정한 결과만 Cosmos DB에 기록한다. 클라이언트가 승패나 참가자 목록을 제출하지 않으므로 결과를 임의로 조작할 수 없다.

## Cosmos DB 컨테이너

서버 시작 시 `PolRobDB`에 `GameRecords` 컨테이너가 없으면 자동 생성한다. 이름은 `CosmosDb` 설정에서 변경할 수 있으며 파티션 키는 `/id`다.

이 컨테이너에는 한 경기당 하나인 원본 전적과 사용자별 파생 문서가 함께 저장된다. 원본 전적이 승률 계산의 기준 데이터이며, 파생 문서는 `player:{gameId}:{playerId}` 형식의 ID를 사용한다.

원본 전적에는 다음 값이 저장된다.

```json
{
  "id": "경기별 GUID",
  "roomId": "게임 방 ID",
  "winnerRole": "Police",
  "policePlayerIds": ["player-1"],
  "robberPlayerIds": ["player-2"],
  "durationSeconds": 184,
  "startedAtUtc": "2026-09-04T01:00:00Z",
  "endedAtUtc": "2026-09-04T01:03:04Z",
  "schemaVersion": 1,
  "playerRecordsIndexed": true
}
```

커스텀 재경기는 같은 방 ID를 다시 사용하므로 `roomId`와 별개인 경기 GUID를 문서 ID로 사용한다. 같은 경기 저장을 재시도해도 `CreateItem` 충돌을 성공으로 처리하며, 사용자별 파생 문서도 경기 GUID와 사용자 ID를 조합한 결정적 ID를 사용해 중복 생성을 막는다.

## 기록 시점과 장애 처리

- 카운트다운이 끝나 실제 플레이가 시작될 때 역할별 참가자 ID를 고정한다.
- 제한 시간 종료 또는 모든 도둑 수감으로 서버가 승자를 확정할 때 기록을 큐에 넣는다.
- 일시적인 Cosmos DB 오류는 지수 백오프로 계속 재시도한다.
- 원본 저장 뒤 사용자 인덱스 생성이 중단되면 미완료 표시를 남기며, 백그라운드 복구 작업이 누락 인덱스를 주기적으로 다시 만든다.
- 정상 서버 종료 시 네트워크 방 루프를 먼저 끝낸 뒤 큐에 들어온 전적을 비운다.
- 실제 플레이가 시작되지 않았거나 모든 연결이 끊겨 승패가 확정되지 않은 게임은 기록하지 않는다.

## 프로필 통계 API

`GET /game-records/me/stats`에 로그인 Bearer 토큰이 필요하다. 요청 사용자 ID는 본문이나 쿼리에서 받지 않고 서버 세션에서만 결정한다.

응답에는 `overall`, `police`, `robber`가 있으며 각 항목은 `totalGames`, `wins`, `losses`, `winRate`를 포함한다.

통계는 `GameRecords`의 원본 전적에서 `policePlayerIds`, `robberPlayerIds`, `winnerRole`을 조회해 계산한다. 과거 버전이 사용자별 파생 문서를 별도 컨테이너에 저장했던 적이 있어, 현재 API는 파생 문서의 위치나 `playerRecordsIndexed` 값에 의존하지 않는다. 따라서 이전에 저장된 원본 전적도 별도 마이그레이션 완료를 기다리지 않고 승률에 포함된다. 현재 파티션 키가 `/id`이므로 이 조회는 여러 파티션에 걸쳐 수행된다.
