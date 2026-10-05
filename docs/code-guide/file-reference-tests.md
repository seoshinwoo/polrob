# 파일별 메서드 사전: 서버 테스트 21개

[파일별 사전 목차](file-reference.md) · [테스트의 실행 방식과 배경](06-tests-and-tools.md)

이 장은 테스트 파일을 열었을 때 **어느 테스트 메서드가 어떤 구현 계약을 확인하는지** 바로 찾기 위한 목록이다. 모든 테스트는 NUnit이다. 아래의 `검증한다`는 표현은 해당 테스트 코드의 assert 범위를 뜻한다. 테스트 파일 이름만으로 서버 전체가 정상 작동한다고 추론하지 않는다. 반복되는 `CreateServer`, `SetField`, `Invoke`, `WaitUntil` 등은 실제 제품 기능이 아니라 테스트 상황을 만들거나 private 메서드를 호출하는 도우미다.

## ActiveGameParticipantRegistryTests.cs

[소스](../../polrob.Server.Tests/ActiveGameParticipantRegistryTests.cs#L1). **대상:** 현재 게임 연결 등록과 활동 lease.

- [`Unregister_FromReplacedConnection_DoesNotRemoveCurrentConnection`](../../polrob.Server.Tests/ActiveGameParticipantRegistryTests.cs#L9): 같은 방·사용자의 연결을 새 ID로 교체한 뒤 이전 ID의 해제 요청이 새 등록을 삭제하지 않는지 확인한다.
- [`Unregister_CurrentConnection_RemovesParticipant`](../../polrob.Server.Tests/ActiveGameParticipantRegistryTests.cs#L28): 현재 ID로 해제하면 등록이 없어지는지 확인한다.
- [`ActiveLease_ExpiresAndCurrentHeartbeatRestoresIt`](../../polrob.Server.Tests/ActiveGameParticipantRegistryTests.cs#L39): 가짜 시계를 45초 lease 이후로 보내 조회가 실패하게 하고, 현재 연결의 갱신으로 다시 유효해지는지 확인한다.
- [`Heartbeat_FromReplacedConnection_CannotRefreshCurrentLease`](../../polrob.Server.Tests/ActiveGameParticipantRegistryTests.cs#L57): 오래된 연결의 heartbeat가 새 연결의 활동 시간을 바꾸지 못하는지 확인한다.
- `ManualTimeProvider.GetUtcNow`, `Advance`: 실제 시간을 기다리지 않고 lease 만료 경계를 만드는 테스트 전용 시계다.

## CanvaMapCollisionProfileTests.cs

[소스](../../polrob.Server.Tests/CanvaMapCollisionProfileTests.cs#L1). **대상:** 기존 Canva 맵 이미지 좌표에서 실제 충돌 모양으로 바꾸는 계산.

- [`RectanglesStartAtOriginalBottomLeftIncludingTransparentMargins`](../../polrob.Server.Tests/CanvaMapCollisionProfileTests.cs#L17): 투명 여백을 제외한 잘린 이미지가 아니라 원본 이미지의 왼쪽 아래를 기준으로 사각형을 배치하는지 검사한다.
- [`CirclesUseTheOriginalCenterAndBothSpriteScaleFactors`](../../polrob.Server.Tests/CanvaMapCollisionProfileTests.cs#L53): 원본 중심과 X/Y 축 배율이 충돌 원·타원에 반영되는지 확인한다.
- [`CrownAndLampHeadAreOutsideTheBaseCollider`](../../polrob.Server.Tests/CanvaMapCollisionProfileTests.cs#L82): 나무·가로등의 위쪽 그림자/장식과 바닥에서 막히는 몸통의 범위를 구분한다.
- [`TracedBoxesKeepTheUpperRightNotchOpen`](../../polrob.Server.Tests/CanvaMapCollisionProfileTests.cs#L95): 외곽선을 직접 딴 상자 더미의 오른쪽 위 빈 부분을 지나갈 수 있어야 한다.
- [`PondBlocksWaterAndRimButNotTheImageCorners`](../../polrob.Server.Tests/CanvaMapCollisionProfileTests.cs#L112): 연못 물·테두리는 막고 PNG의 빈 네 모서리는 통과시킨다.
- [`NonUniformlyScaledCircleUsesTheEllipseEdgeNotAnExpandedRectangle`](../../polrob.Server.Tests/CanvaMapCollisionProfileTests.cs#L134): 축 배율이 다를 때 실제 타원 가장자리를 검사한다. 확장 사각형으로 근사하면 잘못 막히는 지점을 보호한다.

## ChaseTownAssetCatalogTests.cs

[소스](../../polrob.Server.Tests/ChaseTownAssetCatalogTests.cs#L1). **대상:** 기본 Chase 맵의 각 에셋 충돌·시야·감옥 영역.

- [`EveryProfileHasFiniteInBoundsGeometryAndUniqueIdentity`](../../polrob.Server.Tests/ChaseTownAssetCatalogTests.cs#L20): 모든 프로필의 ID가 고유하고 영역 좌표가 유한하며 에셋 논리 경계를 벗어나지 않는지 확인한다.
- [`CornerWallKeepsTheInsideNotchWalkable`](../../polrob.Server.Tests/ChaseTownAssetCatalogTests.cs#L56), [`EntranceShouldersDoNotBecomeInvisibleSolidCorners`](../../polrob.Server.Tests/ChaseTownAssetCatalogTests.cs#L104): 코너·입구 그림의 빈 부분이 보이지 않는 직사각형 벽으로 막히지 않도록 한다.
- [`TreeBlocksOnlyTrunkAndKeepsCanopyAnOcclusionRegion`](../../polrob.Server.Tests/ChaseTownAssetCatalogTests.cs#L65): 이동을 막는 줄기와 시야에 쓰는 수관 영역을 구분한다.
- [`BushIsAnEnterableHideTrigger`](../../polrob.Server.Tests/ChaseTownAssetCatalogTests.cs#L76): 수풀은 들어갈 수 있고 은신 영역으로 탐지돼야 한다.
- [`JailInteriorIsFreeAndGateActuallyControlsTheExit`](../../polrob.Server.Tests/ChaseTownAssetCatalogTests.cs#L87): 감옥 내부 수감 슬롯은 비어 있고 출구를 실제 문/창살 충돌 영역이 제어하는지 확인한다.
- [`PlacementMovesRotatesAndScalesTheColliderWithTheWholeSprite`](../../polrob.Server.Tests/ChaseTownAssetCatalogTests.cs#L110): 에셋의 이동·회전·배율이 물리 영역에도 같이 적용되는지 확인한다.
- [`BuildingRearAllowsPartialOcclusionWithoutMovingSidesOrFront`](../../polrob.Server.Tests/ChaseTownAssetCatalogTests.cs#L128), [`ActiveBuildingsUseTheInsetWhileNonBuildingsKeepTheirOriginalProfiles`](../../polrob.Server.Tests/ChaseTownAssetCatalogTests.cs#L169): 건물 뒤쪽 여백을 허용하는 inset이 의도한 건물에만 적용되고 옆·앞 충돌은 유지되는지 확인한다.
- [`InvalidScaleIsRejected`](../../polrob.Server.Tests/ChaseTownAssetCatalogTests.cs#L188): 사용할 수 없는 배율을 거부한다.
- [`HighResolutionTexturesDoNotChangeLogicalSizesOrPhysics`](../../polrob.Server.Tests/ChaseTownAssetCatalogTests.cs#L192): 그림 픽셀 해상도 변경이 월드의 물리 크기를 바꾸지 않는다.
- [`PackagedPngDimensionsAndManifestMatchSharedPhysics`](../../polrob.Server.Tests/ChaseTownAssetCatalogTests.cs#L214): 실제 포함된 이미지와 manifest의 크기·정의가 Shared의 물리 데이터와 맞는지 확인한다.
- `AtNative`, `Blocked`: 에셋을 기본 배율로 배치하고 특정 지점의 충돌 여부를 쉽게 검사하는 private 도우미다.

## GameMovementValidationTests.cs

[소스](../../polrob.Server.Tests/GameMovementValidationTests.cs#L1). **대상:** 방의 이동 입력 승인 기준.

- [`NonFiniteAndReplayPacketsCannotReplaceLastAcceptedMovement`](../../polrob.Server.Tests/GameMovementValidationTests.cs#L14): 정상 순번 입력 뒤 NaN, Infinity, 이전 순번, 다른 UDP endpoint를 넣어도 위치·마지막 승인 순번이 바뀌지 않는지 확인한다. 거절된 입력 뒤 새 정상 순번은 승인된다.
- `Move`: private `HandleRoomMove`를 호출해 네트워크 소켓 없이 입력 판정을 직접 검사한다.

## GameNetworkLifecycleTests.cs

[소스](../../polrob.Server.Tests/GameNetworkLifecycleTests.cs#L1). **대상:** 방 루프의 빈 방 유예, 연결 세대, 정상·비정상 종료.

- [`RoomWithNoSuccessfulJoinStillExpires`](../../polrob.Server.Tests/GameNetworkLifecycleTests.cs#L14): 만들어졌지만 참가에 성공한 사람이 없는 `GameSession`도 언젠가 정리되는지 확인한다.
- [`LateLeaveCommandCannotRemoveReplacementGameSession`](../../polrob.Server.Tests/GameNetworkLifecycleTests.cs#L26): 교체된 이전 연결의 Leave가 새 PlayerSession을 제거하지 못한다.
- [`EmptyRoomLoopStopsOnlyAfterGracePeriodAndWithNoQueuedCommands`](../../polrob.Server.Tests/GameNetworkLifecycleTests.cs#L46): 빈 방 유예가 지나고 대기 명령이 없어야 loop를 닫는다. 명령이 있으면 빈 시각을 다시 판단한다.
- [`EmptyPlayingRoomLoopAbandonsLobbyRoomAfterReconnectGracePeriod`](../../polrob.Server.Tests/GameNetworkLifecycleTests.cs#L79): Playing 중 모두 끊겼을 때 유예 후 로비 방도 포기 처리하고 가짜 승패를 만들지 않는다.
- [`EndedRoomLoopKeepsCompletedCustomRoomForReplay`](../../polrob.Server.Tests/GameNetworkLifecycleTests.cs#L127): 완료된 네트워크 방은 정리하되 커스텀 로비 방은 재경기에 남길 수 있다.
- `CreateServerWithoutSockets`, `SetField`, `Invoke`: 테스트가 필요한 GameSession과 private 메서드를 직접 준비한다. 이 테스트만으로 실제 listener 시작 성공을 검증하지 않는다.

## GameNetworkProtocolTests.cs

[소스](../../polrob.Server.Tests/GameNetworkProtocolTests.cs#L1). **대상:** TCP 프레임과 UDP JSON 경계.

- [`TcpJoinFrameAcceptsCurrentAndLegacyLength`](../../polrob.Server.Tests/GameNetworkProtocolTests.cs#L21): 현재/기존 길이 형식과 다국어 UTF-8 payload를 읽는다.
- [`TcpFrameRejectsOversizedDeclaredLengthBeforeReadingBody`](../../polrob.Server.Tests/GameNetworkProtocolTests.cs#L46), [`TcpFrameRejectsOversizedStringLengthBeforeReadingBody`](../../polrob.Server.Tests/GameNetworkProtocolTests.cs#L56): 본문 전체를 읽거나 큰 메모리를 잡기 전에 선언된 길이 한도를 검사한다.
- [`TcpFrameRejectsLengthMismatch`](../../polrob.Server.Tests/GameNetworkProtocolTests.cs#L74): 바깥 프레임 길이와 안쪽 문자열 인코딩 구조가 어긋난 입력을 거부한다.
- [`TcpFrameRejectsMalformedStringLengthPrefix`](../../polrob.Server.Tests/GameNetworkProtocolTests.cs#L85), [`TcpFrameRejectsLengthPrefixOutsideInt32Range`](../../polrob.Server.Tests/GameNetworkProtocolTests.cs#L101): 끝나지 않는 7-bit 길이 및 범위 초과 prefix를 거부한다.
- [`TcpFrameRejectsInvalidUtf8AndTruncatedPayload`](../../polrob.Server.Tests/GameNetworkProtocolTests.cs#L117): 잘못된 UTF-8과 중간에 끊긴 바이트열이 정상 Join으로 해석되지 않는다.
- [`UdpMovementAcceptsValidPacketAtLimitAndRejectsOversizedPacket`](../../polrob.Server.Tests/GameNetworkProtocolTests.cs#L133), [`UdpMovementRejectsEmptyAndMalformedPackets`](../../polrob.Server.Tests/GameNetworkProtocolTests.cs#L157): UDP 크기 경계, 빈 배열, JSON 오류를 구분한다.
- `ReadTcpFrame`, `ParseUdpMovement`, `CreateTcpFrame`: private 서버 파서를 Reflection으로 호출하고 테스트 프레임을 만드는 helper다.

## GameNetworkSocketTests.cs

[소스](../../polrob.Server.Tests/GameNetworkSocketTests.cs#L1). **대상:** 실제 loopback TCP 연결에서의 수명과 제한.

- [`ReconnectReplacesTcpSessionAndOldConnectionCannotRemoveIt`](../../polrob.Server.Tests/GameNetworkSocketTests.cs#L21): 같은 사용자 재접속 시 ConnectionId·이동 토큰 교체와 이전 연결의 늦은 Leave/heartbeat 격리를 확인한다.
- [`OversizedFrameAndUnauthenticatedJoinAreClosedBeforeRoomCreation`](../../polrob.Server.Tests/GameNetworkSocketTests.cs#L89): 프레임 크기·로그인·Join 절차를 거부하고 유령 방/참가자를 만들지 않는다.
- [`StartedRoomIsRemovedWhenAllTcpPlayersStayDisconnectedPastGracePeriod`](../../polrob.Server.Tests/GameNetworkSocketTests.cs#L134): 시작된 방의 전원 소켓 단절 후 방 유예 정리가 이루어진다.
- [`ConnectionCapRejectsExcessClientsAndReleasesSlotsAfterDisconnect`](../../polrob.Server.Tests/GameNetworkSocketTests.cs#L195): 동시 TCP 상한이 두 번째 소켓을 막고 첫 소켓 종료 후 슬롯을 회수한다.
- [`PartialFrameCannotKeepAnUnauthenticatedConnectionForever`](../../polrob.Server.Tests/GameNetworkSocketTests.cs#L218): 절반만 보낸 프레임이 Join timeout을 무한히 점유하지 않는다.
- [`ShutdownCancelsIdleReadsAndClosesAllConnections`](../../polrob.Server.Tests/GameNetworkSocketTests.cs#L235): 서버 종료 시 유휴 read를 취소하고 연결 수를 0으로 만든다.
- [`ShutdownAbandonsAnUnfinishedGameWithoutInventingAResult`](../../polrob.Server.Tests/GameNetworkSocketTests.cs#L254): 승패 없는 게임을 중단할 때 기록 큐에 완료 결과를 보내지 않는다.
- `CreateServer`, `ConnectAsync`, `WriteFrame`, `ReadUntilTypeAsync`, `ReadFrameAsync`, `AssertRemoteClosedAsync`, `WaitUntilAsync`, `StopAndDisposeAsync`: 포트 0으로 서버를 띄우고 프레임을 주고받고 비동기 결과를 기다리는 테스트 fixture다. `CreateLoginSession`과 `Logout`은 private 로그인 세션 생성·해제를 반사 호출한다.

[`CountingGameRecordQueue.TryEnqueue`](../../polrob.Server.Tests/GameNetworkSocketTests.cs#L292)는 받은 기록 수를 증가시켜 종료 시 허위 결과 생성 여부를 확인하고, [`NoopGameRecordQueue.TryEnqueue`](../../polrob.Server.Tests/GameNetworkSocketTests.cs#L459)는 저장 없이 성공만 돌려준다. 두 구현 모두 테스트용이며 실제 Outbox 동작을 대체한다.

주요 fixture helper의 작동 방식도 테스트 결과를 해석할 때 필요하다.

| 메서드 | 입력 → 처리 → 결과 |
|---|---|
| [`CreateServer`](../../polrob.Server.Tests/GameNetworkSocketTests.cs#L295) | 포트 0·즉시 drain 기본 설정과 선택적 설정 덮어쓰기를 만들고, 기록 큐를 주입한 `GameNetworkServer`를 반환한다. 포트 0은 운영 포트가 아니라 OS가 고른 임시 포트다. |
| [`WriteFrame`](../../polrob.Server.Tests/GameNetworkSocketTests.cs#L337), [`SevenBitLength`](../../polrob.Server.Tests/GameNetworkSocketTests.cs#L397) | UTF-8 payload의 **바이트** 수와 7-bit 길이 prefix 크기를 계산해 실제 TCP wire format으로 쓴다. |
| [`ReadFrameAsync`](../../polrob.Server.Tests/GameNetworkSocketTests.cs#L358), [`ReadUntilTypeAsync`](../../polrob.Server.Tests/GameNetworkSocketTests.cs#L347) | 타입·문자열 prefix·payload를 읽어 선언된 frame 길이와 실제 바이트 수를 비교한다. 원하는 타입이 나올 때까지 최대 30 frame을 읽는다. |
| [`WaitForTcpPortAsync`](../../polrob.Server.Tests/GameNetworkSocketTests.cs#L419), [`GetTcpPort`](../../polrob.Server.Tests/GameNetworkSocketTests.cs#L425) | listener가 시작되어 실제 임시 포트가 배정될 때까지 기다린 뒤 그 포트를 반환한다. |
| [`GetTcpConnectionCount`](../../polrob.Server.Tests/GameNetworkSocketTests.cs#L428), [`GetGameSessions`](../../polrob.Server.Tests/GameNetworkSocketTests.cs#L431), [`GetField`](../../polrob.Server.Tests/GameNetworkSocketTests.cs#L434) | Reflection으로 서버 private 필드를 읽는다. 따라서 네트워크에서 관찰한 현상에 더해 연결 수와 방 사전까지 확인할 수 있지만, 내부 필드 이름 변경에는 취약하다. |
| [`StopAndDisposeAsync`](../../polrob.Server.Tests/GameNetworkSocketTests.cs#L437) | 5초 취소 기한으로 서버 중지를 시도하고, 중지 실패 시에도 `Dispose`를 실행한다. |

## GameRecordOutboxTests.cs

[소스](../../polrob.Server.Tests/GameRecordOutboxTests.cs#L1). **대상:** 결과를 디스크에 맡긴 뒤 DB 전송과 재시작.

- [`AcceptedRecordSurvivesRestartAndIsDeletedOnlyAfterStoreAcknowledges`](../../polrob.Server.Tests/GameRecordOutboxTests.cs#L26): 디스크 기록은 Outbox 재생성에도 남고 Store 저장이 성공한 뒤에만 삭제된다.
- [`StoreCommitFollowedByLostAcknowledgementReplaysTheSameGameId`](../../polrob.Server.Tests/GameRecordOutboxTests.cs#L51): DB commit 직후 파일 ack가 사라졌을 때 같은 GameId로 다시 전송한다.
- [`PermanentFailureAndCorruptJsonArePreservedAndDoNotBlockValidRecords`](../../polrob.Server.Tests/GameRecordOutboxTests.cs#L74): 영구 오류/손상 JSON은 `.failed`로 보존하고 다른 유효 기록을 막지 않는다.
- [`QuotaBlocksAdmissionBeforeHardLimitAndRejectedRecordsDoNotGrowTheQueue`](../../polrob.Server.Tests/GameRecordOutboxTests.cs#L98), [`ByteQuotaIsEnforcedIndependentlyOfRecordCount`](../../polrob.Server.Tests/GameRecordOutboxTests.cs#L123): 새 경기 임계치, 절대 한도, 바이트 한도가 별도 작동한다.
- [`DuplicateIsIdempotentButConflictingSnapshotIsRejected`](../../polrob.Server.Tests/GameRecordOutboxTests.cs#L112): ID와 내용이 완전히 같은 중복만 성공 처리하고 다른 내용은 거절한다.
- [`DiskWriteFailureClosesAdmissionAndProbeAllowsRecovery`](../../polrob.Server.Tests/GameRecordOutboxTests.cs#L137), [`SecondProcessCannotOwnSameSpool`](../../polrob.Server.Tests/GameRecordOutboxTests.cs#L155): 쓰기 장애 후 수용 중단·probe 복귀와 동일 디렉터리 중복 소유 금지를 확인한다.
- [`InterruptedTemporaryFileIsRecoveredAndReplayed`](../../polrob.Server.Tests/GameRecordOutboxTests.cs#L162), [`CancellationKeepsTheRecordOnDisk`](../../polrob.Server.Tests/GameRecordOutboxTests.cs#L175): 재시작의 `.tmp` 회수와 취소 때 대기 파일 보존을 확인한다.
- [`RestartMarkerDistinguishesInterruptedAndCleanShutdown`](../../polrob.Server.Tests/GameRecordOutboxTests.cs#L187): `.runtime-state`가 clean/unclean 종료를 구별하는지 확인한다.
- `SetUp`, `TearDown`, `Open`, `Writer`, `Record`, `FakeStore`: 임시 디렉터리와 가짜 Store를 만들고 테스트 후 삭제한다. 실제 Cosmos와 연결하지 않는다.

[`FakeStore.SaveGameRecordAsync`](../../polrob.Server.Tests/GameRecordOutboxTests.cs#L212)는 테스트마다 전달한 `save` 함수를 그대로 호출한다. 이 함수에 성공·일시 오류·영구 오류·취소를 주입하므로 Outbox/Writer의 재시도와 파일 보존을 Cosmos 없이 검사한다.

## GameRecordStatsCalculatorTests.cs

[소스](../../polrob.Server.Tests/GameRecordStatsCalculatorTests.cs#L1). **대상:** 역할별 전적 산술.

- [`Calculate_WithNoGames_ReturnsZeroForEveryBreakdown`](../../polrob.Server.Tests/GameRecordStatsCalculatorTests.cs#L9): 경기 0건의 전체/경찰/도둑 값과 승률을 0으로 만든다.
- [`Calculate_AggregatesOverallAndRoleSpecificResults`](../../polrob.Server.Tests/GameRecordStatsCalculatorTests.cs#L22): 경찰·도둑 각 경기의 합계·승패·승률을 정확히 합친다.
- [`Calculate_WithInvalidRole_RejectsTheOutcome`](../../polrob.Server.Tests/GameRecordStatsCalculatorTests.cs#L44): 정의되지 않은 enum을 집계하지 않는다.
- `AssertBreakdown`: 같은 네 숫자 필드를 세 통계 그룹에서 반복 검사한다.

## GameRoomHostAssignmentTests.cs

[소스](../../polrob.Server.Tests/GameRoomHostAssignmentTests.cs#L1). **대상:** 방장 이전과 커스텀 재경기 로비.

- [`RemovePlayer_WhenNonHostLeaves_KeepsExistingHost`](../../polrob.Server.Tests/GameRoomHostAssignmentTests.cs#L11): 일반 참가자가 나가면 방장이 유지된다.
- [`RemovePlayer_WhenHostLeaves_AssignsFirstRemainingPlayer`](../../polrob.Server.Tests/GameRoomHostAssignmentTests.cs#L25): 방장이 나가면 남은 목록의 첫 참가자를 방장으로 정한다.
- [`CompleteGame_ForCustomRoom_PreservesPlayersAndHostForReplay`](../../polrob.Server.Tests/GameRoomHostAssignmentTests.cs#L39): 커스텀 완료 후 남은 명단·방장을 유지하며 시작 상태만 해제한다.
- [`CompleteGame_ForRandomRoom_KeepsExistingCleanupBehavior`](../../polrob.Server.Tests/GameRoomHostAssignmentTests.cs#L57): 랜덤 완료 후 방을 정리한다.
- `CreateServiceWithGame`, `CreatePlayer`: DB 요청 없이 기존 명단을 가진 방을 직접 만든다.

## GameRoomLifecycleTests.cs

[소스](../../polrob.Server.Tests/GameRoomLifecycleTests.cs#L1). **대상:** 방 여러 개의 독립성과 종료/퇴장 경쟁.

- [`CompletingOneRoomDoesNotChangeAnotherRoomsPlayersOrVoiceSession`](../../polrob.Server.Tests/GameRoomLifecycleTests.cs#L12): 한 방 완료가 다른 방의 명단·음성 세션을 바꾸지 않는다.
- [`ConcurrentDuplicateJoinsKeepOnePlayerAndOriginalRole`](../../polrob.Server.Tests/GameRoomLifecycleTests.cs#L44): 32개 동시 중복 입장에도 한 사용자는 한 번만 들어가고 원래 역할이 유지된다.
- [`LateDisconnectFromReplacedConnectionDoesNotAffectOtherRoomOrNewConnection`](../../polrob.Server.Tests/GameRoomLifecycleTests.cs#L69): 예전 연결의 늦은 정리가 새 연결·다른 방 등록에 영향을 주지 않는다.
- [`LastDisconnectBeforeCompletionRemovesEmptyCustomRoom`](../../polrob.Server.Tests/GameRoomLifecycleTests.cs#L92): 마지막 퇴장이 완료보다 먼저 도착한 커스텀 방도 최종적으로 남지 않는다.
- [`RepeatedCompletionAndDisconnectInterleavingsLeaveNoRooms`](../../polrob.Server.Tests/GameRoomLifecycleTests.cs#L113): 퇴장/완료 순서와 동시 실행을 반복해 빈 방 잔류를 검사한다.
- `CreateStartedRoom`: 실제 `GameRoomService`를 통해 테스트용 시작 방을 준비한다.

## GameRoomSoakTests.cs

[소스](../../polrob.Server.Tests/GameRoomSoakTests.cs#L1). **대상:** 선택 실행 장기 반복.

- [`RepeatedGameCompletionAndDisconnectDoesNotRetainRooms`](../../polrob.Server.Tests/GameRoomSoakTests.cs#L12): 지정한 시간 동안 방 생성→시작→완료·퇴장 순서를 반복하고 최종 방 수 0을 확인한다. 실행 시간 환경 변수가 설정되지 않으면 `Assert.Ignore`한다. 이 테스트는 방 생애 상태를 반복할 뿐 실제 DB/소켓 부하를 측정하지 않는다.

## GameRoomVoiceAccessTests.cs

[소스](../../polrob.Server.Tests/GameRoomVoiceAccessTests.cs#L1). **대상:** 방 상태에 따른 팀 음성 권한.

- [`TeamVoiceAccess_IsAvailableOnlyWhileGameIsActive`](../../polrob.Server.Tests/GameRoomVoiceAccessTests.cs#L11): 시작 전·종료 후 거부, 진행 중 실제 참가자에게만 서버가 확정한 역할과 VoiceSessionId를 반환한다.
- [`StartGame_CreatesOneVoiceSessionPerMatchAndRotatesForReplay`](../../polrob.Server.Tests/GameRoomVoiceAccessTests.cs#L50): 같은 경기 중복 Start에서는 세션 ID가 유지되고 재경기에서 새 ID가 나온다.
- [`UndefinedRole_IsRejectedBeforeAnyUserLookup`](../../polrob.Server.Tests/GameRoomVoiceAccessTests.cs#L73): 유효하지 않은 역할은 사용자 DB 조회 전 거절한다.
- `CreateServiceWithGame`, `CreatePlayer`: 사용자 DB가 없는 상태에서 인증된 명단을 구성한다.

## GameRuleTransitionTests.cs

[소스](../../polrob.Server.Tests/GameRuleTransitionTests.cs#L1). **대상:** 이동·체포·탈옥의 서버 상태 전이.

- [`LargeDiagonalInputAndDelayedTickCannotMoveFasterThanTheServerLimit`](../../polrob.Server.Tests/GameRuleTransitionTests.cs#L19): 대각선 입력을 정규화하고 지연된 tick의 이동 시간도 상한으로 제한한다.
- [`ExpiredMovementInputStopsThePlayer`](../../polrob.Server.Tests/GameRuleTransitionTests.cs#L63): 마지막 방향 입력이 만료되면 멈춤 상태와 0 방향이 된다.
- [`ServerMovementStopsAtARealSolidCollider`](../../polrob.Server.Tests/GameRuleTransitionTests.cs#L85): 실제 맵 장애물 앞에서 다음 이동이 벽을 통과하지 않는다.
- [`ArrestLocksBothPlayersThenJailsTheRobberOnlyAfterCompletion`](../../polrob.Server.Tests/GameRuleTransitionTests.cs#L106): 체포 시작 중 이동 잠금과 완료 시각 이후 수감·감옥 배치를 분리한다.
- [`LeavingTheRescueAreaErasesProgressAndRequiresAFullNewCountdown`](../../polrob.Server.Tests/GameRuleTransitionTests.cs#L155): 구조 범위에서 나가면 진행 시간을 버리고 돌아와서 처음부터 다시 채운다.
- [`CompletedRescueReleasesPrisonerAtAWalkablePosition`](../../polrob.Server.Tests/GameRuleTransitionTests.cs#L188): 완료된 구조는 수감자를 통행 가능한 위치에 놓는다.
- `CreateServerWithoutNetworkSockets`, `CreatePlayer`, `AddPlayer`, `PlaceVisiblePairAndDetectArrest`, `Invoke`: 반사 호출에 필요한 GameSession/PlayerSession을 세팅한다. `CreateLoginSession`, `Logout`은 테스트 세션을 준비·정리한다.

`FindOpenSquare`는 맵에서 네 모서리 이동이 가능한 시작점을, `FindOpenPositionWithBlockedStep`은 오른쪽 12 단위 이동만 장애물에 막히는 시작점을 찾는다. `PlaceVisiblePairAndDetectArrest`는 통행 가능한 경찰·도둑 쌍을 놓아 실제 체포 감지가 일어나는 위치를 찾는다. `CreateJailbreakRoom`은 수감자와 구조자를 감옥 영역에 배치하고, `FindOpenPositionFarFrom`은 구조 범위에서 충분히 떨어진 통행 가능 지점을 고른다. [`Distance`](../../polrob.Server.Tests/GameRuleTransitionTests.cs#L321)는 두 좌표의 직선 거리만 계산한다. 이 helper들이 고정 좌표 대신 실제 활성 맵을 탐색하므로 맵 에셋 변경 뒤에도 규칙 테스트가 의미 있는 위치에서 실행된다.

## LiveKitTokenServiceTests.cs

[소스](../../polrob.Server.Tests/LiveKitTokenServiceTests.cs#L1). **대상:** 팀 방 이름, JWT grant, 연결 세대.

- [`CreateTeamVoiceToken_UsesRoleSpecificRoom`](../../polrob.Server.Tests/LiveKitTokenServiceTests.cs#L19): 경찰과 도둑의 room name이 분리된다.
- [`CreateTeamVoiceToken_EmitsParticipantClaimsAndMicrophoneOnlyGrants`](../../polrob.Server.Tests/LiveKitTokenServiceTests.cs#L40): 발급된 JWT의 사용자 메타데이터와 마이크 게시·구독 grant를 읽어 확인한다.
- [`CreateTeamVoiceToken_UsesConnectionSpecificParticipantIdentity`](../../polrob.Server.Tests/LiveKitTokenServiceTests.cs#L75): TCP 연결 세대가 바뀌면 LiveKit identity도 달라진다.
- [`CreateTeamVoiceToken_ClampsAndAppliesConfiguredTtl`](../../polrob.Server.Tests/LiveKitTokenServiceTests.cs#L93): 요청 설정의 토큰 유효 시간이 1~5분으로 제한된다.
- [`CreateTeamVoiceToken_WithInvalidWebSocketUrl_Throws`](../../polrob.Server.Tests/LiveKitTokenServiceTests.cs#L128), [`CreateTeamVoiceToken_WithMissingCredential_Throws`](../../polrob.Server.Tests/LiveKitTokenServiceTests.cs#L145): 주소·키 구성 오류는 발급 단계에서 예외다.
- [`CreateTeamVoiceToken_WithMissingVoiceSessionId_Throws`](../../polrob.Server.Tests/LiveKitTokenServiceTests.cs#L162), [`CreateTeamVoiceToken_WithUndefinedRole_Throws`](../../polrob.Server.Tests/LiveKitTokenServiceTests.cs#L176), [`CreateTeamVoiceToken_WithMissingGameConnectionId_Throws`](../../polrob.Server.Tests/LiveKitTokenServiceTests.cs#L189): 경기·역할·연결 식별자 누락을 거부한다.
- `CreateService`, `CreatePlayer`, `ReadPayload`: 테스트용 옵션·플레이어를 만들고 JWT payload를 읽는다.

## MapManagementTests.cs

[소스](../../polrob.Server.Tests/MapManagementTests.cs#L1). **대상:** 맵 선택이 방에서 게임 서버까지 유지되는가.

- [`NewMapIsDefaultAndClassicRemainsIndependent`](../../polrob.Server.Tests/MapManagementTests.cs#L17): 기본 Chase 맵과 별도 Classic 맵의 영역/인스턴스를 구분한다.
- [`InvalidMapCannotCreateRoomOrPhysics`](../../polrob.Server.Tests/MapManagementTests.cs#L38): 미등록 MapId로 방이나 물리 맵을 만들 수 없다.
- [`RoomMapEndpointRequiresMembershipAndReturnsAuthoritativeId`](../../polrob.Server.Tests/MapManagementTests.cs#L50): 상태 조회에서 참가자가 아니면 맵 ID를 얻지 못하고 참가자에게 서버 선택값을 준다.
- [`RoomMapSurvivesJoinStartAndReplay`](../../polrob.Server.Tests/MapManagementTests.cs#L83): 방 참가·시작·재경기 동안 선택 맵이 유지된다.
- [`ServerUsesSelectedMapForCollisionJailAndSpawns`](../../polrob.Server.Tests/MapManagementTests.cs#L107): 네트워크 게임 세션의 충돌·감옥·spawn 계산이 같은 맵을 사용한다.
- [`ApprovedGroundAndRemovalsArePreserved`](../../polrob.Server.Tests/MapManagementTests.cs#L136), [`PoliceStationHasWalkableRearAndFrontPassages`](../../polrob.Server.Tests/MapManagementTests.cs#L155), [`AuthoredCrateAndWallPassagesHaveContinuousClearance`](../../polrob.Server.Tests/MapManagementTests.cs#L170): 현재 배치의 지면·삭제 대상·경찰서 뒤/앞 통로와 상자/벽 사이 통행 폭을 보호한다.

## MovementAuthenticationTests.cs

[소스](../../polrob.Server.Tests/MovementAuthenticationTests.cs#L1). **대상:** UDP 조작 인증 조건의 결합.

- [`UdpMovementRequiresCurrentConnectionTokenEndpointAndLoginSession`](../../polrob.Server.Tests/MovementAuthenticationTests.cs#L13): 사용자 ID, 현재 ConnectionId, 이동 토큰, UDP endpoint, 유효 로그인 세션이 모두 맞아야 허용된다. 하나씩 잘못 바꾸거나 로그아웃하면 거부한다.
- `IsAuthorized`: 서버의 private 인증 메서드에 테스트 입력을 전달하는 Reflection helper다. 실제 datagram 직렬화·socket 수신 검사는 이 파일의 범위가 아니다.

## NetworkBackpressureTests.cs

[소스](../../polrob.Server.Tests/NetworkBackpressureTests.cs#L1). **대상:** 대기열·송신·기록 저장이 밀릴 때 상한과 재시도.

- [`DisconnectedQueuedJoinDoesNotCreateAGhostPlayer`](../../polrob.Server.Tests/NetworkBackpressureTests.cs#L16): 처리되기 전에 끊긴 Join 명령이 플레이어를 남기지 않는다.
- [`SlowReaderQueueHasBothMessageAndByteLimits`](../../polrob.Server.Tests/NetworkBackpressureTests.cs#L30): 느린 TCP 수신자에게 쌓이는 송신 큐의 메시지 수·바이트 수 한도가 각각 적용된다.
- [`FailedRecordAcceptanceRetriesTheSameSnapshotAndDoesNotMarkItCommitted`](../../polrob.Server.Tests/NetworkBackpressureTests.cs#L46): 기록 인계 실패 후 같은 결과 객체를 다시 전달하며 성공 전에 완료로 표시하지 않는다.
- [`MovementCoalescingKeepsHighestSequenceDespiteOutOfOrderArrival`](../../polrob.Server.Tests/NetworkBackpressureTests.cs#L76): 이동 입력을 합칠 때 마지막 도착값이 아니라 가장 높은 sequence를 택한다.
- [`LobbyCompletionWaitsForDurableAcceptanceEvenAfterAllPlayersDisconnect`](../../polrob.Server.Tests/NetworkBackpressureTests.cs#L104): 모두 끊겼어도 저장 계층이 결과를 받기 전에는 로비 방의 정상 완료를 진행하지 않는다.
- `Set`, `FakeRecordQueue.TryEnqueue`: 소켓 없이 private 상태를 만들고 첫 시도만 거부하는 가짜 큐다.

## OperationsEndpointTests.cs

[소스](../../polrob.Server.Tests/OperationsEndpointTests.cs#L1). **대상:** readiness·drain·metrics·HTTP 제한.

- [`ReadinessChangesAcrossStartupDrainWhileLivenessStaysAvailable`](../../polrob.Server.Tests/OperationsEndpointTests.cs#L65): 시작 전/후와 drain 후 readiness가 바뀌어도 liveness는 살아 있다.
- [`MetricsRequireKeyAndContainBacklogWithoutPlayerIdentifiers`](../../polrob.Server.Tests/OperationsEndpointTests.cs#L78): 운영 키가 없으면 metrics를 읽지 못하고, 반환 지표에는 대기 파일 수 등이 있지만 사용자 식별자는 쓰지 않는다.
- [`HttpAndLoginLimitsReturn429WithRetryAfterWithoutLimitingHealth`](../../polrob.Server.Tests/OperationsEndpointTests.cs#L93): 인증·일반 HTTP 제한은 429/Retry-After를 반환하고 health endpoint는 제한하지 않는다.
- [`RoomLimitAndDrainAreEnforcedInsideRoomService`](../../polrob.Server.Tests/OperationsEndpointTests.cs#L106): HTTP 미들웨어 밖에서 방 서비스를 직접 호출해도 최대 방 수와 drain 조건을 지킨다.
- `Start`, `Stop`, `FakeStore.SaveGameRecordAsync`: 테스트 서버·Outbox/기록 계층을 시작·정리하는 fixture다.

## TownMapPhysicsTests.cs

[소스](../../polrob.Server.Tests/TownMapPhysicsTests.cs#L1). **대상:** 기본 맵의 실제 통행과 감옥 구조 접근성.

- [`ActiveMapRegistersAssetProfilesAndNonSolidHidingAreas`](../../polrob.Server.Tests/TownMapPhysicsTests.cs#L10): 포함 에셋의 충돌 프로필·통과 가능한 은신 영역이 맵에 등록된다.
- [`SpecifiedColliderCentersAndWorldEdgesBlockMovement`](../../polrob.Server.Tests/TownMapPhysicsTests.cs#L44): 지정한 건물/물체 중심과 월드 바깥을 통과하지 못한다.
- [`CircularPropsHaveRoundClearanceInsteadOfInvisibleSquareCorners`](../../polrob.Server.Tests/TownMapPhysicsTests.cs#L69): 원형 소품의 사각형 빈 모서리는 통과 가능하다.
- [`RoleSpawnsAreDistinctWalkableAndStable`](../../polrob.Server.Tests/TownMapPhysicsTests.cs#L83): 역할별 spawn은 서로 다르고 걸을 수 있으며 반복 계산 결과가 같다.
- [`JailFrontSupportsRescueReleaseAndTravelFromBothRoleSpawns`](../../polrob.Server.Tests/TownMapPhysicsTests.cs#L102): 경찰·도둑 출발지에서 감옥 앞으로 갈 수 있고 구조·석방 지점도 접근 가능하다.
- [`JailHoldingSlotsAreCenteredInsideTheArtworkAndDoNotOverlap`](../../polrob.Server.Tests/TownMapPhysicsTests.cs#L121): 수감 슬롯들이 그림 내부에 있고 지정 인원에 대해 겹치지 않는다.
- [`GroundIsThreeReusableTilesAndTheRoadNetworkIsConnected`](../../polrob.Server.Tests/TownMapPhysicsTests.cs#L155): 지면 타일 구성과 길 연결성을 확인한다.
- [`EveryHidingAreaCanBeEnteredFromTheTown`](../../polrob.Server.Tests/TownMapPhysicsTests.cs#L181), [`RescueTriggerIsOutsideTheBarsAndReachable`](../../polrob.Server.Tests/TownMapPhysicsTests.cs#L196), [`ReferenceMapLandmarksJoinThePlayableTown`](../../polrob.Server.Tests/TownMapPhysicsTests.cs#L208): 은신·구조·주요 장소가 단절된 섬이 되지 않는다.
- `FloodWalkableGrid`, `AssertPointReachable`, `FindConnectedGridCell`: 이동 가능 격자를 탐색해 위 도달성 assert를 계산한다.

## VoiceControllerTests.cs

[소스](../../polrob.Server.Tests/VoiceControllerTests.cs#L1). **대상:** `/voice/token`의 HTTP 권한 경계.

- [`CreateToken_WithoutAuthenticatedSession_ReturnsUnauthorized`](../../polrob.Server.Tests/VoiceControllerTests.cs#L32): Bearer 로그인 세션이 없으면 401이다.
- [`CreateToken_WithMissingRoomId_ReturnsBadRequest`](../../polrob.Server.Tests/VoiceControllerTests.cs#L48): 비어 있는 RoomId를 거부한다.
- [`CreateToken_WithoutActiveGameConnection_ReturnsPlain403`](../../polrob.Server.Tests/VoiceControllerTests.cs#L65): 방 참가자여도 현재 활성 게임 연결이 없으면 403이다.
- [`CreateToken_ForActiveParticipant_IssuesTeamVoiceConnectionInfo`](../../polrob.Server.Tests/VoiceControllerTests.cs#L97): 로그인·진행 중 방·참가자·현재 연결을 갖춘 사람에게 팀 음성 정보를 준다.
- `CreateController`, `Authenticate`, `CreateRoomService`, `CreatePlayer`: controller context·방·사용자·로그인 토큰을 만드는 fixture다. `RemoveCreatedSessions`는 테스트 후 정리한다.
