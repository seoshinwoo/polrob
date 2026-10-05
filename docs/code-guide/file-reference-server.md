# 서버 파일·메서드 참고서

[전체 가이드](../code-guide.md) · [서버 흐름](01-server-application.md) · [실시간 게임 흐름](03-server-network.md) · [기록·운영 흐름](05-records-operations.md)

이 문서는 `polrob.Server`의 실행 소스 **38개 `.cs` 파일**을 파일 단위로 설명한다. 파일을 옆에 열고 메서드를 하나씩 읽을 때 찾아보는 참고서다. `입력`은 인자뿐 아니라 함수가 읽는 기존 상태를, `결과`는 반환값뿐 아니라 사전 변경·파일 쓰기·패킷 전송 같은 부작용까지 포함한다. `async Task`는 완료를 기다릴 수 있는 작업이며, 반환형에 데이터가 없더라도 저장·전송 같은 효과가 있을 수 있다.

각 소스 링크의 `#L숫자`는 해당 선언이나 설명 대상이 시작하는 줄이다. `private` 함수는 같은 클래스 내부 구현이며, `GameNetworkServer.*.cs`는 전부 하나의 partial 클래스이므로 서로의 private 함수·필드를 직접 사용한다. 아래에서 “거절한다”는 표현의 구체적인 결과가 HTTP 응답인지, false인지, 예외인지도 함께 구분한다.

## 파일 목차

