# 클라이언트 파일·메서드 참고서

[전체 코드 해설](../code-guide.md) · [클라이언트 실행 흐름](04-client.md)

이 문서는 `polrob.Client`의 작성된 C# 파일 39개를 **파일과 메서드 단위**로 설명한다. 소스의 특정 함수를 찾았을 때 그 함수가 어떤 상태를 읽고, 무엇을 바꾸고, 다음에 무엇을 호출하는지 확인하는 용도다. `obj`/`bin`에서 생성된 파일은 제외했다. 화면 모양·색상·정렬 수치는 생략하지만 화면 이벤트에 들어 있는 인증, 요청, 상태 갱신, 수명 관리는 포함한다.

메서드 설명에서 `→`는 실제 처리 또는 호출 순서다. `Task`만 반환하는 메서드는 성공 여부 값 대신 정상 완료/예외로 완료를 알린다. `async void` 이벤트 handler는 MAUI가 호출하며 일반 호출자가 `await`할 수 없다. 버튼 handler의 `sender`, `EventArgs`가 쓰이지 않는 경우 입력은 버튼 동작과 해당 페이지 필드다. “현재 호출 없음”은 작성된 소스에서 실제 호출자를 찾지 못했다는 뜻으로, 기능이 실행 중이라고 가정하면 안 된다.

## 파일 목차

파일명을 누르면 아래의 해당 파일 설명으로 이동한다. 각 파일 절 안의 소스 링크는 실제 C# 선언 위치로 이동한다.

