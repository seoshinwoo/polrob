<div align="center">

<img width="1916" height="821" alt="PolRob - Police vs Robber" src="https://github.com/user-attachments/assets/e2b3b52f-91d4-499f-911c-a9c6e1b355a9" />

# PolRob

### 경찰과 도도ㅜㄱ의 쫓고 쫓기는 실시간 추격전!

경찰들은 제한 시간 안에 도둑을 모두 체포해야 하고, 도둑들은 맵 구석구석으로 도망치고 잡힌 동료를 탈옥시키며 끝까지 버텨야 합니다. <br />
커스텀 매칭으로 친구들과 함께 플레이하고, 혼자일 때는 랜덤 매칭으로 다른 사람들과 플레이하세요.

<p>
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge" alt=".NET 10" />
  <img src="https://img.shields.io/badge/C%23-Game_Server-239120?style=for-the-badge&logo=csharp&logoColor=white" alt="C#" />
  <img src="https://img.shields.io/badge/.NET_MAUI-Client-512BD4?style=for-the-badge" alt=".NET MAUI" />
  <img src="https://img.shields.io/badge/SkiaSharp-Rendering-00A6A6?style=for-the-badge" alt="SkiaSharp" />
  <img src="https://img.shields.io/badge/SignalR-Lobby-7B42BC?style=for-the-badge&logo=signal&logoColor=white" alt="SignalR" />
  <img src="https://img.shields.io/badge/Azure_Cosmos_DB-Persistence-0078D4?style=for-the-badge&logo=microsoftazure&logoColor=white" alt="Azure Cosmos DB" />
</p>

</div>

---

## 🎮 프로젝트 개요

| Genre | 캐주얼 멀티플레이 게임 |
| Platform | Android, iOS |
| 개발 인원 | 1명 |


## Game Rule

- 기본적으로 게임은 경찰 2명, 도둑 4명으로 구성됩니다
- 한 게임의 시간은 300초이고, 시간 안에 경찰이 모든 도둑을 잡으면 경찰 승리, 못 잡으면 도둑 승리입니다
- 경찰의 시야 안에 도둑이 들어오면 해당 도둑은 감옥에 갇히게 되고 체포되지 않은 도둑이 감옥으로 가서 탈옥시킬 수 있습니다
- 커스텀 방을 만들고 친구들을 초대해 게임을 진행할 수도 있고, 랜덤 매칭을 통해 다른 플레이어들과 게임을 할 수도 있습니다

---

### 통신 채널 분리

| 채널 | 사용 데이터 | 설계 이유 |
|---|---|---|
| HTTP | 회원가입, 로그인, 방 생성·참가·재대결 | 요청/응답 기반 도메인 작업 |
| SignalR | 인원·역할 변경, 매칭 완료, 게임 시작 | 로비 그룹 이벤트와 재연결 처리 |
| TCP | 인증된 게임 입장, 초기 상태, 체포·탈옥, Phase 전환 | 순서와 전달 보장이 필요한 이벤트 |
| UDP | 조이스틱 입력, 서버가 계산한 위치 snapshot | 과거 패킷보다 최신 상태가 중요한 고빈도 데이터 |

---

## ⚙️ 핵심 구현

### 1. Server-Authoritative Movement

클라이언트가 계산한 좌표를 신뢰하지 않습니다. 클라이언트는 정규화 전 조이스틱 입력과 순서 번호만 보내며, 서버가 고정된 속도와 반지름을 기준으로 위치를 계산합니다.

<!-- ```mermaid
sequenceDiagram
    participant C as Client
    participant U as UDP Receiver
    participant Q as Room Command Queue
    participant R as Room Loop

    C->>U: playerId + input vector + sequence + movement token
    U->>Q: enqueue Move command
    Q->>R: latest input per player
    R->>R: validate token / endpoint / sequence
    R->>R: normalize input + simulate movement + collision
    R-->>C: authoritative position snapshot
``` -->

서버 검증 항목:

- 인증된 TCP 연결에서 발급한 movement session token
- 최초 등록된 UDP endpoint와 패킷 송신 endpoint 일치 여부
- 이전 입력보다 큰 sequence인지 확인해 중복·역순 패킷 제거
- `NaN`, `Infinity` 입력 거부 및 입력 벡터 정규화
- 맵 경계, 사각형·원형 장애물, 경찰서·감옥 충돌 판정
- 입력이 일정 시간 도착하지 않으면 자동 정지

클라이언트는 즉각적인 조작감을 위해 로컬 예측을 수행하고, 서버 snapshot으로 최종 위치를 보정합니다.

### 2. End-to-End Authentication Boundary

로그인 응답으로 발급된 session token에서 서버가 사용자 ID를 확정합니다. 이후 요청 body나 Hub 인자로 전달된 사용자 ID를 신뢰하지 않습니다.

