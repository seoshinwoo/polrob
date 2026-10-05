# 부록. 전체 소스 파일 색인

[전체 안내로 돌아가기](../code-guide.md) · [이전: 테스트와 개발 도구](06-tests-and-tools.md)

이 색인은 설명 범위를 파일 단위로 확인하기 위한 목록이다. 소스 링크는 실제 파일로, 설명 위치는 해당 상세 장으로 이동한다. **파일 안의 주요 메서드를 바로 찾으려면 [파일별·메서드별 참고서](file-reference.md)를 연다.**

기준은 작성 시점 Git에 등록된 `.cs`, `.js`, `.py`, `.sh`, `.swift` 소스다. 앱·서버·공용 코드·테스트·개발 도구·실행 스크립트 **149개**를 아래에 나열했다. 그 밖에 음성 WebView의 `index.html`도 실행 연결점으로 포함했다. 생성 파일·외부 라이브러리와 일회성 작업 파일은 끝의 제외 표에서 구분한다.

소스 파일이 이 표에 있다는 사실과 현재 앱 실행에서 호출된다는 사실은 다르다. 사용되지 않는 helper, 과거 맵 데이터, 빈 파일도 별도로 밝혀 두었다.

## 설명 범위 요약

| 구역 | 코드 파일 수 | 설명 위치 |
|---|---:|---|
| 서버 | 38 | 1·3·5장 |
| 공용 모델·맵 | 22 | 2장 |
| 클라이언트 | 40 (C# 39 + JavaScript 1) | 4장 |
| 테스트 봇 | 7 | 6장 |
| 서버 테스트 | 21 | 6장 |
| 개발 도구 | 13 | 6장 |
| 루트 실행 스크립트 | 8 | 6장 |
| 합계 | 149 | 아래 개별 목록 |

## 서버의 시작·Controller·Hub·서비스

| 소스 파일 | 설명 위치 | 범위 |
|---|---|---|
| [polrob.Server/Controllers/AuthController.cs](../../polrob.Server/Controllers/AuthController.cs) | [1장: 서버 API·방·인증](01-server-application.md) | 상세 설명 |
| [polrob.Server/Controllers/GameController.cs](../../polrob.Server/Controllers/GameController.cs) | [1장: 서버 API·방·인증](01-server-application.md) | 상세 설명 |
| [polrob.Server/Controllers/GameRecordsController.cs](../../polrob.Server/Controllers/GameRecordsController.cs) | [5장: 기록·운영](05-records-operations.md) | 상세 설명 |
| [polrob.Server/Controllers/VoiceController.cs](../../polrob.Server/Controllers/VoiceController.cs) | [1장: 서버 API·방·인증](01-server-application.md) | 상세 설명 |
| [polrob.Server/Hubs/GameRoomHub.cs](../../polrob.Server/Hubs/GameRoomHub.cs) | [1장: 서버 API·방·인증](01-server-application.md) | 상세 설명 |
| [polrob.Server/Program.cs](../../polrob.Server/Program.cs) | [1장: 서버 API·방·인증](01-server-application.md) | 상세 설명 |
| [polrob.Server/Services/BotIdentityService.cs](../../polrob.Server/Services/BotIdentityService.cs) | [1장: 서버 API·방·인증](01-server-application.md) | 상세 설명 |
| [polrob.Server/Services/CosmosDbOptions.cs](../../polrob.Server/Services/CosmosDbOptions.cs) | [1장: 서버 API·방·인증](01-server-application.md) | 설정 전달용 자료형: 역할 설명, 값별 설정법 생략 |
| [polrob.Server/Services/GameRecordDbService.cs](../../polrob.Server/Services/GameRecordDbService.cs) | [5장: 기록·운영](05-records-operations.md) | 상세 설명 |
| [polrob.Server/Services/GameRecordOutbox.cs](../../polrob.Server/Services/GameRecordOutbox.cs) | [5장: 기록·운영](05-records-operations.md) | 상세 설명 |
| [polrob.Server/Services/GameRecordReconciler.cs](../../polrob.Server/Services/GameRecordReconciler.cs) | [5장: 기록·운영](05-records-operations.md) | 상세 설명 |
| [polrob.Server/Services/GameRecordStatsCalculator.cs](../../polrob.Server/Services/GameRecordStatsCalculator.cs) | [5장: 기록·운영](05-records-operations.md) | 상세 설명 |
| [polrob.Server/Services/GameRecordWriter.cs](../../polrob.Server/Services/GameRecordWriter.cs) | [5장: 기록·운영](05-records-operations.md) | 상세 설명 |
| [polrob.Server/Services/GameRoomService.cs](../../polrob.Server/Services/GameRoomService.cs) | [1장: 서버 API·방·인증](01-server-application.md) | 상세 설명 |
| [polrob.Server/Services/IGameRecordQueue.cs](../../polrob.Server/Services/IGameRecordQueue.cs) | [5장: 기록·운영](05-records-operations.md) | 상세 설명 |
| [polrob.Server/Services/IGameRecordStore.cs](../../polrob.Server/Services/IGameRecordStore.cs) | [5장: 기록·운영](05-records-operations.md) | 상세 설명 |
| [polrob.Server/Services/LiveKitOptions.cs](../../polrob.Server/Services/LiveKitOptions.cs) | [1장: 서버 API·방·인증](01-server-application.md) | 설정 전달용 자료형: 역할 설명, 값별 설정법 생략 |
| [polrob.Server/Services/LiveKitRoomAdminService.cs](../../polrob.Server/Services/LiveKitRoomAdminService.cs) | [1장: 서버 API·방·인증](01-server-application.md) | 상세 설명 |
| [polrob.Server/Services/LiveKitTokenService.cs](../../polrob.Server/Services/LiveKitTokenService.cs) | [1장: 서버 API·방·인증](01-server-application.md) | 상세 설명 |
| [polrob.Server/Services/UserDbService.cs](../../polrob.Server/Services/UserDbService.cs) | [1장: 서버 API·방·인증](01-server-application.md) | 상세 설명 |

## 서버 실시간 네트워크

| 소스 파일 | 설명 위치 | 범위 |
|---|---|---|
| [polrob.Server/Network/ActiveGameParticipantRegistry.cs](../../polrob.Server/Network/ActiveGameParticipantRegistry.cs) | [3장: 실시간 서버](03-server-network.md) | 상세 설명 |
| [polrob.Server/Network/GameNetworkServer.Lifecycle.cs](../../polrob.Server/Network/GameNetworkServer.Lifecycle.cs) | [3장: 실시간 서버](03-server-network.md) | 상세 설명 |
| [polrob.Server/Network/GameNetworkServer.Observability.cs](../../polrob.Server/Network/GameNetworkServer.Observability.cs) | [3장: 실시간 서버](03-server-network.md) | 상세 설명 |
| [polrob.Server/Network/GameNetworkServer.RoomLoop.cs](../../polrob.Server/Network/GameNetworkServer.RoomLoop.cs) | [3장: 실시간 서버](03-server-network.md) | 상세 설명 |
| [polrob.Server/Network/GameNetworkServer.Transport.cs](../../polrob.Server/Network/GameNetworkServer.Transport.cs) | [3장: 실시간 서버](03-server-network.md) | 상세 설명 |
| [polrob.Server/Network/GameNetworkServer.cs](../../polrob.Server/Network/GameNetworkServer.cs) | [3장: 실시간 서버](03-server-network.md) | 상세 설명 |
| [polrob.Server/Network/GameSession.cs](../../polrob.Server/Network/GameSession.cs) | [3장: 실시간 서버](03-server-network.md) | 상세 설명 |
| [polrob.Server/Network/RuntimeMetricSampler.cs](../../polrob.Server/Network/RuntimeMetricSampler.cs) | [3장: 실시간 서버](03-server-network.md) | 상세 설명 |
| [polrob.Server/Network/TcpPeer.cs](../../polrob.Server/Network/TcpPeer.cs) | [3장: 실시간 서버](03-server-network.md) | 상세 설명 |
| [polrob.Server/Network/UdpRateLimitState.cs](../../polrob.Server/Network/UdpRateLimitState.cs) | [3장: 실시간 서버](03-server-network.md) | 상세 설명 |

## 서버 운영·기록 모델

| 소스 파일 | 설명 위치 | 범위 |
|---|---|---|
| [polrob.Server/Models/CompletedGameRecord.cs](../../polrob.Server/Models/CompletedGameRecord.cs) | [5장: 기록·운영](05-records-operations.md) | 상세 설명 |
| [polrob.Server/Models/PlayerGameOutcome.cs](../../polrob.Server/Models/PlayerGameOutcome.cs) | [5장: 기록·운영](05-records-operations.md) | 상세 설명 |
| [polrob.Server/Operations/GameHubFilter.cs](../../polrob.Server/Operations/GameHubFilter.cs) | [5장: 기록·운영](05-records-operations.md) | 상세 설명 |
| [polrob.Server/Operations/OperationalMetrics.cs](../../polrob.Server/Operations/OperationalMetrics.cs) | [5장: 기록·운영](05-records-operations.md) | 상세 설명 |
| [polrob.Server/Operations/OperationsEndpoints.cs](../../polrob.Server/Operations/OperationsEndpoints.cs) | [5장: 기록·운영](05-records-operations.md) | 상세 설명 |
| [polrob.Server/Operations/RestartPolicyService.cs](../../polrob.Server/Operations/RestartPolicyService.cs) | [5장: 기록·운영](05-records-operations.md) | 상세 설명 |
| [polrob.Server/Operations/ServerAdmission.cs](../../polrob.Server/Operations/ServerAdmission.cs) | [5장: 기록·운영](05-records-operations.md) | 상세 설명 |
| [polrob.Server/Operations/ServerOperations.cs](../../polrob.Server/Operations/ServerOperations.cs) | [5장: 기록·운영](05-records-operations.md) | 상세 설명 |

## 공용 모델·맵

| 소스 파일 | 설명 위치 | 범위 |
|---|---|---|
| [polrob.Shared/Models/CanvaMapCollisions.cs](../../polrob.Shared/Models/CanvaMapCollisions.cs) | [2장: 공용 모델·맵](02-shared-world.md) | 상세 설명 |
| [polrob.Shared/Models/CanvaMapLayout.cs](../../polrob.Shared/Models/CanvaMapLayout.cs) | [2장: 공용 모델·맵](02-shared-world.md) | 상세 설명 |
| [polrob.Shared/Models/ChaseTownAssetCatalog.cs](../../polrob.Shared/Models/ChaseTownAssetCatalog.cs) | [2장: 공용 모델·맵](02-shared-world.md) | 상세 설명 |
| [polrob.Shared/Models/ChaseTownLayout.cs](../../polrob.Shared/Models/ChaseTownLayout.cs) | [2장: 공용 모델·맵](02-shared-world.md) | 상세 설명 |
| [polrob.Shared/Models/Game.cs](../../polrob.Shared/Models/Game.cs) | [2장: 공용 모델·맵](02-shared-world.md) | 상세 설명 |
| [polrob.Shared/Models/GameJoinRequest.cs](../../polrob.Shared/Models/GameJoinRequest.cs) | [2장: 공용 모델·맵](02-shared-world.md) | 상세 설명 |
| [polrob.Shared/Models/GamePhase.cs](../../polrob.Shared/Models/GamePhase.cs) | [2장: 공용 모델·맵](02-shared-world.md) | 상세 설명 |
| [polrob.Shared/Models/GameStateSync.cs](../../polrob.Shared/Models/GameStateSync.cs) | [2장: 공용 모델·맵](02-shared-world.md) | 상세 설명 |
| [polrob.Shared/Models/JailBreakProgressSync.cs](../../polrob.Shared/Models/JailBreakProgressSync.cs) | [2장: 공용 모델·맵](02-shared-world.md) | 상세 설명 |
| [polrob.Shared/Models/JailBreakSync.cs](../../polrob.Shared/Models/JailBreakSync.cs) | [2장: 공용 모델·맵](02-shared-world.md) | 상세 설명 |
| [polrob.Shared/Models/LabelMeCollisionData.cs](../../polrob.Shared/Models/LabelMeCollisionData.cs) | [2장: 공용 모델·맵](02-shared-world.md) | 과거 맵 데이터와 현재 실행 경로의 관계 설명 |
| [polrob.Shared/Models/Map.cs](../../polrob.Shared/Models/Map.cs) | [2장: 공용 모델·맵](02-shared-world.md) | 상세 설명 |
| [polrob.Shared/Models/MapRegistry.cs](../../polrob.Shared/Models/MapRegistry.cs) | [2장: 공용 모델·맵](02-shared-world.md) | 상세 설명 |
| [polrob.Shared/Models/OpponentProximitySync.cs](../../polrob.Shared/Models/OpponentProximitySync.cs) | [2장: 공용 모델·맵](02-shared-world.md) | 상세 설명 |
| [polrob.Shared/Models/Player.cs](../../polrob.Shared/Models/Player.cs) | [2장: 공용 모델·맵](02-shared-world.md) | 상세 설명 |
| [polrob.Shared/Models/PlayerGameStats.cs](../../polrob.Shared/Models/PlayerGameStats.cs) | [2장: 공용 모델·맵](02-shared-world.md) | 상세 설명 |
| [polrob.Shared/Models/PlayerMovementInput.cs](../../polrob.Shared/Models/PlayerMovementInput.cs) | [2장: 공용 모델·맵](02-shared-world.md) | 상세 설명 |
| [polrob.Shared/Models/PlayerMovementSync.cs](../../polrob.Shared/Models/PlayerMovementSync.cs) | [2장: 공용 모델·맵](02-shared-world.md) | 상세 설명 |
| [polrob.Shared/Models/ServerResponse.cs](../../polrob.Shared/Models/ServerResponse.cs) | [2장: 공용 모델·맵](02-shared-world.md) | 상세 설명 |
| [polrob.Shared/Models/TcpMessageType.cs](../../polrob.Shared/Models/TcpMessageType.cs) | [2장: 공용 모델·맵](02-shared-world.md) | 상세 설명 |
| [polrob.Shared/Models/TownMapLayout.cs](../../polrob.Shared/Models/TownMapLayout.cs) | [2장: 공용 모델·맵](02-shared-world.md) | 과거 맵 데이터와 현재 실행 경로의 관계 설명 |
| [polrob.Shared/Models/VoiceChat.cs](../../polrob.Shared/Models/VoiceChat.cs) | [2장: 공용 모델·맵](02-shared-world.md) | 상세 설명 |

## 클라이언트

| 소스 파일 | 설명 위치 | 범위 |
|---|---|---|
| [polrob.Client/AndroidStartupSplashPage.xaml.cs](../../polrob.Client/AndroidStartupSplashPage.xaml.cs) | [4장: 클라이언트](04-client.md) | 앱 진입·수명 흐름 설명, UI·구성 세부 생략 |
| [polrob.Client/App.xaml.cs](../../polrob.Client/App.xaml.cs) | [4장: 클라이언트](04-client.md) | 앱 진입·수명 흐름 설명, UI·구성 세부 생략 |
| [polrob.Client/AppShell.xaml.cs](../../polrob.Client/AppShell.xaml.cs) | [4장: 클라이언트](04-client.md) | 앱 진입·수명 흐름 설명, UI·구성 세부 생략 |
| [polrob.Client/AuthSession.cs](../../polrob.Client/AuthSession.cs) | [4장: 클라이언트](04-client.md) | 상세 설명 |
| [polrob.Client/ClassicTownMapRenderer.cs](../../polrob.Client/ClassicTownMapRenderer.cs) | [4장: 클라이언트](04-client.md) | 동작·상태·자원 수명 설명, 화면 배치·장식 세부 생략 |
| [polrob.Client/ExactMapTileCache.cs](../../polrob.Client/ExactMapTileCache.cs) | [4장: 클라이언트](04-client.md) | 현재 호출되지 않는 구현도 내부 동작 설명 |
| [polrob.Client/GameCreate.xaml.cs](../../polrob.Client/GameCreate.xaml.cs) | [4장: 클라이언트](04-client.md) | 동작·상태·자원 수명 설명, 화면 배치·장식 세부 생략 |
| [polrob.Client/GameJoin.xaml.cs](../../polrob.Client/GameJoin.xaml.cs) | [4장: 클라이언트](04-client.md) | 동작·상태·자원 수명 설명, 화면 배치·장식 세부 생략 |
| [polrob.Client/GameLobby.xaml.cs](../../polrob.Client/GameLobby.xaml.cs) | [4장: 클라이언트](04-client.md) | 동작·상태·자원 수명 설명, 화면 배치·장식 세부 생략 |
| [polrob.Client/GameMatching.xaml.cs](../../polrob.Client/GameMatching.xaml.cs) | [4장: 클라이언트](04-client.md) | 동작·상태·자원 수명 설명, 화면 배치·장식 세부 생략 |
| [polrob.Client/GameOver.xaml.cs](../../polrob.Client/GameOver.xaml.cs) | [4장: 클라이언트](04-client.md) | 동작·상태·자원 수명 설명, 화면 배치·장식 세부 생략 |
| [polrob.Client/GamePlay.Voice.cs](../../polrob.Client/GamePlay.Voice.cs) | [4장: 클라이언트](04-client.md) | 상세 설명 |
| [polrob.Client/GamePlay.xaml.cs](../../polrob.Client/GamePlay.xaml.cs) | [4장: 클라이언트](04-client.md) | 동작·상태·자원 수명 설명, 화면 배치·장식 세부 생략 |
| [polrob.Client/GameSettings.cs](../../polrob.Client/GameSettings.cs) | [4장: 클라이언트](04-client.md) | 상세 설명 |
| [polrob.Client/GameStartTiming.cs](../../polrob.Client/GameStartTiming.cs) | [4장: 클라이언트](04-client.md) | 현재 호출되지 않는 구현도 내부 동작 설명 |
| [polrob.Client/IMapRenderer.cs](../../polrob.Client/IMapRenderer.cs) | [4장: 클라이언트](04-client.md) | 상세 설명 |
| [polrob.Client/Login.xaml.cs](../../polrob.Client/Login.xaml.cs) | [4장: 클라이언트](04-client.md) | 동작·상태·자원 수명 설명, 화면 배치·장식 세부 생략 |
| [polrob.Client/MainPage.xaml.cs](../../polrob.Client/MainPage.xaml.cs) | [4장: 클라이언트](04-client.md) | 동작·상태·자원 수명 설명, 화면 배치·장식 세부 생략 |
| [polrob.Client/MauiProgram.cs](../../polrob.Client/MauiProgram.cs) | [4장: 클라이언트](04-client.md) | 앱 진입·수명 흐름 설명, UI·구성 세부 생략 |
| [polrob.Client/Network/GameNetworkClient.cs](../../polrob.Client/Network/GameNetworkClient.cs) | [4장: 클라이언트](04-client.md) | 상세 설명 |
| [polrob.Client/Platforms/Android/MainActivity.cs](../../polrob.Client/Platforms/Android/MainActivity.cs) | [4장: 클라이언트](04-client.md) | 플랫폼 시작점: 연결 관계만 설명, 기본 구성 세부 생략 |
| [polrob.Client/Platforms/Android/MainApplication.cs](../../polrob.Client/Platforms/Android/MainApplication.cs) | [4장: 클라이언트](04-client.md) | 플랫폼 시작점: 연결 관계만 설명, 기본 구성 세부 생략 |
| [polrob.Client/Platforms/MacCatalyst/AppDelegate.cs](../../polrob.Client/Platforms/MacCatalyst/AppDelegate.cs) | [4장: 클라이언트](04-client.md) | 플랫폼 시작점: 연결 관계만 설명, 기본 구성 세부 생략 |
| [polrob.Client/Platforms/MacCatalyst/Program.cs](../../polrob.Client/Platforms/MacCatalyst/Program.cs) | [4장: 클라이언트](04-client.md) | 플랫폼 시작점: 연결 관계만 설명, 기본 구성 세부 생략 |
| [polrob.Client/Platforms/Windows/App.xaml.cs](../../polrob.Client/Platforms/Windows/App.xaml.cs) | [4장: 클라이언트](04-client.md) | 플랫폼 시작점: 연결 관계만 설명, 기본 구성 세부 생략 |
| [polrob.Client/Platforms/iOS/AppDelegate.cs](../../polrob.Client/Platforms/iOS/AppDelegate.cs) | [4장: 클라이언트](04-client.md) | 플랫폼 시작점: 연결 관계만 설명, 기본 구성 세부 생략 |
| [polrob.Client/Platforms/iOS/Program.cs](../../polrob.Client/Platforms/iOS/Program.cs) | [4장: 클라이언트](04-client.md) | 플랫폼 시작점: 연결 관계만 설명, 기본 구성 세부 생략 |
| [polrob.Client/Profile.xaml.cs](../../polrob.Client/Profile.xaml.cs) | [4장: 클라이언트](04-client.md) | 동작·상태·자원 수명 설명, 화면 배치·장식 세부 생략 |
| [polrob.Client/ProximityHaptics.cs](../../polrob.Client/ProximityHaptics.cs) | [4장: 클라이언트](04-client.md) | 상세 설명 |
| [polrob.Client/Resources/Raw/voice/voice-room.js](../../polrob.Client/Resources/Raw/voice/voice-room.js) | [4장: 클라이언트](04-client.md) | 상세 설명 |
| [polrob.Client/Settings.xaml.cs](../../polrob.Client/Settings.xaml.cs) | [4장: 클라이언트](04-client.md) | 동작·상태·자원 수명 설명, 화면 배치·장식 세부 생략 |
| [polrob.Client/TownMapRenderer.cs](../../polrob.Client/TownMapRenderer.cs) | [4장: 클라이언트](04-client.md) | 동작·상태·자원 수명 설명, 화면 배치·장식 세부 생략 |
| [polrob.Client/Voice/HybridWebViewVoiceRoomClient.cs](../../polrob.Client/Voice/HybridWebViewVoiceRoomClient.cs) | [4장: 클라이언트](04-client.md) | 상세 설명 |
| [polrob.Client/Voice/IVoiceRoomClient.cs](../../polrob.Client/Voice/IVoiceRoomClient.cs) | [4장: 클라이언트](04-client.md) | 상세 설명 |
| [polrob.Client/Voice/TeamVoiceMemberViewModel.cs](../../polrob.Client/Voice/TeamVoiceMemberViewModel.cs) | [4장: 클라이언트](04-client.md) | 동작·상태·자원 수명 설명, 화면 배치·장식 세부 생략 |
| [polrob.Client/Voice/VoiceChatException.cs](../../polrob.Client/Voice/VoiceChatException.cs) | [4장: 클라이언트](04-client.md) | 상세 설명 |
| [polrob.Client/Voice/VoiceChatService.cs](../../polrob.Client/Voice/VoiceChatService.cs) | [4장: 클라이언트](04-client.md) | 상세 설명 |
| [polrob.Client/Voice/VoiceParticipantState.cs](../../polrob.Client/Voice/VoiceParticipantState.cs) | [4장: 클라이언트](04-client.md) | 상세 설명 |
| [polrob.Client/Voice/VoiceTokenClient.cs](../../polrob.Client/Voice/VoiceTokenClient.cs) | [4장: 클라이언트](04-client.md) | 상세 설명 |
| [polrob.Client/Voice/VoiceWebViewPlatformConfiguration.cs](../../polrob.Client/Voice/VoiceWebViewPlatformConfiguration.cs) | [4장: 클라이언트](04-client.md) | 상세 설명 |

## 테스트 봇

| 소스 파일 | 설명 위치 | 범위 |
|---|---|---|
| [polrob.Test/BotClient.cs](../../polrob.Test/BotClient.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 상세 설명 |
| [polrob.Test/BotGameNetworkClient.cs](../../polrob.Test/BotGameNetworkClient.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 상세 설명 |
| [polrob.Test/BotMovementController.cs](../../polrob.Test/BotMovementController.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 상세 설명 |
| [polrob.Test/BotRunner.cs](../../polrob.Test/BotRunner.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 상세 설명 |
| [polrob.Test/BotService.cs](../../polrob.Test/BotService.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 내용 없는 파일임을 명시 |
| [polrob.Test/BotState.cs](../../polrob.Test/BotState.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 현재 사용되지 않는 enum을 명시 |
| [polrob.Test/Program.cs](../../polrob.Test/Program.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 상세 설명 |

## 서버 테스트

| 소스 파일 | 설명 위치 | 범위 |
|---|---|---|
| [polrob.Server.Tests/ActiveGameParticipantRegistryTests.cs](../../polrob.Server.Tests/ActiveGameParticipantRegistryTests.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 기존 테스트가 검증하는 동작과 범위 설명 |
| [polrob.Server.Tests/CanvaMapCollisionProfileTests.cs](../../polrob.Server.Tests/CanvaMapCollisionProfileTests.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 기존 테스트가 검증하는 동작과 범위 설명 |
| [polrob.Server.Tests/ChaseTownAssetCatalogTests.cs](../../polrob.Server.Tests/ChaseTownAssetCatalogTests.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 기존 테스트가 검증하는 동작과 범위 설명 |
| [polrob.Server.Tests/GameMovementValidationTests.cs](../../polrob.Server.Tests/GameMovementValidationTests.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 기존 테스트가 검증하는 동작과 범위 설명 |
| [polrob.Server.Tests/GameNetworkLifecycleTests.cs](../../polrob.Server.Tests/GameNetworkLifecycleTests.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 기존 테스트가 검증하는 동작과 범위 설명 |
| [polrob.Server.Tests/GameNetworkProtocolTests.cs](../../polrob.Server.Tests/GameNetworkProtocolTests.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 기존 테스트가 검증하는 동작과 범위 설명 |
| [polrob.Server.Tests/GameNetworkSocketTests.cs](../../polrob.Server.Tests/GameNetworkSocketTests.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 기존 테스트가 검증하는 동작과 범위 설명 |
| [polrob.Server.Tests/GameRecordOutboxTests.cs](../../polrob.Server.Tests/GameRecordOutboxTests.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 기존 테스트가 검증하는 동작과 범위 설명 |
| [polrob.Server.Tests/GameRecordStatsCalculatorTests.cs](../../polrob.Server.Tests/GameRecordStatsCalculatorTests.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 기존 테스트가 검증하는 동작과 범위 설명 |
| [polrob.Server.Tests/GameRoomHostAssignmentTests.cs](../../polrob.Server.Tests/GameRoomHostAssignmentTests.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 기존 테스트가 검증하는 동작과 범위 설명 |
| [polrob.Server.Tests/GameRoomLifecycleTests.cs](../../polrob.Server.Tests/GameRoomLifecycleTests.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 기존 테스트가 검증하는 동작과 범위 설명 |
| [polrob.Server.Tests/GameRoomSoakTests.cs](../../polrob.Server.Tests/GameRoomSoakTests.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 기존 테스트가 검증하는 동작과 범위 설명 |
| [polrob.Server.Tests/GameRoomVoiceAccessTests.cs](../../polrob.Server.Tests/GameRoomVoiceAccessTests.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 기존 테스트가 검증하는 동작과 범위 설명 |
| [polrob.Server.Tests/GameRuleTransitionTests.cs](../../polrob.Server.Tests/GameRuleTransitionTests.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 기존 테스트가 검증하는 동작과 범위 설명 |
| [polrob.Server.Tests/LiveKitTokenServiceTests.cs](../../polrob.Server.Tests/LiveKitTokenServiceTests.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 기존 테스트가 검증하는 동작과 범위 설명 |
| [polrob.Server.Tests/MapManagementTests.cs](../../polrob.Server.Tests/MapManagementTests.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 기존 테스트가 검증하는 동작과 범위 설명 |
| [polrob.Server.Tests/MovementAuthenticationTests.cs](../../polrob.Server.Tests/MovementAuthenticationTests.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 기존 테스트가 검증하는 동작과 범위 설명 |
| [polrob.Server.Tests/NetworkBackpressureTests.cs](../../polrob.Server.Tests/NetworkBackpressureTests.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 기존 테스트가 검증하는 동작과 범위 설명 |
| [polrob.Server.Tests/OperationsEndpointTests.cs](../../polrob.Server.Tests/OperationsEndpointTests.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 기존 테스트가 검증하는 동작과 범위 설명 |
| [polrob.Server.Tests/TownMapPhysicsTests.cs](../../polrob.Server.Tests/TownMapPhysicsTests.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 기존 테스트가 검증하는 동작과 범위 설명 |
| [polrob.Server.Tests/VoiceControllerTests.cs](../../polrob.Server.Tests/VoiceControllerTests.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 기존 테스트가 검증하는 동작과 범위 설명 |

## 개발 도구

| 소스 파일 | 설명 위치 | 범위 |
|---|---|---|
| [tools/PolRob.AssetBounds/Program.cs](../../tools/PolRob.AssetBounds/Program.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 개발 도구의 알고리즘·입출력·사용 관계 설명; 미술 구현 세부 요약 |
| [tools/PolRob.CharacterAnimation/Program.cs](../../tools/PolRob.CharacterAnimation/Program.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 개발 도구의 알고리즘·입출력·사용 관계 설명; 미술 구현 세부 요약 |
| [tools/PolRob.CharacterAnimation/make-preview.swift](../../tools/PolRob.CharacterAnimation/make-preview.swift) | [6장: 테스트·도구](06-tests-and-tools.md) | 개발 도구의 알고리즘·입출력·사용 관계 설명; 미술 구현 세부 요약 |
| [tools/PolRob.ChaseTownAssets/AssetPackBuilder.cs](../../tools/PolRob.ChaseTownAssets/AssetPackBuilder.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 개발 도구의 알고리즘·입출력·사용 관계 설명; 미술 구현 세부 요약 |
| [tools/PolRob.ChaseTownAssets/HighResolutionAssets.cs](../../tools/PolRob.ChaseTownAssets/HighResolutionAssets.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 개발 도구의 알고리즘·입출력·사용 관계 설명; 미술 구현 세부 요약 |
| [tools/PolRob.ChaseTownAssets/Program.cs](../../tools/PolRob.ChaseTownAssets/Program.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 개발 도구의 알고리즘·입출력·사용 관계 설명; 미술 구현 세부 요약 |
| [tools/PolRob.MapAssetBuilder/Program.cs](../../tools/PolRob.MapAssetBuilder/Program.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 개발 도구의 알고리즘·입출력·사용 관계 설명; 미술 구현 세부 요약 |
| [tools/PolRob.SpriteEdges/Program.cs](../../tools/PolRob.SpriteEdges/Program.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 개발 도구의 알고리즘·입출력·사용 관계 설명; 미술 구현 세부 요약 |
| [tools/PolRob.TownMapPreview/ArchivedTownMapPreview.cs](../../tools/PolRob.TownMapPreview/ArchivedTownMapPreview.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 개발 도구의 알고리즘·입출력·사용 관계 설명; 미술 구현 세부 요약 |
| [tools/PolRob.TownMapPreview/CollisionProfilePreview.cs](../../tools/PolRob.TownMapPreview/CollisionProfilePreview.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 개발 도구의 알고리즘·입출력·사용 관계 설명; 미술 구현 세부 요약 |
| [tools/PolRob.TownMapPreview/PreviewAssetAnalysis.cs](../../tools/PolRob.TownMapPreview/PreviewAssetAnalysis.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 개발 도구의 알고리즘·입출력·사용 관계 설명; 미술 구현 세부 요약 |
| [tools/PolRob.TownMapPreview/Program.cs](../../tools/PolRob.TownMapPreview/Program.cs) | [6장: 테스트·도구](06-tests-and-tools.md) | 개발 도구의 알고리즘·입출력·사용 관계 설명; 미술 구현 세부 요약 |
| [tools/rebuild_robber_run_sprites.py](../../tools/rebuild_robber_run_sprites.py) | [6장: 테스트·도구](06-tests-and-tools.md) | 개발 도구의 알고리즘·입출력·사용 관계 설명; 미술 구현 세부 요약 |

## 실행 스크립트

| 소스 파일 | 설명 위치 | 범위 |
|---|---|---|
| [run_2_sims.sh](../../run_2_sims.sh) | [6장: 테스트·도구](06-tests-and-tools.md) | 실행 흐름·역할·주의점 설명; 환경 설정값 나열 생략 |
| [run_3_sims.sh](../../run_3_sims.sh) | [6장: 테스트·도구](06-tests-and-tools.md) | 실행 흐름·역할·주의점 설명; 환경 설정값 나열 생략 |
| [run_4_sims.sh](../../run_4_sims.sh) | [6장: 테스트·도구](06-tests-and-tools.md) | 실행 흐름·역할·주의점 설명; 환경 설정값 나열 생략 |
| [run_8_sims.sh](../../run_8_sims.sh) | [6장: 테스트·도구](06-tests-and-tools.md) | 실행 흐름·역할·주의점 설명; 환경 설정값 나열 생략 |
| [run_all.sh](../../run_all.sh) | [6장: 테스트·도구](06-tests-and-tools.md) | 실행 흐름·역할·주의점 설명; 환경 설정값 나열 생략 |
| [run_both.sh](../../run_both.sh) | [6장: 테스트·도구](06-tests-and-tools.md) | 실행 흐름·역할·주의점 설명; 환경 설정값 나열 생략 |
| [run_ios_both.sh](../../run_ios_both.sh) | [6장: 테스트·도구](06-tests-and-tools.md) | 실행 흐름·역할·주의점 설명; 환경 설정값 나열 생략 |
| [run_load_metrics.sh](../../run_load_metrics.sh) | [6장: 테스트·도구](06-tests-and-tools.md) | 실행 흐름·역할·주의점 설명; 환경 설정값 나열 생략 |

## 추가 실행 연결점

| 파일 | 설명 위치 | 역할 |
|---|---|---|
| [voice/index.html](../../polrob.Client/Resources/Raw/voice/index.html) | [4장](04-client.md) | LiveKit SDK·HybridWebView bridge·음성 JS를 로드하고 audio 요소를 수용하는 호스트 |

## 상세 분석에서 제외한 파일과 이유

| 대상 | 이유와 문서에서 다루는 범위 |
|---|---|
| `*.xaml`, 색상·스타일 리소스, SVG·PNG·폰트 | 화면 배치와 미술 자산. 해당 화면의 C# 동작은 위 목록과 4장에 포함한다. |
| `*.csproj`, `polrob.slnx`, `appsettings*.json`, manifest, 배포·CI 설정 | 설정 파일 자체를 줄별로 해설하지 않는다. 프로젝트 참조, 실행 수명, 코드 동작을 이해하는 데 필요한 등록은 설명한다. |
| `bin/`, `obj/`, 외부 패키지 | 빌드 생성물·외부 구현. 직접 작성한 런타임 코드로 세지 않는다. |
| [docs/NetworkCodeReview.cs](../../docs/NetworkCodeReview.cs) | 기존 네트워크 코드를 설명하기 위한 과거 사본. 현재 빌드 소스와 다르며 6장에서 구분한다. |
| [output/map-concepts-2d-2026-10-01/compose-character-preview.swift](../../output/map-concepts-2d-2026-10-01/compose-character-preview.swift) | 일회성 지도 이미지·미리보기 또는 포트폴리오 문서 제작 코드. 게임 실행 경로에 포함되지 않아 상세 분석에서 제외한다. |
| [tmp/map_v4_preview.swift](../../tmp/map_v4_preview.swift) | 일회성 지도 이미지·미리보기 또는 포트폴리오 문서 제작 코드. 게임 실행 경로에 포함되지 않아 상세 분석에서 제외한다. |
| [tmp/map_v5_preview.swift](../../tmp/map_v5_preview.swift) | 일회성 지도 이미지·미리보기 또는 포트폴리오 문서 제작 코드. 게임 실행 경로에 포함되지 않아 상세 분석에서 제외한다. |
| [tmp/map_v5_references.swift](../../tmp/map_v5_references.swift) | 일회성 지도 이미지·미리보기 또는 포트폴리오 문서 제작 코드. 게임 실행 경로에 포함되지 않아 상세 분석에서 제외한다. |
| [tmp/map_v6_preview.swift](../../tmp/map_v6_preview.swift) | 일회성 지도 이미지·미리보기 또는 포트폴리오 문서 제작 코드. 게임 실행 경로에 포함되지 않아 상세 분석에서 제외한다. |
| [tmp/map_v7_preview.swift](../../tmp/map_v7_preview.swift) | 일회성 지도 이미지·미리보기 또는 포트폴리오 문서 제작 코드. 게임 실행 경로에 포함되지 않아 상세 분석에서 제외한다. |
| [tmp/pdfs/create_game_server_portfolio.py](../../tmp/pdfs/create_game_server_portfolio.py) | 일회성 지도 이미지·미리보기 또는 포트폴리오 문서 제작 코드. 게임 실행 경로에 포함되지 않아 상세 분석에서 제외한다. |

## 문서와 코드를 함께 갱신하는 방법

새 소스 파일이나 새로운 실행 경로를 추가하면 이 색인에 행을 추가하고 해당 장에 상태 소유자·입력·출력·호출 흐름·실패 처리를 설명한다. 기존 동작을 바꿀 때는 설명의 상수뿐 아니라 Mermaid 흐름과 다른 장의 연결 설명도 함께 확인한다. 코드 행 링크는 탐색 보조이며 리팩터링 후 위치가 바뀔 수 있다.
