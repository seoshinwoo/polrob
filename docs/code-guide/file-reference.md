# PolRob `.cs` 파일별·메서드별 참고서

[전체 구조 가이드로 돌아가기](../code-guide.md)

이 참고서는 **파일을 하나 열고 그 안의 메서드를 따라 읽는 용도**로 작성했다. 프로젝트 흐름을 처음부터 이해하려면 [전체 가이드](../code-guide.md)의 1~6장을 읽고, 궁금한 파일의 입력·처리·반환값·호출 관계를 확인할 때 아래 사전을 연다.

| 대상 | 파일별 사전 | 설명 범위 |
|---|---|---|
| 서버 | [서버 `.cs` 38개](file-reference-server.md) | 시작점, Controller, Hub, Services, Operations, TCP/UDP 게임 서버의 partial 파일 전부 |
| 공용 코드 | [Shared `.cs` 22개](file-reference-shared.md) | DTO, Map, 충돌·좌표 계산, 맵 데이터와 현재 호출 경로 |
| 클라이언트 | [Client `.cs` 39개](file-reference-client.md) | 로그인·방·게임·음성·렌더링 코드비하인드와 핵심 메서드 |
| 테스트 봇 | [Bot `.cs` 7개](file-reference-bot.md) | 로그인·매칭·게임 접속·행동 루프의 메서드와 상태 |
| 서버 테스트 | [테스트 `.cs` 21개](file-reference-tests.md) | 각 테스트 메서드가 확인하는 구체적인 계약과 fixture helper |
| 개발 도구 | [Tools `.cs` 11개](file-reference-tools.md) | 별도 실행 프로그램의 지역 함수·핵심 메서드와 생성물 |

개발 도구의 C#은 주로 앱의 화면 자산을 만드는 별도 프로그램이다. 입력·출력과 현재 앱과의 연결은 [테스트·도구 장의 5절](06-tests-and-tools.md#5-맵에셋-개발-도구-전체)에 설명했다. UI 배치용 XAML과 설정 JSON은 대상에서 뺐다. 전체 파일 목록과 생략 범위는 [소스 색인](07-source-index.md)에서 확인할 수 있다.

처음 질문에 적힌 `GameRecordStatusCalculator.cs`의 실제 파일명은 `GameRecordStatsCalculator.cs`이고, `GameRoomSerivce.cs`는 `GameRoomService.cs`다. 두 파일 모두 [서버 참고서](file-reference-server.md)에서 해당 이름으로 찾을 수 있다. 게임 기록 파일이 나뉜 이유와 결과의 저장 순서는 [5장 5.1~5.3절](05-records-operations.md#51-기록할-내용은-단순하지만-저장-시점은-여러-단계다)에 먼저 설명했다.

## 메서드 설명을 읽는 순서

각 파일 절에서 먼저 그 파일의 책임과 보유 상태를 읽는다. 그다음 **진입 메서드 → 내부 helper → 실제 호출자와 다음 호출 대상** 순서로 보면 된다. 예를 들어 `GameRecordWriter.ProcessPendingAsync`를 보려면 서버 사전에서 그 메서드를 찾고, `IGameRecordStore.SaveGameRecordAsync`를 따라 `GameRecordDbService`의 저장 절로 이동한다.

`public`이라는 이유만으로 외부 요청에서 바로 호출되는 것은 아니다. 메서드 설명에는 실제 호출 경로를 기준으로 현재 실행 경로와 아직 연결되지 않은 코드도 구분했다. 테스트용 Reflection 호출이나 과거 맵 빌더에서만 쓰이는 메서드는 활성 게임 로직으로 간주하지 않는다.