```text
Login Session
 ├─ HTTP   : Authorization: Bearer <session>
 ├─ SignalR: AccessTokenProvider → 연결 및 Hub 호출 시 검증
 └─ TCP    : GameJoinRequest(session, roomId)
               └─ 로비 멤버십·역할·이름을 서버에서 조회
                    └─ UDP movement token 발급
```

- 방 생성·참가·재대결은 인증된 사용자 본인에게만 적용
- SignalR 그룹 참가 전 실제 방 멤버십 확인
- 커스텀 방 시작은 서버가 확인한 방장만 가능
- 역할 변경과 매칭 취소에서 클라이언트 제공 `userId` 제거
- TCP 입장 시 로그인 세션과 방 멤버십을 함께 검증

### 3. Room-Scoped Single-Consumer Loop

네트워크 수신부는 게임 상태를 직접 변경하지 않고 방별 `Channel<RoomCommand>`에 명령을 기록합니다. 각 방의 single-consumer 비동기 루프가 입력과 규칙을 순서대로 처리해 방 사이 상태를 격리합니다.

| 주기 | 처리 내용 |
|---:|---|
| `50 ms` | 명령 큐 drain, 최신 이동 입력 병합, 서버 이동 simulation |
| `100 ms` | 이동 snapshot broadcast, 시야·체포·탈옥 규칙 처리 |
| `1 s` | 카운트다운, 남은 시간, 승패와 전체 게임 상태 동기화 |

> `ConcurrentDictionary`는 개별 컬렉션 연산의 안전성을 담당하고, 복합 게임 규칙의 처리 순서는 방별 single-consumer loop가 담당합니다.

### 4. Server-Authoritative Game Rules

- 시야 거리와 90도 시야각을 이용한 상대 탐지
- 건물·벽·연못·부쉬에 의한 line-of-sight 차단
- 일정 시간 접촉을 유지해야 완료되는 체포 상태
- 서버가 결정하는 감옥 배치와 이동 잠금
- 구조자의 위치와 시간을 기준으로 계산하는 탈옥 진행률
- 제한 시간과 전체 도둑 수감 여부에 따른 승패 판정
- 역할과 시야 상태에 따른 상대 위치 패킷 필터링

### 5. Matchmaking & Room Lifecycle

- Police 2명, Robber 4명 구성을 맞추는 랜덤 매칭
- 6자리 room code 기반 커스텀 방 생성·참가
- 방장 시작 권한과 로비 역할 변경
- SignalR 연결 종료 유예와 재접속 추적
- 랜덤 방 종료 정리, 커스텀 방 재대결, 빈 방 만료 처리

---

## 📊 성능 측정과 최적화

UI 시뮬레이터를 여러 개 띄우는 대신 실제 프로토콜과 매칭·게임 흐름을 사용하는 headless bot을 구현했습니다. `60 / 300 / 600 / 900`봇 구간에서 동일한 스크립트로 CPU, 네트워크와 .NET runtime 지표를 수집했습니다.

### 측정 지표

```text
UDP/TCP packets · UDP bytes · JSON serialization · connections · players · rooms
CPU · Working Set · GC allocation/pause · lock contention · ThreadPool queue/threads
bot failures · room phase · eligible Playing samples
```

### 네트워크 처리 경로 최적화 결과

아래 결과는 `32620197` baseline과 네트워크 최적화를 결합한 `a7441c4`를 동일한 로컬 환경에서 비교한 값입니다.

| 부하 | CPU | Total PPS | UDP bytes/s | GC allocation | Working Set |
|---:|---:|---:|---:|---:|---:|
| 600 bots / 100 rooms | `13.85 → 1.90` **-86.3%** | `33,654 → 22,951` **-31.8%** | `6.14 → 2.08 MB` **-66.1%** | `22.29 → 11.83 MB/s` **-46.9%** | `908 → 397 MB` **-56.3%** |
| 900 bots / 150 rooms | `13.89 → 2.93` **-78.9%** | `33,762 → 34,367` **+1.8%** | `6.22 → 3.13 MB` **-49.6%** | `23.38 → 17.61 MB/s` **-24.7%** | `1,197 → 523 MB` **-56.4%** |

적용한 변경:

1. 전체 `Player` 대신 이동 전용 경량 payload 사용
2. 입력마다 즉시 broadcast하지 않고 방의 100ms send tick에서 최신 상태 전송
3. 한 tick에 쌓인 동일 플레이어 입력을 최신값 하나로 coalescing
4. 정지 플레이어의 불필요한 입력 송신 빈도 감소

<!-- > [!IMPORTANT]
> 이 수치는 동일 로컬 환경에서 **최적화 전후의 상대적 변화**를 비교한 결과이며 실제 서비스 동시접속 수용량을 의미하지 않습니다. 이후 추가된 server-authoritative simulation과 인증 payload의 비용은 현재 구조에서 별도로 재측정해야 합니다.

실행 스크립트와 개별 실험은 [`run_load_metrics.sh`](run_load_metrics.sh), [`서버 최적화 리포트`](docs/server_optimization_report.md)에서 확인할 수 있습니다. -->

---

<div align="center">

### Thank You

</div>