| 구역 | 파일 |
|---|---|
| 시작 | [01 Program.cs](#01-programcs) |
| HTTP | [02 AuthController.cs](#02-controllersauthcontrollercs), [03 GameController.cs](#03-controllersgamecontrollercs), [04 GameRecordsController.cs](#04-controllersgamerecordscontrollercs), [05 VoiceController.cs](#05-controllersvoicecontrollercs) |
| 로비 Hub | [06 GameRoomHub.cs](#06-hubsgameroomhubcs) |
| 결과 모델 | [07 CompletedGameRecord.cs](#07-modelscompletedgamerecordcs), [08 PlayerGameOutcome.cs](#08-modelsplayergameoutcomecs) |
| 게임 네트워크 | [09 ActiveGameParticipantRegistry.cs](#09-networkactivegameparticipantregistrycs), [10 GameNetworkServer.cs](#10-networkgamenetworkservercs), [11 Transport](#11-networkgamenetworkservertransportcs), [12 RoomLoop](#12-networkgamenetworkserverroomloopcs), [13 Lifecycle](#13-networkgamenetworkserverlifecyclecs), [14 Observability](#14-networkgamenetworkserverobservabilitycs), [15 GameSession.cs](#15-networkgamesessioncs), [16 RuntimeMetricSampler.cs](#16-networkruntimemetricsamplercs), [17 TcpPeer.cs](#17-networktcppeercs), [18 UdpRateLimitState.cs](#18-networkudpratelimitstatecs) |
| 운영 | [19 GameHubFilter.cs](#19-operationsgamehubfiltercs), [20 OperationalMetrics.cs](#20-operationsoperationalmetricscs), [21 OperationsEndpoints.cs](#21-operationsoperationsendpointscs), [22 RestartPolicyService.cs](#22-operationsrestartpolicyservicecs), [23 ServerAdmission.cs](#23-operationsserveradmissioncs), [24 ServerOperations.cs](#24-operationsserveroperationscs) |
| 서비스 | [25 BotIdentityService.cs](#25-servicesbotidentityservicecs), [26 CosmosDbOptions.cs](#26-servicescosmosdboptionscs), [27 GameRecordDbService.cs](#27-servicesgamerecorddbservicecs), [28 GameRecordOutbox.cs](#28-servicesgamerecordoutboxcs), [29 GameRecordReconciler.cs](#29-servicesgamerecordreconcilercs), [30 GameRecordStatsCalculator.cs](#30-servicesgamerecordstatscalculatorcs), [31 GameRecordWriter.cs](#31-servicesgamerecordwritercs), [32 GameRoomService.cs](#32-servicesgameroomservicecs), [33 IGameRecordQueue.cs](#33-servicesigamerecordqueuecs), [34 IGameRecordStore.cs](#34-servicesigamerecordstorecs), [35 LiveKitOptions.cs](#35-serviceslivekitoptionscs), [36 LiveKitRoomAdminService.cs](#36-serviceslivekitroomadminservicecs), [37 LiveKitTokenService.cs](#37-serviceslivekittokenservicecs), [38 UserDbService.cs](#38-servicesuserdbservicecs) |

## 01. Program.cs

소스: [Program.cs](../../polrob.Server/Program.cs#L1).

**역할:** 서버 실행 진입점이다. 명시적인 클래스·`Main` 메서드 없이 top-level 문장으로 작성되어 있다. `builder`는 서비스 등록·설정을 모으고, `app`은 완성된 HTTP 애플리케이션과 수명을 관리한다. 게임 상태를 직접 보관하지 않는다.

### 서비스 등록 블록

입력은 실행 인자와 `builder.Configuration`이다. Controller·OpenAPI·SignalR·HTTP 제한을 등록하고 운영 상태, Outbox, DB, 로비, 봇, 음성 서비스를 DI에 연결한다. SignalR 최대 수신 크기는 16KiB, Kestrel 본문 제한은 64KiB로 설정한다. 종료 기한은 제한된 drain 시간에 15초를 더한다.

`GameRecordWriter`를 singleton으로 한 번 등록한 뒤 `IGameRecordQueue`와 `IHostedService`가 그 인스턴스를 조회하게 한다. `IGameRecordStore`도 같은 `GameRecordDbService` 인스턴스를 가리킨다. 따라서 “결과 접수용 Writer”와 “백그라운드 소비용 Writer”가 따로 만들어지지 않는다. Hosted service 순서는 RestartPolicy → Writer → Reconciler → 활성화된 경우 GameNetworkServer다.

### CosmosClient 구성·DB 초기화 블록

연결 문자열 후보를 순서대로 확인하여 첫 비어 있지 않은 값을 고른다. 없거나 `AccountEndpoint=`·`AccountKey=` 형식이 없으면 `InvalidOperationException`으로 시작을 중단한다. `CosmosClient`는 HTTPS Gateway 모드와 camelCase 문서 직렬화를 사용하며 두 DB 서비스가 공유한다.

`builder.Build()` 후 `UserDbService.InitializeAsync`, `GameRecordDbService.InitializeAsync`를 차례로 기다린다. 이 단계 예외는 잡아서 무시하지 않는다. 따라서 DB 없이 시작한 뒤 Outbox만으로 운용하는 초기 시작 경로는 없다. 런타임 중 DB 장애를 파일로 버티는 정책과 구분해야 한다.

### 수명 callback·HTTP pipeline 블록

`ApplicationStarted`는 `ServerOperations.MarkStarted`, `ApplicationStopping`은 `BeginDrain`을 호출한다. 개발 환경은 OpenAPI endpoint를, 그 밖에는 HTTPS redirection을 활성화한다. 이어 routing → rate limiter → 신규 `/game` POST 수용 검사 → 운영/Controller/Hub endpoint를 연결하고 `app.Run()`으로 수명을 유지한다.

수용 검사 미들웨어는 `ServerAdmission.CanAcceptNewGames == false`일 때 `/game` 경로의 POST에 HTTP 503, `Retry-After: 30`, JSON 오류를 돌려주고 다음 단계 호출을 생략한다. GET 상태·전적과 `/auth/logout`까지 이 조건으로 막는 것은 아니다. LiveKit options의 URL·키 검증은 시작 시 수행한다.

## 02. Controllers/AuthController.cs

소스: [AuthController.cs](../../polrob.Server/Controllers/AuthController.cs#L9).

**역할·상태:** `/auth` HTTP 요청을 처리하고 정적 `Sessions` 사전에 로그인 토큰 → `(UserId, Expires)`를 보관한다. 생성자는 `UserDbService`, `BotIdentityService`, `IConfiguration`을 보관한다. DB 계정과 메모리 로그인 세션은 서로 다른 데이터다.

### 요청·응답 record

| 타입 | 필드와 의미 |
|---|---|
| `SignUpRequest`, `LoginRequest` | `Name`, `Password`: 가입/로그인 원문 입력 |
| `LoginResponse` | `SessionToken`, `UserId`, `Name`: 이후 요청에 쓸 세션과 사용자 식별 |
| `LogoutRequest` | 제거할 `SessionToken` |
| `BotLoginRequest` | 선택적 `Name`, `Role`: 봇 이름과 생성 로그용 역할 |
| `BotLoginResponse` | 일반 로그인처럼 토큰·ID·이름 |

### 공개 메서드

| 선언 | 입력 → 처리 → 반환/부작용 | 호출 관계·경계 |
|---|---|---|
| [`SignUp(req)`](../../polrob.Server/Controllers/AuthController.cs#L37) | 요청이 null인지 확인하고 `ValidateCredentials` 실행 → `CreateUserAsync` → 생성된 사용자로 `CreateLoginResponse` → HTTP 200 | null/잘못된 형식은 400, DB 서비스가 중복으로 null을 돌려주면 409. 성공 시 로그인 세션도 생성한다. DB 예외를 별도 복구하지 않는다. |
| [`Login(req)`](../../polrob.Server/Controllers/AuthController.cs#L60) | 비어 있는 이름/비밀번호 거절 → `ValidateUserAsync` → 새 세션과 사용자 응답 | 빈 요청은 400, 사용자 없음/비밀번호 불일치는 401. 가입 규칙 전체를 다시 검사하는 함수는 아니다. |
| [`Logout(req)`](../../polrob.Server/Controllers/AuthController.cs#L78) | 본문 토큰이 있으면 `Sessions.TryRemove` → 항상 204 | 없는 토큰·null 요청도 같은 최종 상태. TCP/Hub 연결 목록을 이 함수가 즉시 닫지는 않는다. |
| [`BotLogin(req)`](../../polrob.Server/Controllers/AuthController.cs#L89) | 봇 인증 기능과 `X-Polrob-Bot-Key` 확인 → `BotIdentityService.Create` → 12시간 로그인 토큰 발급 | 기능 꺼짐은 404, 잘못된 키는 401. null 요청이면 기본 이름·Robber를 사용한다. DB 계정 생성은 없다. |
| [`ValidateSession(token, out userId)`](../../polrob.Server/Controllers/AuthController.cs#L109) | 토큰 조회 → 만료 검사 → 유효하면 true와 사용자 ID | 빈/없는 토큰은 false; `Expires < UtcNow`이면 항목 제거 후 false. 다른 Controller, Hub, TCP·UDP 인증이 호출한다. 호출할 때마다 만료를 연장하지 않는다. |

### private 보조 함수

| 선언 | 구체 동작 |
|---|---|
| [`ValidateCredentials(name, password)`](../../polrob.Server/Controllers/AuthController.cs#L132) | 이름 trim 결과 길이 4~20와 허용 문자, 비밀번호 공백 여부·8자 이상을 검사한다. 실패 메시지 문자열 또는 성공 시 null. `SignUp`이 사용한다. |
| [`IsAllowedNameCharacter(ch)`](../../polrob.Server/Controllers/AuthController.cs#L158) | 영문 대소문자·숫자·밑줄·하이픈이면 true. 유니코드 전체 문자/숫자를 허용하는 검사와 다르다. |
| [`IsValidBotApiKey(expected, provided)`](../../polrob.Server/Controllers/AuthController.cs#L167) | 빈 키는 false. UTF-8 바이트 길이가 같고 `FixedTimeEquals`가 true인지 반환한다. |
| [`CreateLoginResponse(user)`](../../polrob.Server/Controllers/AuthController.cs#L180) | `CreateSession(user.Id)`의 새 토큰과 사용자 ID·이름으로 응답 record 생성. |
| [`CreateSession(userId)`](../../polrob.Server/Controllers/AuthController.cs#L185) | GUID의 N 형식 문자열을 만들고 `UtcNow + 12시간`과 함께 정적 사전에 넣는다. 토큰 문자열 반환. 기존 사용자 토큰을 제거하지 않는다. |

서버 재시작 시 이 사전은 비며, 별도의 만료 항목 전체 청소 루프는 없다. 토큰은 JWT 본문을 해독하는 값이 아니라 사전 검색 키다.

## 03. Controllers/GameController.cs

소스: [GameController.cs](../../polrob.Server/Controllers/GameController.cs#L9).

**역할·상태:** `/game` HTTP 요청을 `GameRoomService` 호출로 바꾸고 필요하면 `IHubContext<GameRoomHub>`로 다른 참가자에게 알린다. 생성자는 두 의존성을 보관하며 자기 방 목록을 만들지 않는다.

| 선언 | 입력 → 동작 → 응답 | 중요한 분기·호출 대상 |
|---|---|---|
| [`CreateRoom(request)`](../../polrob.Server/Controllers/GameController.cs#L24) | Bearer 사용자 확인 → 요청 Type/Role/IsPrivate/MapId로 방 생성 → `ServerResponse` 반환 | 인증 실패 401, 서비스 실패 400, 성공 200. 사용자 ID는 요청 본문에 없다. |
| [`GetRoomStatus(roomId)`](../../polrob.Server/Controllers/GameController.cs#L47) | 인증 → `GetRoomStatus` → 응답 Players에 자신이 있는지 확인 | 방 없음 404, 비참가자 403, 인증 실패 401. 아는 방 ID만으로 명단을 얻을 수 없다. |
| [`JoinCustomGame(request)`](../../polrob.Server/Controllers/GameController.cs#L57) | 인증·방 코드 검사 → `JoinCustomGame` → 최신 상태를 Hub 그룹 `RoomStatusUpdated`로 전송 → HTTP 응답 | 빈 코드/서비스 실패 400. Hub 알림은 await하며 이 메서드 안에서 알림 실패를 별도로 무시하지 않는다. |
| [`JoinRandomGame(request)`](../../polrob.Server/Controllers/GameController.cs#L95) | 역할로 랜덤 참가 → 그룹 상태 알림 → 최신 상태가 Matched이면 `StartGameIfMatched` 후 `GameStarted` 전송 | 참가 실패 400. 시작 서비스 결과를 `GameStarted` payload로 보내지만 이 부분에 추가 `Success` 검사는 없다. HTTP 응답은 원래 참가 결과다. |
| [`ResetRoom(request)`](../../polrob.Server/Controllers/GameController.cs#L128) | 인증·RoomId 검사 → `RejoinCustomRoomForReplay` → 그룹 상태 알림 | 존재하는 커스텀 방 재참가용이다. 임의의 새 방을 만드는 함수가 아니다. 실패 400, 성공 200. |
| [`TryGetAuthenticatedUserId(out userId)`](../../polrob.Server/Controllers/GameController.cs#L157) | Authorization의 `Bearer ` 접두사를 대소문자 무시로 검사 → 나머지 trim → `ValidateSession` | true일 때 out 값에 인증 사용자 ID. 다른 이름의 사용자 ID를 본문에서 가져오지 않는다. |

**파일 안의 요청 타입:** `CreateRoomRequest` 기본값은 Type=`custom`, Role=Police, IsPrivate=true, MapId=`MapRegistry.DefaultId`다. `JoinCustomGameRequest`는 RoomCode와 기본 Robber 역할, `JoinRandomGameRequest`는 Role, `ResetRoomRequest`는 RoomId와 Role을 담는다.

## 04. Controllers/GameRecordsController.cs

소스: [GameRecordsController.cs](../../polrob.Server/Controllers/GameRecordsController.cs#L8).

**역할·상태:** 자신의 경기 통계를 읽는 `/game-records/me/stats` endpoint다. 생성자는 `GameRecordDbService` 하나를 보관한다.

- [`GetMyStats(cancellationToken)`](../../polrob.Server/Controllers/GameRecordsController.cs#L18): Bearer로 사용자 ID를 확인한다. 실패하면 401, 성공하면 `GetPlayerStatsAsync(userId, cancellationToken)`을 기다려 200과 `PlayerGameStats`를 반환한다. URL에 다른 사용자의 ID를 받지 않는다. 요청 취소·DB 예외는 여기서 다른 값으로 치환하지 않는다.
- [`TryGetAuthenticatedUserId(out userId)`](../../polrob.Server/Controllers/GameRecordsController.cs#L28): Bearer 접두사, trim한 토큰, `AuthController.ValidateSession`, 비어 있지 않은 사용자 ID를 확인하여 bool/out 값을 반환한다. `GameController`와 같은 형태의 수동 인증이다.

## 05. Controllers/VoiceController.cs

소스: [VoiceController.cs](../../polrob.Server/Controllers/VoiceController.cs#L9).

**역할·상태:** 게임에 참가한 현재 연결이 자기 팀 LiveKit 방에 입장할 토큰을 받게 한다. 생성자는 로비 서비스, 토큰 발급 서비스, 활성 연결 registry, logger를 보관한다.

- [`CreateToken(request)`](../../polrob.Server/Controllers/VoiceController.cs#L29): `VoiceTokenRequest.RoomId`와 로그인 사용자 ID로 `TryGetAuthenticatedTeamVoiceAccess`를 호출한다. 시작 상태의 방·참가자·음성 세션을 얻고 registry에서 현재 연결 ID를 얻으면 `CreateTeamVoiceToken`을 호출해 `VoiceConnectionInfo`를 반환한다. 로그인 실패 401, 빈 방 ID 400, 방/참가/활동 자격 실패 403이다. `InvalidOperationException`은 설정 오류로 로그를 남기고 503 Problem 응답으로 바꾼다.
- [`TryGetAuthenticatedUserId(out userId)`](../../polrob.Server/Controllers/VoiceController.cs#L73): Authorization Bearer 토큰의 유효성을 확인한다. 팀·이름·사용자 ID는 클라이언트의 토큰 요청 필드로 정하지 않는다.

활동 자격은 registry에 남은 연결과 heartbeat lease로 판단한다. 매 발급 때 소켓에 probe를 보내 확인하는 구조가 아니므로 물리적인 연결 단절과 자격 제거 사이에 지연이 있을 수 있다.

## 06. Hubs/GameRoomHub.cs

소스: [GameRoomHub.cs](../../polrob.Server/Hubs/GameRoomHub.cs#L9).

**역할·상태:** 인증된 SignalR 연결을 방 그룹에 등록하고 로비 시작·역할 변경·참가 취소를 처리한다. `Connections`는 연결 ID → 방/사용자, `ActiveUserConnections`는 `roomId:userId` → 최신 연결 ID다. 두 사전은 static이며 재접속 이전 연결의 정리를 구분한다. `Context.Items`에는 인증 사용자 ID와 원래 토큰을 저장한다. 기본 disconnect 유예는 10초다.

### 공개 Hub 메서드

| 선언 | 입력 → 처리 → 출력/부작용 |
|---|---|
| [`OnConnectedAsync()`](../../polrob.Server/Hubs/GameRoomHub.cs#L32) | query `access_token`을 먼저 보고 비었으면 Bearer 헤더 사용 → 로그인 검증 → Context.Items 저장 후 base 호출. 실패하면 `Context.Abort()`하고 돌아간다. 아직 특정 방 그룹에는 들어가지 않는다. |
| [`JoinRoom(roomId)`](../../polrob.Server/Hubs/GameRoomHub.cs#L59) | 사용자 재인증 → 빈 ID/로비 미참가이면 caller에 실패 `RoomStatusUpdated` → 추적 사전 갱신·그룹 추가·현재 상태 전달. Matched이고 `!IsPrivate`인 경우에만 `StartGameIfMatched`와 caller `GameStarted`를 추가 실행한다. |
| [`StartGame(roomId)`](../../polrob.Server/Hubs/GameRoomHub.cs#L99) | 사용자 재인증·방 ID·방장 권한 확인 → 서비스 시작 검사. Success와 Matched가 모두 true면 그룹 전체에 `GameStarted`; 아니면 caller에 상태 응답. |
| [`ChangeRole(roomId, role)`](../../polrob.Server/Hubs/GameRoomHub.cs#L133) | 인증 사용자 자신의 역할을 서비스로 변경 → 실패는 caller, 성공은 그룹에 `RoomStatusUpdated`. 다른 참가자의 ID를 받지 않는다. |
| [`LeaveRoom(roomId)`](../../polrob.Server/Hubs/GameRoomHub.cs#L157) | 빈 ID면 조용히 반환. 사용자 재인증 후 추적된 방/사용자와 일치하면 추적 제거, 그룹에서 현재 연결 제거. **로비 참가 명단을 지우지 않는다.** 정상 게임 화면 이동 시 사용한다. |
| [`CancelMatching(roomId)`](../../polrob.Server/Hubs/GameRoomHub.cs#L177) | 아래 acknowledgement 버전을 기다리는 기존 호출 호환용 래퍼. 반환 데이터는 외부로 전달하지 않는다. |
| [`CancelMatchingWithAcknowledgement(roomId)`](../../polrob.Server/Hubs/GameRoomHub.cs#L182) | 인증·방 ID 검사 → `RemovePlayer` → 추적 제거 → 그룹 제거와 갱신 방송 → 처리 시간 로그 → `ServerResponse` 반환. 참가자 제거 성공 뒤 알림 실패는 경고만 남기고 성공 결과를 유지한다. |
| [`OnDisconnectedAsync(exception)`](../../polrob.Server/Hubs/GameRoomHub.cs#L225) | 추적 없으면 base로 종료. 있으면 10초 대기 후 최신 연결 ID와 비교하고 옛 연결 항목을 제거한다. 여전히 이 연결이 최신이고 게임이 진행 중이 아니면 로비 참가자 제거와 그룹 상태 알림. 진행 중 게임의 로비 명단은 이 경로에서 지우지 않는다. |

### private 메서드·내부 타입

| 선언 | 입력·결과와 연결점 |
|---|---|
| [`RemoveConnectionTracking(connectionId, roomId, userId)`](../../polrob.Server/Hubs/GameRoomHub.cs#L258) | 연결 역방향 항목 제거 후 `RemoveActiveUserConnection` 호출. Leave와 Cancel이 사용한다. |
| [`RemoveActiveUserConnection(roomId, userId, connectionId)`](../../polrob.Server/Hubs/GameRoomHub.cs#L264) | 최신 ID가 인자와 같을 때만 사용자 키를 제거한다. 이 함수의 구현은 조회 후 `TryRemove`이며 하나의 compare-and-remove 연산은 아니다. |
| [`CreateUserKey(roomId, userId)`](../../polrob.Server/Hubs/GameRoomHub.cs#L274) | 두 문자열 사이에 `:`를 넣어 인덱스 키 반환. |
| [`GetAuthenticatedUserId()`](../../polrob.Server/Hubs/GameRoomHub.cs#L279) | Context.Items의 두 값과 현재 로그인 사전을 다시 비교한다. 성공하면 사용자 ID, 실패하면 `HubException`. 연결 당시 인증에만 의존하지 않도록 각 호출이 사용한다. |
| `RoomConnection` | 방 ID와 사용자 ID를 묶는 private record. 실제 TCP의 ConnectionId와 SignalR ConnectionId는 서로 별개다. |

그룹의 존재가 방 참가 권한을 대신하지 않는다. HTTP에서 참가 명단을 바꾸고, Hub에서는 이미 참가한 사용자의 구독을 확인한다.

## 07. Models/CompletedGameRecord.cs

소스: [CompletedGameRecord.cs](../../polrob.Server/Models/CompletedGameRecord.cs#L3).

**역할:** 방 루프가 확정한 경기 요약을 Outbox와 DB로 넘기는 positional `sealed record`다. 자체 메서드나 저장 동작은 없으며 다음 값으로 생성한다.

| 필드 | 생성·소비 의미 |
|---|---|
| `Id`, `RoomId` | 한 경기와 그 경기의 방. Writer/DB의 중복 처리 기준은 경기 ID다. |
| `WinnerRole` | 서버가 판정한 승리 팀 |
| `PolicePlayerIds`, `RobberPlayerIds` | 시작 때 고정한 역할별 사용자 목록. `IReadOnlyList`는 이 인터페이스로 수정하지 못하게 하지만 참조한 원본 컬렉션까지 자동으로 깊은 불변화하지 않는다. |
| `StartedAtUtc`, `EndedAtUtc` | 경기 시작·종료 시각 |
| `DurationSeconds` | 방 루프가 산출한 진행 시간. 저장 서비스는 이를 timestamp 차이로 다시 계산하지 않는다. |

호출자는 `TryEnqueueCompletedGameRecord`, 직렬화·소비자는 Outbox/Writer/DbService다. record 생성자 자체에는 ID·날짜·역할 검증이 없고 소비 단계의 검증을 따른다.

## 08. Models/PlayerGameOutcome.cs

소스: [PlayerGameOutcome.cs](../../polrob.Server/Models/PlayerGameOutcome.cs#L3).

**역할:** `PlayerRole`, `WinnerRole` 두 필드의 `readonly record struct`다. “이 참가자가 어느 팀이었고 어느 팀이 이겼나”만 표현한다. DB 통계 projection을 읽은 뒤 이 값으로 바꾸어 `GameRecordStatsAccumulator.Add`에 넣는다. ID·시각·좌표는 없다. 자체 검증은 없고 accumulator가 enum 범위를 검사한다.

## 09. Network/ActiveGameParticipantRegistry.cs

소스: [ActiveGameParticipantRegistry.cs](../../polrob.Server/Network/ActiveGameParticipantRegistry.cs#L5).

**역할·상태:** `(RoomId, UserId)`별 최신 TCP 연결의 `ConnectionLease(ConnectionId, LastSeenUtc)`를 저장한다. `_connections`는 동시성 사전이고 `_timeProvider`, `_activeLease`가 시간 판정 기준이다. 게임 좌표나 로그인 토큰 저장소가 아니다.

| 선언 | 입력 → 처리 → 결과/부작용 |
|---|---|
| [`ActiveGameParticipantRegistry()`](../../polrob.Server/Network/ActiveGameParticipantRegistry.cs#L14) | 시스템 시간과 기본 45초 lease로 다른 생성자를 호출한다. |
| [`ActiveGameParticipantRegistry(timeProvider, activeLease)`](../../polrob.Server/Network/ActiveGameParticipantRegistry.cs#L19) | 시간원과 기간을 보관한다. 테스트에서 가짜 시간을 넣을 수 있다. |
| [`Register(roomId, userId, connectionId)`](../../polrob.Server/Network/ActiveGameParticipantRegistry.cs#L27) | 현재 시간으로 새 lease를 만들어 같은 방/사용자 항목을 교체한다. `HandleRoomJoin`이 호출한다. |
| [`Refresh(roomId, userId, connectionId)`](../../polrob.Server/Network/ActiveGameParticipantRegistry.cs#L34) | 현재 ID가 같으면 새 시각의 lease로 `TryUpdate`; 경쟁에서 실패하면 재조회한다. 새 연결이거나 항목이 없으면 false. 승인된 TCP heartbeat가 호출한다. |
| [`Unregister(roomId, userId, connectionId)`](../../polrob.Server/Network/ActiveGameParticipantRegistry.cs#L54) | 같은 연결인지 확인하고 조회한 key/value 쌍 자체가 현재 항목일 때만 삭제한다. 오래된 퇴장으로 교체 연결이 삭제되는 것을 제한한다. |
| [`IsActive(roomId, userId)`](../../polrob.Server/Network/ActiveGameParticipantRegistry.cs#L69) | `TryGetConnectionId`의 bool 결과만 돌려준다. |
| [`TryGetConnectionId(roomId, userId, out connectionId)`](../../polrob.Server/Network/ActiveGameParticipantRegistry.cs#L74) | 존재하고 `현재 - LastSeen <= lease`이면 true와 ID, 아니면 false와 빈 문자열. 만료 항목을 여기서 지우지는 않는다. VoiceController가 사용한다. |

## 10. Network/GameNetworkServer.cs

소스: [GameNetworkServer.cs](../../polrob.Server/Network/GameNetworkServer.cs#L13).

**역할:** `BackgroundService`인 실시간 게임 서버의 본체다. 이 파일에는 의존성·상수·상태 사전, 방 입퇴장, 이동 시뮬레이션, 체포·탈옥, 시야 차폐 수학이 있다. 소켓 프레임·방 반복·종료·계측 메서드는 다른 partial 파일에 있다.

### 필드와 소유권

| 필드 묶음 | 의미·접근 방식 |
|---|---|
| `_gameSessions`, `_playerRooms` | 방 ID → GameSession, 사용자 ID → 현재 방/연결. 네트워크 입력 라우팅과 오래된 연결 배제에 사용한다. |
| `_tcpPeers`, `_tcpClientTasks`, `_roomLoopTasks` | writer에 해당하는 실제 송신 큐, 살아 있는 TCP 작업, 방 작업. 서버 종료까지 추적한다. |
| `_udpRateLimits`, `_globalUdpRateLimit` | 사용자별·서버 전체 token bucket. |
| `_tcpListener`, `_udpClient` | 서버 소켓. 기본 TCP 7777/UDP 7778; 0은 테스트의 OS 지정 포트다. |
| `_gameRoomService`, `_activeGameParticipants`, `_liveKitRoomAdminService`, `_gameRecordQueue` | 로비 조회, 활동 연결, 음성 정리, 결과 접수의 외부 경계. |
| `_operations`, `_admission`, `_metrics`, `_logger`, `_runtimeMetrics` | 운영 수용 판단과 관측. 앞의 세 의존성은 null이 허용되어 테스트 등에서도 사용한다. |
| `_acceptedTcpConnections`, `_currentTcpConnections`, `_pendingUdpSends`, `_draining` | 접속 허용 수·현재 처리 수·진행 중 UDP 송신·종료 준비. 공유 접근은 Interlocked/Volatile을 쓴다. |
| `*ThisSecond` 카운터 | 다음 계측 callback에서 가져가고 0으로 초기화하는 구간 수치. |

방 내부 Player·체포·구조 상태의 변경은 방 루프에서 수행한다. ConcurrentDictionary는 항목 접근을 지원할 뿐 내부 Player의 여러 속성을 자동으로 한 트랜잭션으로 바꾸지 않는다.

### 상수

| 상수 | 실제 계산에서의 의미 |
|---|---|
| `RoomTickInterval=50ms` | 반복 마지막의 대기 길이 |
| `UdpMovementBroadcastInterval=100ms`, `GameRuleTickInterval=100ms` | 위치 전송·체포/탈옥 주기 누적 기준 |
| `GameStateSyncInterval=1s`, `EmptyRoomStopDelay=2s` | phase/시간 갱신과 빈 방 정리 유예 |
| `ServerPlayerSpeed=4`, `MovementUnitsPerSecondMultiplier=60` | 최대 이동량 240 좌표 단위/초 |
| `ServerPlayerRadius=25`, `MovementInputTimeout=250ms` | 서버 원 반경과 입력 만료 |
| `VisionRangePlayerSizeMultiplier=2.5`, `VisionConeAngleDegrees=90` | 시야 거리 `25×2×2.5=125`, 좌우 45도 |
| `GameDurationSeconds=300`, `ArrestDurationSeconds=2`, `JailBreakDurationSeconds=3` | 경기 제한시간·체포 진행·한 구조 완료 시간 |
| `JailBreakReleaseOffset=20`, `TcpListenBacklog=2048` | 감옥 아래 석방 여유와 TCP accept 대기열 인자 |

### 생성자·서비스 수명

[`GameNetworkServer(...)`](../../polrob.Server/Network/GameNetworkServer.cs#L83)는 의존성을 보관하고 설정에서 제한값을 읽어 clamp한다. 기본 TCP 최대 접속 2048, 송신 큐 64개/262144바이트, Join/idle/send 기한 10/45/5초, UDP 미완료 송신 1024, 방 명령 큐 4096이다. 방 큐는 최소 256, drain은 0~300초로 제한한다. UDP socket은 생성자에서 실제 bind되고 TCP listener는 다음 `ExecuteAsync`에서 시작한다. 포트 사용 충돌 등 소켓 생성 오류는 생성자에서 발생할 수 있다.

[`ExecuteAsync(stoppingToken)`](../../polrob.Server/Network/GameNetworkServer.cs#L129)는 TCP listen 시작 → NetworkRunning 표시 → 1초 계측 Timer 생성 → accept/UDP receive 작업을 동시에 기다린다. finally에서는 추적 중 TCP 작업을 모두, 이어 방 루프 작업을 모두 기다린 뒤 NetworkRunning을 false로 만든다. 방 루프의 마지막 기록 인계가 끝나기 전에 생산자인 네트워크 서비스가 정상 종료했다고 표시하지 않기 위한 순서다.

[`Dispose()`](../../polrob.Server/Network/GameNetworkServer.cs#L174)는 Timer, TCP listener, UDP client, RuntimeMetricSampler를 해제하고 base를 호출한다. drain 대기를 담당하는 함수는 별도 partial의 `StopAsync`다.

### HandleRoomJoin

소스: [`HandleRoomJoin(roomId, gameSession, command)`](../../polrob.Server/Network/GameNetworkServer.cs#L183).

- **입력:** Join 명령에 담긴 Player ID, TCP client/writer, 연결 ID, 로그인 토큰과 해당 방의 현재 상태.
- **처리:** 이미 끊긴 client면 반환한다. 로그인 사용자 ID와 명령 Player ID를 다시 비교하고, 로비 서비스에서 현재 참가 Player 사본을 다시 조회한다. 실패하면 소켓을 닫는다. 속도 4·반경 25·각도 0·정지·비수감으로 초기화하고 `PositionPlayerForRoom`으로 스폰한다.
- **부작용:** 새 이동 토큰과 PlayerSession을 만들고 Sessions, `_playerRooms`, 활동 registry를 갱신한다. 빈 방 시각과 이전 UDP rate limit을 초기화한다. 보이는 플레이어 목록을 만든 뒤 `MovementSession → InitialState → 진행 중 Arrested들 → GameState` 순으로 송신 큐에 넣는다. 동료 Joined와 시야·근접 갱신도 수행한다.
- **실패·호출 관계:** DrainRoomCommands가 호출한다. 첫 MovementSession 접수가 false이면 즉시 `HandleRoomLeave`를 수행한다. 스폰 예외 등은 방 루프의 외부 catch로 전파된다. 같은 사용자가 다시 들어와도 이전 위치·수감 상태를 복원하는 함수가 아니다.

### HandleRoomLeave

소스: [`HandleRoomLeave(roomId, gameSession, command)`](../../polrob.Server/Network/GameNetworkServer.cs#L284).

- **입력:** 플레이어 ID·떠난 연결 ID·역할. **먼저** 이전 음성 참가자 제거를 요청한다.
- **처리:** 현재 세션의 ConnectionId가 다르면 현재 게임 세션은 건드리지 않고 반환한다. 같으면 Sessions에서 제거하고 수감 시각, 구조 시작·진행률, UDP 대기 ID, rate limit, 그 사람이 경찰/도둑인 체포 항목을 삭제한다.
- **부작용:** `_playerRooms`/활동 등록 정리 후 시작 취소 조건이면 Rematching을 처리하고 종료한다. 그 밖에는 동료와 자신을 보고 있던 상대에게 Left를 전송하고 구조/시야/근접 정보를 갱신한다. 수감자 퇴장이면 남은 수감자를 재배치한다.
- **호출자:** 일반 퇴장 명령 처리, PendingLeaves 복구, 입장 토큰 송신 실패. 옛 연결의 늦은 Leave가 새 세션을 지우지 않는 ID 비교가 핵심이다.

### 입퇴장 보조 메서드

| 선언 | 입력 → 처리 → 결과/부작용 |
|---|---|
| [`RemoveTeamVoiceParticipant(roomId, playerId, connectionId, role)`](../../polrob.Server/Network/GameNetworkServer.cs#L366) | 로비가 현재 팀 음성 접근 정보를 주면 그 VoiceSessionId와 연결 ID로 LiveKit 참가자 제거를 시작한다. 원격 완료를 await하지 않는다. 현재 로비 조건이 충족되지 않으면 요청하지 않는다. |
| [`RemovePlayerRoomRegistration(playerId, connectionId)`](../../polrob.Server/Network/GameNetworkServer.cs#L387) | 사용자 등록의 연결 ID가 같을 때 key/value 쌍으로 삭제하고 registry의 같은 연결을 Unregister한다. 새 등록을 오래된 cleanup이 무조건 지우지 않는다. |
| [`TryAbortGameStart(roomId, gameSession, leavingPlayerId)`](../../polrob.Server/Network/GameNetworkServer.cs#L403) | 일반 방의 Waiting/Countdown이고 로비가 Matched일 때 `AbortGameStart` 요청. 성공하면 Rematching·countdown 0·시작 시각 null로 바꾸고 구조 진행을 지운 뒤 새 방장 포함 GameState 방송, true. 그 밖은 false. |

### HandleRoomMove

소스: [`HandleRoomMove(roomId, gameSession, command)`](../../polrob.Server/Network/GameNetworkServer.cs#L450).

현재 세션을 찾아 명령의 이동 토큰과 로그인 세션을 재확인한다. endpoint가 없으면 명령의 원격 주소를 등록하고, 이미 있으면 같은 endpoint인지 확인한다. 이후 X/Y가 유한한 float인지, Sequence가 마지막 승인 값보다 큰지 검사한다. 잘못된 숫자는 invalid 카운터, 오래된 순번은 duplicate/late 카운터를 증가시킨다.

벡터 길이가 1을 넘으면 두 축을 길이로 나누고, 아니면 원 크기를 유지한다. `InputX/Y`, 마지막 순번, 서버 수신 시각을 갱신한다. **이 함수는 Player 좌표를 이동시키지 않는다.** FlushCoalescedMoves가 호출한 뒤 같은 방 반복의 이동 시뮬레이션이 이 입력을 소비한다. 첫 순번의 기본값이 0이므로 Sequence 0은 승인하지 않으며 endpoint 등록은 숫자·순번 검사보다 먼저다.

### SimulateAuthoritativeMovement

소스: [`SimulateAuthoritativeMovement(gameSession, elapsed, now)`](../../polrob.Server/Network/GameNetworkServer.cs#L495).

입력 elapsed를 0~0.1초로 제한하고 접속자별 최근 방향을 읽는다. Playing이 아니거나 체포/수감 잠금이 있거나 최근 입력이 250ms보다 오래되면 방향을 0으로 만든다. 움직이던 사람이 멈추면 정지 정보도 다음 UDP 대상으로 등록한다.

방향이 있으면 `Speed × 60 × deltaSeconds`로 후보 이동량을 계산한다. 반경만큼 맵 경계를 피하도록 좌표를 clamp한 뒤 X축 후보, 이어 갱신된 X에서 Y축 후보를 충돌 검사한다. 허용 축만 반영하고 `atan2(y,x)×180/π−90`으로 각도를 바꾼다. `IsMoving`은 입력이 아니라 실제 좌표 변화 여부이며, 수감 시각 보조 갱신과 UDP 대기 등록을 한다. 벽에 막혀도 입력 방향에 따른 회전은 가능하다.

`RunRoomTickLoopAsync`가 반복마다 한 번 호출한다. 다른 플레이어 원과의 충돌은 여기서 검사하지 않는다. 긴 서버 지연을 전부 한 번에 큰 이동으로 보상하지 않는다.

### 이동·스폰·전송 보조

| 선언 | 처리와 결과 |
|---|---|
| [`IsMovementPositionBlocked(gameSession,x,y,radius,nearbyObstacles)`](../../polrob.Server/Network/GameNetworkServer.cs#L547) | 맵의 동명 함수에 그대로 전달해 bool 반환. 장애물 후보 목록은 PlayerSession의 재사용 버퍼다. |
| [`FlushPendingUdpMovementBroadcasts(gameSession)`](../../polrob.Server/Network/GameNetworkServer.cs#L552) | 대기 ID가 없으면 반환. 복사 후 집합을 비우고 시야/근접을 갱신한다. 살아 있는 각 세션의 `PlayerMovementSync`를 JSON/UTF-8로 만들고 허용된 수신자에게 UDP 전송한다. 같은 ID의 중간 위치들은 최신 상태 하나로 합쳐진다. |
| [`PositionPlayerForRoom(player,gameSession)`](../../polrob.Server/Network/GameNetworkServer.cs#L578) | 같은 ID를 제외한 접속자 목록을 만들고 맵의 역할별 spawn slot을 0부터 조회한다. 기존 플레이어의 원과 겹치지 않는 첫 후보를 Player.X/Y에 쓴다. 맵이 슬롯을 찾지 못하면 예외가 전파된다. 입장 시 배치에만 쓰인다. |

### DetectRobbersForArrest

소스: [`DetectRobbersForArrest(gameSession)`](../../polrob.Server/Network/GameNetworkServer.cs#L649).

체포 중이 아닌 경찰과 모든 도둑을 각각 ID 순서로 정렬한다. 경찰별로 도둑을 순회하며 수감자·이미 체포 대상은 건너뛴다. 도둑이 부쉬 안이면 경찰도 그 부쉬 안에 있어야 한다. 시야 거리·각도와 장애물 차폐 검사를 통과한 첫 도둑에게 `StartArrest`를 호출하고 해당 경찰의 검색을 끝낸다.

반환 데이터는 없고 ActiveArrests와 방송 상태를 바꾼다. `ProcessRoomRuleTick`이 호출하며 “가장 가까운 도둑” 정렬이나 체포 버튼 입력을 사용하지 않는다.

### StartArrest와 CompletePendingArrests

[`StartArrest(gameSession,police,robber)`](../../polrob.Server/Network/GameNetworkServer.cs#L689)는 현재 시각 + 2초의 `ArrestState`를 도둑 ID에 저장한다. 두 사람의 IsMoving을 false로 하고 도둑 팀에 체포 경찰을 먼저 보이게 만든 뒤 방 전체에 `Arrested("policeId,robberId")`와 각 PlayerState를 보낸다. 좌표를 즉시 감옥으로 옮기지는 않는다.

[`CompletePendingArrests(gameSession)`](../../polrob.Server/Network/GameNetworkServer.cs#L602)는 현재 시각까지 완료될 체포를 시간·도둑 ID 순으로 처리한다. 도둑이 사라졌으면 체포만 지운다. 남아 있으면 IsJailed=true·정지·각도 0으로 바꾸고 완료 예정 시각을 수감 시각으로 저장한다. 완료 항목 제거 후 수감자 전체를 재배치하고 상태를 알리며 체포 경찰도 정지 상태를 보낸다. 규칙 tick에서 새 체포 검색보다 먼저 실행된다.

### ArrangeJailedRobbers

소스: [`ArrangeJailedRobbers(gameSession)`](../../polrob.Server/Network/GameNetworkServer.cs#L711).

명시적으로 수감된 도둑을 수감 시각·ID 순으로 정렬한다. 수감 시각이 없으면 `DateTime.MaxValue`로 뒤쪽에 놓는다. 최대 반경을 기준으로 `Map.GetJailHoldingPosition(index,count,radius)`에 슬롯을 요청하고 각 좌표·각도 0·정지 상태를 변경한다. 변경한 수감자 목록을 `IReadOnlyList<Player>`로 반환하며 호출자가 방송한다. 수감자가 없으면 빈 목록을 반환한다. 감옥 슬롯 부족 예외를 자체 처리하지 않는다.

### UpdateJailBreakProgress

소스: [`UpdateJailBreakProgress(roomId,gameSession)`](../../polrob.Server/Network/GameNetworkServer.cs#L742).

수감자가 없으면 진행 상태를 지운다. 자유로운 도둑 중 체포 중이 아니고 구조 범위 안에 있는 사람을 고른다. 기존 구조 시작 시각이 빠른 사람을 우선하고 ID를 보조 정렬로 써서 수감자 수까지만 선택한다. 선택에서 빠진 구조자의 시작 시각과 진행률을 제거한다.

각 구조자의 시작 시각을 처음 한 번 기록하고 `(now-start)/3초`를 0~1로 제한한다. 1에 도달한 구조자가 있으면 `ReleaseJailedRobbers`를 호출하고 최종 진행률 사본을 방송한다. 반환값은 없으며 방의 두 구조 사전이 상태다. 규칙 tick마다 호출되고 별도 “구조 시작” 네트워크 요청은 없다.

### ReleaseJailedRobbers와 진행률 보조

[`ReleaseJailedRobbers(roomId,gameSession,readyRescuers,now)`](../../polrob.Server/Network/GameNetworkServer.cs#L824)는 오래 수감된 순서로 완료 구조자 수만큼 대상을 고른다. 누락된 수감 시각은 now로 보충한다. 석방 위치로 이동·각도 0·정지·IsJailed=false를 적용하고 수감 시각을 삭제한다. 도둑 팀에 구조자/대상/좌표의 JailBreak를 보내고 허용 대상에 PlayerState를 보낸다. 남은 수감자를 재배치하며 완료 구조자의 진행을 제거한다. 수감자가 0명이면 모든 구조 상태를 비운다.

| 선언 | 입력 → 처리 → 결과 |
|---|---|
| [`ClearJailBreakProgress(gameSession,roomId)`](../../polrob.Server/Network/GameNetworkServer.cs#L810) | 두 구조 사전이 모두 비었으면 반환. 아니면 둘을 비우고 진행률 방송. 조건 이탈·대기/종료 phase에서 사용한다. |
| [`BroadcastJailBreakProgress(gameSession,roomId)`](../../polrob.Server/Network/GameNetworkServer.cs#L895) | 현재 진행률 사전의 **사본**과 방 ID로 DTO를 만들어 방 전체 TCP 전송. 경찰도 메시지를 받지만 진행 바 표시는 클라이언트 정책이다. |
| [`RefreshJailEntry(gameSession,player)`](../../polrob.Server/Network/GameNetworkServer.cs#L911) | 경찰이면 반환. 수감 도둑이면 시각 누락을 보충하고 자유 도둑이면 시각 제거. 좌표로 수감을 추론하지 않는다. |
| [`IsPlayerInActiveArrest(gameSession,playerId)`](../../polrob.Server/Network/GameNetworkServer.cs#L929) | 도둑 키로 존재하거나 체포 값의 PoliceId에 있으면 true. 경찰·도둑 양쪽 이동 잠금에 쓰인다. |
| [`IsPlayerMovementLocked(gameSession,player)`](../../polrob.Server/Network/GameNetworkServer.cs#L936) | 진행 중 체포 당사자이거나 수감 도둑이면 true. |
| [`IsInJail(player)`](../../polrob.Server/Network/GameNetworkServer.cs#L944) | Role=Robber와 IsJailed=true의 논리곱. 감옥 모양 안의 좌표라는 사실은 조건이 아니다. |

### IsPointInVision와 IsVisionBlockedByObstacle

[`IsPointInVision(player,x,y)`](../../polrob.Server/Network/GameNetworkServer.cs#L950)는 플레이어 중심에서 대상점까지 거리 제곱을 시야 거리 제곱과 비교하고, 정면과 대상 각도 차이가 45도 이하인지 검사해 bool을 반환한다. 여기에는 맵이나 벽 인자가 없다. **팀의 상대 표시 판정은 이 함수까지 사용한다.**

[`IsVisionBlockedByObstacle(gameSession,police,robber)`](../../polrob.Server/Network/GameNetworkServer.cs#L970)는 두 중심을 잇는 선분과 BlocksVision 건물/장애물의 교차 여부를 검사한다. 경찰이 안에 있는 부쉬는 제외한다. Polygon/Rect/Circle별 보조 함수 중 하나라도 true면 차단 true, 전부 통과하면 false다. **체포 판정은 이 추가 검사까지 사용한다.**

### 기하 계산 private 함수

| 선언 | 입력 → 계산 → 반환 및 경계 |
|---|---|
| [`DoesSegmentIntersectRectangle(startX,startY,endX,endY,left,top,right,bottom)`](../../polrob.Server/Network/GameNetworkServer.cs#L1042) | 선분 방향을 구하고 허용 매개변수 구간 [0,1]을 네 경계로 자른다. 네 번의 Clip이 모두 통과하면 true. |
| [`DoesSegmentIntersectBuilding(...,building)`](../../polrob.Server/Network/GameNetworkServer.cs#L1063) | CollisionPolygon이 3점 이상이면 그 다각형 사용. 아니면 선분 양끝을 건물 충돌 로컬 좌표로 바꾸고 유효 폭/높이 사각형에 교차 검사한다. 회전된 건물도 로컬 좌표로 처리한다. |
| [`DoesSegmentIntersectPolygon(...,polygon)`](../../polrob.Server/Network/GameNetworkServer.cs#L1096) | 점 3개 미만이면 false. 양끝 중 하나가 내부면 true. 아니면 마지막→첫점까지 포함한 모든 변과 선분 교차를 검사한다. |
| [`IsPointInsidePolygon(pointX,pointY,polygon)`](../../polrob.Server/Network/GameNetworkServer.cs#L1135) | 점이 변 위면 true. 그 밖은 수평선 교차마다 bool을 뒤집는 홀짝 판정. 위치를 변경하지 않는다. |
| [`DoSegmentsIntersect(first...,second...)`](../../polrob.Server/Network/GameNetworkServer.cs#L1169) | 각 선분에 대한 상대 양끝의 외적 부호가 모두 서로 반대면 true. 공선/끝점 접촉은 0.001 오차와 IsPointOnSegment로 추가 인정한다. |
| [`CrossProduct(startX,startY,endX,endY,pointX,pointY)`](../../polrob.Server/Network/GameNetworkServer.cs#L1224) | `(endX-startX)*(pointY-startY) - (endY-startY)*(pointX-startX)` 반환. 점이 진행 방향 어느 쪽에 있는지를 부호로 표현한다. |
| [`IsPointOnSegment(point...,start...,end...)`](../../polrob.Server/Network/GameNetworkServer.cs#L1236) | 외적 절댓값이 0.001보다 크면 false. 아니면 축별 min/max 범위 안인지 같은 오차로 검사한다. |
| [`ClipSegmentToAxis(direction,distance,ref minimum,ref maximum)`](../../polrob.Server/Network/GameNetworkServer.cs#L1255) | direction이 사실상 0이면 distance≥0 반환. 그 밖은 ratio=distance/direction으로 들어가는/나가는 구간 끝을 갱신한다. 구간이 불가능하면 false. `ref` 인자가 변경되는 수학 helper다. |
| [`DoesSegmentIntersectCircle(...,centerX,centerY,radius)`](../../polrob.Server/Network/GameNetworkServer.cs#L1289) | 원 중심의 선분 투영을 0~1로 제한해 최근접점을 구하고 거리 제곱≤반경 제곱이면 true. 길이가 0에 가까운 선분은 false다. |
| [`GetVisionRange(player)`](../../polrob.Server/Network/GameNetworkServer.cs#L1320) | 반지름×2×2.5 반환. 기본 반지름에서는 125. |
| [`GetFacingAngle(player)`](../../polrob.Server/Network/GameNetworkServer.cs#L1326) | 저장 각도+90을 정규화하여 실제 정면 각도로 변환. |
| [`NormalizeDegrees(degrees)`](../../polrob.Server/Network/GameNetworkServer.cs#L1332) | 360 나머지를 구하고 음수면 360을 더해 0≤각도<360 반환. |
| [`ShortestAngleDifference(from,to)`](../../polrob.Server/Network/GameNetworkServer.cs#L1344) | to−from을 정규화하고 180보다 크면 360을 빼 최단 차이 반환. |

### 구조 범위·석방 후보 함수

[`IsTouchingOrNearJail(gameSession,player)`](../../polrob.Server/Network/GameNetworkServer.cs#L1351)는 JailRescueArea가 있으면 중심점 포함 여부를 반환한다. 없으면 감옥 건물까지 거리 제곱이 `(반지름+90)²` 이하인지 검사한다. 기존 맵과 별도 구조 trigger를 가진 맵을 같은 인터페이스로 다룬다.

[`GetJailReleasePosition(gameSession,radius,releaseIndex)`](../../polrob.Server/Network/GameNetworkServer.cs#L1364)는 감옥 collision bounds 아래+반경+20에서 시작한다. 5줄×가로 5개 후보를 맵 경계 안으로 제한하고 `IsReleasePositionBlocked`로 검사한다. 통과 후보 중 releaseIndex를 선택하며 후보보다 index가 크면 마지막을 재사용한다. 후보가 하나도 없으면 기본 중앙/아래 좌표로 반환하며 이 fallback은 다시 충돌 검증하지 않는다.

[`IsReleasePositionBlocked(gameSession,x,y,radius)`](../../polrob.Server/Network/GameNetworkServer.cs#L1400)는 새 장애물 후보 목록으로 맵 이동 충돌을 호출한다. 다른 플레이어와 겹치는지 검사하지 않으며, bool은 맵 경계·건물·장애물에 대한 결과다.

## 11. Network/GameNetworkServer.Transport.cs

소스: [GameNetworkServer.Transport.cs](../../polrob.Server/Network/GameNetworkServer.Transport.cs#L10).

**역할·상태:** 같은 GameNetworkServer의 소켓 입력, 프레임 해석, 메시지 대상 선택, 실제 송신 연결을 구현한다. 이 파일의 전용 상수는 TCP 입력 payload 최대 4096바이트, UDP 이동 datagram 최대 2048바이트다. `StrictUtf8`은 잘못된 바이트열을 대체 문자로 숨기지 않고 예외로 처리한다. 나머지 상태 사전·제한값·카운터는 본체와 공유한다.

### AcceptTcpClientsAsync

소스: [`AcceptTcpClientsAsync(stoppingToken)`](../../polrob.Server/Network/GameNetworkServer.Transport.cs#L17).

TCP listener에서 연결을 기다리고 받아들인 수를 Interlocked로 증가시킨다. drain 상태 또는 허용 접속 수 초과이면 수를 되돌리고 소켓을 닫으며 `tcp_connections_rejected_total`을 올린다. 통과하면 작업 ID를 부여하여 `HandleTcpClientAsync`를 시작하고 추적 사전에 저장한 뒤 `ObserveTcpClientAsync`로 완료를 관찰한다.

`ExecuteAsync`가 한 번 실행하는 지속 루프다. 취소 중 accept 예외는 종료로 취급하고, 그 밖의 예외는 오류 로그를 남긴 뒤 다음 accept를 시도한다. 새 접속 수락과 게임 Join 승인은 별도 단계다.

### HandleTcpClientAsync

소스: [`HandleTcpClientAsync(client,stoppingToken)`](../../polrob.Server/Network/GameNetworkServer.Transport.cs#L51).

**준비:** socket stream, BinaryWriter, 연결별 취소 토큰, TcpPeer를 만들고 writer→peer를 등록한다. TcpPeer 송신 작업과 token bucket(초당 5, burst 10)을 시작하며 서버 취소 시 client.Close를 호출하도록 등록한다. 현재 연결 수를 올린다.

**수신:** 프레임마다 linked deadline을 만든다. 아직 Join 전이면 기본 10초, 이후이면 기본 45초 안에 **프레임 전체**를 읽어야 한다. 프레임을 받은 뒤 rate limit을 차감하고 타입을 확인한다. 첫 프레임은 Join, 이후에는 Heartbeat만 가능하고 같은 연결의 두 번째 Join은 오류다.

**Join 분기:** JSON을 GameJoinRequest로 해석하고 로그인 사용자 ID·로비 참가 Player·방 맵 일치를 검사한다. 연결 GUID를 만들고 신규 참가 수용 상태를 확인한 뒤 `GetOrCreateGameSession`과 `TryWriteRoomCommand(Join)`을 호출한다. 큐 접수 실패는 예외를 거쳐 연결 종료로 이어진다. 이 시점에는 좌표를 직접 바꾸지 않는다.

**Heartbeat 분기:** 기억한 로그인 토큰이 같은 사용자에게 유효하고 registry의 현재 연결 ID를 Refresh할 수 있으면 받은 payload 그대로 HeartbeatAcknowledged를 돌려준다. 교체된 이전 연결이나 사라진 활동 등록은 실패하며 연결을 종료한다.

**예외·finally:** 서버 취소는 정상 종료, 별도 deadline 취소는 read timeout 계측, IO/폐기 오류는 disconnect 로그, 잘못된 데이터/JSON은 거절 로그로 처리한다. 마지막에 Leave 명령을 시도한다. 큐가 차면 현재 연결인 경우 PendingLeaves에 저장하고 음성/방 등록 정리도 한다. peer 완료·취소·소켓 닫기 후 송신 작업을 기다리고 peer 사전과 두 접속 수를 정리한다. 메서드 반환값은 Task이며 주요 결과는 접속 등록/명령/정리다.

### ReadTcpFrame과 길이 helper

[`ReadTcpFrame(reader)`](../../polrob.Server/Network/GameNetworkServer.Transport.cs#L220)는 `(TcpMessageType Type,string Payload)`를 반환한다. 읽는 형식은 `[little-endian Int32 길이][타입 1바이트][7-bit UTF-8 길이][UTF-8 payload]`다. 선언 길이 1~4102, 문자열 바이트 수 최대 4096을 먼저 검사하고 정확히 그 바이트를 읽는다. 부족하면 EndOfStreamException, UTF-8 오류나 길이 불일치면 InvalidDataException이다.

선언 길이는 `1+접두사 바이트+payload 바이트`가 맞아야 하지만 이전 클라이언트의 `1+payload.Length`도 호환용으로 허용한다. 외부 선언 길이만 믿고 큰 버퍼를 만드는 경로가 아니다. 비동기 리더가 최종 검증을 이 함수로 재사용한다.

| 선언 | 처리·결과 |
|---|---|
| [`ReadSevenBitEncodedLength(reader,out prefixByteLength)`](../../polrob.Server/Network/GameNetworkServer.Transport.cs#L261) | 최대 5바이트를 7비트 단위로 조립해 문자열 바이트 길이 반환. out 값은 사용한 접두사 바이트 수. 마지막 바이트의 허용 범위 초과, Int32 초과, 불필요하게 긴 표현, 계속되는 접두사는 InvalidDataException. |
| [`GetSevenBitEncodedLength(value)`](../../polrob.Server/Network/GameNetworkServer.Transport.cs#L293) | value를 7비트씩 줄여 필요한 접두사 바이트 수 계산. SendTcp의 전체 길이 계산용이며 원문을 읽거나 쓰지 않는다. |
| [`ReadTcpFrameAsync(stream,cancellationToken)`](../../polrob.Server/Network/GameNetworkServer.Transport.cs#L639) | 4바이트 헤더·타입·접두사를 ReadExactlyAsync로 읽고 범위를 확인한 뒤 payload를 읽는다. 메모리 프레임을 만들어 ReadTcpFrame으로 최종 검증한다. 모든 읽기에 같은 프레임 deadline token을 전달해 바이트가 조금씩 도착해도 기한이 연장되지 않는다. |

### TCP 송신 메서드

| 선언 | 입력 → 처리 → 결과/부작용 |
|---|---|
| [`SendTcp(writer,type,payload)`](../../polrob.Server/Network/GameNetworkServer.Transport.cs#L306) | writer에 lock → UTF-8 바이트 길이·7-bit 접두사를 포함한 길이/타입/문자열 쓰기 → TCP 전송 수 증가. 직접 쓰는 fallback이며 소켓 예외를 자체로 잡지 않는다. |
| [`TrySendTcp(writer,type,payload)`](../../polrob.Server/Network/GameNetworkServer.Transport.cs#L318) | 등록된 peer가 있으면 `peer.TrySend`; 없으면 SendTcp. 성공 bool을 반환하고 예외는 실패 수치·로그와 false로 바꾼다. peer 경로의 true는 큐 접수이지 상대 수신 확인이 아니다. |
| [`BroadcastTcp(gameSession,type,payload,excludeId)`](../../polrob.Server/Network/GameNetworkServer.Transport.cs#L343) | 방의 모든 현재 세션을 순회하되 일치하는 excludeId는 건너뛰어 TrySendTcp. 개별 실패 bool 때문에 나머지 전송을 중단하지 않는다. |
| [`BroadcastTcpToRole(gameSession,role,type,payload,excludeId)`](../../polrob.Server/Network/GameNetworkServer.Transport.cs#L486) | 위 방식에 역할 일치 필터를 추가한다. 동료 Joined/Left와 도둑 팀 JailBreak 전송에 사용한다. |
| [`BroadcastPlayerState(gameSession,player)`](../../polrob.Server/Network/GameNetworkServer.Transport.cs#L357) | 시야·근접을 갱신하고 전체 Player를 한 번 JSON으로 만든다. 같은 팀 또는 이 ID를 VisibleOpponentPlayerIds에 가진 상대에게 PlayerState 전송. 체포 완료·석방 등의 신뢰성 있는 상태 변경에 사용한다. |

### 상대 가시성과 근접 알림 메서드

[`RefreshOpponentVisibility(gameSession)`](../../polrob.Server/Network/GameNetworkServer.Transport.cs#L419)는 현재 세션 사본을 만들고 역할별 팀/상대 목록을 나눈다. 상대 한 명마다 `IsPlayerVisibleToTeam`을 계산하여 팀원 모두의 보이는 ID 집합에 반영한다. 새로 보이면 Joined(Player JSON), 사라지면 Left(ID)를 전송한다. 이미 같은 상태면 전송하지 않는다. 위치 이동 flush, 입퇴장, 중요한 상태 변경이 호출한다.

[`IsPlayerVisibleToTeam(gameSession,sessions,teamRole,target)`](../../polrob.Server/Network/GameNetworkServer.Transport.cs#L458)는 경찰이 수감 도둑을 보는 경우 true, 도둑 팀이 현재 체포 수행 경찰을 보는 경우 true다. 그 밖에는 주어진 세션 중 같은 팀 누구라도 IsPointInVision이면 true다. **장애물 차폐 함수를 호출하지 않는다.** 그래서 표시 조건과 체포 조건은 다르다.

[`RefreshOpponentProximityAlerts(gameSession)`](../../polrob.Server/Network/GameNetworkServer.Transport.cs#L375)는 수신자마다 가장 가까운 상대의 표면 거리 `max(0,중심거리−두 반경)`를 찾는다. 시야·벽·수감 필터는 없다. `OpponentProximitySync.FromSurfaceDistance`로 0/100/200/300/400/500ms 단계로 바꾸고 직전 단계와 다를 때만 `OpponentProximity` JSON을 전송한다. 상대가 없으면 무한 거리에서 0으로 변환되며 상대 ID·좌표는 이 DTO에 없다.

### ReceiveUdpAsync

소스: [`ReceiveUdpAsync(stoppingToken)`](../../polrob.Server/Network/GameNetworkServer.Transport.cs#L505).

한 datagram을 받으면 패킷/바이트 계측 → 서버 전체 token bucket → JSON 파싱 → 사용자 현재 방 조회 → 세션·인증 검사 → 개인별 token bucket → MoveRoomCommand 큐 접수 순서다. 개인별 허용량은 인증을 통과한 패킷에만 차감한다. ID만 아는 외부 발신자가 다른 사용자의 개인 허용량을 직접 소진시키는 경로를 줄이려는 순서다.

반환값 없는 지속 Task다. JSON/크기 오류는 invalid를 올리고 무시, 서버 취소와 취소 중 소켓 폐기는 종료, 소켓 오류는 경고 후 계속, 나머지 오류는 로그 후 계속한다. 큐가 가득 차 Move가 접수되지 않아도 UDP 재전송 응답을 만들지 않는다.

### UDP 파싱·인증·송신 helper

| 선언 | 입력 → 처리 → 출력/부작용 |
|---|---|
| [`ParseUdpMovement(datagram)`](../../polrob.Server/Network/GameNetworkServer.Transport.cs#L570) | 0바이트 또는 2048바이트 초과면 InvalidDataException, 그 밖에는 PlayerMovementInput JSON 역직렬화. JSON `null`은 null 반환, 형식 오류는 JsonException. 순번·토큰·float 의미 검증은 이후 단계다. |
| [`IsAuthorizedMovement(movement,remoteEndPoint,registration,playerSession)`](../../polrob.Server/Network/GameNetworkServer.Transport.cs#L580) | 등록/세션의 연결 ID 일치, 비어 있지 않은 이동 토큰 일치, endpoint 미등록 또는 일치, 현재 로그인 토큰의 사용자 ID 일치를 모두 검사해 bool 반환. 상태를 변경하지 않는다. |
| [`BroadcastUdpToVisiblePlayers(gameSession,movingPlayer,buffer)`](../../polrob.Server/Network/GameNetworkServer.Transport.cs#L595) | 같은 역할 또는 보이는 상대이며 UDP endpoint가 있는 수신자만 선택. pending send 증가 후 최대치를 넘으면 되돌려 drop 계측, 아니면 SendUdpObservedAsync를 시작한다. 자신도 같은 역할이므로 수신 대상이다. |
| [`SendUdpObservedAsync(buffer,endpoint)`](../../polrob.Server/Network/GameNetworkServer.Transport.cs#L622) | UdpClient.SendAsync 완료 후 패킷/바이트 증가. 소켓/폐기 오류는 실패 계측과 debug 로그. finally에서 pending send를 반드시 감소시킨다. 실패 패킷을 보관해 재전송하지 않는다. |
| [`NormalizeRoomId(roomId)`](../../polrob.Server/Network/GameNetworkServer.Transport.cs#L673) | null·공백이면 `default`, 그 밖에는 입력 문자열 그대로 반환. 일반 문자열을 trim해서 다른 ID로 바꾸지 않는다. |

## 12. Network/GameNetworkServer.RoomLoop.cs

소스: [GameNetworkServer.RoomLoop.cs](../../polrob.Server/Network/GameNetworkServer.RoomLoop.cs#L5).

**역할·상태:** 방 객체 생성·제거, 명령 처리 순서, 주기 누적, 게임 phase와 결과 스냅샷 인계를 맡는다. 별도 서비스가 아니며 본체의 사전과 GameSession 상태를 사용한다. 방마다 단일 소비자 루프를 유지하는 것이 함수들의 공통 전제다.

### GetOrCreateGameSession·작업 추적

[`GetOrCreateGameSession(roomId,stoppingToken)`](../../polrob.Server/Network/GameNetworkServer.RoomLoop.cs#L7)은 기존 세션을 찾아 CommandGate 안에서 IsStopping이 아니면 반환한다. 종료 중이면 Thread.Yield 후 재조회한다. 없으면 로비에서 맵을 확인하고 GameSession을 만든다. 일반 방이 사라졌으면 InvalidOperationException, 서버 취소면 OperationCanceledException이다.

TryAdd 경쟁을 이긴 호출만 Task.Run으로 방 루프를 시작하고 작업 ID→Task를 저장한다. 반환값은 현재 GameSession이다. `RemoveCompletedRoomLoopAsync`를 동시에 시작하여 완료를 관찰한다. 이 Task.Run에는 CancellationToken.None을 주고 실제 반복 함수에는 stoppingToken을 전달하므로 예약 취소로 finally 자체가 생략되지 않도록 한다.

[`RemoveCompletedRoomLoopAsync(loopId,loopTask)`](../../polrob.Server/Network/GameNetworkServer.RoomLoop.cs#L46)는 task를 기다리고 외부로 나온 예외를 실패 계측/로그로 처리한 뒤 finally에서 작업 추적을 제거한다. 게임 상태를 계산하지 않는 작업 수명 helper다.

### TryWriteRoomCommand

소스: [`TryWriteRoomCommand(gameSession,command)`](../../polrob.Server/Network/GameNetworkServer.RoomLoop.cs#L63).

CommandGate lock 안에서 IsStopping이면 false, Channel.Writer.TryWrite가 성공하면 대기 수를 올리고 true다. 큐가 가득 찬 경우 drop 카운터를 올린 뒤 false다. Channel FullMode가 Wait여도 이 함수는 기다리는 WriteAsync가 아니라 TryWrite를 사용한다. 명령을 보낸 쪽이 Join 종료, Move 드롭, Leave 보류라는 후속 행동을 선택한다.

### RunRoomTickLoopAsync

소스: [`RunRoomTickLoopAsync(roomId,gameSession,stoppingToken)`](../../polrob.Server/Network/GameNetworkServer.RoomLoop.cs#L84).

루프 지역 상태는 마지막 시각과 UDP/규칙/상태 갱신의 누적 경과 시간이다. 매 반복에서 명령 처리 → 이동 시뮬레이션 → 밀린 100ms UDP flush → 밀린 100ms 규칙 처리 → 밀린 1초 상태 갱신 → 빈 방 종료 검사 → 비용 계측 → 50ms 대기를 수행한다. 각 누적 주기는 while로 따라잡지만 이동은 elapsed를 받은 한 번의 호출이다.

취소는 정상 종료로 취급한다. 그 밖의 예외는 방 실패 계측과 로그 후 루프를 끝낸다. finally에서 IsStopping 설정, PendingGameRecord 마지막 접수 시도, 미완료 Playing/Countdown 중단 처리, 등록 제거·소켓 닫기, 미처리 Join 소켓 닫기, 세션/보류 퇴장 비우기, 자기 방 객체의 사전 항목 제거, Channel 완료를 수행한다. 스폰·규칙 예외도 이 방 전체 정리로 이어질 수 있다.

50ms는 반복 마지막 대기이며 정확히 20Hz로 시작 시각을 고정하는 타이머는 아니다. 처리 시간이 50ms를 넘으면 overrun 계측을 올린다.

### TryStopRoomLoop

소스: [`TryStopRoomLoop(roomId,gameSession,now)`](../../polrob.Server/Network/GameNetworkServer.RoomLoop.cs#L189).

미접수 결과·보류 퇴장·접속자가 있으면 EmptySinceUtc를 비우고 false를 반환한다. 없다면 첫 빈 시각을 기록하고 2초를 기다린다. 기한 이후 CommandGate 안에서 접속자와 큐를 다시 확인하여 들어온 Join 등을 놓치지 않도록 한다.

자기 GameSession이 현재 사전 항목인 경우 IsStopping을 표시한다. Playing인 일반 방은 `AbandonGameAfterDisconnect`로 로비도 정리한다. 이 호출 예외면 IsStopping을 풀고 false라 다음 반복에서 재시도할 수 있다. 현재 key/value 쌍 제거가 성공하면 Channel을 완료하고 true, 실패하면 stopping을 풀고 false다. 결과가 보류 중인 빈 방은 이 함수로 폐기하지 않는다.

### DrainRoomCommands와 FlushCoalescedMoves

[`DrainRoomCommands(roomId,gameSession)`](../../polrob.Server/Network/GameNetworkServer.RoomLoop.cs#L255)는 최대 512개만 읽고 대기 수를 감소시킨다. Move는 플레이어 ID별로 **Sequence가 가장 큰 명령**을 임시 사전에 남긴다. Join/Leave를 만나면 앞까지 모인 Move를 먼저 flush하고 임시 사전을 비운 뒤 입퇴장을 처리한다. 한도/큐 끝에서도 남은 Move를 flush하고 PendingLeaves를 꺼내 실제 Leave를 처리한다.

[`FlushCoalescedMoves(roomId,gameSession,latestMoveByPlayerId)`](../../polrob.Server/Network/GameNetworkServer.RoomLoop.cs#L294)는 null/빈 사전이면 반환하고 남은 각 Move에 `HandleRoomMove`를 호출한다. 새 좌표를 직접 계산하지 않는다. 병합 결과의 토큰·순번 재검증은 HandleRoomMove에 있다. 병합은 입퇴장 경계를 넘겨 합치지 않으므로 세션 변경 전후 입력의 순서를 보존하려 한다.

### ProcessRoomRuleTick

소스: [`ProcessRoomRuleTick(roomId,gameSession)`](../../polrob.Server/Network/GameNetworkServer.RoomLoop.cs#L311).

Playing이 아니거나 현재 접속자가 0이면 구조 진행을 지우고 반환한다. 진행 중이면 체포 완료 → 새 체포 탐색 → 구조 진행/석방 순서로 호출한다. 반환 데이터는 없고 GameSession 및 TCP 이벤트가 결과다. 승리 판정은 이 함수가 아니라 다음 상태 동기화 함수가 담당한다.

### ProcessRoomStateSync

소스: [`ProcessRoomStateSync(roomId,gameSession)`](../../polrob.Server/Network/GameNetworkServer.RoomLoop.cs#L325).

먼저 로비의 만료 빈 방을 정리하고 PendingGameRecord 재접수를 시도한다. 실패하면 즉시 반환한다. 성공하면 로비 CompleteGame을 호출한다. 이 결과 재시도는 접속자 0명 검사보다 먼저 있으므로 전원 이탈 후에도 실행된다.

현재 접속자가 있으면 phase별로 다음을 처리한다.

| phase | 입력 상태 → 변경 |
|---|---|
| Waiting | 신규 경기 허용+명단 준비이면 Countdown, 카운트 3, GameTime 300, 승자 null, 경과 0 |
| Countdown | 카운트를 1 감소. **0 미만**일 때 시작 조건을 재확인한다. 실패면 Waiting/3/시작 시각 null, 성공이면 Playing/0/현재 시각/새 경기 ID/정렬·중복 제거한 시작 명단/접수 플래그 false |
| Playing | GameTime 감소, 현재 연결된 도둑의 수감 상태 갱신. 시간 만료 또는 한 명 이상인 현재 도둑 전원 수감이면 Ended. 시간이 0 이하면 도둑, 그 밖은 경찰 승리. 경과 시간 계산 후 결과 접수 성공 때만 CompleteGame으로 진행 |
| Ended·Rematching | 이 함수에서 새 경기로 되돌리지 않고 현재 상태 DTO를 방송 |

끝의 GameState에는 방/맵/phase/시간/승자와 현재 전체·수감 도둑 수가 들어간다. 시작 명단은 기록용, 현재 도둑 목록은 승패 판정용이라는 차이가 있다. 0명의 도둑을 “전원 수감”으로 간주하지 않는다. 시간 만료와 전원 수감이 같은 갱신에 겹치면 도둑 승리다.

### 결과 접수·준비 조건 helper

| 선언 | 입력 → 동작 → 결과·경계 |
|---|---|
| [`CanStartNewGame()`](../../polrob.Server/Network/GameNetworkServer.RoomLoop.cs#L448) | 로컬 drain=0, 운영 drain 아님, Admission 거절 아님이면 true. 설정값을 읽는 함수가 아니라 현재 수용 상태 판단이다. |
| [`TryEnqueueCompletedGameRecord(roomId,gameSession,endedAtUtc)`](../../polrob.Server/Network/GameNetworkServer.RoomLoop.cs#L451) | 이미 성공 플래그면 true. 경기 ID/시작 시각/승자 누락이면 InvalidOperationException. 최초 스냅샷을 PendingGameRecord에 만들고 IGameRecordQueue.TryEnqueue. 성공하면 성공 플래그·pending 제거·미저장 운영 표시 해제; false/예외면 같은 결과 유지·미저장 표시·실패 계측 후 false. |
| [`IsRoomReadyForCountdown(roomId,gameSession)`](../../polrob.Server/Network/GameNetworkServer.RoomLoop.cs#L487) | default는 접속자>0. 일반 방은 로비 조회 성공+Matched+TCP 접속 수≥max(1,로비 CurrentCount). 준비 bool 반환. |
| [`HasRequiredConnectedRoles(gameSession)`](../../polrob.Server/Network/GameNetworkServer.RoomLoop.cs#L504) | 현재 Sessions의 역할 집합에 Police·Robber가 모두 있는지 반환. 일반 방 카운트다운 마지막 단계에서 사용한다. |

GameRecordEnqueueAttempted는 이름과 달리 “시도했다”가 아니라 “접수에 성공했다”일 때만 true다. 정상 종료 방송은 접수 뒤지만 별도 Join 초기 상태는 현재 Ended phase를 읽을 수 있으므로 모든 메시지 경로의 전역 장벽으로 이해하면 안 된다.

## 13. Network/GameNetworkServer.Lifecycle.cs

소스: [GameNetworkServer.Lifecycle.cs](../../polrob.Server/Network/GameNetworkServer.Lifecycle.cs#L5).

**역할·상태:** 서비스 종료 요청과 TCP Task 완료 관찰. 필드는 본체의 `_draining`, `_operations`, `_gameSessions`, `_tcpClientTasks`를 사용한다.

- [`StopAsync(cancellationToken)`](../../polrob.Server/Network/GameNetworkServer.Lifecycle.cs#L7): 운영/local drain을 설정한다. Stopwatch로 기한을 재며 Playing 또는 PendingGameRecord가 있는 방이 남아 있는 동안 최대 drain 시간까지 100ms씩 대기한다. 종료 토큰이 취소되면 대기 취소를 잡고 `base.StopAsync`로 넘어간다. Countdown만 있는 방을 이 대기 조건 때문에 끝날 때까지 기다리지는 않는다. 실제 방 finally는 base가 전달하는 서비스 취소 이후 실행된다.
- [`ObserveTcpClientAsync(id,task)`](../../polrob.Server/Network/GameNetworkServer.Lifecycle.cs#L25): TCP 작업을 기다리고 밖으로 나온 예외를 logger에 남긴 뒤 finally에서 추적 ID를 제거한다. 소켓 읽기 자체나 게임 명단 정리는 HandleTcpClientAsync가 담당한다.

## 14. Network/GameNetworkServer.Observability.cs

소스: [GameNetworkServer.Observability.cs](../../polrob.Server/Network/GameNetworkServer.Observability.cs#L6).

**역할·상태:** 1초 Timer가 네트워크 카운터와 현재 방 상태를 계측한다. `_samplingMetrics`는 중복 callback 방지 플래그이며, 계측 대상 상태의 소유자는 기존 게임/소켓 코드다.

| 선언 | 입력 → 처리 → 결과 |
|---|---|
| [`LogLoadMetricsCallback(state)`](../../polrob.Server/Network/GameNetworkServer.Observability.cs#L11) | Timer 인자 state는 사용하지 않는다. Interlocked로 실행권을 얻지 못하면 반환, 얻으면 SampleLoadMetrics. 예외는 로그, finally에서 guard 해제. |
| [`SampleLoadMetrics()`](../../polrob.Server/Network/GameNetworkServer.Observability.cs#L19) | 구간 카운터를 Exchange(...,0)로 수집, 현재 접속/방/참가/큐/phase와 로비 snapshot·런타임 값 조회. OperationalMetrics Add/Set 후 `[LoadMetrics]` 한 줄 출력. 상태를 치료하거나 접속자를 제거하지 않는다. |
| [`SerializeForMetrics<T>(value)`](../../polrob.Server/Network/GameNetworkServer.Observability.cs#L96) | 직렬화 횟수 증가 후 JsonSerializer.Serialize(value) 문자열 반환. UDP/TCP 메시지 준비에 사용하며 serialization 예외를 자체 처리하지 않는다. |

`/s` 카운터는 지난 Timer callback 이후 누적량이다. 타이머가 늦어졌다고 실제 경과초로 나누는 보정은 이 파일에 없다. 현재 사전의 snapshot과 카운터를 서로 다른 순간에 읽으므로 완전히 원자적인 전체 서버 상태 캡처도 아니다.

## 15. Network/GameSession.cs

소스: [GameSession.cs](../../polrob.Server/Network/GameSession.cs#L9).

**역할:** 방의 변경 가능한 상태와 명령·접속 상태 자료형을 한 파일에 정의한다. 메서드가 많은 서비스가 아니라 방 루프가 사용하는 상태 컨테이너다.

[`GameSession(commandQueueCapacity,mapId=MapRegistry.DefaultId)`](../../polrob.Server/Network/GameSession.cs#L11)는 GameMap을 만들고 SingleReader=true/SingleWriter=false인 bounded Channel을 만든다. FullMode는 Wait지만 실제 쓰기 함수는 TryWrite이므로 운영 경로는 큐가 차면 false를 받는다. 유효하지 않은 맵·채널 용량의 예외를 생성자에서 별도로 복구하지 않는다.

| GameSession 필드 | 읽기·변경 주체와 의미 |
|---|---|
| `Map` | 이 방의 선택된 맵. 생성 후 속성 참조는 바꾸지 않는다. |
| `CommandGate`, `Commands`, `QueuedCommandCount`, `IsStopping` | 수신자가 명령을 넣고 방 루프가 소비한다. gate는 종료 결정과 새 큐 입력을 조정한다. |
| `PendingLeaves` | 큐 접수 실패 퇴장을 연결 ID별로 보관하는 동시성 사전. 루프가 따로 소비한다. |
| `Sessions` | 플레이어 ID → PlayerSession. 로비 Players와 별도이며 현재 게임 연결만 포함한다. |
| `JailEntryTimes` | 수감자 ID → 서버 확정 수감 시각. 배치·석방 순서 결정. |
| `ActiveArrestsByRobberId` | 도둑 ID → 체포 경찰/완료 시각. 방 루프 소유 일반 Dictionary. |
| `JailBreakStartedAtByRescuer`, `JailBreakProgressByRescuer` | 구조자별 시작 시각과 0~1 진행률. |
| `PendingUdpMovementPlayerIds` | 다음 flush에서 최신 상태를 보낼 사람의 HashSet. |
| `GamePhase`, `CountdownTime`, `GameTime` | 기본 Waiting(enum 0), 3, 300. 실제 전이는 RoomLoop가 수행. |
| `WinnerRole`, `ElapsedGameTime`, `GameStartedAtUtc`, `GameRecordId` | 결과 계산을 위한 경기 상태. |
| `StartingPolicePlayerIds`, `StartingRobberPlayerIds` | Playing 진입 시 만든 기록 참가자 명단. |
| `PendingGameRecord`, `GameRecordEnqueueAttempted` | 같은 결과의 접수 재시도와 접수 성공 여부. |
| `HasHadPlayers`, `EmptySinceUtc` | 입장 이력·빈 시각. 현재 빈 방 종료는 HasHadPlayers를 조건으로 검사하지 않는다. |

| 추가 타입 | 필드·의미 |
|---|---|
| `RoomCommand` | abstract record. 명령 종류를 switch pattern으로 구분하는 공통 타입. |
| `JoinRoomCommand` | Player, Client, Writer, ConnectionId, SessionToken. 큐에서 입장을 재검증하는 재료. |
| `LeaveRoomCommand` | PlayerId, ConnectionId, Role. 같은 사용자의 이전 연결 퇴장 구별. |
| `MoveRoomCommand` | PlayerMovementInput과 RemoteEndPoint. 큐 처리 때 토큰·주소를 재확인. |
| `PlayerRoomRegistration` | RoomId와 ConnectionId를 담은 readonly record struct. UDP 라우팅 사전 값. |
| `ArrestState` | PoliceId, RobberId, CompletesAtUtc. 체포 시작 시 만들고 완료/퇴장 때 제거. |

`PlayerSession`은 접속과 Player를 연결한다. ConnectionId·SessionToken·MovementSessionToken은 init 속성이고, Client/Writer/PlayerState는 연결·송신 식별·현재 좌표다. UdpEndPoint는 첫 인증 명령에서 등록한다. InputX/Y·LastMovementInputSequence·LastMovementInputAtUtc가 이동의 최신 입력이며 마지막 시각 기본값은 DateTime.MinValue다. NearbyCollisionObstacles는 재사용 목록, VisibleOpponentPlayerIds는 수신자가 아는 상대 ID, LastOpponentProximityPulseMilliseconds=-1은 최초 단계 알림을 유도하는 sentinel이다.

## 16. Network/RuntimeMetricSampler.cs

소스: [RuntimeMetricSampler.cs](../../polrob.Server/Network/RuntimeMetricSampler.cs#L6).

**역할·상태:** .NET System.Runtime meter의 9개 instrument를 읽어 사람이 볼 로그 문자열로 만든다. `_series`는 instrument+tag별 최근 관측값, `_previousValues`는 이름별 직전 샘플, `_previousSampleAtUtc`는 비율 계산의 시간 기준이다. GameNetworkServer가 객체 하나를 소유하고 관측 callback에서 Sample을 호출한다.

| 선언 | 입력 → 계산 → 결과 |
|---|---|
| [`RuntimeMetricSampler()`](../../polrob.Server/Network/RuntimeMetricSampler.cs#L26) | MeterListener를 만들고 지정된 System.Runtime instrument만 활성화한다. int/long/float/double/decimal measurement callback을 RecordMeasurement에 연결하고 시작한다. |
| [`Sample()`](../../polrob.Server/Network/RuntimeMetricSampler.cs#L44) | observable instrument 측정 요청 → 실제 경과초 최소 0.001 → 같은 이름의 태그별 시리즈 합산 → delta/current 지표 계산 → `exceptions/s=...` 등의 문자열 반환. 이전 값·시각을 갱신한다. |
| [`Dispose()`](../../polrob.Server/Network/RuntimeMetricSampler.cs#L81) | listener 자원 해제. |
| [`RecordMeasurement<T>(instrument,measurement,tags,state)`](../../polrob.Server/Network/RuntimeMetricSampler.cs#L86) | CreateSeriesKey로 키 생성, 값을 double로 변환, 시리즈를 추가하거나 최근 Value를 덮어쓴다. state는 계산에 쓰지 않는다. 값을 자체 누적하는 콜백은 아니다. |
| [`GetDeltaPerSecond(values,name,elapsedSeconds)`](../../polrob.Server/Network/RuntimeMetricSampler.cs#L105) | 현재−직전 차이를 0 이상으로 제한하고 경과초로 나눈다. 직전 값이 없으면 0으로 시작한다. |
| [`GetCurrent(values,name)`](../../polrob.Server/Network/RuntimeMetricSampler.cs#L117) | 값이 있으면 반환, 없으면 0. |
| [`BytesToMegabytes(bytes)`](../../polrob.Server/Network/RuntimeMetricSampler.cs#L122) | 1024²로 나눈 값 반환. |
| [`CreateSeriesKey(instrument,tags)`](../../polrob.Server/Network/RuntimeMetricSampler.cs#L127) | 이름 뒤에 순서대로 `|태그키=값`을 붙인다. 태그가 없으면 이름만 반환한다. |

`RuntimeMetricSeries`는 이름과 변경 가능한 Value를 묶는 private 클래스다. 예외·GC 횟수·할당·pause·lock 경합·CPU에는 delta를, working set·thread pool queue/thread에는 현재값을 사용한다. 첫 샘플이나 제공되지 않은 instrument의 0을 실제 정상 여부에 대한 보장으로 읽지 않아야 한다.

## 17. Network/TcpPeer.cs

소스: [TcpPeer.cs](../../polrob.Server/Network/TcpPeer.cs#L9).

**역할·상태:** 한 TCP 연결의 송신 버퍼다. `_outgoing`은 제한된 byte[] Channel, `_queuedBytes`는 전송 완료 전까지의 바이트 수, `_client`·`_sendTimeout`은 소켓과 프레임 기한이다. `_onFailure`·`_onSent` callback은 GameNetworkServer 카운터를 갱신한다.

- [`TcpPeer(client,capacity,maxQueuedBytes,sendTimeout,onFailure,onSent)`](../../polrob.Server/Network/TcpPeer.cs#L19): 입력 제한과 callback을 보관하고 단일 소비자 bounded Channel을 만든다. 여기서는 실제 송신 루프를 시작하지 않는다.
- [`TrySend(type,payload)`](../../polrob.Server/Network/TcpPeer.cs#L31): UTF-8 payload 65536바이트 초과면 Fail. 정상은 길이/타입/문자열 프레임을 byte[]로 만들고 바이트 수를 증가시킨다. 바이트 제한 안이고 Channel.TryWrite가 성공하면 true, 실패면 증가량을 되돌리고 Fail한다. 두 한도는 메시지 개수와 총 byte 수를 별도로 제한한다.
- [`RunAsync(stoppingToken)`](../../polrob.Server/Network/TcpPeer.cs#L49): Channel을 순서대로 읽고 각 프레임에 linked timeout을 만들어 WriteAsync한다. 완료하면 byte 수 감소·onSent. 서버 취소는 정상 종료, IO/소켓/폐기/개별 timeout은 Fail로 처리하고 finally에서 writer를 완료한다.
- [`Complete()`](../../polrob.Server/Network/TcpPeer.cs#L69): 더 이상 추가 입력을 받지 않도록 Channel writer를 완료한다. 그 자체로 TCP client를 닫거나 모든 전송 완료를 await하지 않는다.
- [`Fail()`](../../polrob.Server/Network/TcpPeer.cs#L70): 실패 callback, Channel 완료, client.Close를 수행하고 false 반환. 느린 수신자의 큐가 차면 해당 연결을 종료하는 정책을 구현한다.

실제 바이트는 RunAsync가 쓰므로 방 루프의 TrySend 호출은 원격 수신 완료까지 기다리지 않는다. 다만 프레임 바이트 배열 생성과 큐 접수 준비는 호출 스레드에서 수행한다.

## 18. Network/UdpRateLimitState.cs

소스: [UdpRateLimitState.cs](../../polrob.Server/Network/UdpRateLimitState.cs#L3).

**역할·상태:** `_tokens`, `_lastRefillUtc`, `_lock`으로 token bucket을 구현한다. UDP뿐 아니라 TCP 프레임·Hub 호출량 제한에도 같은 타입을 쓴다.

- [`UdpRateLimitState(initialTokens)`](../../polrob.Server/Network/UdpRateLimitState.cs#L9): 초기 토큰을 0 이상으로 보관한다. 마지막 충전 시각은 필드 초기화의 현재 UTC다.
- [`TryConsume(nowUtc,tokensPerSecond,burstSize)`](../../polrob.Server/Network/UdpRateLimitState.cs#L14): lock 안에서 음수가 아닌 경과 시간을 구하고 `min(burstSize,기존토큰+경과초×보충량)`으로 채운다. 1 미만이면 false, 충분하면 1을 빼고 true다. 인자로 받은 제한값의 유효성을 이 함수가 별도로 검사하지 않으므로 호출자가 적절한 양수를 정한다. 시간 역행은 그 호출의 충전량을 0으로 만든다.

## 19. Operations/GameHubFilter.cs

소스: [GameHubFilter.cs](../../polrob.Server/Operations/GameHubFilter.cs#L7).

**역할·상태:** SignalR의 IHubFilter로 모든 Hub 연결·메서드 호출 앞뒤에서 자원을 제한한다. primary constructor로 Admission·설정·Metrics를 받고 `_connections`에 연결별 token bucket, `_count`에 현재 허용 연결 수, `_maxConnections`에 기본 2048 이상의 양수 한도를 보관한다. 실제 인증은 GameRoomHub가 한다.

| 선언 | 입력 → 처리 → 반환/부작용 |
|---|---|
| [`OnConnectedAsync(context,next)`](../../polrob.Server/Operations/GameHubFilter.cs#L14) | 연결 수 증가 → 한도 초과/신규 수용 불가면 감소·거절 계측·Context.Abort·HubException. 통과하면 burst 20의 limiter를 저장하고 next를 기다린다. next가 실패하면 Remove 후 재throw한다. |
| [`InvokeMethodAsync(context,next)`](../../polrob.Server/Operations/GameHubFilter.cs#L29) | 연결 limiter가 없거나 초당 10/burst 20 토큰이 부족하면 계측 후 HubException. 그 밖에는 next 결과 `object?`를 그대로 반환한다. HTTP 429를 만드는 함수는 아니다. |
| [`OnDisconnectedAsync(context,exception,next)`](../../polrob.Server/Operations/GameHubFilter.cs#L41) | 연결 자원을 Remove한 뒤 Hub의 다음 disconnect 처리를 await한다. Hub의 10초 유예를 기다리기 전 이 필터 카운터는 줄어든다. |
| [`Remove(id)`](../../polrob.Server/Operations/GameHubFilter.cs#L48) | 사전에서 실제 항목을 제거했을 때만 연결 수 감소. 실패/종료 cleanup이 겹쳐도 같은 항목의 카운터를 두 번 줄이지 않도록 한다. |

## 20. Operations/OperationalMetrics.cs

소스: [OperationalMetrics.cs](../../polrob.Server/Operations/OperationalMetrics.cs#L8).

**역할·상태:** `_values: ConcurrentDictionary<string,double>`에 서버 지표를 보관한다. HTTP, Hub, 기록, 게임 네트워크가 공유하는 singleton이다. 태그별 외부 시계열 DB나 히스토그램 구현은 없다.

| 선언 | 입력 → 처리 → 출력 |
|---|---|
| [`OperationalMetrics()`](../../polrob.Server/Operations/OperationalMetrics.cs#L12) | 주요 기록/방/tick/거절/실패 이름을 0으로 초기화한다. 나머지 gauge는 이후 Set에서 추가될 수 있다. |
| [`Add(name,value=1)`](../../polrob.Server/Operations/OperationalMetrics.cs#L25) | 새 이름이면 value, 있으면 기존+value로 AddOrUpdate. 호출 성공 자체는 데이터 반환이 없다. |
| [`Set(name,value)`](../../polrob.Server/Operations/OperationalMetrics.cs#L26) | 현재값을 직접 덮어쓴다. 순간 접속 수·outbox 용량·CPU 누적값 갱신 등에 쓴다. |
| [`Export()`](../../polrob.Server/Operations/OperationalMetrics.cs#L28) | 이름순으로 순회해 `# TYPE polrob_<이름> counter/gauge`와 숫자 줄을 만든다. `_total` 접미사는 counter, 나머지는 gauge이며 숫자는 invariant round-trip 형식이다. 완성 문자열 반환. |

이름의 접미사로 출력 타입을 고르며 실제 Add/Set에 “counter는 감소하면 안 된다”는 검증을 강제하지 않는다. 고정된 이름을 사용하고 사용자/방/IP/토큰을 이름에 넣지 말라는 주석은 지표 종류의 무한 증가를 피하기 위한 규칙이다.

## 21. Operations/OperationsEndpoints.cs

소스: [OperationsEndpoints.cs](../../polrob.Server/Operations/OperationsEndpoints.cs#L8).

**역할:** 서비스 등록/endpoint 매핑 확장 메서드다. 상태를 자체 필드로 보관하기보다 DI로 받은 ServerOperations·Admission·Writer·Metrics를 사용한다. Program이 두 확장 메서드를 호출한다.

### AddRequestLimits

[`AddRequestLimits(services,configuration)`](../../polrob.Server/Operations/OperationsEndpoints.cs#L10)는 두 partitioned limiter를 chain하여 ASP.NET rate limiter를 등록한다.

첫 limiter는 `/hubs`를 제외한 요청 전체를 같은 `http` partition으로 묶는 동시성 제한이다. 기본 128이며 queue 0이다. 둘째는 `auth여부:원격IP`를 키로 1분 고정 윈도를 만들며 기본 auth 30회/기타 600회, queue 0이다. Hub 관련 HTTP 요청도 둘째 제한은 적용받을 수 있다. 값은 최소 1로 제한한다.

거절 callback은 `http_rate_limited_total`, `Retry-After: 60`, JSON 오류를 기록한다. 상태 코드는 options의 429다. 응답을 나중에 처리하려고 무한 대기 큐에 넣지 않는다. 반환값은 없고 DI 등록이 결과다.

### MapOperations

[`MapOperations(app)`](../../polrob.Server/Operations/OperationsEndpoints.cs#L45)는 다음 handler와 필터를 매핑한다.

| handler | 입력/호출 대상 → 응답과 부작용 |
|---|---|
| `GET /health/live` | 고정 `{status:"alive"}`의 200. 다른 의존성 probe는 하지 않는다. |
| `GET /health/ready` | Admission.IsReady가 true면 200 ready, 아니면 503 not_ready. |
| `/ops` group filter | 설정 Operations:ApiKey와 `X-Operations-Key`를 IsAuthorized로 비교. 실패 403, 통과하면 next. |
| `GET /ops/metrics` | Writer.PublishMetrics, readiness/drain/uptime, 프로세스 working set/CPU, managed heap/threadpool 값을 Set한 뒤 Export 문자열 반환. Content-Type은 Prometheus text 0.0.4다. |
| `GET /ops/status` | BootId·시작 시각·이전 clean·drain·readiness·Outbox Snapshot·정책 문자열 JSON. |
| `POST /ops/drain` | BeginDrain을 호출하고 draining 응답. 프로세스를 여기서 직접 종료하거나 기존 경기를 즉시 닫지 않는다. |

health와 ops는 DisableRateLimiting이며 ops group은 운영 키 검사를 추가한다. API 사용자 로그인과 다른 인증 체계다.

### IsAuthorized

[`IsAuthorized(expected,provided)`](../../polrob.Server/Operations/OperationsEndpoints.cs#L87)는 비어 있지 않은 두 키를 UTF-8로 바꾸고 길이 일치 및 FixedTimeEquals 결과를 bool로 반환한다. 여러 토큰을 파싱하거나 권한 등급을 판단하는 함수는 아니다.

## 22. Operations/RestartPolicyService.cs

소스: [RestartPolicyService.cs](../../polrob.Server/Operations/RestartPolicyService.cs#L6).

**역할·상태:** IHostedService로 시작/종료 marker를 파일에 남긴다. primary constructor의 Outbox·Operations·Metrics·logger를 사용한다. `StatePath`는 Outbox 디렉터리의 `.runtime-state`다. 진행 중 GameSession을 복원하거나 프로세스를 재실행하는 기능은 없다.

- [`StartAsync(cancellationToken)`](../../polrob.Server/Operations/RestartPolicyService.cs#L11): marker가 있으면 RuntimeState JSON을 읽어 PreviousShutdownClean에 반영한다. CleanShutdown이 true가 아니면 비정상 재시작 계측·경고를 남긴다. JsonException만 잡아 unknown 상태로 로그 처리한다. 그 뒤 현재 실행은 아직 끝나지 않았으므로 WriteState(false), 정책 로그, CompletedTask를 반환한다. 실제 파일 IO는 동기식이며 token을 각 IO에 사용하지 않는다.
- [`StopAsync(cancellationToken)`](../../polrob.Server/Operations/RestartPolicyService.cs#L35): BeginDrain 후 `!HasUnpersistedResults && !NetworkRunning`을 clean 값으로 WriteState한다. Outbox 대기 파일이 남아 있는지 자체를 clean 실패 조건으로 삼지 않는다.
- [`WriteState(clean)`](../../polrob.Server/Operations/RestartPolicyService.cs#L42): BootId·clean·현재 UTC의 RuntimeState를 `.next`에 serialize하고 Flush(true) 후 원래 경로로 overwrite move한다. IO 권한/디스크 오류를 이 메서드가 별도 무시하지 않는다.
- `RuntimeState(BootId,CleanShutdown,UpdatedAtUtc)`: 파일에 남는 private record. 로그인·로비·위치 데이터는 들어 있지 않다.

## 23. Operations/ServerAdmission.cs

소스: [ServerAdmission.cs](../../polrob.Server/Operations/ServerAdmission.cs#L3).

**역할:** 현재 서버가 신규 경기를 받아도 되는지를 하나의 계산 속성으로 모은다. primary constructor로 Operations·Outbox·Configuration을 받으며 자체 background loop나 변경 메서드는 없다.

| 속성 | 계산·사용처 |
|---|---|
| `MaxRooms` | GameNetwork:MaxRooms 기본 500을 1~100000 범위로 제한한 생성 시 값. GameRoomService가 새 방 수를 제한할 때 사용. |
| `CanAcceptNewGames` | `!IsDraining && !HasUnpersistedResults && outbox.CanAcceptGames`. HTTP game POST, 로비 생성/참가/시작, Hub 연결, TCP 참가/경기 시작이 읽는다. |
| `IsReady` | `HasStarted && CanAcceptNewGames`. readiness endpoint가 사용한다. NetworkRunning·Cosmos·LiveKit 상태를 추가 probe하지 않는다. |

DB 장애 자체가 즉시 false가 되는 식이 아니다. 파일 보존 여유·쓰기가 가능한지를 통해 신규 수용 상태에 연결된다.

## 24. Operations/ServerOperations.cs

소스: [ServerOperations.cs](../../polrob.Server/Operations/ServerOperations.cs#L3).

**역할·상태:** 게임 규칙과 별개인 프로세스 수명·미보존 결과 상태다. 생성 시 BootId GUID와 StartedAtUtc를 만든다. `_draining`, `_started`, `_networkRunning`은 int 플래그이고 읽기는 Volatile.Read다. `_unpersisted`는 파일 접수도 못 한 결과 ID의 동시성 집합이다. PreviousShutdownClean은 재시작 서비스가 설정하는 nullable bool이다.

| 멤버 | 입력 → 변화/반환 |
|---|---|
| [`MarkNetworkRunning(running)`](../../polrob.Server/Operations/ServerOperations.cs#L15) | bool을 1/0으로 Exchange. GameNetworkServer 시작/정상 정리 완료가 호출한다. |
| `HasUnpersistedResults` | `_unpersisted`가 비어 있지 않으면 true. 파일만 남아 있고 접수에 성공한 결과는 여기 포함되지 않는다. |
| [`SetUnpersisted(gameId,pending)`](../../polrob.Server/Operations/ServerOperations.cs#L17) | pending이면 ID 추가, 아니면 제거. 여러 방 중 하나라도 결과를 못 맡겼는지 모을 수 있다. |
| [`MarkStarted()`](../../polrob.Server/Operations/ServerOperations.cs#L22) | `_started=1`. ApplicationStarted callback용. |
| [`BeginDrain()`](../../polrob.Server/Operations/ServerOperations.cs#L23) | `_draining=1`. endpoint·프로세스 종료·재시작 marker 정리가 호출한다. 되돌리는 메서드는 없다. |

## 25. Services/BotIdentityService.cs

소스: [BotIdentityService.cs](../../polrob.Server/Services/BotIdentityService.cs#L4).

**역할·상태:** DB 계정 없이 부하 봇을 사용자처럼 조회하게 하는 메모리 저장소다. `_identities`는 ID→BotIdentity, IdentityLifetime은 12시간, logger는 역할별 생성 현황을 출력한다. 생성자는 logger만 받는다.

| 선언 | 입력 → 처리 → 결과/부작용 |
|---|---|
| [`Create(requestedName,role)`](../../polrob.Server/Services/BotIdentityService.cs#L15) | 만료 전체 정리 → GUID로 `bot-<GUID>` ID → 이름은 요청 trim 또는 `Bot-앞8글자` → User와 role/만료를 저장 → 현황 로그 → User 반환. Role의 유효 enum 여부를 여기서 따로 검사하지 않는다. |
| [`Get(userId)`](../../polrob.Server/Services/BotIdentityService.cs#L40) | 없으면 null. 만료 전이면 User, 만료됐으면 TryRemove 후 null. GameRoomService의 사용자 조회가 먼저 호출한다. |
| [`RemoveExpiredIdentities()`](../../polrob.Server/Services/BotIdentityService.cs#L56) | 사전 전체를 순회하여 만료 시각≤현재인 ID를 제거한다. 별도 타이머가 아니라 Create 때 호출한다. |
| [`LogCurrentCounts()`](../../polrob.Server/Services/BotIdentityService.cs#L68) | 현재 값 사본을 모아 총 수·Police·Robber 수를 로그로 출력. 역할에 따라 방 명단을 바꾸지 않는다. |

`BotIdentity(User,Role,ExpiresAt)`는 private record다. Role은 생성 통계용이고 실제 게임 팀은 방 서비스에서 별도 Player.Role로 정한다. 일반 로그인 Sessions의 만료와 봇 사전 만료는 별도로 존재한다.

## 26. Services/CosmosDbOptions.cs

소스: [CosmosDbOptions.cs](../../polrob.Server/Services/CosmosDbOptions.cs#L1).

**역할:** Program이 `CosmosDb` 섹션을 바인딩하고 두 DB 서비스 생성자에 전달하는 값 객체다. 실행 메서드·인증 토큰·CosmosClient 자체는 없다.

| 필드/속성 | 기본값과 소비자 |
|---|---|
| `SectionName` | 상수 `CosmosDb`. Program의 설정 섹션 선택. |
| `DatabaseId` | `PolRobDB`. UserDbService·GameRecordDbService가 초기화할 DB 이름. |
| `UsersContainerId` | `Users`. UserDbService의 사용자 컨테이너. |
| `GameRecordsContainerId` | `GameRecords`. GameRecordDbService의 원본+참가자 기록 컨테이너. |

DB 서비스는 비어 있는 이름에 기본값을 다시 적용한다. 연결 문자열은 이 클래스 속성이 아니라 Program이 별도로 읽어 CosmosClient에 전달한다.

## 27. Services/GameRecordDbService.cs

소스: [GameRecordDbService.cs](../../polrob.Server/Services/GameRecordDbService.cs#L6).

**역할·상태:** `IGameRecordStore`의 실제 Cosmos 구현이다. `_cosmosClient`, logger, DB/컨테이너 이름을 보관하고 초기화 후 `_gameRecordsContainer`를 사용한다. partition key 경로는 `/id`다. 별도 파일 queue 상태나 통계 cache는 보관하지 않는다.

### 생성자·InitializeAsync

[`GameRecordDbService(cosmosClient,options,logger)`](../../polrob.Server/Services/GameRecordDbService.cs#L16)는 의존성과 `GetConfiguredName`으로 보정한 이름을 저장한다. 네트워크 DB 생성 요청은 아직 수행하지 않는다.

[`InitializeAsync(cancellationToken=default)`](../../polrob.Server/Services/GameRecordDbService.cs#L31)는 DB가 없으면 생성하고 이어 `/id` partition의 기록 컨테이너가 없으면 생성한다. 반환 응답의 Container를 필드에 저장한다. Program이 시작 전 호출하며 이 초기화 전 Save/Get을 호출하는 사용법을 내부에서 보호해 주지는 않는다. DB 오류·취소는 호출자에게 전파된다.

### SaveGameRecordAsync

소스: [`SaveGameRecordAsync(gameRecord,cancellationToken=default)`](../../polrob.Server/Services/GameRecordDbService.cs#L42).

1. null 결과, 공백 경기/방 ID, 정의되지 않은 승자 enum을 거절한다.
2. 양 팀 ID 목록을 `NormalizePlayerIds`로 바꾸고 같은 ID가 양쪽에 있으면 ArgumentException을 던진다. 빈 명단 자체를 금지하는 검사는 없다.
3. 음수 DurationSeconds를 거절하고 시각을 UTC로 맞춘 뒤 종료<시작을 거절한다. DurationSeconds를 시각 차이로 다시 산출하지 않는다.
4. 승자를 문자열로, SchemaVersion=1·PlayerRecordsIndexed=false인 원본 문서를 만들고 ID를 partition key로 CreateItemAsync한다.
5. 409 Conflict만 잡아 같은 ID 원본을 ReadItemAsync로 읽는다. 요청과 원본 내용을 다시 동일 비교하거나 원본을 덮어쓰지 않는다.
6. 생성/조회한 원본으로 `EnsurePlayerRecordsIndexedAsync`를 기다린다.

반환형 Task의 성공은 원본 생성만이 아니라 참가자별 문서와 완료 플래그까지 이 함수 경로가 완료됐다는 뜻이다. Writer가 호출하며 실패면 로컬 파일을 유지하거나 예외 종류에 따라 격리한다. 일반 DB/취소 예외를 이 서비스가 성공으로 바꾸지 않는다. 409도 기존 문서를 읽거나 색인하는 후속 요청이 실패하면 전체 Task는 실패한다.

### EnsurePlayerRecordsIndexedAsync

소스: [`EnsurePlayerRecordsIndexedAsync(document,cancellationToken)`](../../polrob.Server/Services/GameRecordDbService.cs#L154).

입력 원본의 PlayerRecordsIndexed가 true면 즉시 반환한다. false이면 ID·방·승자 형식을 검사하고 양 팀 명단을 정규화한다. 이 복구 경로에서는 null 명단을 빈 배열로 취급한다. 승자 형식 오류나 양팀 중복은 InvalidDataException이다.

경찰 명단은 Police 역할로, 도둑 명단은 Robber 역할로 참가자 문서를 생성한다. 각 `SavePlayerGameRecordAsync` Task를 `Task.WhenAll`로 기다려 전부 성공한 뒤 원본의 `/playerRecordsIndexed`를 true로 Patch한다. 참가자 문서와 원본 patch를 하나의 트랜잭션에 묶지는 않는다. 일부만 저장된 상태와 모든 문서는 있으나 patch가 실패한 상태가 가능하므로 같은 문서 ID의 재시도를 전제로 한다.

원본 객체의 bool 속성을 직접 true로 고치지는 않는다. DB patch가 이후 재조회에서 보이는 값이다. Writer의 Save와 Reconciler의 Repair가 이 함수를 공유한다.

### RepairIncompletePlayerIndexesAsync

소스: [`RepairIncompletePlayerIndexesAsync(cancellationToken=default)`](../../polrob.Server/Services/GameRecordDbService.cs#L118).

`playerId`가 정의되지 않은 원본 중 `playerRecordsIndexed`가 없거나 false인 문서를 질의한다. iterator를 페이지 단위로 읽고 각 문서의 Ensure를 **순서대로** 기다린다. 한 문서 실패는 ID 포함 오류를 기록하고 다음 원본으로 진행한다. 서버 취소 예외는 재throw한다. 페이지 읽기 자체의 실패는 문서별 catch 바깥이므로 Reconciler까지 전파된다.

복구 입력은 로컬 Outbox 파일이 아니라 이미 DB에 있는 원본이다. 원본 자체가 DB와 파일 양쪽에서 사라진 경기를 찾아낼 수는 없다. 반환 데이터 없이 DB의 참가자 문서·완료 flag가 결과다.

### GetPlayerStatsAsync

소스: [`GetPlayerStatsAsync(userId,cancellationToken=default)`](../../polrob.Server/Services/GameRecordDbService.cs#L196).

공백 userId를 거절하고 `playerId == @userId`인 문서에서 playerRole/winnerRole만 projection한다. 각 페이지를 읽어 두 역할을 case-insensitive로 파싱하고 정의된 enum인지 확인한다. 잘못된 문서는 경고하고 건너뛴다. 정상 문서는 PlayerGameOutcome으로 만들어 accumulator.Add에 넣는다. 마지막에 accumulator.Build의 전체/역할별 통계를 반환한다.

GameRecordsController가 호출한다. 현재 참가자 색인 문서를 조회할 뿐 원본의 PlayerRecordsIndexed=true 여부를 함께 요구하지 않으므로 일부 참가자의 통계만 먼저 나타날 수 있다. DB 전체 결과를 List로 모은 뒤 계산하는 구조가 아니라 페이지별 누적이다.

### private 저장·변환 helper

| 선언 | 입력 → 처리 → 결과/예외 |
|---|---|
| [`SavePlayerGameRecordAsync(document,cancellationToken)`](../../polrob.Server/Services/GameRecordDbService.cs#L236) | document.Id로 CreateItemAsync. 409면 이미 있는 참가자 기록으로 debug 로그 후 성공 취급. 다른 오류 전파. 기존 문서 내용 비교·수정은 하지 않는다. |
| [`CreatePlayerDocument(gameRecord,playerId,playerRole)`](../../polrob.Server/Services/GameRecordDbService.cs#L256) | ID=`player:<경기ID>:<사용자ID>`, 사용자/방/역할/승자/진행시간/시각/schema 복사한 새 DTO 반환. DB 쓰기는 하지 않는다. |
| [`TryParsePlayerRole(value,out role)`](../../polrob.Server/Services/GameRecordDbService.cs#L272) | Enum.TryParse(ignoreCase:true)와 Enum.IsDefined의 논리곱. 숫자처럼 parse되더라도 미정의 값이면 false. |
| [`NormalizePlayerIds(playerIds)`](../../polrob.Server/Services/GameRecordDbService.cs#L277) | null이면 ArgumentNullException. null/공백 항목 제거 → ordinal 중복 제거 → ordinal 정렬 → 새 배열. 남은 문자열의 앞뒤 공백을 trim하지는 않는다. |
| [`NormalizeUtc(value)`](../../polrob.Server/Services/GameRecordDbService.cs#L288) | UTC는 그대로, Local은 ToUniversalTime, Unspecified는 같은 clock 값을 UTC로 지정하여 반환. |
| [`ValidateIdentifier(value,parameterName)`](../../polrob.Server/Services/GameRecordDbService.cs#L298) | null/공백이면 ArgumentException. 허용 문자 집합이나 ID 최대 길이를 이 helper에서 검사하지 않는다. |
| [`GetConfiguredName(configuredName,defaultName)`](../../polrob.Server/Services/GameRecordDbService.cs#L306) | null/공백은 기본 이름, 아니면 원 문자열 반환. |

### 내부 DB 모델

| 타입 | 필드와 용도 |
|---|---|
| `GameRecordDocument` | Id, RoomId, 문자열 WinnerRole, 두 팀 ID 배열, DurationSeconds, StartedAtUtc/EndedAtUtc, SchemaVersion, PlayerRecordsIndexed. 경기 원본이며 playerId가 없다. |
| `PlayerGameRecordDocument` | Id, PlayerId, RoomId, 문자열 PlayerRole/WinnerRole, 시간·schema. 사용자별 조회용 별도 문서다. |
| `PlayerGameRecordStatsProjection` | nullable 문자열 PlayerRole/WinnerRole만 가진 질의 결과 모양. 잘못된 역할 검출을 허용한다. |

## 28. Services/GameRecordOutbox.cs

소스: [GameRecordOutbox.cs](../../polrob.Server/Services/GameRecordOutbox.cs#L1).

**역할·상태:** “아직 DB 저장이 끝나지 않은 결과”를 JSON 파일로 보관한다. `_ownership` FileStream은 같은 디렉터리의 동시 소유를 막고 `_gate`는 파일 상태와 `_pending`, `_quarantined`, `_bytes`, `_writeFailed` 갱신을 묶는다. pending과 quarantine 모두 공간을 차지한다.

### 같은 파일의 Options·Snapshot

| 속성 | 기본값·쓰임 |
|---|---|
| `Directory` | `data/game-record-outbox`. Program의 PostConfigure가 content root 기준 절대경로로 보정한다. |
| `MaxRecords`, `MaxBytes` | 10000개, 268435456바이트. 대기+격리 파일의 절대 상한. |
| `MaxRecordBytes` | 65536바이트. 개별 JSON 결과 상한. |
| `AdmissionThreshold` | 0.8. 절대 상한 전에 새 경기 수용을 제한하는 비율. |
| `RetrySeconds`, `WriteTimeoutSeconds` | 5초/15초. Writer의 반복 간격과 개별 DB 작업 기한. |
| `OutboxSnapshot` | Pending, Quarantined, Bytes, WriteFailed, OldestAgeSeconds를 반환하는 record. |

### 생성자

[`GameRecordOutbox(options,logger)`](../../polrob.Server/Services/GameRecordOutbox.cs#L33)는 한도들의 유효 범위를 검사한다. records<1, record bytes<1024, total bytes<record bytes, threshold≤0 또는 >1, 재시도/timeout<1이면 InvalidOperationException이다. 절대 경로의 디렉터리를 만들고 `.owner.lock`을 FileShare.None으로 연다.

남은 `*.tmp`는 같은 `.json`이 있으면 삭제, 없으면 `.json`으로 이동한다. 이어 모든 `.json`·`.failed`의 수와 크기를 세어 메모리 계측 상태를 복원한다. 스캔 오류면 ownership handle을 닫고 예외를 전파한다. 여기서 각 JSON 내용까지 읽어 검증하지는 않으므로 완성되지 않은 tmp도 다음 Read에서 격리될 수 있다.

### CanAcceptGames·Snapshot

`CanAcceptGames` getter는 gate 안에서 쓰기 실패가 아니고 `(pending+quarantine) < MaxRecords×threshold`, `bytes < MaxBytes×threshold`인지 반환한다. 동일 파일에 다시 넣는 멱등 확인과 신규 파일을 절대 한도까지 받아들이는 TryAppend 정책은 이 getter와 별개다.

[`Snapshot()`](../../polrob.Server/Services/GameRecordOutbox.cs#L77)은 gate 안에서 pending JSON들의 마지막 수정 시각 최소값을 구하고 현재 시각과의 차이를 0 이상으로 제한한다. 파일이 없으면 나이 0에 해당하는 현재시각을 사용한다. 카운터와 함께 새 OutboxSnapshot을 반환하며 metrics·status가 읽는다. 파일 열거 오류를 자체로 잡지 않는다.

### TryAppend

소스: [`TryAppend(record)`](../../polrob.Server/Services/GameRecordOutbox.cs#L88).

**입력:** 완료 경기 record. null과 공백 ID는 예외다. 먼저 JSON UTF-8 byte[]로 직렬화하여 단일 크기 한도를 넘으면 false를 반환한다. ID의 SHA-256 hex를 파일 이름으로 사용하므로 ID를 경로 문자열로 직접 붙이지 않는다.

**gate 안 분기:** 같은 `.json`이 이미 있으면 현재 bytes와 파일 bytes가 완전히 같을 때만 true다. 같은 ID의 `.failed`가 있으면 false다. pending+quarantine 개수나 총 byte 절대 한도가 초과될 새 결과도 false다. 공간이 있으면 `.tmp`에 동기 Write→Flush(true)→Dispose 후 `.json`으로 Move한다. pending/bytes를 증가시키고 writeFailed=false, true를 반환한다.

**실패 의미:** IO/권한 예외는 writeFailed=true와 critical 로그 후 false다. null/ID 검증·직렬화 예외는 이 IO catch 밖에서 전파될 수 있다. false를 “저장이 어딘가에서 나중에 알아서 될 것”으로 취급하면 안 된다. 게임 서버가 PendingGameRecord로 보관하고 재호출해야 한다.

**성공 의미:** 이 호출에서 flush한 파일 또는 동일 내용의 기존 JSON이 있다. Cosmos DB 반영 여부는 확인하지 않는다. DB 저장이 성공하여 파일이 삭제된 후 같은 ID를 다시 TryAppend하면 새 파일을 만들 수 있으며 DB 계층의 ID 기반 처리도 함께 필요하다. Writer.TryEnqueue의 실제 저장 작업은 이 함수다.

### 소비·삭제·격리 메서드

| 선언 | 입력 → 처리 → 결과/실패 경계 |
|---|---|
| [`PendingFiles()`](../../polrob.Server/Services/GameRecordOutbox.cs#L129) | `*.json` 경로의 지연 열거 반환. 정렬·FIFO·파일 잠금 snapshot을 보장하지 않는다. Writer가 Take(64)로 소비한다. |
| [`Read(path)`](../../polrob.Server/Services/GameRecordOutbox.cs#L131) | 파일 크기 검사 → bytes JSON 역직렬화 → 결과 null/ID·RoomId 공백/팀 목록 null/승자 enum 검사 → record 반환. 크기/필드 실패 InvalidDataException, JSON 오류 JsonException, 일반 파일 IO 오류는 그대로 전파. 날짜 순서·양팀 중복 검사는 DB Save 쪽이다. |
| [`Acknowledge(path)`](../../polrob.Server/Services/GameRecordOutbox.cs#L143) | gate 안에서 파일 길이 확인→Delete→pending 감소·bytes 감소. DB Save 성공 뒤 Writer가 호출한다. 파일 삭제 실패면 카운터 감소 이전에 예외가 나온다. |
| [`Quarantine(path)`](../../polrob.Server/Services/GameRecordOutbox.cs#L154) | gate 안에서 확장자를 `.failed`로 Move→pending 감소·quarantine 증가. 파일 내용과 총 bytes는 유지. 이미 목적 파일이 있거나 Move가 실패하면 예외가 전파된다. |
| [`ProbeWritable()`](../../polrob.Server/Services/GameRecordOutbox.cs#L166) | gate 안에서 `.write-probe`에 1바이트 Write·Flush(true)·Delete. 성공하면 writeFailed=false, IO/권한 오류면 true와 오류 로그. 결과 데이터를 시험용으로 만들 필요 없이 쓰기 회복을 검사한다. |
| [`Dispose()`](../../polrob.Server/Services/GameRecordOutbox.cs#L186) | ownership FileStream을 닫아 디렉터리 소유 lock을 해제한다. pending/quarantine 파일을 지우지 않는다. |

격리 파일은 Writer가 자동 소비하는 대상에서 제외된다. `.failed`를 보존하는 것과 자동 복구하는 것은 다른 기능이다. 이 파일의 lock은 한 프로세스 내부 동작을 조정하며 디렉터리 소유 handle은 같은 디렉터리를 공유하는 다른 Outbox 인스턴스를 배제하는 별도 장치다.

## 29. Services/GameRecordReconciler.cs

소스: [GameRecordReconciler.cs](../../polrob.Server/Services/GameRecordReconciler.cs#L1).

**역할·상태:** DB에 원본은 있지만 참가자별 색인이 미완료인 경기를 주기적으로 찾는 BackgroundService다. GameRecordDbService·logger와 고정 5분 간격을 보관한다. 로컬 파일 목록이나 자체 retry queue는 없다.

- [`GameRecordReconciler(gameRecordDbService,logger)`](../../polrob.Server/Services/GameRecordReconciler.cs#L8): 의존성 보관만 한다.
- [`ExecuteAsync(stoppingToken)`](../../polrob.Server/Services/GameRecordReconciler.cs#L16): 시작하자마자 `RepairIncompletePlayerIndexesAsync`를 기다리고 이후 5분 대기한다. 즉 고정 시각 5분마다 시작하는 스케줄이 아니라 **한 번 복구가 끝난 뒤** 5분 쉰다. 서버 취소는 종료, 그 밖의 복구 예외는 로그 후 다음 주기를 계속한다. 지연 중 취소도 잡고 반환한다.

Writer와 같은 Ensure 색인 함수를 사용하지만 할 일을 찾는 기준이 DB 질의다. 로컬 파일이 사라졌어도 원본이 남아 있으면 복구 기회가 있고, DB 원본도 없다면 이 서비스가 결과를 재구성하지 않는다.

## 30. Services/GameRecordStatsCalculator.cs

소스: [GameRecordStatsCalculator.cs](../../polrob.Server/Services/GameRecordStatsCalculator.cs#L3).

**역할·상태:** DB 없이 역할별 승패를 계산하는 함수와 누적기를 제공한다. static Calculator는 상태가 없고 Accumulator는 전체 경기/승리, 경찰 경기/승리, 도둑 경기/승리의 6개 int를 갖는다.

| 선언 | 입력 → 처리 → 결과·경계 |
|---|---|
| [`GameRecordStatsCalculator.Calculate(outcomes)`](../../polrob.Server/Services/GameRecordStatsCalculator.cs#L5) | null 열거자는 ArgumentNullException. 새 accumulator를 만들고 모든 outcome에 Add한 뒤 Build 반환. 테스트·한 번에 완성된 열거형 계산에 유용하다. |
| [`GameRecordStatsAccumulator.Add(outcome)`](../../polrob.Server/Services/GameRecordStatsCalculator.cs#L28) | 두 역할이 정의된 enum인지 먼저 검사하여 아니면 ArgumentOutOfRangeException. 승리=`PlayerRole==WinnerRole`로 전체와 해당 역할 카운터 증가. 반환값 없음. 중복 경기 ID를 검사할 데이터는 이 타입에 없다. |
| [`Build()`](../../polrob.Server/Services/GameRecordStatsCalculator.cs#L53) | 현재 6개 누적값으로 Overall/Police/Robber breakdown이 있는 새 PlayerGameStats 반환. 누적기를 비우지 않으므로 이후 Add 뒤 다시 Build할 수 있다. |
| [`CreateBreakdown(totalGames,wins)`](../../polrob.Server/Services/GameRecordStatsCalculator.cs#L63) | TotalGames, Wins, Losses=`total−wins`, WinRate=`wins×100d/total` 생성. 0경기면 승률 0. 소수점 반올림은 하지 않는다. |

GameRecordDbService는 페이지마다 Add하여 전체 결과 목록의 메모리 적재를 피한다. 이 계산기의 승리 기준은 “사용자 개인의 체포/구조 횟수”가 아니라 자신이 속한 팀의 승패다.

## 31. Services/GameRecordWriter.cs

소스: [GameRecordWriter.cs](../../polrob.Server/Services/GameRecordWriter.cs#L7).

**역할·상태:** IGameRecordQueue 구현과 파일→DB 소비 BackgroundService를 함께 맡는다. 생성자는 Outbox, IGameRecordStore, OutboxOptions, Metrics, logger를 보관한다. 추가적인 무제한 RAM queue는 없다. DI에서 두 역할이 같은 singleton을 사용한다.

### TryEnqueue

[`TryEnqueue(record)`](../../polrob.Server/Services/GameRecordWriter.cs#L27)는 Outbox.TryAppend를 **동기적으로** 호출한 뒤 true/false에 따라 record_accepted_total/record_rejected_total을 증가시키고 bool을 그대로 반환한다. 따라서 게임 루프는 DB 네트워크를 기다리지 않지만 로컬 파일 write/flush 시간은 겪는다. TryAppend가 예외를 던지면 이 함수는 잡지 않으며 이 두 bool 기반 카운터까지 도달하지 않는다. 네트워크의 TryEnqueueCompletedGameRecord가 실패 보관을 담당한다.

### ExecuteAsync

[`ExecuteAsync(stoppingToken)`](../../polrob.Server/Services/GameRecordWriter.cs#L34)는 ProbeWritable → ProcessPendingAsync → PublishMetrics를 수행하고 RetrySeconds만큼 기다린다. 일반 순회 예외는 outbox_worker_failures_total과 로그를 남긴 뒤 다음 반복으로 간다. 서버 취소는 바깥 catch에서 정상 종료한다. 매 반복의 작업 시간에 추가로 기본 5초를 쉬는 방식이다.

쓰기 probe가 false 상태를 만들었다고 곧바로 DB 소비를 생략하는 조건은 없다. 오래된 결과를 DB로 보내 공간을 확보할 수 있는 경로와 새 파일 쓰기 가능 여부는 분리되어 있다.

### ProcessPendingAsync

소스: [`ProcessPendingAsync(cancellationToken)`](../../polrob.Server/Services/GameRecordWriter.cs#L59).

**파일 선택:** PendingFiles().Take(64)를 열거하고 파일마다 서버 취소를 확인한다. 정렬된 FIFO 처리 순서가 아니며 한번에 최대 64개를 처리한다.

**읽기 단계:** Outbox.Read가 JsonException 또는 InvalidDataException을 던지면 해당 파일을 Quarantine하고 격리 계측을 올린 뒤 다음 파일로 간다. 일반 IOException·권한 오류는 이 읽기 catch가 잡지 않으므로 전체 순회가 실패하여 ExecuteAsync의 worker 오류 처리로 간다.

**DB 단계:** 읽기에 성공한 record마다 linked cancellation source를 만들고 기본 15초 뒤 취소한다. Store.SaveGameRecordAsync(record,deadline.Token)를 기다린 후 성공하면 Acknowledge로 파일을 삭제하고 저장 계측·로그를 남긴다. **파일 삭제도 이 try 블록 안**에 있으므로 DB 저장 뒤 삭제 실패도 재시도 분기에 들어갈 수 있다.

| DB 단계의 결과 | 처리와 보존 상태 |
|---|---|
| 호출자 취소 | OperationCanceledException을 재throw하여 종료한다. 아직 지우지 않은 파일 유지. |
| IsPermanentRecordError=true | Quarantine, 격리 계측·오류 로그 후 다음 파일. 결과 내용은 failed 파일로 남는다. |
| 그 밖의 예외/개별 deadline | retry 계측·경고, 현재 파일 유지, `break`로 이번 순회 종료. 다음 주기에 다시 시도. |
| Save 및 Acknowledge 성공 | pending 파일 제거. 이후 이 Writer의 할 일에서 제외. |

한 DB 장애에서 나머지 수십 개 결과를 연속 실패시키지 않도록 일반 실패 때 break한다. 기존 파일 삭제에 실패해 같은 결과를 DB에 다시 보내더라도 DbService의 동일 문서 ID 처리가 중복 생성을 제한한다.

### 분류·계측 helper

[`IsPermanentRecordError(ex)`](../../polrob.Server/Services/GameRecordWriter.cs#L101)는 ArgumentException 계열 또는 CosmosException의 HTTP 400/413일 때만 true다. 자격 증명/설정 실패를 포함한 다른 DB 예외는 일반 재시도로 남는다. DB의 InvalidDataException은 읽기 단계의 동명 예외 처리와 달리 이 분류에 포함되지 않는다.

[`PublishMetrics()`](../../polrob.Server/Services/GameRecordWriter.cs#L104)는 Outbox Snapshot으로 pending/quarantine/bytes/oldest age/write failure gauge를 Set한다. Writer 반복과 `/ops/metrics` handler가 호출한다. DB 저장을 수행하거나 큐를 비우지는 않는다.

## 32. Services/GameRoomService.cs

소스: [GameRoomService.cs](../../polrob.Server/Services/GameRoomService.cs#L5).

**역할·상태:** HTTP Controller·SignalR Hub·TCP 게임 서버가 공유하는 **로비 방 관리자**다. `Games`는 메모리 `List<Game>`, `_roomLock`은 그 목록과 방 객체의 입퇴장·역할·시작 상태를 직렬화한다. 서버 재시작 뒤 이 목록은 복원되지 않는다. 사용자 조회는 lock **밖**에서 먼저 끝내므로 Cosmos 지연 중 모든 방 변경을 막지는 않는다. 생성자는 사용자 DB, 봇 신원, 로거와 선택적 LiveKit 관리·신규 경기 수용 상태를 저장한다. 경기 결과의 DB 저장은 이 클래스가 맡지 않는다.

### 방 생성·참가·조회

| 메서드 | 입력 → 처리 → 결과/거절 조건 |
|---|---|
| [`CreateRoom(userId,type,role,isPrivate,mapId)`](../../polrob.Server/Services/GameRoomService.cs#L28) | MapRegistry와 역할 검사 → 봇 또는 DB 사용자 조회 → lock 안에서 신규 경기 수용·만료 방·최대 방 수 확인 → 고유 6자리 코드와 HostUserId를 가진 Game 생성 → 첫 Player 추가 → 상태 `ServerResponse`. 지원하지 않는 맵/역할·없는 사용자·수용 불가·방 상한은 `Success=false`; DB 조회 예외는 이 메서드가 복구하지 않는다. |
| [`JoinCustomGame(userId,roomCode,role)`](../../polrob.Server/Services/GameRoomService.cs#L81) | 역할·사용자 검증 후 코드를 trim/공백 제거/대문자로 정규화해 비공개 방의 RoomCode **또는 원본 roomCode와 같은 Id**를 찾는다. 진행 중이면 거절, 이미 참가했으면 기존 Role 상태 반환, 아니면 전체 6·경찰 2·도둑 4 한도를 확인해 Player를 추가한다. 빈 방 만료 시각을 취소한다. |
| [`JoinRandomGame(userId,roomId,role)`](../../polrob.Server/Services/GameRoomService.cs#L158) | 기존 공개 random 방 목록을 순서대로 돌며 이미 참가한 방 또는 희망 역할 여유가 있는 방에 넣는다. 적합한 방이 없으면 `CreateRandomRoom`으로 새 방을 만든다. `roomId` 매개변수는 본문에서 사용하지 않으며 기존 방 검색에도 `IsOnGame` 제외 조건이 없다. 새 방 생성 전 수용·방 수 제한을 확인한다. |
| [`GetRoomStatus(roomId)`](../../polrob.Server/Services/GameRoomService.cs#L239) | lock에서 만료 빈 방 제거 후 ID 조회. 없으면 `Success=false`, 있으면 `CreateRoomStatusResponse`. 이 함수 자체는 사용자 멤버십을 확인하지 않으므로 Controller가 별도로 검사한다. |
| [`GetAuthenticatedGamePlayer(roomId,userId)`](../../polrob.Server/Services/GameRoomService.cs#L263) | 방 안에서 해당 ID의 Player를 찾아 **속성까지 새 객체로 복사**해 반환한다. 없으면 null. Hub의 방 구독과 TCP Join의 멤버십 검증에 쓰인다. |
| [`TryGetAuthenticatedTeamVoiceAccess(roomId,userId,out player,out voiceSessionId)`](../../polrob.Server/Services/GameRoomService.cs#L274) | 방이 `IsOnGame`, 사용자가 참가자, VoiceSessionId가 비어 있지 않을 때만 true. out Player는 복사본, out 세션 ID는 현재 경기 값이다. 실패는 null/빈 문자열로 초기화한다. 역할은 호출자 입력이 아닌 방의 Player에서 얻는다. |
| [`IsRoomHost(roomId,userId)`](../../polrob.Server/Services/GameRoomService.cs#L302) | 방 ID와 HostUserId의 일치 여부를 lock 안에서 bool로 반환한다. 멤버십 자체를 재조회하지 않는다. |
| [`GetLoadSnapshot()`](../../polrob.Server/Services/GameRoomService.cs#L310) | 만료 방을 제거한 뒤 전체 방/참가자, 공개 random 방/참가자, 6명 이상 또는 진행 중인 random 방, 진행 중 random 방 수를 record로 반환한다. 운영 계측용 snapshot이며 참가자를 변경하지 않는다. |

### 퇴장·역할·재경기·시작

| 메서드 | 입력 → 상태 변경·결과 |
|---|---|
| [`RemovePlayer(roomId,userId)`](../../polrob.Server/Services/GameRoomService.cs#L332) | 방이 이미 없으면 최종 퇴장 상태로 보고 성공 응답. 있으면 사용자를 제거하고, 빈 **미진행** 방이면 방 자체를 삭제한다. 그 외 남은 명단에서 방장 재배정 후 상태 응답. 진행 중 방의 마지막 퇴장만으로는 여기서 방을 지우지 않는다. |
| [`AbortGameStart(roomId,leavingUserId)`](../../polrob.Server/Services/GameRoomService.cs#L372) | 시작 직전 이탈자를 제거하고 IsOnGame=false, VoiceSessionId 비움, 만료 시각 해제. 빈 방 삭제 또는 방장 승계 후 로비 복귀 응답. lock 밖에서 이전 LiveKit 팀 방 삭제를 **await하지 않고** 시작한다. 실제 완료 경기나 전적을 만들지 않는다. |
| [`ChangePlayerRole(roomId,userId,role)`](../../polrob.Server/Services/GameRoomService.cs#L426) | 유효 역할·방·미진행·참가자 여부를 확인하고 같은 역할이면 현재 상태 그대로 반환. 변경하려는 팀의 경찰 2/도둑 4 한도 안에서 Player.Role 변경. 실패는 상태와 이유를 담은 `Success=false`. |
| [`RejoinCustomRoomForReplay(roomId,userId,role)`](../../polrob.Server/Services/GameRoomService.cs#L480) | 사용자 조회 후 신규 수용 검사, 기존 비공개·미진행 방 찾기, 중복 참가면 기존 상태 반환. 인원/역할 한도를 지키며 다시 참가시키고 빈 방 만료를 취소한다. 방 코드를 통해 새 방을 만드는 것이 아니라 같은 RoomId의 남은 커스텀 방에 복귀한다. |
| [`CompleteGame(roomId)`](../../polrob.Server/Services/GameRoomService.cs#L554) | **경기 기록 DB 저장 함수가 아니다.** 비공개 방은 IsOnGame/VoiceSessionId를 지우고 참가자가 남았으면 방·방장을 유지해 재경기 준비; 비었으면 삭제. 공개 방은 상태 응답 후 방 삭제. 이전 LiveKit 팀 방 삭제는 lock 밖에서 await 없이 시작한다. |
| [`AbandonGameAfterDisconnect(roomId)`](../../polrob.Server/Services/GameRoomService.cs#L606) | 진행 중인 방을 찾아 승패·기록 없이 제거하고 이전 음성 방 삭제를 비동기로 시작, 성공 bool 반환. 방이 없거나 이미 미진행이면 false. 전원 단절 유예 뒤 게임 방 루프가 호출한다. |
| [`RemoveExpiredEmptyRooms()`](../../polrob.Server/Services/GameRoomService.cs#L629) | lock을 잡아 `RemoveExpiredEmptyRoomsCore(now)` 호출. 조건에 맞는 비공개·미진행·0명 방만 제거한다. |
| [`IsRoomMatched(roomId)`](../../polrob.Server/Services/GameRoomService.cs#L637) | 방이 진행 중 **또는** 인원≥6이면 true. 팀별 인원·역할을 따로 검증하는 `StartGameIfMatched`와 동일한 조건식은 아니다. |
| [`IsGameInProgress(roomId)`](../../polrob.Server/Services/GameRoomService.cs#L648) | ID와 IsOnGame이 모두 맞는 방의 존재 여부를 반환한다. |
| [`StartGameIfMatched(roomId)`](../../polrob.Server/Services/GameRoomService.cs#L657) | 수용 가능·방 존재·참가자≥1·양 역할 최소1명 확인. 공개 방이 6명 미만이면 **성공 상태 응답이지만 시작 상태 변경 없음**. 비공개 방은 양 역할만 있으면 시작 가능하다. 처음 시작할 때 새 VoiceSessionId를 만들고 IsOnGame=true; 중복 호출이면 기존 음성 세션을 유지한다. |

### private helper와 반환 객체의 의미

| 메서드 | 처리·호출 관계 |
|---|---|
| [`CreateRandomRoom`](../../polrob.Server/Services/GameRoomService.cs#L703), [`LogRandomMatchingCompletedIfNeeded`](../../polrob.Server/Services/GameRoomService.cs#L729) | 공개 random 방 생성과 방 수/완성 수 로깅. 게임 TCP session 생성은 하지 않는다. |
| [`CreateRandomJoinResponse`](../../polrob.Server/Services/GameRoomService.cs#L749), [`CreateRoomStatusResponse`](../../polrob.Server/Services/GameRoomService.cs#L773) | 현재 Game의 인원·역할·맵·방장·Matched 등을 ServerResponse에 복사. `Players = game.Players.ToList()`는 **리스트만 새로 만들고 내부 Player는 공유**한다. |
| [`CreateRoomFailureResponse`](../../polrob.Server/Services/GameRoomService.cs#L797), [`CreateInvalidRoleResponse`](../../polrob.Server/Services/GameRoomService.cs#L804) | 방 맥락을 담되 Success=false로 바꾼 오류 응답 또는 잘못된 enum 역할 응답을 만든다. 예외를 던지지 않는다. |
| [`HasRequiredRoles`](../../polrob.Server/Services/GameRoomService.cs#L817), [`ReassignHostIfNeeded`](../../polrob.Server/Services/GameRoomService.cs#L823) | 양 팀 존재 여부 검사; 기존 방장이 명단에 없으면 첫 남은 Player를 방장으로 승계, 명단이 비면 HostUserId를 빈 값으로 만든다. |
| [`GetUserAsync`](../../polrob.Server/Services/GameRoomService.cs#L840), [`CreatePlayer`](../../polrob.Server/Services/GameRoomService.cs#L846), [`CopyPlayer`](../../polrob.Server/Services/GameRoomService.cs#L857) | 봇 사전 우선·일반 사용자 DB 후순위 조회. 기본 Player 생성은 ID/이름/방/역할만 설정하며, 인증 반환용 CopyPlayer는 좌표·수감 등 전체 속성을 복사한다. |
| [`CreateUniqueRoomCode`](../../polrob.Server/Services/GameRoomService.cs#L875), [`GenerateRoomCode`](../../polrob.Server/Services/GameRoomService.cs#L887), [`NormalizeRoomCode`](../../polrob.Server/Services/GameRoomService.cs#L899) | `ABCDEFGHJKLMNPQRSTUVWXYZ23456789`에서 6자 생성 후 현 방들과 대소문자 무시 중복 검사를 반복한다. 입력 코드는 trim→일반 공백 제거→대문자. 방 코드 충돌을 DB에서 확인하지 않는다. |
| [`RemoveExpiredEmptyRoomsCore`](../../polrob.Server/Services/GameRoomService.cs#L904) | 비공개·미진행·명단 0명·만료 시각 경과를 모두 만족하는 방을 제거한다. `RemovePlayer`의 즉시 삭제와 다른 경로다. |

`GameRoomLoadSnapshot`은 운영 지표의 여섯 정수를 담는 record이며 계산은 `GetLoadSnapshot`이 수행한다. 메서드 결과의 `Matched`는 `IsOnGame || Players.Count >= 6`이라는 응답용 표시다. 실제 게임 시작에는 양 역할·수용 상태 등 추가 조건이 적용된다.

## 33. Services/IGameRecordQueue.cs

소스: [IGameRecordQueue.cs](../../polrob.Server/Services/IGameRecordQueue.cs#L1). **역할:** 게임 방 루프가 완료 기록을 맡기는 좁은 계약. 선언된 메서드는 `bool TryEnqueue(CompletedGameRecord gameRecord)` 하나다. `true`는 production 구현에서 Outbox의 **로컬 파일 flush 완료**를 뜻하며 Cosmos 저장 완료가 아니다. `false`면 호출자가 같은 결과를 보유하고 재시도해야 한다. 인터페이스에는 메모리 큐, 스레드, DB 저장 코드가 없고 운영 구현은 [GameRecordWriter](#31-servicesgamerecordwritercs)다. `GameNetworkServer.RoomLoop`는 PendingGameRecord와 운영 미저장 표시로 실패를 처리한다. 테스트는 이 계약의 가짜 구현으로 인계 실패를 재현한다.

## 34. Services/IGameRecordStore.cs

소스: [IGameRecordStore.cs](../../polrob.Server/Services/IGameRecordStore.cs#L1). **역할:** Writer가 한 기록의 DB 저장 완료를 기다리는 계약. 선언된 메서드는 `Task SaveGameRecordAsync(CompletedGameRecord record, CancellationToken cancellationToken = default)` 하나다. 반환 데이터는 없고 정상 완료/예외/취소로 결과를 알린다. 운영 구현은 [GameRecordDbService](#27-servicesgamerecorddbservicecs). 초기화·복구·전적 조회는 이 인터페이스에 없으므로 `GameRecordReconciler`는 구체 DbService에 의존한다. 테스트의 FakeStore는 성공·영구/일시 실패를 주입한다.

## 35. Services/LiveKitOptions.cs

소스: [LiveKitOptions.cs](../../polrob.Server/Services/LiveKitOptions.cs#L1). **역할:** `LiveKit` 설정 section의 값을 바인딩하는 가변 DTO. 직접 작성된 메서드는 없다. `SectionName="LiveKit"`, `Url`, `ApiKey`, `ApiSecret`은 기본 빈 문자열, `TokenLifetimeMinutes` 기본 2다. 이 속성만으로 값의 유효성이 보장되지는 않는다. 토큰 서비스의 `ValidateConfiguration`이 발급 직전 `wss://` URL과 비어 있지 않은 키/secret을 검사하고, Program도 시작 시 옵션을 검증한다. `ApiSecret`은 토큰 서명과 관리 API 호출에 쓰이는 서버 측 값이다.

## 36. Services/LiveKitRoomAdminService.cs

소스: [LiveKitRoomAdminService.cs](../../polrob.Server/Services/LiveKitRoomAdminService.cs#L7). **역할·상태:** 종료·퇴장 때 LiveKit 팀 방과 참가자를 정리하는 서버 측 관리 API 래퍼. 생성자는 `IOptions<LiveKitOptions>.Value`와 logger를 필드에 저장할 뿐 연결하지 않는다.

| 메서드 | 입력 → 처리 → 결과·예외 경계 |
|---|---|
| [`DeleteTeamRoomsAsync(roomId,voiceSessionId)`](../../polrob.Server/Services/LiveKitRoomAdminService.cs#L18) | 둘 중 공백이면 즉시 완료. 한 RoomServiceClient를 만들고 PlayerRole의 경찰·도둑 각각에 대해 `CreateTeamRoomName`으로 방 이름을 만든 뒤 DeleteRoom 요청. 개별 삭제 오류는 경고만 남기고 다음 역할을 계속 처리한다. `CreateClient` 오류는 foreach의 try **밖**이므로 밖으로 전파된다. 방이 없거나 이미 사라졌어도 게임 결과 처리를 중단하려는 메서드는 아니다. |
| [`RemoveParticipantAsync(roomId,voiceSessionId,role,userId,gameConnectionId)`](../../polrob.Server/Services/LiveKitRoomAdminService.cs#L48) | 사용자 ID 또는 게임 연결 ID가 공백이면 종료. 팀 방 이름과 `roomId/userId/gameConnectionId` 기반 identity를 만든 뒤 RemoveParticipant. 호출 중 예외는 경고 로그 후 정상 완료한다. room/voiceSession/role 오류는 identity 생성 단계에서 try보다 먼저 발생할 수 있다. 연결 세대별 identity 덕분에 이전 TCP 접속 정리가 새 접속의 음성을 제거하지 않는다. |
| [`CreateClient()`](../../polrob.Server/Services/LiveKitRoomAdminService.cs#L83) | 설정의 WebSocket URL을 Uri로 파싱해 scheme을 HTTPS로, 기본 포트는 생략하고 비기본 포트는 유지해 RoomServiceClient를 만든다. ApiKey/ApiSecret 전달. URL 오류는 이 함수가 삼키지 않는다. |

GameRoomService의 `CompleteGame`, `AbortGameStart`, `AbandonGameAfterDisconnect`는 방 삭제 작업을 fire-and-forget으로 시작한다. 따라서 로비 함수의 반환이 LiveKit 방 실제 삭제 완료를 뜻하지 않는다.

## 37. Services/LiveKitTokenService.cs

소스: [LiveKitTokenService.cs](../../polrob.Server/Services/LiveKitTokenService.cs#L9). **역할·상태:** 인증된 게임 참가자를 팀별 LiveKit 방에 들이는 짧은 JWT를 만든다. 생성자는 옵션 값을 보관한다. 사용자 인증·방 멤버십 확인은 앞단 VoiceController, GameRoomService, ActiveGameParticipantRegistry가 수행하며 이 클래스는 전달받은 Player와 연결 ID를 사용한다.

| 메서드 | 입력 → 처리 → 반환/거절 |
|---|---|
| [`CreateTeamVoiceToken(player,voiceSessionId,gameConnectionId)`](../../polrob.Server/Services/LiveKitTokenService.cs#L17) | 설정·두 ID 검증 → Player.RoomId/Role과 현재 음성 세션으로 팀 방 이름 생성 → 게임 연결 세대별 identity 생성 → 유효 기간을 설정값에서 1~5분으로 clamp → LiveKit AccessToken 서명. grant는 지정 방 참가·구독·게시를 허용하되 데이터 게시는 막고 게시 source는 microphone만 허용한다. `VoiceConnectionInfo(ServerUrl,ParticipantToken,RoomName,Role,ExpiresAtUtc)` 반환. 캐시한 이전 경기 토큰이 새 방에 쓰이지 않게 음성 세션을 방 이름에 포함한다. |
| [`ValidateConfiguration()`](../../polrob.Server/Services/LiveKitTokenService.cs#L72) | URL이 절대 `wss`가 아니거나 key/secret이 공백이면 InvalidOperationException. 옵션 DTO의 setter에서 검증하는 것이 아니다. |
| [`CreateTeamRoomName(roomId,voiceSessionId,role)`](../../polrob.Server/Services/LiveKitTokenService.cs#L89) | `polrob-{roomId}-{voiceSessionId}-police/robber` 반환. 두 enum 값 외에는 ArgumentOutOfRangeException. 문자열 ID를 별도 escape하지 않는다. |
| [`CreateParticipantIdentity(roomId,userId,gameConnectionId)`](../../polrob.Server/Services/LiveKitTokenService.cs#L106) | 세 문자열을 줄바꿈으로 연결한 UTF-8 SHA-256을 소문자 hex로 만들어 `player-` 접두사를 붙인다. 같은 게임 사용자라도 TCP 재접속 ID가 다르면 음성 identity가 달라진다. |

`ExpiresAtUtc`는 응답 생성 시 `DateTime.UtcNow + 수명`으로 계산한다. 이 시각이 지나도 이미 연결된 세션을 여기서 강제로 끊는 코드가 아니다. 토큰 생성은 LiveKit 관리 API 네트워크 호출이 아니라 서버에서 JWT를 만드는 작업이다.

## 38. Services/UserDbService.cs

소스: [UserDbService.cs](../../polrob.Server/Services/UserDbService.cs#L8). **역할·상태:** 일반 사용자 계정을 Cosmos에 저장하고 사용자 ID 조회를 메모리 사전으로 가속한다. 봇은 별도 BotIdentityService를 사용한다. 생성자는 CosmosClient와 옵션의 DB/Users 컨테이너 ID를 보관하며 빈 설정은 `PolRobDB`/`Users`로 대체한다. `_container`는 `InitializeAsync` 뒤에만 준비된다.

| 메서드 | 입력 → 처리 → 반환/실패 |
|---|---|
| [`InitializeAsync()`](../../polrob.Server/Services/UserDbService.cs#L30) | DB가 없으면 만들고 `/id` partition key를 가진 Users 컨테이너를 준비한다. `/name` unique key 정책을 새 컨테이너에 설정한다. 시작 시 Program이 await하므로 실패 시 앱 시작도 실패한다. 기존 컨테이너의 정책을 마이그레이션하는 코드는 없다. |
| [`CreateUserAsync(name,password)`](../../polrob.Server/Services/UserDbService.cs#L41) | 이름을 trim·소문자 정규화하고 해당 이름 문서를 먼저 조회. 있으면 null. 없으면 새 GUID ID, PBKDF2 비밀번호 hash 문서를 생성해 `/id` partition에 CreateItemAsync. 성공하면 User를 ID cache에 넣고 반환, Cosmos 409면 null. 비밀번호/이름 형식 정책은 AuthController가 앞에서 확인하며 이 함수는 재검증하지 않는다. 다른 Cosmos 예외는 전파된다. |
| [`ValidateUserAsync(name,password)`](../../polrob.Server/Services/UserDbService.cs#L70) | 정규화 이름으로 문서 찾기 → 없거나 VerifyPassword=false면 null → 성공이면 ID cache를 갱신하고 ID/Name User 반환. 원문 비밀번호를 cache에 넣지 않는다. malformed 저장 hash의 Base64 파싱 예외를 별도 복구하지 않는다. |
| [`GetUserAsync(id)`](../../polrob.Server/Services/UserDbService.cs#L83) | `_usersById`에 있으면 즉시 반환. 없으면 ID를 partition key로 Cosmos point read, 성공 시 cache 채움; 404만 null로 바꾸고 나머지 예외는 전파. 이름 조회와 달리 쿼리하지 않는다. |
| [`GetDocumentByNameAsync(normalizedName)`](../../polrob.Server/Services/UserDbService.cs#L103) | `SELECT TOP 1 ... WHERE c.name=@name` parameter query를 페이지 단위로 읽어 첫 문서를 반환하거나 null. `/id` partition에 이름만으로 점 조회할 수 없어 query가 필요하다. 호출자가 이름을 정규화한 상태를 전제로 한다. |
| [`NormalizeName(name)`](../../polrob.Server/Services/UserDbService.cs#L124) | 양끝 공백 제거 후 invariant 소문자. DB 문서 이름에도 이 값이 저장된다. |
| [`HashPassword(password)`](../../polrob.Server/Services/UserDbService.cs#L126) | 랜덤 16바이트 salt, 100,000회 PBKDF2-SHA256, 32바이트 hash를 만들어 `알고리즘:반복수:salt:hash` Base64 문자열 반환. 동일 비밀번호도 salt가 다르면 저장 문자열이 달라진다. |
| [`VerifyPassword(password,storedHash)`](../../polrob.Server/Services/UserDbService.cs#L140) | 네 부분 형식·알고리즘·반복수 확인 → 저장 salt로 같은 길이의 hash 계산 → FixedTimeEquals 결과 반환. 부분 수/알고리즘/반복수 파싱 실패는 false, Base64 자체 오류는 여기서 잡지 않는다. |
| [`UserDocument.ToUser()`](../../polrob.Server/Services/UserDbService.cs#L176) | Cosmos 내부 문서의 Id/Name만 새 User DTO로 복사. PasswordHash는 외부 반환 객체에 포함되지 않는다. |

`UserDocument`는 id/name/passwordHash의 Cosmos JSON 속성명을 명시한 private 형식이며, 파일 끝의 `User`는 ID/Name만 가진 가변 모델이다. ID cache에는 만료/삭제 동기화가 없어 이 프로세스가 직접 계정을 삭제하는 흐름이 없다. `UserDbService`의 실패와 비밀번호 검증 결과는 AuthController가 HTTP 상태로 바꾼다.