| 파일 | 파일 | 파일 |
|---|---|---|
| [App.xaml.cs](#polrobclientappxamlcs) | [AppShell.xaml.cs](#polrobclientappshellxamlcs) | [AndroidStartupSplashPage.xaml.cs](#polrobclientandroidstartupsplashpagexamlcs) |
| [MauiProgram.cs](#polrobclientmauiprogramcs) | [AuthSession.cs](#polrobclientauthsessioncs) | [Login.xaml.cs](#polrobclientloginxamlcs) |
| [MainPage.xaml.cs](#polrobclientmainpagexamlcs) | [GameCreate.xaml.cs](#polrobclientgamecreatexamlcs) | [GameJoin.xaml.cs](#polrobclientgamejoinxamlcs) |
| [GameMatching.xaml.cs](#polrobclientgamematchingxamlcs) | [GameLobby.xaml.cs](#polrobclientgamelobbyxamlcs) | [Network/GameNetworkClient.cs](#polrobclientnetworkgamenetworkclientcs) |
| [GamePlay.xaml.cs](#polrobclientgameplayxamlcs) | [GamePlay.Voice.cs](#polrobclientgameplayvoicecs) | [Voice/IVoiceRoomClient.cs](#polrobclientvoiceivoiceroomclientcs) |
| [Voice/VoiceChatService.cs](#polrobclientvoicevoicechatservicecs) | [Voice/VoiceTokenClient.cs](#polrobclientvoicevoicetokenclientcs) | [Voice/VoiceChatException.cs](#polrobclientvoicevoicechatexceptioncs) |
| [Voice/VoiceParticipantState.cs](#polrobclientvoicevoiceparticipantstatecs) | [Voice/TeamVoiceMemberViewModel.cs](#polrobclientvoiceteamvoicememberviewmodelcs) | [Voice/HybridWebViewVoiceRoomClient.cs](#polrobclientvoicehybridwebviewvoiceroomclientcs) |
| [Voice/VoiceWebViewPlatformConfiguration.cs](#polrobclientvoicevoicewebviewplatformconfigurationcs) | [GameOver.xaml.cs](#polrobclientgameoverxamlcs) | [Profile.xaml.cs](#polrobclientprofilexamlcs) |
| [IMapRenderer.cs](#polrobclientimaprenderercs) | [TownMapRenderer.cs](#polrobclienttownmaprenderercs) | [ClassicTownMapRenderer.cs](#polrobclientclassictownmaprenderercs) |
| [ExactMapTileCache.cs](#polrobclientexactmaptilecachecs) | [GameSettings.cs](#polrobclientgamesettingscs) | [Settings.xaml.cs](#polrobclientsettingsxamlcs) |
| [ProximityHaptics.cs](#polrobclientproximityhapticscs) | [GameStartTiming.cs](#polrobclientgamestarttimingcs) | [Platforms/Android/MainActivity.cs](#polrobclientplatformsandroidmainactivitycs) |
| [Platforms/Android/MainApplication.cs](#polrobclientplatformsandroidmainapplicationcs) | [Platforms/iOS/Program.cs](#polrobclientplatformsiosprogramcs) | [Platforms/iOS/AppDelegate.cs](#polrobclientplatformsiosappdelegatecs) |
| [Platforms/MacCatalyst/Program.cs](#polrobclientplatformsmaccatalystprogramcs) | [Platforms/MacCatalyst/AppDelegate.cs](#polrobclientplatformsmaccatalystappdelegatecs) | [Platforms/Windows/App.xaml.cs](#polrobclientplatformswindowsappxamlcs) |

## `polrob.Client/App.xaml.cs`

[소스](../../polrob.Client/App.xaml.cs#L5). **역할:** MAUI Application과 최초 Window를 만든다. 게임 상태를 보유하지 않는다.

- **`App()`**: MAUI가 앱 생성 시 호출한다. `InitializeComponent()`로 앱 리소스를 읽는다. 반환값은 생성된 앱 인스턴스이며 HTTP 요청이나 게임 접속은 하지 않는다.
- **`CreateWindow(IActivationState? activationState)`**: OS 창 생성 요청 → `_ = AuthSession.LoadAsync()`를 먼저 시작 → Android는 `AndroidStartupSplashPage`, 다른 플랫폼은 `AppShell`을 루트로 가진 `Window` 반환. 로그인 저장소 읽기 완료를 기다리지 않으므로 각 페이지가 나중에 `AuthSession.LoadAsync`를 다시 기다린다. `activationState`로 이전 경기를 복원하는 코드는 없다.

## `polrob.Client/AppShell.xaml.cs`

[소스](../../polrob.Client/AppShell.xaml.cs#L3). **역할:** MAUI Shell 탐색 경로를 등록한다. 별도의 가변 업무 필드는 없다.

- **`AppShell()`**: XAML 초기화 → `Routing.RegisterRoute`로 `GamePlay`, `GameCreate`, `GameJoin`, `GameMatching`, `GameLobby`, `GameOver`, `Login`, `Profile`, `Settings`를 등록한다. 다른 페이지의 `Shell.Current.GoToAsync("GameLobby?... ")` 같은 호출이 실제 페이지 타입을 찾게 한다. 경로 등록이 인증 또는 방 참가 검증을 수행하지는 않는다.

## `polrob.Client/AndroidStartupSplashPage.xaml.cs`

[소스](../../polrob.Client/AndroidStartupSplashPage.xaml.cs#L3). **역할:** Android에서 첫 Shell 앞의 지연 화면. **상태:** `_hasNavigated`는 동일 객체가 두 번 루트 페이지를 바꾸지 않게 한다.

- **`AndroidStartupSplashPage()`**: XAML 초기화.
- **`OnAppearing()`**: 이미 이동했으면 즉시 종료 → `_hasNavigated=true` → 1.5초 대기 → 앱의 Window가 있으면 첫 Window의 `Page`를 새 `AppShell`로 교체한다. 창이 없으면 이동하지 않는다. 대기에 별도 취소 토큰은 없다. 이 시간은 서버 준비나 자산 로딩을 확인한 결과가 아니라 고정 지연이다.

## `polrob.Client/MauiProgram.cs`

[소스](../../polrob.Client/MauiProgram.cs#L6). **역할:** 플랫폼 진입점들이 사용하는 MAUI 앱 구성 함수. 정적 구성 외 업무 상태는 없다.

- **`CreateMauiApp()`**: builder 생성 → `UseMauiApp<App>()` → `UseSkiaSharp()` → 폰트 등록 → DEBUG일 때 Debug 로그 등록 → `builder.Build()` 반환. 플랫폼별 `AppDelegate`/`MainApplication`/Windows `App`이 호출한다. 게임 서버 서비스나 음성 client를 DI로 등록하는 메서드는 아니며, 음성 객체는 `GamePlay.Voice`에서 직접 조합한다.

## `polrob.Client/AuthSession.cs`

[소스](../../polrob.Client/AuthSession.cs#L8). **역할:** 화면들이 함께 사용하는 로그인 정보와 서버 주소 접근점. `static`이므로 페이지 인스턴스별 복사본이 아니다.

**상태:** `SessionToken`, `UserId`, `Name`; 첫 저장소 읽기 완료 표시 `_isLoaded`; 동시 읽기를 직렬화하는 `LoadLock`; 관찰자에게 알리는 `Changed` 이벤트. 토큰은 `SecureStorage`, 이름·ID는 `Preferences`에 저장한다.

### 속성

- **`IsLoggedIn`**: 토큰과 ID가 모두 비어 있지 않으면 true. 저장된 토큰의 서버 유효성·만료를 조회하지 않는다.
- **`ApiBaseUrl`**: `GetBuildMetadata("PolRobApiBaseUrl")`가 있으면 끝의 slash를 정규화해 사용한다. 없으면 플랫폼에 맞는 HTTP 주소를 반환한다. MainPage·인증·방·프로필·음성 HTTP 요청이 사용한다.
- **`GameServerHost`**: Android/iOS용 호스트 또는 데스크톱 loopback을 반환한다. HTTP base URL을 파싱해 재사용하지 않는다. `GamePlay.GetServerIpAddress`가 사용한다.
- **`AndroidServerHost` / `IosServerHost` / `PhysicalServerHost`**: 가상 기기이면 emulator별 loopback 접근 주소, 실기기이면 빌드 metadata 또는 fallback LAN 호스트를 선택한다.

### 메서드

- **[`LoadAsync()`](../../polrob.Client/AuthSession.cs#L27)**: 여러 페이지 또는 앱 초기화가 호출한다. `_isLoaded`이면 종료 → semaphore 대기 → 다시 `_isLoaded` 확인 → 보안 저장소 토큰과 Preferences ID/이름 읽기 → loaded 표시 → finally에서 잠금 해제 → `Changed` 발생. 반환 `Task`는 읽기가 끝났음을 뜻한다. 저장소 예외를 삼키지 않으며 실패 시 아래의 변경 알림까지 도달하지 않는다. 잠금은 이 읽기 작업 사이의 중복을 막는 것이며 모든 세션 변경을 하나의 트랜잭션으로 묶지 않는다.
- **[`SetLoggedInAsync(sessionToken, userId, name)`](../../polrob.Client/AuthSession.cs#L55)**: `Login.SendAuthRequestAsync`가 정상 응답을 넘긴다. 세 필드와 `_isLoaded` 갱신 → `SecureStorage.SetAsync` → Preferences 두 값 기록 → `Changed`. 인자의 서버 검증을 여기서 다시 수행하지 않는다. 저장소 쓰기가 실패하면 이미 바꾼 메모리 값을 되돌리는 rollback은 없다.
- **[`LogoutAsync()`](../../polrob.Client/AuthSession.cs#L69)**: 현재 토큰을 캡처 → 토큰이 있으면 임시 HttpClient로 `auth/logout` POST → 서버 호출 실패도 무시 → `ClearLocalSession()`. `Profile.OnLogoutClicked`가 호출한다. 오프라인이어도 로컬 로그아웃을 마치기 위한 순서다.
- **[`ClearLocalSession()`](../../polrob.Client/AuthSession.cs#L89)**: 메모리 세 값 null, `_isLoaded=true`, 현재 저장 키와 예전 `playerId/loginId/displayName` 제거 → `Changed`. HTTP 요청은 없다. 401 처리나 게임 연결 종료에서도 직접 호출한다.
- **[`ApplyAuthorization(HttpClient httpClient)`](../../polrob.Client/AuthSession.cs#L107)**: 현재 토큰으로 클라이언트의 기본 Authorization 헤더를 Bearer로 바꾸거나 토큰이 없으면 null로 지운다. 반환값 없이 전달받은 객체를 변경한다. Profile 전적 조회는 이 함수 대신 요청 메시지에 캡처한 토큰을 넣는다.
- **[`GetBuildMetadata(string key)`](../../polrob.Client/AuthSession.cs#L161)**: 현재 assembly의 `AssemblyMetadataAttribute`를 열거하고 ordinal 문자열 비교로 첫 일치 값 반환, 없으면 null. 위 주소 속성들의 공통 조회 함수다.
- **`LogoutRequest`**: `SessionToken` 하나를 가진 private record. 로그아웃 JSON 본문 형식이다.

## `polrob.Client/Login.xaml.cs`

[소스](../../polrob.Client/Login.xaml.cs#L6). **역할:** 로그인/회원가입 입력을 HTTP 인증과 연결한다. **상태:** API base URL과 10초 제한의 `_httpClient`, 가입 여부 `_isSignUpMode`.

| 메서드 | 입력 → 처리 → 반환/부작용과 호출 관계 |
|---|---|
| `Login()` | XAML 생성 → `SetMode(false)`로 로그인 모드 초기화. |
| `OnHomeClicked` | 홈 동작 → `GoToAsync("//MainPage")`; 서버 로그아웃은 수행하지 않음. |
| `OnSignInClicked` / `OnSignUpClicked` | 모드 버튼 → 각각 `SetMode(false/true)`. |
| `OnContinueClicked` | 상태 문구 초기화 → `_isSignUpMode`에 따라 `SignUpAsync` 또는 `LoginAsync`를 await. |
| [`SetMode(bool isSignUpMode)`](../../polrob.Client/Login.xaml.cs#L51) | 모드 필드 갱신, 비밀번호 확인 칸·버튼 상태 조정. 입력 문자열이나 로그인 세션을 바꾸지는 않음. |
| [`SignUpAsync()`](../../polrob.Client/Login.xaml.cs#L63) | 이름 Trim, 비밀번호/확인 읽기 → 이름 공백 또는 확인 불일치면 `ShowStatus` 후 종료 → `SendAuthRequestAsync("auth/signup", SignUpRequest)`. 나머지 가입 정책은 서버 판단. |
| [`LoginAsync()`](../../polrob.Client/Login.xaml.cs#L84) | 이름 Trim과 비밀번호 읽기 → `SendAuthRequestAsync("auth/login", LoginRequest)`. 가입용 확인 검증은 하지 않음. |
| [`ReadErrorMessageAsync(response)`](../../polrob.Client/Login.xaml.cs#L138) | 응답 본문 읽기 → 비어 있으면 상태 코드가 든 기본 문구, 있으면 양끝 따옴표 제거한 문자열 반환. 응답 전체를 구조화된 오류 DTO로 파싱하지 않음. |
| `SetBusy(bool)` | 전송 중 버튼들을 끄고 계속 버튼 문구 변경; 중복 사용자 조작 억제. |
| `ShowStatus(message, isError)` | 상태 문구와 오류/성공 표현만 갱신. 예외를 던지거나 탐색하지 않음. |

**[`SendAuthRequestAsync<TRequest>(string route, TRequest request)`](../../polrob.Client/Login.xaml.cs#L92)**는 실제 요청 함수다. busy 표시 → JSON POST → HTTP 실패이면 서버 오류 문자열 표시 후 return → `LoginResponse` 역직렬화 → null이면 응답 오류 표시 → `AuthSession.SetLoggedInAsync` → 환영 문구 → 홈 탐색. 가입 응답도 동일한 로그인 응답을 기대하므로 가입 성공 시 세션을 바로 보관한다. `OperationCanceledException`은 시간 초과, `HttpRequestException`은 연결 오류, 일반 예외는 처리 오류로 보여 주고 finally에서 busy를 해제한다. DTO는 `SignUpRequest(Name,Password)`, `LoginRequest(Name,Password)`, `LoginResponse(SessionToken,UserId,Name)` 세 record다.

## `polrob.Client/MainPage.xaml.cs`

[소스](../../polrob.Client/MainPage.xaml.cs#L7). **역할:** 로그인 상태 표시, 참가 화면 진입, 실제 커스텀 방 생성. **상태:** 정적 HttpClient; 선택 맵은 `MapPicker.SelectedItem`에 있다.

- **`MainPage()`**: XAML 생성 → `MapRegistry.All`을 picker 데이터로 지정 → `DisplayName`을 표시 항목으로 연결 → 기본 맵 선택.
- **[`OnAppearing()`](../../polrob.Client/MainPage.xaml.cs#L22)**: `Changed` 중복 구독 방지를 위해 먼저 해제 후 등록 → 세션 Load → `UpdateAuthHeader` → 최초 마이크 권한 요청. 외부 세션 변경 시 같은 표시 갱신 함수를 호출한다.
- **`OnDisappearing()`**: `AuthSession.Changed` 구독 해제. 로그인 값 자체나 HttpClient는 폐기하지 않는다.
- **`OnLoginClicked` / `OnProfileClicked` / `OnSettingsClicked` / `OnJoinClicked`**: 각각 `Login`, `Profile`, `Settings`, `GameJoin` 경로로 이동한다. 이 함수들에는 방 참가 HTTP가 없다.
- **[`OnCreateClicked`](../../polrob.Client/MainPage.xaml.cs#L53)**: 세션 Load → 로그인/ID 없으면 Login으로 이동하고 종료 → Create 버튼 비활성 → 인증 헤더 → `game/create`에 `CreateRoomRequest("custom", Police, true, selectedMapId)` POST. HTTP 401이면 세션 삭제·알림·Login, 기타 실패면 본문 오류 알림. 성공 응답이 null/Success 아님/RoomId 없음이면 이동하지 않는다. 유효한 방 ID·코드를 URI 인코딩해 `GameLobby?...&role=Police&isHost=true`로 이동한다. 통신/일반 예외를 알림으로 바꾸고 finally에서 버튼 복구.
- **`UpdateAuthHeader()`**: `AuthSession.IsLoggedIn/Name`으로 로그인·프로필 노출을 바꾼다. 인증 헤더를 만드는 메서드는 이름과 달리 여기서 호출하지 않는다.
- **`ReadErrorMessageAsync(response)`**: 본문을 문자열로 읽어 오류 문구 반환. 비어 있으면 HTTP 상태 코드 문구.
- **`CreateRoomRequest`**: Type·Role·IsPrivate·MapId를 가진 요청 record. 생성자를 경찰로 요청하는 정책이 이 호출부에 있다.

## `polrob.Client/GameCreate.xaml.cs`

[소스](../../polrob.Client/GameCreate.xaml.cs#L3). **역할:** 생성 방법 선택용으로 남은 화면. 자체 방 상태 필드는 없고 실제 방 생성 HTTP는 `MainPage`에 있다.

- **`GameCreate()`**: XAML 초기화.
- **`OnAppearing()`**: AuthSession 로드 후 `UpdateAuthHeader`.
- **`OnHomeClicked`**: `GoToAsync("..", true)`로 한 단계 돌아간다.
- **`OnProfileClicked`**: Profile 탐색.
- **`OnRandomClicked`**: **현재 본문에 주석만 있으며 동작 없음.** 랜덤 매칭은 `GameJoin → GameMatching`에서 수행한다.
- **`OnCustomClicked`**: 단순히 `GoToAsync("GameLobby")`. RoomId를 생성하거나 전달하지 않으므로 이 메서드만으로 정상 커스텀 방 생성이 끝나는 구조가 아니다.
- **`UpdateAuthHeader()`**: 로그인 여부/이름으로 프로필 버튼 갱신.

## `polrob.Client/GameJoin.xaml.cs`

[소스](../../polrob.Client/GameJoin.xaml.cs#L8). **역할:** 랜덤 역할 선택과 방 코드 입장. **상태:** nullable `_selectedRole`, 역할 이미지 로딩 완료 `_roleImagesLoaded`.

| 메서드 | 구체 동작 |
|---|---|
| `GameJoin()` | XAML 초기화. 역할을 기본 선택하지 않음. |
| `OnAppearing()` | 세션 읽기 → `LoadRoleImagesAsync` → 인증 표시 갱신. |
| `OnHomeClicked` / `OnProfileClicked` | 상위 화면 / Profile로 이동. |
| `OnRandomClicked` | 두 방식의 이전 선택 상태를 초기화 → 랜덤 역할 선택 노출. HTTP 없음. |
| `OnCustomClicked` | 두 방식 초기화 → 코드 입력 노출 → dispatcher로 entry focus 예약. |
| `OnChangeMethodClicked` | 선택 상태·코드 입력을 지우고 방식 선택 화면 복귀. |
| `OnRoomCodeTextChanged` | 새 문자열을 `NormalizeRoomCode`로 정규화; 이미 같으면 다시 대입하지 않아 변경 이벤트 반복을 줄임. |
| `OnPoliceRoleClicked` / `OnRobberRoleClicked` | `SelectRole(Police/Robber)` 호출. |
| `OnMatchingClicked` | 선택 역할 null이면 종료; 아니면 역할 query를 담아 GameMatching으로 이동. |
| `UpdateAuthHeader` | AuthSession의 이름·로그인 여부를 표시. |
| [`SelectRole(PlayerRole role)`](../../polrob.Client/GameJoin.xaml.cs#L167) | `_selectedRole=role`, 선택 표시, 매칭 버튼 활성. 서버의 확정 역할을 바꾸는 요청은 없음. |
| `ClearRandomSelection()` | 역할을 null로, 랜덤 선택 화면 숨김, 매칭 버튼 비활성. |
| `ClearCustomSelection()` | 코드 참가 화면 숨김과 오류 문자열 제거. 코드 문자열 자체의 삭제는 `OnChangeMethodClicked`에서 함. |
| [`NormalizeRoomCode(string? roomCode)`](../../polrob.Client/GameJoin.xaml.cs#L226) | null을 빈 값으로 → 앞뒤 Trim → 일반 공백 제거 → invariant 대문자 문자열 반환. |
| `ReadErrorMessageAsync(response)` | 실패 본문 또는 상태 코드 기본 문구 반환. |

**[`OnJoinCustomClicked`](../../polrob.Client/GameJoin.xaml.cs#L65)**: 세션·ID 확인 → 정규화 코드가 비었으면 오류 후 종료 → 버튼 비활성 → 임시 HttpClient에 인증 → `game/join-custom`에 `JoinCustomGameRequest(code, Robber)` POST. 401이면 세션 삭제와 Login 이동. HTTP 성공 후에도 `ServerResponse.Success`와 RoomId 검사. 응답 역할이 있으면 그 역할을, 없으면 Robber를 사용해 `GameLobby`로 이동한다. 요청/일반 오류는 현재 화면에 표시하고 finally에서 버튼 복구. 서버 참가가 성공했어도 이후 탐색 오류를 별도 서버 취소 요청으로 되돌리지는 않는다.

**[`LoadRoleImagesAsync()`](../../polrob.Client/GameJoin.xaml.cs#L204)**: `_roleImagesLoaded`이면 종료; 경찰/도둑 아이콘을 `LoadPackageImageSourceAsync`로 각각 읽고 둘 다 끝난 후 true. **`LoadPackageImageSourceAsync(fileName)`**은 패키지 stream을 byte 배열로 복사하고 닫은 뒤 `ImageSource.FromStream(() => new MemoryStream(bytes))`를 반환한다. 소비자가 나중에 요청할 때 새 stream을 만들므로 닫힌 원본 stream을 재사용하지 않는다. 이미지 로딩 예외는 이 두 함수 내부에서 처리하지 않는다.

## `polrob.Client/GameMatching.xaml.cs`

[소스](../../polrob.Client/GameMatching.xaml.cs#L11). **역할:** 랜덤 참가 요청 이후 인원 상태를 구독하고 게임으로 전환한다.

**상태:** `_hasRequestedMatching`은 같은 페이지의 재요청 방지, `_roomId`는 배정된 방, `_isMatched`는 매칭 완료, `_isNavigatingToGame`은 이중 탐색 방지, `_hubConnection`은 대기실 구독. `Role` query setter는 대소문자 무시 Enum 파싱 성공 시에만 기본 Robber를 바꾼다.

- **`GameMatching()`**: XAML 초기화.
- **[`OnAppearing()`](../../polrob.Client/GameMatching.xaml.cs#L46)**: 이미 요청했다면 종료 → AuthSession Load → 로그인/ID 검사 → `JoinRandomGameAsync(userId, role)`. 로그인 필요 시 Login으로 이동한다.
- **`OnCancelMatchingClicked`**: 취소 중 표시 → `DisconnectRoomUpdatesAsync(true)` → 요청 플래그 false → 이전 화면. disconnect 함수가 transport 오류를 삼키므로 여기의 화면 이동이 명시적 서버 성공 응답을 요구하는 형태는 아니다.
- **[`UpdateMatchingCount(int currentCount, int maxCount=6)`](../../polrob.Client/GameMatching.xaml.cs#L77)**: 인원을 `0..MatchingCapacity(6)`으로 clamp → 숫자 표시와 canvas invalidate. `maxCount` 인자는 현재 내부 계산에 사용하지 않는다.
- **`OnMatchingRingPaintSurface`**: `_currentMatchingCount`를 읽어 6개 구간 그림을 그린다. 업무 상태나 서버 인원을 바꾸지 않는다. paint/path는 using으로 정리한다.
- **[`JoinRandomGameAsync(string userId, PlayerRole role)`](../../polrob.Client/GameMatching.xaml.cs#L170)**: 요청 플래그 true → 인증 헤더 → `game/join-random` POST(`JoinRandomGameRequest(role)`). 성공 응답에서 방 ID·Matched·인원을 저장하고 현재 AuthSession ID가 있으면 `StartRoomUpdatesAsync`. `userId` 인자는 요청 본문에 넣지 않는다. 401은 로컬 로그아웃과 Login 탐색, 기타 오류는 문구·진행 표시·요청 플래그를 복구한다. null 성공 응답을 별도 재시도하는 로직은 없다.
- **`OnDisappearing()`**: `_ = DisconnectRoomUpdatesAsync(removePlayer:!_isMatched)` 예약. 페이지가 사라지는 동작 자체는 이 정리 완료를 기다리지 않는다.
- **[`StartRoomUpdatesAsync(roomId,userId)`](../../polrob.Client/GameMatching.xaml.cs#L240)**: 기존 연결을 플레이어 제거 없이 정리 → 토큰 provider와 자동 재연결이 있는 HubConnection 생성 → RoomStatusUpdated/GameStarted handler 등록 → Reconnected에서 JoinRoom 재호출 → StartAsync → JoinRoom(roomId). `userId`는 Hub 인자로 보내지 않고 토큰에서 서버가 사용자 식별. 상태 handler는 UI 스레드로 넘긴다. 두 시작 알림 모두 `NavigateToGameAsync`를 부를 수 있다.
- **[`NavigateToGameAsync(ServerResponse response)`](../../polrob.Client/GameMatching.xaml.cs#L300)**: 이미 이동 중이거나 응답 실패/미매칭이면 종료. 성공이면 두 플래그 true, 완료 표시 → 구독만 정리 → GamePlay에 roomId·role·gameType=random 전달. 초기 HTTP 응답의 Matched만으로 직접 호출하지 않고 Hub 알림을 통해 들어오는 함수다.
- **[`DisconnectRoomUpdatesAsync(bool removePlayer)`](../../polrob.Client/GameMatching.xaml.cs#L321)**: 연결 null이면 종료; 로컬 참조에 옮기고 필드 null → 연결됨/방 있음일 때 `removePlayer && !_isMatched && 사용자 있음`이면 CancelMatching, 그 외 LeaveRoom → DisposeAsync. 오류는 삼킨다. 이 순서에서 Hub 호출이 던지면 뒤의 Dispose까지 실행되지는 않는 현재 구현이다.
- **`ReadErrorMessageAsync(response)`**: 본문/기본 HTTP 문구 반환. **`JoinRandomGameRequest`**는 Role만 가진 record다.

## `polrob.Client/GameLobby.xaml.cs`

[소스](../../polrob.Client/GameLobby.xaml.cs#L12). **역할:** 커스텀 대기실 상태와 명령을 연결한다. **상태:** room/code/role, 서버 기준 `_isHost`, 양쪽 역할 존재 `_canStartGame`, 게임·홈 탐색 방지 플래그, HubConnection.

**Query 속성:** `RoomId`는 문자열 저장, `RoomCode`는 저장과 코드 표시, `Role`은 enum 파싱 성공 때 변경, `IsHost`는 bool 파싱. 초기 query 값은 다음 서버 상태에서 갱신된다.

| 메서드 | 입력·호출자 → 처리 → 결과 |
|---|---|
| `GameLobby()` | 탐색으로 생성 → XAML 초기화. |
| [`OnAppearing()`](../../polrob.Client/GameLobby.xaml.cs#L68) | 세션 로드/표시 → roomId 없으면 오류와 시작 버튼 숨김 후 종료 → role/host 초기 표시 → 사용자 ID 있으면 `StartRoomUpdatesAsync`. |
| `OnHomeClicked` | 홈 이동 중이면 종료; 플래그 true → `LeaveRoomForHomeAsync` → 실패면 플래그 false, 성공이면 절대 홈 경로 이동. |
| `OnProfileClicked` | Profile로 이동; 이 함수에 방 연결 정리는 없음. |
| `OnCopyRoomCodeClicked` | 코드 없으면 오류; 있으면 Clipboard에 복사 → 안내 문구 → 1.5초 후 여전히 같은 문구일 때만 지움. 새 오류/다른 문구를 덮어 지우지 않음. |
| `OnPoliceAreaTapped` / `OnRobberAreaTapped` | `ChangeRoleAsync`에 대상 역할 전달. |
| `OnGameStartClicked` | Hub Connected 확인 실패면 문구 후 종료; 아니면 `StartGame(roomId)` 호출. UI 조건 외 최종 검증은 서버. 호출 예외를 이 handler 안에서 catch하지 않음. |
| `UpdateAuthHeader` | AuthSession으로 프로필 표시만 갱신. |
| `UpdateRoleAreaBackgrounds` | `_role` 선택 표시만 갱신. |
| `UpdateStartButtonVisibility` | `_isHost && _canStartGame`를 버튼 IsVisible에 대입. |
| `RenderPlayerList(list, players, role)` | 대상 Layout을 비우고 전달된 Player마다 카드 생성; 로컬 ID 강조, 이름 없으면 ID 사용. `Players` 데이터 자체나 서버 역할을 수정하지 않음. |

**[`LeaveRoomForHomeAsync()`](../../polrob.Client/GameLobby.xaml.cs#L111) → Task<bool>**: Hub가 없거나 연결 중이 아니면 false; room/user 없으면 false; `CancelMatchingWithAcknowledgement(roomId)` 응답을 기다린다. 응답 없음/실패면 문구와 false. 성공해야 필드 Hub를 null로 분리하고 `_ = DisposeConnectionAsync(connection)` 예약 후 true. 홈 이동은 이 bool을 기다린다. 예외도 문구로 바꿔 false. **`DisposeConnectionAsync(connection)`**은 연결 폐기 오류를 삼킨다.

**[`StartRoomUpdatesAsync(roomId,userId)`](../../polrob.Client/GameLobby.xaml.cs#L221)**: 이미 Hub가 있으면 종료. WebSockets 전용·SkipNegotiation·토큰 provider·자동 재연결로 구성한다. RoomStatusUpdated → UI에서 `ApplyRoomStatus`. GameStarted가 성공/Matched이면 `NavigateToGameAsync`, 아니면 `ApplyRoomStatus`. Reconnected → 현재 방 JoinRoom. 마지막에 StartAsync와 JoinRoom을 기다린다. 전달된 userId를 명령 인자로 사용하지 않는다.

**[`ApplyRoomStatus(ServerResponse response)`](../../polrob.Client/GameLobby.xaml.cs#L273)**: 실패면 오류와 역할 표시만 복구. 성공이면 서버 코드/맵 표시 갱신, 양쪽 역할 존재 여부 계산, HostUserId와 로컬 ID로 방장 다시 계산, 응답 목록에서 자기 역할 재설정 → 버튼/선택/두 역할 명단 갱신. 전체 응답이 현재 로비 상태의 기준이다.

**[`ChangeRoleAsync(PlayerRole role)`](../../polrob.Client/GameLobby.xaml.cs#L316)**: 누름 표현과 120ms 대기 → 연결·사용자 검사 → `ChangeRole(roomId,role)` 전송. `_role=role`을 먼저 확정하지 않으며 서버 상태 이벤트를 기다린다. 연결 없음/사용자 없음은 표시 복구 후 종료; Invoke 예외 catch는 없다.

**[`NavigateToGameAsync(response)`](../../polrob.Client/GameLobby.xaml.cs#L435)**: 중복 이동/실패/미매칭이면 종료 → 이동 플래그 true → `DisconnectRoomUpdatesAsync(false)` → 현재 방·코드·역할·방장·gameType=custom query로 GamePlay 이동.

**[`DisconnectRoomUpdatesAsync(bool removePlayer)`](../../polrob.Client/GameLobby.xaml.cs#L453)**: Hub 필드 분리 → 연결됨/방 있음이면 removePlayer=true와 ID 있음일 때 CancelMatching, 아니면 LeaveRoom → Dispose. catch는 탐색을 막지 않기 위해 오류를 삼킨다. 현재 게임 진입 호출은 false를 넘기며 홈은 위 확인 응답 전용 함수를 사용한다. 일반 OnDisappearing override는 없다.

## `polrob.Client/Network/GameNetworkClient.cs`

[소스](../../polrob.Client/Network/GameNetworkClient.cs#L9). **역할:** 게임 전용 TCP/UDP 전송과 수신 이벤트 변환. UI의 승패·보간 로직은 가지지 않는다.

**상태:** TCP/UDP client와 binary reader/writer; `_isDisconnected`; 중복 실패 보고 `_connectionFailureReported`; 입력 번호 `_movementInputSequence`; 현재 연결 이동 토큰 `_movementSessionToken`; 입장 승인 `_joinAcknowledged`; 요청별 heartbeat 대기 사전과 heartbeat 취소 토큰. 이벤트들은 GamePlay가 구독한다.

### `ConnectAsync(ipAddress, localPlayer, sessionToken, cancellationToken, mapId)`

[선언](../../polrob.Client/Network/GameNetworkClient.cs#L40). 호출자는 `GamePlay.InitializeNetworkAsync`. 입력의 `localPlayer` 전체를 신뢰용 데이터로 직렬화하는 대신 `RoomId`를 꺼내 `GameJoinRequest(SessionToken,RoomId,MapId)`를 보낸다.

1. 연결 종료/실패 표시, 이동 번호/토큰, 승인 Task를 초기화한다.
2. TCP 7777 연결 후 reader/writer 생성.
3. IPv6 여부에 맞춰 UDP를 생성하고 7778 상대 endpoint 설정. UDP 쪽은 `IPAddress.Parse(ipAddress)`를 사용하므로 호스트 문자열 입력에 제약이 있다.
4. TCP/UDP 수신 작업을 시작하고 `SendTcp(Join, JSON)`.
5. 서버 MovementSession 응답을 최대 10초 기다린다. 응답은 이동 토큰도 전달한다.
6. 승인 후 전용 CTS로 heartbeat loop 시작, Task 정상 완료.

호출자 취소/승인 timeout/승인 대기 예외는 `Disconnect` 후 재던진다. TCP connect처럼 이 대기 try 앞에서 생긴 예외는 GamePlay 호출자가 잡고 정리한다. Task 완료는 입장 등록 승인을 뜻하며 UI에 큐잉된 InitialState 처리 완료까지 보장하지 않는다.

### 발신·연결 관리 메서드

- **[`SendTcp(TcpMessageType type, string payload)`](../../polrob.Client/Network/GameNetworkClient.cs#L91)**: 종료되었거나 writer 없으면 no-op. writer를 lock → payload UTF-8 byte 수 및 BinaryWriter 문자열의 7-bit 길이 prefix 크기 계산 → `Int32(뒤따르는 총 길이)`, byte type, string을 쓴다. 공유 스트림에서 heartbeat와 Join의 쓰기가 섞이지 않게 한다. 쓰기 예외는 여기서 catch하지 않는다.
- **[`SendMoveUdp(string playerId,float inputX,float inputY)`](../../polrob.Client/Network/GameNetworkClient.cs#L109)**: 종료/UDP 없음이면 no-op → sequence 선증가 → ID, 두 입력값, sequence, 이동 토큰 DTO → UTF-8 JSON → SendAsync 호출. 반환형은 void이고 SendAsync 완료를 기다리지 않는다. 위치를 보내는 함수가 아니며 입력 clamp는 GamePlay/서버에서 한다.
- **[`RefreshServerRegistrationAsync(CancellationToken)`](../../polrob.Client/Network/GameNetworkClient.cs#L149)**: 연결이 없으면 IOException. GUID 요청 ID로 completion을 concurrent dictionary에 등록 → TCP Heartbeat → 해당 ack를 최대 5초 대기 → finally에서 제거. 요청 등록 실패도 IOException. GamePlay resume와 주기 loop가 호출하며 각 ID가 따로라 응답을 구분한다.
- **[`RunHeartbeatLoopAsync(token)`](../../polrob.Client/Network/GameNetworkClient.cs#L283)**: 10초 대기와 `RefreshServerRegistrationAsync`를 반복. 자신의 CTS 취소는 정상 종료; 그 외 오류가 나고 아직 연결 종료 전이면 로그와 `ReportConnectionLost`.
- **[`Disconnect()`](../../polrob.Client/Network/GameNetworkClient.cs#L125)**: 종료 플래그 → join completion 취소 → heartbeat 취소/CTS 해제 → 진행 중 ack 모두 취소/사전 비움 → UDP/TCP/reader/writer를 개별 try로 정리 → 필드 null. 반복 호출을 허용하며 서버 방 참가 취소 Hub 호출은 하지 않는다.
- **[`ReportConnectionLost(Exception)`](../../polrob.Client/Network/GameNetworkClient.cs#L269)**: 이미 종료/보고했으면 return. 승인 완료 여부를 기억 → join completion에 IOException 설정 시도 → Disconnect. 이미 승인된 연결만 UI 스레드의 OnConnectionLost를 발생시킨다. 따라서 입장 중 실패는 ConnectAsync 예외, 입장 후 실패는 페이지 이벤트 경로로 전달된다.
- **`CreateJoinAcknowledgementSource()`**: RunContinuationsAsynchronously 옵션 TaskCompletionSource 반환. TCP 수신 중 승인 완료를 설정해도 기다리던 상위 continuation을 그 수신 스택에서 곧바로 깊게 실행하지 않도록 한다.

### 수신 메서드

**[`ReceiveTcpLoop()`](../../polrob.Client/Network/GameNetworkClient.cs#L178)**는 reader가 없으면 종료한다. 종료되지 않은 동안 Int32 length, byte type, BinaryReader string을 동기적으로 읽는다. 바깥 length 값은 별도 검증·read limit에 쓰지 않는다. MovementSession이면 토큰 저장/승인 완료, HeartbeatAcknowledged면 payload 요청 ID를 사전에서 제거하여 완료한다. 이 두 종류는 UI 전달을 기다리지 않는다.

나머지 타입은 MainThread에 작업을 예약한다. InitialState→List<Player>, Joined/PlayerState→Player, GameState/JailBreak/JailBreakProgress/OpponentProximity→해당 Shared DTO, Left→ID 문자열, Arrested→쉼표로 나눈 정확히 두 ID로 변환한다. 역직렬화 결과가 null이면 해당 이벤트를 생략한다. 읽기 루프 예외는 연결 종료 전이면 실패 보고로 보낸다. **UI 큐 안에서 나중에 발생하는 역직렬화/구독자 예외까지 이 루프의 바깥 catch가 잡는 구조는 아니다.**

**[`ReceiveUdpLoop()`](../../polrob.Client/Network/GameNetworkClient.cs#L303)**는 receive→UTF-8→PlayerMovementSync 해석을 반복한다. 압축 필드 `i`가 유효하면 이동 이벤트를 UI에 예약하고 다음 패킷으로 넘어간다. 아니면 일반 Player 형식으로 읽어 기존 전체 상태 이벤트를 호출한다. UDP 오류는 로그 후 계속하며, 이미 종료/Disposed 예외이면 loop를 끝낸다. 수신 결과에는 서버 sequence/time이 없어 도착 시각에 따라 상위가 보간한다.

## `polrob.Client/GamePlay.xaml.cs`

[소스](../../polrob.Client/GamePlay.xaml.cs#L17). **역할:** 실행 중 게임 화면의 상태 소유자. 네트워크 객체는 전송을, Shared GameMap은 충돌을, IMapRenderer는 맵 그림을 담당한다. 이 파일은 그 결과를 사용자 입력·표시에 연결한다. `GamePlay.Voice.cs`와 같은 객체다.

### 필드·속성 참고

| 상태 묶음 | 구체 필드와 읽고 쓰는 위치 |
|---|---|
| 플레이어 | `_player`, `_players`: 초기 상태/전체 상태/입장·퇴장 콜백이 구성하고 physics/draw/voice roster가 읽음. 사전에 모든 적이 항상 있는 것은 아님. |
| 서버 경기 상태 | `_gamePhase`, `_remainingTime`, `_winnerRole`, 도둑 전체/수감 수: GameState에서 수신. 결과 query 구성에도 사용. |
| 입력 | `_activeTouchId`, 중심·thumb·radius: Canvas_Touch가 갱신하고 UpdatePhysics가 정규화. |
| 이동 전송 | `_lastSyncTime`, `_lastSyncedIsMoving`: 50ms 이동/500ms 정지 전송과 상태 전환 즉시 송신 판단. |
| 타인 이동 | ID별 snapshot 목록, 120ms 지연·250ms gap·최대4개: UDP 콜백이 넣고 timer가 보간. |
| 판정 표시 | 체포 만료 사전, 지연 제거 ID 집합, 구출 진행률 사전: 서버 이벤트를 시각적 시간/상태로 표현. |
| 자산 | 비트맵, source bounds, 맵 prop 사전, renderer, `_assetsLoaded`, `_assetLoadLock`: asset 함수 및 화면 정리에서 사용. |
| 진행 제어 | `_isInitialized`는 최초 상태 수신, `_isGameOverTransitioning`은 종료 탐색 중복 방지. |
| 탐색 정보 | roomId/type/code/role/host와 `_rematchHostUserId`: 최초 query와 서버 상태를 결과/재매칭 route로 전달. |
| 진동 | pulse 길이, 다음 재생 TickCount64, ProximityHaptics: TCP 근접 알림과 timer가 제어. |

`RoomId` setter는 필드뿐 아니라 이미 생성된 `_player.RoomId`도 바꾼다. `Role` setter는 enum 파싱이 되면 선택 역할과 `_player.Role`을 바꾼다. `GameType`, `RoomCode`는 null을 빈 문자열로 저장하고 `IsHost`는 bool 파싱한다. 이 값들이 서버의 권한 검증을 대신하지 않는다.

### 생성·화면 수명·연결

**[`GamePlay()`](../../polrob.Client/GamePlay.xaml.cs#L151)**: XAML → 기본 GameMap 생성과 크기 일치 검사(10×256,15×256) → 로컬 Player 임시 생성(ID는 AuthSession/Preferences/GUID, 이름은 GetLocalName, 반지름25, 맵 중앙) → dictionary에 추가 → SKCanvasView의 Touch/PaintSurface 연결 → 16ms dispatcher timer 생성·시작 → InitializeTeamVoiceControls. timer는 매번 지연 제거→타인 보간→내 physics→진동→invalidate→UI 순서로 실행한다. 서버 초기 상태를 받기 전 physics는 실제 이동을 하지 않는다.

**[`OnAppearing()`](../../polrob.Client/GamePlay.xaml.cs#L203)**: voice lifetime 시작/Window 이벤트 연결 → AuthSession Load → 임시 ID와 실제 ID가 다르면 사전 key 교체 → room/role/name 반영과 voice roster 예약 → LoadRoomMapAsync 실패면 중단 → LoadAssetsAsync → InitializeNetworkAsync 실패면 중단 → InitializeTeamVoiceAsync. asset 예외를 함수 전체에서 잡는 catch는 없다. 기존 timer를 여기서 다시 Start하지 않는다.

**`GetServerIpAddress()`**는 `AuthSession.GameServerHost` 반환. 이름과 달리 독자적인 DNS 탐색이나 HTTP 상태 조회는 없다.

**[`InitializeNetworkAsync()`](../../polrob.Client/GamePlay.xaml.cs#L236) → Task<bool>**: `_voiceLifetimeGate` 안에서 페이지 활성·CTS 존재를 확인하고 연결 취소 token을 캡처한다. 비활성이면 false. 새 GameNetworkClient 생성과 아래 이벤트 등록 → ConnectAsync(host, player, 로그인 토큰, lifetime token, 현재 MapId). 성공 true, 취소면 disconnect/null 후 false, 일반 예외면 로그·정리·voice 상태 문구 후 false. 최초 입장 실패 시 자동으로 재시도하는 loop는 없다.

이 함수 안의 이벤트 람다가 실제 업무 로직의 큰 부분이므로 각각 따로 읽어야 한다.

| 콜백 | 입력 → 처리 → 상태 변화 |
|---|---|
| `OnConnectionLost(message)` | 이 callback을 등록한 client가 현재 client가 아니거나 Ended이면 무시. 그 외 StopGameClient→StopTeamVoice→로컬 세션 삭제→알림→홈→Login. |
| `OnInitialStateReceived(players)` | 플레이어·보간·지연 제거·체포·구출 사전 모두 초기화 → 받은 목록 등록 → 로컬 ID 인덱서로 `_player` 교체 → 타인 보간 초기화 → initialized true → roster 예약. 로컬 ID 누락 시 fallback 없이 예외 가능. |
| `OnPlayerJoined(player)` | 해당 지연 제거 취소; 자기 이외면 등록·보간 초기화; roster 예약. |
| `OnPlayerMoved(player)` | 지연 제거 취소 → 없으면 새 항목 → RoomId/X/Y/Speed/Radius/Angle/IsMoving/IsJailed/Role 및 비어 있지 않은 이름 복사. 로컬이면 참조 교체·수감 시 touch 해제, 타인이면 snapshot reset. 새 항목/이름/역할 변화 때 roster 갱신. |
| `OnPlayerMovementReceived(movement)` | 사전에 없는 ID면 버림. 로컬이면 ApplyTo로 좌표·각도·이동 즉시 덮어쓰기; 타인이면 AddRemoteMovementSnapshot. 수감 로컬은 touch 해제. |
| `OnPlayerLeft(id)` | 체포 연출 만료 전이면 deferred 집합에 넣고 종료. 아니면 플레이어·보간·체포·구출 상태 제거와 roster 갱신. |
| `OnOpponentProximityReceived(sync)` | pulse를 Shared Normalize로 검증. 기존 값과 같으면 종료; 달라지면 이전 진동 정지 후 새 길이 저장. |
| `OnPlayerArrested(policeId,robberId)` | UI 작업에서 TriggerArrestVisuals. |
| `OnPlayerJailBroken(sync)` | UI 작업에서 ApplyJailBreak. |
| `OnJailBreakProgressReceived(sync)` | 비어 있지 않은 RoomId가 현재 방과 다르면 무시; 진행률 사전 교체(null이면 빈 사전), invalidate. |
| `OnGameStateReceived(sync)` | UI에서 phase/time/winner/host/도둑 수 갱신. Countdown 표시, Playing 표시 정리, Ended/Rematching이면 아래 전환 실행. |

GameState의 도둑 전체 수는 0 이상, 수감 수는 0..전체 수로 clamp한다. Ended는 중복 플래그를 먼저 세우고 3초 후 StopGameClient→StopTeamVoice→BuildGameOverRoute로 이동한다. Rematching은 1초 후 같은 정리와 BuildRematchingRoute. 이 지연에는 별도 페이지 취소 token이 없고 GameState 전체에 방 ID 검사는 따로 없다.

**[`RefreshOrReconnectGameNetworkAsync()`](../../polrob.Client/GamePlay.xaml.cs#L521)**: resume에서 사용. 기존 client가 있으면 RefreshServerRegistrationAsync 성공 시 true. 실패하면 로그·그 client disconnect·아직 같은 참조라면 필드 null. voice lifetime이 끝났으면 false, 남았으면 InitializeNetworkAsync. 새 TCP 입장은 새 token/session/spawn이며 이전 수감 상태 복원 API가 아니다.

**[`OnDisappearing()`](../../polrob.Client/GamePlay.xaml.cs#L553)**: resume 플래그 해제·Window 이벤트 분리 → StopTeamVoiceAsync를 먼저 호출해 수명 종료 표시 → StopGameClient → asset lock을 얻어 renderer Dispose/null → 잠금 반환 → voice 종료 await. renderer 비트맵 원본을 이 자리에서 전부 Dispose하지 않는다.

**[`StopGameClient()`](../../polrob.Client/GamePlay.xaml.cs#L575)**: timer stop, 진동 stop/pulse0, network disconnect/null, 타인 보간 clear. voice, renderer의 정리는 호출부 책임이다.

### 자산과 맵 메서드

- **[`LoadRoomMapAsync()`](../../polrob.Client/GamePlay.xaml.cs#L659) → bool**: 인증된 15초 HTTP GET `game/{escaped roomId}/status`, lifetime token 전달. Success/지원 MapId 검사 → page 비활성이면 false → 이미 자산을 읽었는데 맵이 달라졌으면 예외 → `new GameMap(MapId)` 후 true. 정상 화면 취소는 false, 기타 실패는 알림 후 false. 로딩에 실패했는데 기본 맵으로 조용히 플레이하지 않는다.
- **[`LoadAssetsAsync()`](../../polrob.Client/GamePlay.xaml.cs#L688)**: asset lock. 이미 읽었으면 renderer가 없을 때만 재생성. 최초면 특수/기본 캐릭터→맵 props+타일의 중복 제거 경로→renderer→경찰/도둑 달리기8장→loaded true. finally lock release. 패키지 개별 실패는 아래 LoadBitmap이 null로 바꾼다.
- **[`CreateTownMapRenderer()`](../../polrob.Client/GamePlay.xaml.cs#L730) → IMapRenderer**: ChaseTown→TownMapRenderer, ClassicTown→ClassicTownMapRenderer(GeneratedAssetBounds callback). 다른 ID는 InvalidOperationException. 맵 정의를 별도 서버 요청으로 생성하지 않는다.
- **`LoadCharacterBitmapAsync(fileName)`**: LoadBitmapAsync 호출. null이 아니면 GeneratedAssetBounds.GetCharacter로 source bounds를 사전에 등록. bitmap 반환.
- **`LoadBitmapAsync(fileName)`**: 패키지 stream을 열고 SKBitmap.Decode; 오류 로그 후 null. using으로 stream만 닫고 반환 비트맵은 페이지가 보유.
- **`LoadTerrainTilesAsync()`**: TerrainTiles의 4×4 PNG를 `_terrainTiles`에 채운다. **현재 호출자 없음**; 현 renderer 로딩의 일부라고 읽으면 안 된다.

### 입력·이동 메서드

**[`Canvas_Touch(sender, SKTouchEventArgs e)`](../../polrob.Client/GamePlay.xaml.cs#L777)**: Playing이 아니면 Handled=true 후 종료. Pressed는 활성 손가락이 없고 화면 왼쪽 아래(또는 폭0)일 때 ID와 중심/thumb 지정. Moved는 그 ID만 처리하며 중심 거리≤반경이면 원위치, 초과면 원 경계로 thumb 제한. Released/Cancelled는 같은 ID면 -1. 모든 정상 처리 후 Handled=true. 서버로 직접 보내지는 않고 UpdatePhysics가 이 상태를 읽는다.

**[`UpdatePhysics()`](../../polrob.Client/GamePlay.xaml.cs#L952)**: 초기화 전 또는 Playing 아니면 IsMoving=false 후 종료. 먼저 입력0/이동false로 초기화. active touch가 있고 체포 표시 만료 후이며 미수감이면 입력 계산과 이동을 수행한다.

- 정규화 inputX/Y는 thumb delta/조이스틱 반경을 -1..1로 clamp한다.
- 프레임 이동량은 delta/반경×Player.Speed. 실제 deltaTime을 재측정해 곱하지 않는다.
- 어느 축 delta가 0.1보다 크면 이동true, angle=atan2의 degree-90.
- 후보 좌표를 반지름을 고려한 맵 경계에 제한한다.
- X축을 먼저 현재 Y로 검사하고 이동; Y축은 갱신된 X로 검사한다. 한 축이 막혀도 다른 축으로 미끄러질 수 있다.
- 전역 달리기 animation timer에 0.016을 더하고 0.1 이상이면 8 frame 중 다음으로 이동.
- network가 있으면 이동 중50ms/정지500ms 경과 또는 이동 여부 변경 시 SendMoveUdp. 보낸 시각/상태 기록.

이 함수는 체포·구출 승패를 판정하지 않는다. 서버의 로컬 movement 수신은 즉시 좌표를 덮어쓰며 이전 입력 replay는 없다. **`IsColliding(x,y,radius)`**는 `_gameMap.IsMovementPositionBlocked(...,_nearbyCollisionObstacles)`의 bool 반환을 그대로 사용하며 목록을 재사용한다.

**[`ResetRemotePlayerInterpolation(Player player)`](../../polrob.Client/GamePlay.xaml.cs#L835)**: 자기 ID면 보간 항목 제거. 타인이면 새 state와 현재 값 snapshot 하나(UTC now)를 넣어 기존 버퍼 교체. InitialState/Joined/PlayerState/구출에서 호출한다.

**[`AddRemoteMovementSnapshot(player,movement)`](../../polrob.Client/GamePlay.xaml.cs#L848)**: 수신시각 캡처, state 없으면 생성. 비었거나 이전 수신에서250ms 넘었으면 clear 후 화면상의 현재 Player를 `now-120ms`의 시작점으로 넣음. 받은 좌표·각도·이동 상태를 now snapshot으로 추가하고 4개 초과분 앞에서 제거. 여기서 타인 Player 좌표를 직접 바꾸지 않는다.

**[`UpdateRemotePlayerInterpolation()`](../../polrob.Client/GamePlay.xaml.cs#L879)**: renderAt=UTC now-120ms. 사전에 사라진 ID는 따로 모아 마지막에 제거. 너무 오래된 snapshot을 정리하되 보간에 필요한2개 유지. snapshot 없음→건너뜀, 하나/목표시각이 첫 점 이전→첫 점 적용, 목표시각 뒤 점 없음→최신 점 적용. 그 외 양쪽 수신 시각의 비율을0..1로 제한하여 XY 선형 보간·각도 최단 회전. 보간 도중 IsMoving은 양쪽 중 하나라도 true면 true, 완료시 도착 값. 미래 좌표 외삽 없음.

**`ApplyRemoteSnapshot(player,snapshot)`**: X/Y/Angle/IsMoving 네 값 복사. snapshot은 서버 tick 대신 클라이언트 수신 시각을 가진다. **`RemoteMovementSnapshot.FromPlayer(player,receivedAt)`**는 같은 네 값과 인자 시각의 immutable record를 반환한다. **`RemotePlayerInterpolationState`**는 해당 record의 List를 소유한다.

### 판정 표시·제거·화면 전환 메서드

- **[`TriggerArrestVisuals(policeId,robberId)`](../../polrob.Client/GamePlay.xaml.cs#L1930)**: 두 ID의 연출 만료를 Now+2초로 지정, robber 구출 진행 제거. 자신이 둘 중 하나면 중앙 표시 만료 설정과 touch 해제. IsJailed나 감옥 위치를 직접 확정하지 않는다.
- **[`ApplyJailBreak(JailBreakSync syncData)`](../../polrob.Client/GamePlay.xaml.cs#L1869)**: 도둑 ID를 모르면 종료. 전달 좌표/angle0/움직임false/수감false 적용. 타인 보간 reset, 체포 timer와 rescuer 진행 제거. 로컬 도둑이면 touch 해제와 자기 위치 명시 반영. invalidate.
- **[`RemoveExpiredDeferredPlayers()`](../../polrob.Client/GamePlay.xaml.cs#L1042)**: 체포 때문에 보류했던 ID를 복사해 순회. 아직 연출 중이면 유지, 만료면 플레이어/보간/체포/구출에서 제거. 실제 플레이어 삭제가 있었으면 voice roster 예약. timer가 호출한다.
- **`UpdateUI()`**: UI thread에 현재 수감수/전체수 label 변경을 예약. 타이머 tick이 호출하며 dictionary를 세어 재계산하지 않는다.
- **[`BuildGameOverRoute()`](../../polrob.Client/GamePlay.xaml.cs#L619) → string**: 현재 방/역할/type/code/host/winner(없으면 Robber)/남은 시간/수감수/전체수로 결과 query 구성, 문자열 항목 URI escape. 서버 기록 조회 없음.
- **[`BuildRematchingRoute()`](../../polrob.Client/GamePlay.xaml.cs#L633) → string**: custom이면 같은 방 코드와 역할, 서버 마지막 HostUserId 비교로 host query를 만들어 GameLobby; 그 외 GameMatching(role). `_isHost`를 무조건 재사용하지 않는다.
- **`UpdateProximityVibration()`**: 설정 off/초기화 전/Playing 아님/pulse0이면 Stop. TickCount64가 예약 시각 이상이면 PlayPulse(pulse) 후 다음 시각=now+2×pulse. 서버가 정규화한 길이만 쓰며 적 좌표로 계산하지 않는다.
- **`StopProximityVibration()`**: 예약시각0이면 종료; 그렇지 않으면 haptics Stop과 예약시각0. `_proximityVibrationPulseMilliseconds` 자체는 여기서 지우지 않는다.

### 실제 사용되는 그리기·기하 메서드

| 메서드 | 읽는 값과 처리·반환·자원 경계 |
|---|---|
| `Canvas_PaintSurface` | Skia canvas와 픽셀 width/height를 `Draw`에 전달. |
| [`Draw(canvas,width,height)`](../../polrob.Client/GamePlay.xaml.cs#L1080) | 내 좌표 기반 clamp camera, 2배 world transform, 가시 bounds+padding 계산 → background→vision overlay→players→props→구출 bar → transform 복원→화면 공간 문구/joystick. 서버 상태 변경 없음. |
| `ClampCameraCenter(target,viewportSize,worldSize)` | 화면이 세계 이상이면 세계 중심; 아니면 반 화면폭~세계-반 화면폭으로 target clamp한 float 반환. |
| `DrawMapBackground(canvas,bounds)` | renderer가 있으면 DrawBackground 위임. |
| `DrawMapProps(canvas,bounds)` | renderer에 bounds와 로컬 XY viewer를 전달. |
| [`DrawPlayers(canvas)`](../../polrob.Client/GamePlay.xaml.cs#L1532) | 각 Player의 부쉬/체포/구출/role/moving을 읽어 그릴지와 frame 선택. 타인이 부쉬에 있고 자신이 같은 부쉬에 없으면 생략하되 체포 중 경찰 공개 예외. 그림 없으면 원 fallback. 이름도 그림. |
| `GetVisibleSpriteBounds(bitmap)` | 자산 로딩 시 등록한 source rect 조회. 없으면 InvalidOperationException; 즉석 픽셀 스캔 fallback 없음. |
| `CreatePlayerDestinationRect(sourceRect,profile,playerDiameter)` | body width/pivot과 논리 지름×0.86으로 scale 계산, crop된 source 각 변을 pivot 기준 world rect로 변환하여 반환. 팔의 길이가 collision radius를 바꾸지 않음. |
| `GetLocalName()` | AuthSession.Name→Preferences name→"Player"의 첫 non-null 반환. 공백 문자열 검사는 별도 하지 않음. |
| `DrawPlayerName(canvas,player)` | 공백 이름은 Player, 아니면 Trim; FitTextToWidth 결과를 그린다. font/typeface/paint는 using으로 해제. |
| `FitTextToWidth(text,font,maxWidth)` | 이미 맞으면 원문. 아니면 길이를 최대24부터 줄이며 `...`을 붙여 맞는 첫 후보 반환. 아무것도 안 맞으면 `...`. |
| `DrawJailBreakProgressBar(canvas)` | 로컬이 도둑이고 진행률 있음일 때만 양수 항목을 ID순으로 그린다. 화면에서 구출 성공 판정을 만들지 않음. |
| `DrawVisionOverlay(canvas)` | 새 layer에 세계 어둠을 칠한 후 CreateVisionPath 부분을 Clear blend로 지우고 복원. players/props 이전에 그림. |
| `CreateVisionPath(player)` | XY 중심, GetVisionRange와 GetFacingAngle의90도 부채꼴 SKPath 반환. 호출자가 using으로 Dispose. 벽 교차를 검사하지 않음. |
| `GetVisionRange(player)` | 반지름×2×2.5 float 반환. |
| `GetFacingAngle(player)` | NormalizeDegrees(player.Angle+90) 반환. |
| `DegreesToRadians(degrees)` | degrees×π/180 반환. |
| `NormalizeDegrees(degrees)` | 나머지 연산 후 음수면360 추가; [0,360) 값 반환. |
| `ShortestAngleDifference(from,to)` | normalize(to-from)이180 초과면360 빼기; 보간이 짧은 방향으로 회전하게 함. |

`PlayerSpriteProfile`은 body pixel 폭과 pivot X/Y를 묶는 record다. 체포·달리기 frame마다 투명 여백이 달라도 몸통의 world 크기·회전 중심을 고정하기 위한 입력이다.

### 현재 게임 그리기 경로에서 호출하지 않는 보조 메서드

아래 함수들은 소스에는 있지만 현재 `Draw → IMapRenderer` 경로에서 도달하지 않는다. 서로 호출하는 helper가 있어도 최상위 호출이 없다.

| 메서드 | 구현된 처리와 현재 호출 관계 |
|---|---|
| `DrawOuterTerrain(canvas,bounds)` | 고정 외곽 숲/물 사각형 목록에 TerrainTile과 DrawTiledRect 사용. 현재 외부 호출 없음. |
| `DrawGeneratedMapLots(canvas,bounds)` | 고정 포장/흙 lot 사각형을 DrawPavedLot/DrawDirtLot에 전달. 현재 호출 없음. |
| `DrawPavedLot(canvas,bounds,rect)` | 화면 겹침 검사 후 바닥/안쪽/격자 그림. 위 미사용 lots 함수가 호출. |
| `DrawDirtLot(canvas,bounds,rect)` | 겹침 검사 후 흙 바닥/테두리 그림. 위 미사용 lots 함수가 호출. |
| `DrawGeneratedRoadNetwork(canvas,bounds)` | GeneratedRoadPaths 각 path를 DrawRoad로, 고정 지점을 DrawCrosswalk로 그림. bounds 인자는 실제 culling에 사용하지 않음. 현재 호출 없음. |
| `GeneratedRoadPaths()` | 고정 cubic curve6개를 새 SKPath로 만들어 List 반환. 위 미사용 network 함수에서 호출하며 그 함수에는 반환 path Dispose가 없다. |
| `DrawRoad(canvas,path)` | 폭을 달리한 여러 stroke로 도로 그림. paint는 using. 입력 path의 소유권을 넘겨받아 해제하지는 않음. |
| `DrawCrosswalk(canvas,center,rotationDegrees)` | canvas save→이동/회전→횡단보도 stripe→restore. |
| `TerrainTile(row,column)` | `_terrainTiles[row,column]` 직접 반환. 범위 검증 없음. |
| `DrawTerrainRegion(canvas,bounds,visible,fill,verticalEdge,horizontalEdge,fallback,invertWaterEdges=false)` | 내부와 네 edge를 flip 방향을 달리해 DrawTiledRect에 전달. 현재 호출 없음. |
| `DrawTiledRect(canvas,bounds,visible,tile,fallback,flipX=false,flipY=false)` | 겹침 없으면 종료; tile null이면 단색. 아니면256 grid 시작점 계산→bounds clip→보이는 타일 반복→DrawTerrainTile. |
| `DrawTerrainTile(canvas,tile,destination,flipX,flipY)` | flip 없으면 바로 bitmap, 있으면 중심 이동/부호 scale로 뒤집어 그린 뒤 restore. |
| `RectsIntersect(first,second)` | 축정렬 rect의 네 비교 bool 반환. 위 이전 렌더 helper들이 사용. |

이 파일의 정리 함수를 읽을 때 원본 bitmap은 페이지에 유지하고 renderer native 자원만 화면 이탈 시 해제한다는 차이도 함께 확인해야 한다.

## `polrob.Client/GamePlay.Voice.cs`

[소스](../../polrob.Client/GamePlay.Voice.cs#L7). **역할:** 같은 GamePlay 객체의 팀 음성 부분. HTTP 참가 권한, WebView 연결, 화면/창 수명, 팀원 표시를 연결한다.

**주요 상태:** `_voiceChatService`; 현재 수명 CTS와 활성 bool; 현재 Window와 resume 플래그; 연결 직렬화 `_voiceConnectionLock`, mute 직렬화 `_voiceToggleLock`, 수명 필드 보호 `_voiceLifetimeGate`; `_voiceStopTask`; 재연결 요청 generation/worker 실행 여부; UI 목록 갱신 예약 여부. 공개 `VoiceMembers`는 기존 행을 수정·이동하는 ObservableCollection이다.

### 초기화·종료·수명 검사

- **[`InitializeTeamVoiceControls()`](../../polrob.Client/GamePlay.Voice.cs#L35)**: GamePlay 생성자가 호출. 플랫폼 configuration Attach → HybridWebViewVoiceRoomClient와 VoiceTokenClient를 VoiceChatService로 조합 → 참가자/연결 이벤트 구독 → RefreshTeamVoiceRoster. 여기서는 아직 LiveKit에 connect하지 않는다.
- **[`BeginTeamVoiceLifetime()`](../../polrob.Client/GamePlay.Voice.cs#L47)**: 수명 gate를 lock; 이미 active이며 CTS가 있으면 return. 아니면 새 CTS와 active=true. OnAppearing과 Window resume가 호출한다. 같은 게임 페이지 객체에 여러 차례 수명이 생길 수 있다.
- **[`InitializeTeamVoiceAsync()`](../../polrob.Client/GamePlay.Voice.cs#L61)**: 연결 semaphore 획득 → 서비스와 gate 안의 현재 active CTS를 캡처 → 서비스/수명/방이 없으면 상태 문구 후 종료 → JoinTeamVoiceAsync(roomId,token) → 동일 수명이 아직 살아 있는지 확인 → 연결/mute 문구와 roster 갱신. 취소는 정상 종료로 삼키며 기타 오류는 로그 후 현재 수명일 때만 실패 표시. finally semaphore release. 토큰 요청 실패를 무한 재시도하는 루프는 이 함수에 없다.
- **[`StopTeamVoiceAsync()`](../../polrob.Client/GamePlay.Voice.cs#L113) → Task**: gate lock. 이미 inactive이면 기존 `_voiceStopTask` 반환. active=false, 현재 CTS를 로컬로 옮기고 필드 null, 취소 → StopTeamVoiceCoreAsync 작업을 저장·반환. 즉 “수명 종료 표시”는 await 없이 즉시 먼저 일어난다.
- **[`StopTeamVoiceCoreAsync(CTS? lifetimeToStop)`](../../polrob.Client/GamePlay.Voice.cs#L131)**: 연결 semaphore를 기다려 기존 connect와 겹치지 않게 함 → 서비스 LeaveAsync. 오류는 로그. finally에서 캡처한 CTS만 Dispose, 현재도 inactive이면 종료 표시/roster 갱신, semaphore release. 새 수명의 CTS를 무조건 해제하지 않는다.
- **`IsCurrentTeamVoiceLifetime(CTS lifetime)` → bool**: gate 안에서 active, CTS 참조 동일, 취소 안 됨을 모두 검사. 지연 완료가 새 화면을 덮어쓰는지 판단하는 함수.
- **`IsTeamVoiceLifetimeActive()` → bool**: gate 안에서 active 값 반환. 특정 수명의 동일성까지 검사하지는 않는다.

### Window 이벤트와 재연결

| 메서드 | 입력 → 처리 → 결과/경계 |
|---|---|
| [`AttachTeamVoiceWindowLifecycle()`](../../polrob.Client/GamePlay.Voice.cs#L175) | 현재 Window가 이미 구독 대상이면 종료. 이전 구독 해제 → Window 없으면 종료 → Stopped/Resumed 구독과 필드 저장. |
| `DetachTeamVoiceWindowLifecycle()` | 대상 Window 없으면 종료; 두 이벤트 해제 후 필드 null. |
| `OnVoiceWindowStopped` | 현재 active 여부를 resume 플래그에 기억; true이면 StopTeamVoiceAsync 대기. 게임 TCP를 직접 Disconnect하지는 않음. |
| `OnVoiceWindowResumed` | 재개할 음성이 없거나 이미 active면 종료. resume 플래그 해제→새 수명→RefreshOrReconnectGameNetworkAsync. 실패면 voice 종료/오류 표시, 성공하면 InitializeTeamVoiceAsync. |
| `OnVoiceParticipantsChanged` | 현재 수명이 active인 경우에만 roster 갱신 예약. |
| [`OnVoiceConnectionStateChanged(sender,args)`](../../polrob.Client/GamePlay.Voice.cs#L242) | inactive인데 Disconnected가 아닌 상태는 무시. enum을 사용자 문구로 변환→표시→roster 예약. active에서 Disconnected이면 재연결 예약. Warning/Error만 받았다고 동일 재연결을 예약하지는 않음. |
| [`ScheduleTeamVoiceReconnect()`](../../polrob.Client/GamePlay.Voice.cs#L275) | 요청 generation 증가. CompareExchange로 worker가 이미 있으면 종료, 없으면 ReconnectTeamVoiceAsync 실행. |
| [`ReconnectTeamVoiceAsync()`](../../polrob.Client/GamePlay.Voice.cs#L286) | active 동안 generation 캡처→1초 대기→active 재확인→서비스 미연결이면 InitializeTeamVoiceAsync→새 요청 generation이 없으면 return. finally에서 worker flag를 내리고 아직 처리 안 된 generation이 생겼으면 worker 재시작. 단순 실패를 영원히 반복하는 정책이 아니라 접수한 disconnected 요청을 처리함. |

resume의 heartbeat는 서버 active lease를 확인/갱신한 뒤 voice/token을 요청하기 위한 순서다. 기존 등록이 유지된 TCP와 새 TCP Join은 다르며, 후자는 새 PlayerSession과 스폰으로 입장한다.

### 팀원 목록과 조작

**[`ScheduleTeamVoiceRosterRefresh()`](../../polrob.Client/GamePlay.Voice.cs#L325)**는 Interlocked로 이미 예약되었으면 return하고, UI thread에 한 작업을 예약한다. 실행할 때 예약 flag를 풀고 RefreshTeamVoiceRoster. 네트워크 이벤트가 연속으로 와도 동일 UI 턴의 갱신을 합친다.

**[`RefreshTeamVoiceRoster()`](../../polrob.Client/GamePlay.Voice.cs#L339)**: UI thread가 아니면 다시 예약 후 return. GamePlay의 `_players`에서 자기 역할만 복사; 순회 중 InvalidOperationException이면 다음 UI 턴 재시도. 자신이 빠졌으면 추가. voice 참가자 배열을 identity별 dictionary로 만들고, game player는 ID 중복 제거→자기 우선→이름순 정렬한다. 불필요한 행 삭제, 기존 행 재정렬/새 행 추가 후 이름·슬롯·색·자기·발화·mute·연결을 Update한다. voice 상태가 없는 원격 행은 기존 mute 값을 유지하고 보이스 대기로 표시할 수 있다.

**[`OnVoiceMemberTapped(sender,args)`](../../polrob.Client/GamePlay.Voice.cs#L421)**: Parameter가 ID 문자열이 아니거나 service 없음/행 없음/busy이면 return. 서비스 또는 행이 미연결이면 문구 후 return. mute semaphore 대기→busy true→기존 mute 반전. 자신이면 SetLocalMicrophoneMutedAsync와 상태 문구; 타인이면 SetRemotePlaybackMutedAsync(identity). 성공해야 행 mute 갱신과 roster 예약. 예외는 사용자용 오류 표시, finally busy false와 잠금 반환. 원격 mute는 이 기기 재생만 바꾼다.

**`SetVoiceStatus(string status)`**: 로그와 Label 갱신. UI thread면 즉시, 아니면 BeginInvoke. 문자열의 실패/오류/연결/경고 여부로 표현을 고르며 연결 객체의 상태를 바꾸지는 않는다. **`GetVoiceErrorMessage(Exception)`**: VoiceChatException이면 그 메시지, 그 외는 일반 재시도 문구를 반환한다.

## `polrob.Client/Voice/IVoiceRoomClient.cs`

[소스](../../polrob.Client/Voice/IVoiceRoomClient.cs#L7). **역할:** GamePlay/서비스가 LiveKit 구체 타입에 직접 의존하지 않게 하는 계약. 필드나 구현은 없다.

| 멤버 | 입력/출력 계약과 현재 구현의 호출 관계 |
|---|---|
| `IsConnected`, `IsLocalMicrophoneMuted`, `Participants` | 현재 연결·내 마이크·참가자 읽기. VoiceChatService가 그대로 노출. |
| `ParticipantsChanged`, `ConnectionStateChanged` | 어댑터가 수신한 상태를 상위에 알림. 화면이 subscribe. |
| `ConnectAsync(connectionInfo, token=default)` | 서버가 발급한 접속 정보로 연결; 성공 완료 또는 예외. HybridWebViewVoiceRoomClient가 구현. |
| `SetLocalMicrophoneMutedAsync(muted,token)` | 자기 송출 on/off 요청. 다른 사람 마이크를 조작하는 계약이 아님. |
| `SetRemotePlaybackMutedAsync(participantIdentity,muted,token)` | 특정 참가자의 이 기기 재생만 제어. |
| `SetRemotePlaybackVolumeAsync(volume,token)` | 모든 원격 음성의 이 기기 볼륨 제어. 현재 Settings→실행 중 service 호출 경로는 없음. |
| `DisconnectAsync(token)` | 현재 방 연결 해제. 어댑터 자체 재사용 가능. |
| 상속된 `IAsyncDisposable.DisposeAsync()` | 연결과 어댑터의 이벤트/대기 자원까지 폐기. Disconnect와 수명이 다름. |

## `polrob.Client/Voice/VoiceChatService.cs`

[소스](../../polrob.Client/Voice/VoiceChatService.cs#L3). **역할:** 게임 서버 참가 권한 요청과 room client를 조합한다. **필드:** 생성자로 받은 `_tokenClient`, `_roomClient`. 자체 참가자 목록을 복제하지 않는다.

- **`VoiceChatService(tokenClient,roomClient)`**: 두 협력 객체를 보관한다. 연결 시작은 하지 않는다.
- **속성/이벤트:** IsConnected/IsLocalMicrophoneMuted/Participants는 roomClient에서 읽는다. ParticipantsChanged/ConnectionStateChanged의 add/remove도 그 객체에 직접 전달하므로 service 자체 별도 이벤트 발생은 없다.
- **[`JoinTeamVoiceAsync(string roomId,CancellationToken)`](../../polrob.Client/Voice/VoiceChatService.cs#L30)**: GetConnectionInfoAsync(roomId,token) await→그 결과로 roomClient.ConnectAsync. 앞 단계 실패면 연결을 호출하지 않는다. 키/secret 대신 짧은 참가 토큰만 전달한다.
- **`SetLocalMicrophoneMutedAsync`**, **`SetRemotePlaybackMutedAsync`**, **`SetRemotePlaybackVolumeAsync`**: 인자를 그대로 roomClient 함수에 넘기고 해당 Task 반환. 별도 catch·retry·사용자 설정 저장 없음.
- **`LeaveAsync(token)`**: roomClient.DisconnectAsync의 Task 반환. GamePlay 화면 종료가 실제로 호출하는 함수다.
- **`DisposeAsync()`**: DisconnectAsync 대기 후 roomClient.DisposeAsync 대기. 어댑터 이벤트·진행 명령까지 완전히 정리하는 API지만 현재 GamePlay의 Stop 경로는 LeaveAsync를 사용한다.

## `polrob.Client/Voice/VoiceTokenClient.cs`

[소스](../../polrob.Client/Voice/VoiceTokenClient.cs#L7). **역할:** 인증된 HTTP 음성 접속 정보 발급. 지속 필드가 없으며 요청마다 HttpClient를 만든다.

**[`GetConnectionInfoAsync(string roomId,CancellationToken)`](../../polrob.Client/Voice/VoiceTokenClient.cs#L9) → VoiceConnectionInfo**: roomId 공백이면 ArgumentException → AuthSession.LoadAsync → 미로그인이면 VoiceChatException → API client와 Bearer 헤더 → `voice/token`에 VoiceTokenRequest(roomId) POST. token은 요청/본문 읽기에 전달된다. 401은 세션 만료, 403은 방 권한 없음으로 예외를 던지지만 여기서 AuthSession을 직접 지우지는 않는다.

기타 실패는 진단 본문을 로그에 남기고 503=서버 설정, 5xx=일시 문제, 400=잘못된 요청, 기타=접속 정보 실패로 VoiceChatException을 만든다. 성공 JSON을 VoiceConnectionInfo로 반환하고 null이면 응답 해석 오류. 별도 재시도 없음; 네트워크/취소 예외는 호출자에게 전달한다.

## `polrob.Client/Voice/VoiceChatException.cs`

[소스](../../polrob.Client/Voice/VoiceChatException.cs#L3). **역할:** 사용자에게 보여 줄 수 있는 음성 오류를 구분하는 Exception 하위 타입. 추가 상태·처리 없음.

- **`VoiceChatException(string message)`**: base(message).
- **`VoiceChatException(string message,Exception innerException)`**: base(message,innerException). 예컨대 명령 timeout의 원래 예외를 보존한다.

`GamePlay.GetVoiceErrorMessage`는 이 타입일 때 메시지를 직접 표시하며 일반 예외와 구분한다.

## `polrob.Client/Voice/VoiceParticipantState.cs`

[소스](../../polrob.Client/Voice/VoiceParticipantState.cs#L4). **역할:** SDK 타입을 화면에 노출하지 않는 값 타입 모음.

- **`VoiceParticipantState(Identity,Name,IsLocal,IsSpeaking,HasMicrophoneTrack,IsMuted)`**: sealed record의 생성 인자가 읽기 상태다. 별도 메서드 구현 없음. 어댑터가 JS snapshot을 이 형태로 바꾸고 GamePlay가 Player.Id와 Identity로 결합한다. IsMuted는 로컬이면 자기 마이크, 원격이면 이 기기의 듣기 설정을 뜻한다.
- **`VoiceConnectionStateChangedEventArgs(state,message=null)`**: 인자를 읽기 전용 State/Message에 저장. 연결 상태 이벤트 payload.
- **`VoiceConnectionState` enum**: Disconnected, Connecting, Connected, Reconnecting, Warning, Error. 경고/오류 전달이 항상 물리 연결 종료를 뜻하지는 않는다.

## `polrob.Client/Voice/TeamVoiceMemberViewModel.cs`

[소스](../../polrob.Client/Voice/TeamVoiceMemberViewModel.cs#L8). **역할:** 게임 팀원과 음성 상태를 결합한 한 행의 변경 알림 객체. **필드:** display name, local/speaking/muted/voiceConnected/busy, slot number/color. Identity는 생성 후 고정.

- **`TeamVoiceMemberViewModel(identity,displayName)`**: 두 초기 값을 보관. 네트워크 요청은 하지 않는다.
- **각 속성 setter**: SetField로 동일 값이면 변경 알림을 생략한다. IsLocal 변경 시 DisplayNameWithSelf/StatusText/MuteGlyph, IsMuted 변경 시 StatusText/MuteGlyph, IsVoiceConnected와 IsBusy 변경 시 StatusText/RowOpacity도 알린다. IsMuted/IsBusy는 화면에서 바꿀 수 있고 나머지 setter는 private이다.
- **파생 속성**: DisplayNameWithSelf는 자기 이름 뒤 “나”; MuteGlyph는 mute/local 조합; StatusText는 busy→미연결→local/remote mute 순서로 문구 선택; RowOpacity는 busy와 연결 여부로 결정. 값 계산만 하며 외부 상태 변경 없음.
- **[`Update(displayName,slotNumber,slotColor,isLocal,isSpeaking,isMuted,isVoiceConnected)`](../../polrob.Client/Voice/TeamVoiceMemberViewModel.cs#L120)**: 각 속성에 입력값을 차례대로 넣고 DisplayNameWithSelf 변경을 명시 알림. GamePlay.RefreshTeamVoiceRoster가 호출. 이름만 바뀌어도 파생 이름 표시가 갱신된다.
- **`SetField<T>(ref field,value,[CallerMemberName] propertyName)` → bool**: EqualityComparer로 같으면 false. 다르면 필드 대입→OnPropertyChanged→true. setter가 파생 알림 필요 여부를 판단한다.
- **`OnPropertyChanged(propertyName)`**: 이벤트 구독자에게 통지. 스스로 UI thread 전환하지 않으므로 호출자인 roster가 UI thread에서 실행한다.

## `polrob.Client/Voice/HybridWebViewVoiceRoomClient.cs`

[소스](../../polrob.Client/Voice/HybridWebViewVoiceRoomClient.cs#L11). **역할:** IVoiceRoomClient를 JSON 메시지 기반 C#↔JavaScript bridge로 구현한다. LiveKit SDK 호출은 voice-room.js가 수행한다.

**필드/상태:** `_webView`; JS ready 완료 `_webViewReady`; requestId별 `_pendingCommands`; 참가자 snapshot 배열과 읽기/교체 lock; `_preferredLocalMicrophoneMuted`, `_preferredRemotePlaybackVolume`; `_bridgeGeneration`; `_disposed`. 공개 IsConnected/IsLocalMicrophoneMuted는 bridge 이벤트에 따라 갱신되고 Participants는 lock 안에서 현재 배열 참조를 반환한다.

### 공개 메서드

- **`HybridWebViewVoiceRoomClient(HybridWebView webView)`**: null이면 ArgumentNullException, 아니면 WebViewInitializing/RawMessageReceived 구독. 볼륨 선호 필드는 객체 생성 시 GameSettings.SoundVolume으로 초기화된다.
- **[`ConnectAsync(VoiceConnectionInfo connectionInfo,CancellationToken)`](../../polrob.Client/Voice/HybridWebViewVoiceRoomClient.cs#L55)**: disposed 검사, info null 검사, 주소/참가토큰 공백이면 VoiceChatException. 이미 IsConnected면 return. Connecting 이벤트→OS microphone 권한 확인→권한 있고 선호 mute=false일 때만 enableMicrophone=true→로컬 mute 표시 갱신→SendCommandAsync("connect", url/token/enableMicrophone/playbackVolume). 여기서 직접 IsConnected=true로 만들지 않고 JS connection 이벤트를 기다린다. 기본 선호 mute=false라 권한이 있으면 처음에 마이크 켜기를 요청한다.
- **[`SetLocalMicrophoneMutedAsync(bool muted,token)`](../../polrob.Client/Voice/HybridWebViewVoiceRoomClient.cs#L94)**: disposed 검사. 켜려는데 OS 권한이 없으면 VoiceChatException. setLocalMuted 명령 성공 후에만 선호 값과 공개 IsLocalMicrophoneMuted를 갱신한다. 권한 팝업을 여기서 요청하지 않는다.
- **`SetRemotePlaybackMutedAsync(identity,muted,token)`**: disposed/공백 identity 검사 → setRemoteMuted 명령 Task 반환. 상대 존재·연결 여부의 최종 검사는 JS가 한다. mute 집합 자체는 JS에 있다.
- **`SetRemotePlaybackVolumeAsync(volume,token)`**: disposed 검사→선호 볼륨을0..1로 clamp. 미연결이면 저장만 하고 return, 연결되었으면 setPlaybackVolume 명령. 이후 connect에도 선호 값을 전달한다.
- **[`DisconnectAsync(token)`](../../polrob.Client/Voice/HybridWebViewVoiceRoomClient.cs#L153)**: disposed이며 미연결이면 return. ready Task가 완료된 상태이면 연결된 cleanup CTS를 만들어5초 제한으로 disconnect 명령. 내부5초 취소는 로그로 끝내고, 외부 caller가 취소한 OperationCanceledException은 전파한다. 다른 정리 예외는 로그로 처리. 마지막에 SetDisconnectedState. 외부 취소 예외로 나가면 마지막 로컬 정리까지 도달하지 않을 수 있다.
- **[`DisposeAsync()`](../../polrob.Client/Voice/HybridWebViewVoiceRoomClient.cs#L183)**: 이미 disposed면 return→DisconnectAsync→disposed=true→두 WebView 이벤트 해제→진행 명령에 ObjectDisposedException을 설정하고 사전 clear. 이후 재연결용 객체로 사용하지 않는다.

### `SendCommandAsync(commandType, values, cancellationToken)`

[선언](../../polrob.Client/Voice/HybridWebViewVoiceRoomClient.cs#L202). 모든 공개 명령이 사용하는 RPC 유사 함수다.

1. 현재 generation/ready completion을 캡처하고 준비 메시지를 최대20초 기다린다.
2. generation이 달라졌으면 “다시 준비 중” 예외. GUID requestId와 RunContinuationsAsynchronously completion을 pending dictionary에 등록한다.
3. `type`, `requestId`, values를 한 dictionary로 조합해 web JSON 형식으로 직렬화한다.
4. generation을 다시 확인하고 MainThread에서 WebView.SendRawMessage(json).
5. 해당 commandResult를 최대30초 기다린다. Success=false면 전달된 Error 또는 기본 오류로 VoiceChatException.
6. 명령 전송/결과 대기 try에서 생긴 TimeoutException은 VoiceChatException으로 감싸며 finally에서 pending ID를 제거한다.

**ready 대기는 이 try보다 앞에 있다.** 따라서20초 준비 timeout과30초 명령 결과 timeout이 모두 같은 catch를 통과하는 구조는 아니다. 취소가 C#의 await를 끝내도 이미 전송된 JS 작업을 임의로 되감지는 않는다. 별도 disconnect 명령과 generation 검사가 늦은 연결 완료를 다룬다.

### 준비·수신·상태 변환 메서드

| 메서드 | 입력 → 실제 처리 → 결과 |
|---|---|
| [`OnWebViewInitializing`](../../polrob.Client/Voice/HybridWebViewVoiceRoomClient.cs#L265) | 새 ready completion으로 교체, generation 증가, 이전 ready 취소, pending 명령들에 재생성 예외 설정 후 clear, SetDisconnectedState. 상위가 새 bridge 연결을 재시도하도록 알림. |
| `CreateReadySource()` | continuation을 비동기로 실행하는 TaskCompletionSource 반환. |
| `HasMicrophonePermissionAsync(token)` | 호출 전 취소 검사→Permissions.CheckStatusAsync<Microphone>→다시 취소 검사→Granted bool 반환. **RequestAsync 없음.** |
| [`OnRawMessageReceived(sender,args)`](../../polrob.Client/Voice/HybridWebViewVoiceRoomClient.cs#L297) | 빈 메시지/없는 type은 무시. JSON parse 후 ready/commandResult/participants/connection/warning/error로 분기. 잘못된 JSON의 JsonException은 로그. |
| `HandleCommandResult(root)` | string requestId가 있고 pending 항목이 있으면 success 값이 JSON true인지 확인하여 VoiceCommandResult를 completion에 설정. 모르는/이미 끝난 ID는 무시. |
| `HandleParticipants(root)` | participants 속성 없으면 종료; DTO 배열 변환→빈 identity 제외→빈 name이면 identity 사용→공개 record 배열 생성. lock에서 참가자 배열 교체와 로컬 mute를 로컬 행 기준으로 갱신→ParticipantsChanged. 로컬 행이 없으면 기존 로컬 mute 유지. |
| `HandleConnectionState(root)` | connecting/connected/reconnecting/error를 enum으로, 나머지는 Disconnected로 매핑. Connected 또는 Reconnecting이면 IsConnected=true. disconnected면 로컬 정리(중복 연결 이벤트 억제) 후 해당 상태 이벤트 발생. |
| `SetDisconnectedState(raiseConnectionEvent=true)` | IsConnected=false, 로컬 mute=true, lock으로 참가자 빈 배열; ParticipantsChanged; 옵션 true면 Disconnected 이벤트도 발생. 선호 mute/volume까지 초기화하지는 않음. |
| `TryGetString(element,propertyName)` | 속성이 JSON string일 때만 string 반환, 없거나 다른 타입이면 null. |

private `VoiceCommandResult` record는 요청 성공/오류만 담는다. `VoiceParticipantDto`는 JS의 camelCase identity/name/isLocal/isSpeaking/hasMicrophoneTrack/isMuted를 받는 역직렬화 전용 타입이다. SDK에 원격 음소거 상태를 저장하는 C# 모델이 아니다.

## `polrob.Client/Voice/VoiceWebViewPlatformConfiguration.cs`

[소스](../../polrob.Client/Voice/VoiceWebViewPlatformConfiguration.cs#L5). **역할:** HybridWebView의 native 마이크/재생 처리를 플랫폼 API에 연결. iOS delegate를 `_iOSUiDelegate`에 강하게 보관한다.

- **`Attach(HybridWebView webView)`**: HandlerChanged 이벤트 구독 후 즉시 Configure. GamePlay.InitializeTeamVoiceControls가 호출. 여러 번 호출하는 것을 자체로 dedup하지 않는다.
- **`Detach(webView)`**: HandlerChanged 구독 해제. native delegate/client를 원상복구하는 코드나 dispose까지 수행하지 않는다. 현재 GamePlay 정리에서 호출하지 않는다.
- **`OnHandlerChanged(sender,args)`**: sender가 HybridWebView일 때 Configure. native handler 교체 후에도 설정을 다시 적용한다.
- **[`Configure(webView)`](../../polrob.Client/Voice/VoiceWebViewPlatformConfiguration.cs#L30)**: Android native WebView이면 재생 제스처 요구를 끄고 VoiceWebChromeClient를 설치한다. iOS WKWebView이면 VoiceWebViewUiDelegate를 만들어 UIDelegate로 설정. 지원 branch가 아니거나 handler가 아직 없으면 동작 없음.
- **`VoiceWebChromeClient.OnPermissionRequest(request)`**: null이면 return. 요청 resource가 비어 있지 않고 모두 AudioCapture인지, origin이 `https://0.0.0.1`인지, OS RecordAudio 권한이 Granted인지 확인. 모두 만족해야 audio만 Grant, 그 외 Deny. 카메라를 함께 요청하면 전체를 허용하지 않는다.
- **`VoiceWebViewUiDelegate.RequestMediaCapturePermission(webView,origin,frame,type,decisionHandler)`**: origin protocol=app/host=0.0.0.1이고 type=Microphone일 때 Grant callback, 그 외 Deny. OS 권한 요청 자체는 GameSettings가 별도 수행한다.

## `polrob.Client/GameOver.xaml.cs`

[소스](../../polrob.Client/GameOver.xaml.cs#L18). **역할:** 서버 종료 상태에서 전달받은 결과 표시, 커스텀 방 유지/로비 복귀, 랜덤 재매칭.

**상태:** room/code/role/type/host, winner/remaining/captured/total; `_hubConnection`은 결과 화면의 커스텀 방 presence; `_autoReplayCancellation`은5초 자동 복귀; `_isNavigating`은 중복 이동 억제; `_appliedLayoutDensity`는 화면 배치 계산 중복 억제.

**Query setter:** 문자열은 null→빈 문자열; role/winner는 enum 파싱이 되면 변경; host는 bool parse; 세 통계 정수는 ParseNonNegativeInt. 이 화면은 통계를 DB에서 조회하거나 승자를 자체 판정하지 않고 GamePlay가 전달한 값을 받는다.

### 표시·사용자 진입점

| 메서드 | 입력 → 처리 → 결과/경계 |
|---|---|
| `GameOver()` | XAML 초기화. |
| `OnSizeAllocated(width,height)` | 크기가0 이하이면 종료. 높이/폭 기준 density 결정; 이전과 같으면 종료, 다르면 저장 후 ApplyLayoutDensity. 순수 표현 처리. |
| [`OnAppearing()`](../../polrob.Client/GameOver.xaml.cs#L122) | 세션 읽기→인증/결과 표시. custom이고 room/user ID가 있으면 자동 복귀 countdown 시작 후 StartRoomPresenceAsync. |
| [`OnHomeClicked`](../../polrob.Client/GameOver.xaml.cs#L138) | 이동 중이면 return→플래그 true→countdown 취소. custom이면 DisconnectRoomPresenceAsync(true); 실패시 플래그false/countdown 재시작/알림 후 남음. 성공 또는 random이면 홈 이동. |
| `OnProfileClicked` | Profile 탐색. 그 결과 OnDisappearing에서 countdown/presence 정리. |
| [`OnPlayAgainClicked`](../../polrob.Client/GameOver.xaml.cs#L168) | 이동 중이면 종료. AuthSession Load/로그인 검사. random이면 같은 역할 GameMatching, 그 외 NavigateToCustomLobbyAsync. random branch 자체는 `_isNavigating=true`를 세우지 않음. |
| `UpdateAuthHeader` | 현재 세션 이름/로그인 표시. |
| `UpdateResultDisplay` | winner role로 승리 표시 선택, FormatTime과 min(captured,total)로 결과 수치 표현. |
| `ApplyLayoutDensity(density)` / `SetStatsRowHeights(rowHeight)` | 여백·폰트·행 높이 조정. 네트워크/게임 결과 변경 없음. |
| `FormatTime(int totalSeconds)` | 음수→0, 분/초 두 자리 문자열 반환. |
| `ParseNonNegativeInt(string? value)` | int parse 실패→0; 성공→max(0,value). query 입력 방어. |
| `ReadErrorMessageAsync(response)` | 응답 본문 또는 상태 코드 기본 문구 반환. |

### 방 복귀와 presence

**[`NavigateToCustomLobbyAsync()`](../../polrob.Client/GameOver.xaml.cs#L286)**: 이동 중이면 return, roomId 없음이면 알림 후 종료. try에서 이동 flag true/countdown 취소 → 인증된 임시 HTTP POST `game/reset-room` with ResetRoomRequest(roomId,role). 401이면 로컬 세션 삭제, presence 구독 해제(false), 안내 후 Login 이동. 기타 HTTP/응답 오류면 안내하고 이동 flag false. 성공은 응답 room/code/role과 HostUserId 비교로 새 query를 만든 뒤 presence false 정리와 GameLobby 탐색. 요청/일반 예외도 안내 후 flag false. 실패 때 자동 countdown을 무조건 재시작하는 함수는 아니다.

**[`StartRoomPresenceAsync()`](../../polrob.Client/GameOver.xaml.cs#L359)**: 필드 연결이 있으면 return. WebSockets/skip negotiation/토큰 provider/자동 재연결 Hub를 로컬 변수에 생성한다. RoomStatusUpdated 성공이면 HostUserId를 비교해 host 갱신. Reconnected는 JoinRoom. StartAsync→JoinRoom 성공 후에야 필드에 저장한다. 실패면 해당 연결 Dispose; 오류는 밖으로 올리지 않아 HTTP reset-room 복귀 경로를 계속 사용할 수 있다.

- **`StartCustomReplayCountdown()`**: 이전 timer 취소 → 새 CTS → ReturnToCustomLobbyAfterDelayAsync를 비동기로 시작.
- **`ReturnToCustomLobbyAfterDelayAsync(token)`**:5초 delay(token)→NavigateToCustomLobbyAsync. OperationCanceledException은 정상 종료로 삼킴. delay 이후 HTTP reset 요청에는 이 countdown token을 직접 전달하지 않는다.
- **`CancelCustomReplayCountdown()`**: CTS 필드를 null로 분리→이전 CTS Cancel/Dispose. 여러 종료 경로에서 호출 가능.

**[`DisconnectRoomPresenceAsync(bool removePlayer)`](../../polrob.Client/GameOver.xaml.cs#L437) → bool**: 연결 없으면 `!removePlayer` 반환. 즉 구독 정리는 이미 끝났다고 볼 수 있지만 플레이어 제거 확인은 성공이라 하지 않는다. 연결 필드를 null로 분리하고 Connected이면 removePlayer=true에 대해 `CancelMatchingWithAcknowledgement` 성공을 요구한다. 실패 응답/미연결/제거 중 예외는 연결 필드를 복구하고 false. removePlayer=false이면 LeaveRoom을 호출한다. 정상 종료는 Dispose→true. false 정리 경로의 catch에서도 Dispose를 시도하고 true를 반환하지만 그 Dispose 자체의 추가 예외를 다시 catch하는 구조는 아니다.

**[`OnDisappearing()`](../../polrob.Client/GameOver.xaml.cs#L488)**: countdown 취소, Hub 필드를 분리, 남은 연결 DisposeAsync를 기다리지 않고 시작, base 호출. 정상 홈/로비 이동은 앞에서 명시 처리했고 그 외 이탈은 서버 disconnect grace 정리에 맡긴다. `ResetRoomRequest`는 RoomId/Role record, `GameOverLayoutDensity` enum은 표현 전용이다.

## `polrob.Client/Profile.xaml.cs`

[소스](../../polrob.Client/Profile.xaml.cs#L8). **역할:** 현재 사용자 전적의 안전한 조회와 로그아웃. **상태:** 재사용 HttpClient, 활성 요청 CTS, 요청 세대 `_statsRequestVersion`, 페이지 표시 여부 `_isProfileVisible`.

- **`Profile()`**: XAML 초기화.
- **[`OnAppearing()`](../../polrob.Client/Profile.xaml.cs#L23)**: visible=true→세션 Load→그 사이 사라졌으면 종료→로그인 안 됐으면 홈→이름/ID 표시→LoadGameStatsAsync.
- **`OnDisappearing()`**: visible=false→CancelStatsLoad→base. 늦은 응답이 화면을 덮지 못하게 한다.
- **`OnBackClicked`**: 조회 취소 후 상위 탐색. **`OnLogoutClicked`**: 조회 취소→AuthSession.LogoutAsync→홈. **`OnStatsRefreshClicked`**: 새 LoadGameStatsAsync.

### `LoadGameStatsAsync()`

[선언](../../polrob.Client/Profile.xaml.cs#L72). 기존 요청 취소 → 현재 token/user 캡처 → 화면 비활성/정보 공백이면 종료 → version 증가 →15초 CTS를 필드에 저장 → loading 표현.

SendStatsRequestAsync 후 IsCurrentStatsRequest를 검사한다. 응답401이면 세션 삭제/오류/알림/Login. 기타 실패 HTTP는 오류 문구. 성공 JSON을 PlayerGameStats로 읽고 **다시** current 검사한다. null이면 응답 오류, 아니면 ShowStats. 통신·시간 초과·일반 예외도 현재 요청일 때만 오류 표시. finally는 자신이 아직 필드의 CTS일 때만 필드를 null로 하고 캡처 CTS Dispose.

예를 들어 A 사용자 요청 중 B로 로그인해도 captured token/user와 현재 세션이 다르면 그 응답을 버린다. 취소와 별도로 버전/참조/identity를 검사하므로 완료가 거의 동시에 도착하는 경합도 걸러낸다.

### 조회·상태 보조 메서드

- **[`SendStatsRequestAsync(string sessionToken,CancellationToken)`](../../polrob.Client/Profile.xaml.cs#L158) → HttpResponseMessage**: 최대3회. 각 시도에 새 GET `game-records/me/stats` 메시지를 만들고 그 메시지에 캡처한 Bearer 설정→SendAsync. 마지막 시도이거나 비일시 상태이면 response 반환(호출자가 Dispose). 일시 실패면 RetryAfter.Delta 또는300×attempt ms를100..3000ms에 제한→실패 response Dispose→delay. HttpRequestException도 마지막 전까지 delay/retry. 취소는 retry로 삼키지 않는다.
- **`IsTransient(HttpStatusCode)` → bool**: RequestTimeout(408),TooManyRequests(429),500/502/503/504일 때 true. 401이나 모든4xx를 재시도하지 않는다.
- **[`IsCurrentStatsRequest(version,cancellation,sessionToken,userId)`](../../polrob.Client/Profile.xaml.cs#L201) → bool**: visible, 현재 버전 동일, CTS 참조 동일, 세션 토큰/사용자 동일을 모두 검사. CTS의 IsCancellationRequested만으로 판단하지 않는다.
- **`CancelStatsLoad()`**: version 증가→Interlocked.Exchange로 CTS 분리→Cancel. 이전 LoadGameStatsAsync의 finally가 Dispose를 책임진다.
- **`SetStatsLoading()`**: 전적 grid 숨김, loading/문구 표시, refresh 비활성.
- **`ShowStats(PlayerGameStats stats)`**: 전체·경찰·도둑 breakdown을 SetBreakdownLabels로 출력, loading 숨김, refresh 활성.
- **`ShowStatsError(string message)`**: loading/전적 숨김, 오류 표시, refresh 활성. 기존 값으로 성공처럼 표시하지 않는다.
- **[`SetBreakdownLabels(GameStatsBreakdown? breakdown,Label recordLabel,Label winRateLabel)`](../../polrob.Client/Profile.xaml.cs#L255)**: null→빈 breakdown, 총 경기수 max0, 승/패 각각0..total clamp, 승률 유한성/0..100 검사→전적/백분율 문자열. DB 기록으로 통계를 재집계하는 함수는 아니다.

## `polrob.Client/IMapRenderer.cs`

[소스](../../polrob.Client/IMapRenderer.cs#L6). **역할:** GamePlay와 맵 미리보기 도구가 같은 맵 renderer를 호출하기 위한 계약. 가변 필드·직접 구현 없음.

- **`DrawBackground(SKCanvas canvas,SKRect visible)`**: 주어진 세계 좌표 가시 영역에 바닥을 그린다. 반환값 없음.
- **`DrawProps(canvas,visible,PointF? viewer=null)`**: prop을 그린다. viewer는 로컬 위치에 따른 가림 투명도 처리 입력이며 구현별로 사용 여부가 다르다.
- **`DrawCollisionOverlay(canvas,GameMap map)`**: 공통 collision 데이터의 표시. 플레이어 충돌을 계산/수정하는 계약이 아니다.
- **상속 `Dispose()`**: renderer가 생성한 native image/paint/path 해제. 원본 bitmap 소유권과 구분한다.

구현은 TownMapRenderer/ClassicTownMapRenderer이며 GamePlay.CreateTownMapRenderer가 맵 ID에 따라 선택한다.

## `polrob.Client/TownMapRenderer.cs`

[소스](../../polrob.Client/TownMapRenderer.cs#L7). **역할:** ChaseTownLayout 타일/prop을 실제 게임과 preview에서 그린다. **필드:** 자산명→SKImage/source rectangle, 타일명→SKPaint(shader), 재사용 sprite/stroke/fill paint, prop 하단Y 정렬 배열, prop별 가림 영역 `_fadeAreas`. 공개 정적 `TileAssets`는 grass/road/paving 경로다.

- **[`TownMapRenderer(IReadOnlyDictionary<string,SKBitmap?> bitmaps)`](../../polrob.Client/TownMapRenderer.cs#L21)**: Placements의 첫 occlusion/hiding region을 대응 prop과 연결한다. ground3종 paint를 기본색으로 만들고 타일 bitmap이 있으면 world TileSize에 맞춘 반복 shader를 설정한다. 타일이 아닌 non-null bitmap은 전체 원본 영역과 SKImage로 변환해 저장한다. bitmap 픽셀 크기에서 world prop 크기나 collider를 만들지 않는다.
- **[`DrawBackground(canvas,visible)`](../../polrob.Client/TownMapRenderer.cs#L55)**: world clip → visible을 tile index 범위로 변환하고 맵 경계 clamp → Ground[x,y]에 맞는 타일 사각형 그림. 도로이면 지역함수 `Land(cx,cy)`로4이웃의 유효한 비도로 여부를 확인해 땅과 접한 경계에만 curb를 그린다. canvas restore. 바깥 tile index를 직접 읽지 않게 제한한다.
- **[`DrawProps(canvas,visible,viewer=null)`](../../polrob.Client/TownMapRenderer.cs#L140)**: world clip 후 정렬 prop 순회. 폭/높이≤0 또는 이미지 없음이면 skip, destination이 visible과 안 겹쳐도 skip. viewer가 fadeAreas 안에 있으면 sprite paint alpha140, 아니면255로 설정→DrawImage→restore. 같은 paint를 재사용하므로 프레임별 값도 명시적으로 다시 설정한다.
- **[`DrawCollisionOverlay(canvas,map)`](../../polrob.Client/TownMapRenderer.cs#L158)**: BlocksMovement 건물은 CollisionPolygon으로 path 생성, 장애물은 Circle이면 타원(EffectiveRadiusY), Polygon이면 점 목록 path, 그 외 rect. 임시 path는 using. overlay를 그릴 뿐 map/player 상태를 바꾸지 않는다.
- **`Intersects(SKRect a,SKRect b)`**: 경계 포함 축정렬 overlap bool. DrawProps culling에 사용.
- **[`Dispose()`](../../polrob.Client/TownMapRenderer.cs#L199)**: 저장된 SKImage, tile shader/paint, 공통 paint 해제. 생성자로 받은 bitmap dictionary의 원본 비트맵을 직접 Dispose하지 않는다. 별도 disposed flag로 중복 호출을 방어하는 로직은 없다.

**남아 있는 경로 helper:** 현 타일 기반 DrawBackground에서는 아래를 호출하지 않는다.

- **`DrawTexturedStroke(canvas,path,tile,width)`**: 타일 paint의 style/폭/join/cap을 stroke로 바꿔 경로를 그리고 style을 Fill로 복구. 해당 클래스 내 현재 호출 없음.
- **`CreatePavedBlocks(roadArea,width,height)` → SKPath**: world rect에서 road area difference → contour를 순회하며 world edge에 닿는 땅은 제외 → 내부 contour만 새 blocks path에 추가해 반환. difference 실패면 InvalidOperationException. 반환 path는 호출자 소유. 현재 생성자에서 사용하지 않는다.
- **`MergeAreas(surfaces,trails)` → SKPath**: SVG surface와 `(Path,Width)` trail을 parse; trail은 stroke를 면으로 변환; OpBuilder union으로 하나의 path 반환. Resolve 실패면 결과 Dispose 후 InvalidOperationException. builder/중간 path/paint는 using. 이 클래스의 현재 실행 경로에서 호출되지 않는다.

## `polrob.Client/ClassicTownMapRenderer.cs`

[소스](../../polrob.Client/ClassicTownMapRenderer.cs#L7). **역할:** CanvaMapLayout의 곡선 도로·구획·prop을 그린다. **필드:** SVG에서 parse한 `_roads`, 도로 union `_roadArea`, 내부 포장 `_pavedBlocks`, tile paint와 prop image/source 사전, 공통 paint·dash, 정렬 prop 배열.

- **[`ClassicTownMapRenderer(bitmaps,getSourceBounds)`](../../polrob.Client/ClassicTownMapRenderer.cs#L24)**: 필드 초기화에서 도로 paths와 roadArea를 생성한 뒤 CreatePavedBlocks로 내부 블록 생성. grass/asphalt/paving paint에 bitmap 반복 shader를 연결하며 paving은 별도 repeat size. 나머지 bitmap은 callback이 반환한 source bounds와 SKImage로 저장한다. callback은 GamePlay에서 GeneratedAssetBounds.GetMap을 연결한다.
- **[`DrawBackground(canvas,visible)`](../../polrob.Client/ClassicTownMapRenderer.cs#L54)**: world clip→visible 잔디→포장 blocks→전체 도로를 layer별 stroke로 그림→DrawTexturedStroke 아스팔트→Marked 도로 dash→각 Crosswalk→restore. 모든 도로 path를 순회하며 prop처럼 도로별 bounds skip을 따로 하지 않는다.
- **`DrawTexturedStroke(canvas,path,tile,width)`**: 공유 tile paint를 임시 stroke로 사용한 뒤 Fill로 되돌린다. DrawBackground가 호출한다.
- **[`CreatePavedBlocks(roadArea,width,height)`](../../polrob.Client/ClassicTownMapRenderer.cs#L93)**: world−road의 contour 중 세계 edge에 붙지 않은 영역만 새 SKPath로 반환. 생성자에서 한 번 계산하여 매 frame 반복하지 않는다. difference null이면 오류.
- **[`MergeAreas(surfaces,trails)`](../../polrob.Client/ClassicTownMapRenderer.cs#L115)**: SVG 경로 및 폭 있는 trail 면을 union. 임시 자원 정리, 결과 path 반환; Resolve 실패 시 결과도 정리하고 오류. `_roadArea` 필드 초기화에서 사용한다.
- **[`DrawProps(canvas,visible,viewer=null)`](../../polrob.Client/ClassicTownMapRenderer.cs#L141)**: 유효 크기/이미지 존재/visible overlap을 검사하고 source rect→논리 destination으로 이미지를 그린다. `viewer` 인자는 이 구현에서 사용하지 않는다. prop은 하단Y 기준 순서다.
- **`DrawCollisionOverlay(canvas,map)`**: 이동을 막는 건물은 GameMap.GetBuildingCollisionBounds rect로, obstacle은 타원/polygon/rect로 그린다. Town renderer의 건물 polygon 표시와 구현이 다르다.
- **`DrawCrosswalk(canvas,x,y,verticalRoad)`**: roadArea로 clip해 중앙선을 아스팔트 patch로 덮고 위치/회전 후 줄무늬를 그림. canvas state 복원. DrawBackground에서 호출.
- **`Intersects(a,b)`**: prop의 축정렬 overlap 검사.
- **[`Dispose()`](../../polrob.Client/ClassicTownMapRenderer.cs#L210)**: 각 road, pavedBlocks, roadArea, image, shader/paint, 공통 paint와 dash 해제. 원본 bitmap은 호출자에 남는다. 결과 path를 own하는 객체가 이 renderer다.

## `polrob.Client/ExactMapTileCache.cs`

[소스](../../polrob.Client/ExactMapTileCache.cs#L16). **역할:** 512px×10열×15행의 base/foreground 이미지 쌍을 필요한 위치만 디코딩하는 캐시. **현재 작성 소스에서 다른 생성/호출자를 찾을 수 없다.** GamePlay는 위 IMapRenderer 구현 둘을 사용한다.

**상태:** TileKey(row,column)→CachedTilePair(base,foreground,LRU node); pending Task 사전; 실패 key 집합; LRU linked list; `_sync`; 동시 load 제한2인 semaphore; 완료 callback; disposed. 최대 보관은36쌍. `ExactMapTileLayer`는 Base/Foreground 선택 enum이다.

| 메서드 | 구체 입력·알고리즘·반환/부작용 |
|---|---|
| `ExactMapTileCache(Action tileLoaded)` | callback 보관. 파일을 아직 읽지 않음. |
| [`PreloadAroundAsync(float worldX,float worldY)`](../../polrob.Client/ExactMapTileCache.cs#L41) | 좌표를 tile index로 floor/clamp→중심 좌우2/상하3 범위에서 유효 key 최대35개→EnsureLoadedAsync 목록→Task.WhenAll 반환. 개별 load 실패가 내부에서 삼켜지므로 정상 Task 완료가 모든 이미지 존재를 보장하지 않음. |
| `QueueVisible(SKRect bounds)` | EnumerateTileKeys 범위마다 EnsureLoadedAsync를 기다리지 않고 호출. |
| [`DrawLayer(canvas,layer,bounds)`](../../polrob.Client/ExactMapTileCache.cs#L68) | 먼저 visible 요청 예약→임시 paint→sync lock 안에서 로딩된 key만 LRU touch→선택 layer bitmap을512 world rect로 그림. 미완료 타일은 skip. Dispose/eviction과 DrawBitmap이 같은 lock을 써 native 사용 중 해제를 막음. |
| [`EnsureLoadedAsync(TileKey key)`](../../polrob.Client/ExactMapTileCache.cs#L100) | lock에서 disposed면 완료Task; cached면 touch 후 완료Task; failed면 완료Task; pending이면 기존Task. 나머지는 새 completion을 pending에 등록하고 Task.Run으로 LoadPairAsync 시작 후 그 Task 반환. 중복 로드를 합침. |
| [`LoadPairAsync(key,completion)`](../../polrob.Client/ExactMapTileCache.cs#L132) | load gate 대기→base/foreground를 동시에 읽고 둘 다 await→gate 반환. null decode면 오류. sync lock에서 미폐기/미등록이면 둘을 캐시에 소유권 이전하고 LRU trim. 오류는 failed 집합/로그. finally에서 로컬에 남은 bitmap Dispose, pending 제거, completion은 성공 완료, 추가 성공이면 callback 호출. |
| `LoadBitmapAsync(assetPath)` | 패키지 stream 열기→SKBitmap.Decode 반환; stream은 using. 예외는 LoadPairAsync가 처리. |
| `TouchLocked(CachedTilePair pair)` | LRU node를 현재 위치에서 제거하고 맨 뒤로 이동. caller가 sync를 소유한 상태에서 호출. |
| `TrimLocked()` | count>36 동안 맨 앞 key 제거와 pair의 두 bitmap Dispose. 최근 읽기/그리기가 후순위 eviction이 되게 함. |
| `TilePath(layer,key)` → string | layer에 맞는 `exact_map/base` 또는 `foreground` 아래 `map_행두자리_열두자리.png` 경로 생성. |
| `EnumerateTileKeys(bounds)` → IEnumerable | 세계와 아예 안 겹치면 yield break. 좌상단 floor/clamp, 우하단은 최대 world와0.001 여유로 경계 타일 중복 방지→row/column 순서 yield. |
| [`Dispose()`](../../polrob.Client/ExactMapTileCache.cs#L255) | sync lock, 이미 폐기면 return. disposed=true→현재 pair 모두 Dispose→cache/LRU/failed clear. 진행 worker를 취소하거나 join하지는 않음. 늦은 worker는 disposed 검사를 통해 새 cache 등록을 하지 않는다. |

callback은 worker에서 호출되므로 UI invalidate를 연결하려면 호출자가 UI thread 전환을 해야 한다. 실패 key는 같은 캐시 객체에서 다시 요청해도 재로딩하지 않는다. 배경 타일과 foreground를 하나의 pair로 다루는 것은 서로 다른 좌표/수명으로 엇갈리지 않게 하려는 구조다.

## `polrob.Client/GameSettings.cs`

[소스](../../polrob.Client/GameSettings.cs#L9). **역할:** 기기별 소리·진동 설정과 마이크 권한 API 접근을 모은다. **상태:** Preferences key3개와 최초 요청 semaphore. 서버/계정별 설정 저장소가 아니다.

- **`SoundVolume` getter/setter**: Preferences 기본값1.0을 읽거나 전달값을 기록할 때0..1 clamp. HybridWebViewVoiceRoomClient가 생성될 때 읽는다. 값 변경 이벤트는 없다.
- **`VibrationEnabled` getter/setter**: 기본 true인 bool 읽기/기록. GamePlay는 진동 update마다 읽는다.
- **`GetMicrophonePermissionStatusAsync()`**: Permissions.CheckStatusAsync<Microphone>의 Task<PermissionStatus> 그대로 반환. 시스템 요청 창을 띄우지 않는다.
- **`RequestMicrophonePermissionAsync()`**: Permissions.RequestAsync<Microphone> 전달. Settings와 최초 실행 함수가 호출한다.
- **[`RequestMicrophonePermissionOnFirstLaunchAsync()`](../../polrob.Client/GameSettings.cs#L38)**: semaphore 대기→Preferences의 이미 질문함 값이 true면 종료. **질문 전에** 그 값을 true로 저장→권한 상태 확인→Granted가 아니면 요청. 오류는 debug 로그로 삼켜 앱 시작을 계속한다. finally에서 semaphore release. MainPage.OnAppearing가 호출하고 같은 설치에서 반복 질문을 줄인다. 질문 중 앱이 종료되어도 다음 실행마다 자동 재질문하지 않는다.

## `polrob.Client/Settings.xaml.cs`

[소스](../../polrob.Client/Settings.xaml.cs#L5). **역할:** 설정 컨트롤을 저장 값과 OS 권한 변경에 연결. **상태:** 저장값을 컨트롤에 채우는 중 `_isLoadingSettings`, 권한 중복 요청 방지 `_isRequestingMicrophonePermission`.

| 메서드 | 실제 입력 → 처리 → 결과/경계 |
|---|---|
| `Settings()` | XAML→LoadStoredSettings. |
| `OnAppearing()` | 다시 저장값 로드→현재 OS 마이크 상태 갱신. 시스템 설정에서 돌아왔을 때도 최신 상태를 읽음. |
| `LoadStoredSettings()` | loading flag true→volume slider/문구/vibration switch 설정→false. 컨트롤 변경 event가 다시 저장하는 것을 막음. |
| `OnSoundVolumeChanged(sender,ValueChangedEventArgs e)` | NewValue를0..1 제한하고 문구 갱신. loading이 아닐 때만 GameSettings.SoundVolume 기록. 활성 음성 서비스의 SetRemotePlaybackVolumeAsync를 직접 호출하지 않음. |
| `OnVibrationToggled(sender,ToggledEventArgs e)` | loading 아니면 e.Value를 GameSettings.VibrationEnabled로 기록. |
| [`OnMicrophonePermissionClicked`](../../polrob.Client/Settings.xaml.cs#L50) | 이미 요청 중이면 return. flag와 버튼 상태 설정→현재 상태 조회→Granted 아니면 시스템 요청→여전히 거절이면 설정 열기 여부를 사용자에게 물음→승인 시 AppInfo.ShowSettingsUI. 예외는 로그/알림. finally 요청 flag 해제와 RefreshMicrophonePermissionAsync. |
| [`RefreshMicrophonePermissionAsync()`](../../polrob.Client/Settings.xaml.cs#L95) | OS 상태→표시 문구/버튼 이름/가능 여부. Granted면 요청 버튼 비활성. 조회 예외면 상태 확인 불가와 설정 열기 문구를 보여 주지만 click handler 자체는 같은 권한 요청 흐름이다. |
| `OnBackClicked` | 한 단계 이전 화면으로 이동. |
| `FormatVolume(double)` | clamp0..1→100곱→반올림→백분율 문자열 반환. |
| `GetPermissionStatusText(PermissionStatus)` | Denied/Disabled/Restricted/Limited를 각각 문구로, 나머지는 미요청 문구로 변환. 정책 판단이나 권한 변경 없음. |

## `polrob.Client/ProximityHaptics.cs`

[소스](../../polrob.Client/ProximityHaptics.cs#L13). **역할:** 요청한 길이의 진동을 플랫폼별로 재생하고 멈춘다. iOS에만 engine/player 필드가 존재한다. 근접 거리를 계산하지 않으며 GamePlay가 서버 pulse를 입력으로 준다.

- **`PlayPulse(TimeSpan duration)`**: iOS→PlayIosPulse, 그 외→Vibration.Default.Vibrate(duration). 모든 일반 예외는 debug log로 바꾸고 호출자에게 전파하지 않는다.
- **`Stop()`**: iOS player가 있으면 Stop(0) 후 참조 null; 다른 플랫폼은 Vibration.Cancel. 오류는 로그. engine은 재사용을 위해 여기서 Dispose하지 않는다.
- **[`PlayIosPulse(duration)`](../../polrob.Client/ProximityHaptics.cs#L57)**: hardware SupportsHaptics 아니면 종료→이전 pulse Stop→없으면 CreateIosEngine→engine 없으면 종료→engine Start, 실패하면 로그/종료. intensity1/sharpness0.45인 continuous event를 입력 duration초로 만들고 pattern→player→Start. 각 API의 error/result를 확인하고 실패시 로그/return. 이는 MAUI iOS 기본 고정 진동 길이를 대신하는 구현이다.
- **`CreateIosEngine()` → CHHapticEngine?**: AutoShutdownEnabled/PlaysHapticsOnly engine 생성. 오류 없으면 반환, 있으면 로그·engine.Dispose·null.
- **`Dispose()`**: Stop 후 iOS engine Stop/Dispose/null. GamePlay는 현재 일반 정리에서 Stop을 호출하며 이 Dispose까지 호출하는 경로는 없다.

## `polrob.Client/GameStartTiming.cs`

[소스](../../polrob.Client/GameStartTiming.cs#L6). **역할:** 한 프로세스의 게임 시작 구간을 측정하는 정적 도구. **현재 작성 소스에 이 타입을 호출하는 다른 코드가 없다.** 클래스 존재만으로 로그가 자동 발생하지 않는다.

**필드:** `Sync` lock, `_trace` Stopwatch, 이전 checkpoint timestamp, 짧은 `_traceId`.

| 메서드 | 입력 → 처리 → 반환/부작용 |
|---|---|
| `BeginTrace(string message)` | lock→기존 trace를 새 Stopwatch로 교체→checkpoint를 현재 monotonic timestamp로→GUID8자리→WriteCore(message,0,0). 새 추적을 강제로 시작. |
| `EnsureTraceStarted(message)` | lock→trace 있으면 return. 없을 때만 위 초기화. 기존 측정의 시작 시간을 보존. |
| `StartSegment()` → long | Stopwatch.GetTimestamp 반환. 외부가 시작값을 보관해야 함. |
| `CompleteSegment(message,long startedAt)` | Stopwatch.GetElapsedTime(startedAt)의 ms를 계산해 Write로 전달. 전체 trace와 별도 구간 길이를 기록. |
| `Mark(message)` | 구간 길이 null로 Write 호출. |
| `Write(message,double? segmentMilliseconds)` | lock, trace 없으면 지연 생성; 이전 checkpoint~현재 경과 계산과 checkpoint 갱신→WriteCore. |
| `WriteCore(message,segmentMilliseconds,sinceLastMilliseconds)` | trace ID/전체 경과/이전점 이후/선택 구간 길이를 한 문자열로 만들어 Debug와 Console에 각각 출력. 시간 측정은 DateTime 대신 Stopwatch를 사용. |

## `polrob.Client/Platforms/Android/MainActivity.cs`

[소스](../../polrob.Client/Platforms/Android/MainActivity.cs#L8). **역할:** Android Activity 진입점. MauiAppCompatActivity를 상속하며 작성된 메서드·필드 본문은 없다. Activity attribute가 launcher/theme/launch mode/configuration 변경 처리를 지정한다. 실제 앱 구성은 MainApplication→MauiProgram이 담당한다. 게임/로그인 로직 없음.

## `polrob.Client/Platforms/Android/MainApplication.cs`

[소스](../../polrob.Client/Platforms/Android/MainApplication.cs#L7). **역할:** Android 프로세스의 MAUI application 연결. 별도 업무 상태 없음.

- **`MainApplication(IntPtr handle,JniHandleOwnership ownership)`**: Android runtime이 넘긴 native handle 소유권을 base constructor로 전달한다.
- **`CreateMauiApp()`**: MauiProgram.CreateMauiApp 결과 반환. Android에서 다른 게임 서버나 다른 규칙을 생성하지 않는다.

## `polrob.Client/Platforms/iOS/Program.cs`

[소스](../../polrob.Client/Platforms/iOS/Program.cs#L6). **역할:** iOS executable의 진입점. 별도 필드 없음.

- **`Main(string[] args)`**: OS 실행 인자를 UIApplication.Main(args,null,typeof(AppDelegate))에 전달하여 UIKit application event loop를 시작한다. 사용자 게임 입력을 처리하는 loop는 이 파일에 없다.

## `polrob.Client/Platforms/iOS/AppDelegate.cs`

[소스](../../polrob.Client/Platforms/iOS/AppDelegate.cs#L6). **역할:** `[Register("AppDelegate")]`로 native 진입에 연결되는 MAUI delegate.

- **`CreateMauiApp()`**: MauiProgram.CreateMauiApp을 반환한다. OS 마이크/게임 재개 처리는 이 delegate override에 들어 있지 않고 GameSettings/GamePlay의 Window event 쪽에 있다.

## `polrob.Client/Platforms/MacCatalyst/Program.cs`

[소스](../../polrob.Client/Platforms/MacCatalyst/Program.cs#L6). **역할:** Mac Catalyst executable의 진입점. 별도 상태 없음.

- **`Main(string[] args)`**: UIApplication.Main(args,null,typeof(AppDelegate)) 호출. 구조가 iOS와 같지만 해당 플랫폼 target으로 컴파일되는 별도 파일이다.

## `polrob.Client/Platforms/MacCatalyst/AppDelegate.cs`

[소스](../../polrob.Client/Platforms/MacCatalyst/AppDelegate.cs#L6). **역할:** Catalyst MAUI app 생성 delegate.

- **`CreateMauiApp()`**: 공통 MauiProgram.CreateMauiApp으로 위임. 독립 업무 상태·재접속 함수 없음.

## `polrob.Client/Platforms/Windows/App.xaml.cs`

[소스](../../polrob.Client/Platforms/Windows/App.xaml.cs#L11). **역할:** `polrob.Client.WinUI` namespace의 Windows MAUI host. 루트 `polrob.Client.App`과 다른 타입이다.

- **`App()`**: WinUI 쪽 InitializeComponent 호출.
- **`CreateMauiApp()`**: 공통 MauiProgram.CreateMauiApp 반환. 게임 화면 생성과 네트워크 동작은 공통 클라이언트에 위임한다.

## 참고: C#에서 호출하는 JavaScript와 생성 소스

`HybridWebViewVoiceRoomClient.SendCommandAsync`의 반대편은 [voice-room.js](../../polrob.Client/Resources/Raw/voice/voice-room.js#L361)의 `handleCommand`다. type별로 connect/setLocalMuted/setRemoteMuted/setPlaybackVolume/disconnect를 실행하고 같은 requestId로 commandResult를 반환한다. 일반 명령은 Promise queue에 넣고 disconnect는 즉시 실행한다. SDK 참가자/트랙 이벤트는 `participants`, `connection`, `warning`, `error` 메시지로 되돌아온다. C# 메서드 설명의 “명령 성공”은 이 응답을 받은 상태를 말한다. JS 세부 함수는 [클라이언트 흐름 문서의 음성 절](04-client.md#477-voice-roomjs-실제-음성-sdk를-움직이는-코드)에 설명되어 있다.

`GeneratedAssetBounds`는 직접 작성된 Client C# 목록에 없지만 GamePlay/ClassicTownMapRenderer 입력에 쓰인다. 빌드 도구가 `obj/.../generated/GeneratedAssetBounds.g.cs`에 생성한다. `GetCharacter`, `GetMap`은 준비된 비트맵의 투명 여백을 제외한 source rectangle을 제공하며, 런타임 게임 규칙이나 collider를 결정하지 않는다.
