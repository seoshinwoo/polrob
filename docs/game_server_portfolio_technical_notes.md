# 신입 게임 서버 개발자 포트폴리오 기술 정리

> 분석 기준: 2026-08-30의 로컬 작업 트리. README의 설명뿐 아니라 서버, 클라이언트, 부하 테스트 코드와 저장소 설정을 함께 확인했다.

## 전체 포지셔닝

이 포트폴리오의 중심 문장은 다음과 같이 잡는 것이 좋다.

> 실시간 상호작용에서 발생하는 상태 동기화, 동시성, 인증 경계와 성능 병목을 직접 설계하고 측정해 개선한 C#/.NET 서버 개발자

프로젝트 배치는 다음 순서를 권장한다.

1. **PolRob — 메인 프로젝트:** 게임 서버 직무와 직접 연결되는 서버 권위형 실시간 멀티플레이 게임
2. **CanvaSync — 메인 프로젝트:** SignalR 기반 실시간 동기화, 동시 쓰기 제어, 캐시와 DB의 수명 주기 설계를 보여주는 협업 서비스
3. **LabelSpaceApp — 보조 프로젝트:** WebView와 네이티브 장치 사이의 프로토콜 경계, 플랫폼별 통신과 안정성 처리를 보여주는 상용 앱

기술 이름을 나열하기보다 각 프로젝트를 `문제 → 설계 선택 → 구현 → 검증 → 한계와 다음 개선` 순서로 설명해야 한다.

---

## 1. PolRob

### 한 줄 소개

경찰 2명과 도둑 4명이 한 방에서 플레이하는 모바일 실시간 추격 게임으로, 클라이언트 좌표를 신뢰하지 않는 **서버 권위형 이동·게임 규칙 서버**를 C#/.NET으로 구현했다.

### 기술 스택

- 서버: C#, .NET 10, ASP.NET Core, BackgroundService
- 실시간 통신: Raw TCP, UDP, SignalR, HTTP
- 동시성: `Channel<T>`, `ConcurrentDictionary`, room-scoped single-consumer loop
- 데이터: Azure Cosmos DB
- 클라이언트: .NET MAUI, SkiaSharp
- 음성 통신: LiveKit
- 검증: headless bot, `System.Diagnostics.Metrics`, Bash 기반 반복 부하 테스트

### 핵심 아키텍처

통신을 기능의 전달 보장과 빈도에 따라 네 채널로 나눈 것이 첫 번째 특징이다.

| 채널 | 담당 기능 | 선택 이유 |
|---|---|---|
| HTTP | 가입·로그인, 방 생성·참가·재대결 | 명확한 요청/응답이 필요한 도메인 작업 |
| SignalR | 로비 인원·역할 변경, 매칭 완료, 게임 시작 | 방 그룹 단위 이벤트 전달과 재연결 처리 |
| TCP | 인증된 게임 입장, 초기 상태, 체포·탈옥, 게임 Phase | 순서와 전달 보장이 필요한 상태 전이 |
| UDP | 조이스틱 입력과 서버 위치 snapshot | 과거 데이터보다 최신 상태가 중요한 고빈도 이동 데이터 |

이 구분은 “실시간이라서 모두 WebSocket을 사용했다”가 아니라, **데이터의 의미에 맞춰 프로토콜을 선택했다**는 판단을 보여준다.

### 기술적 특징 1 — 서버 권위형 이동

클라이언트는 좌표가 아니라 조이스틱 입력 벡터와 sequence를 전송한다. 서버는 다음 값을 직접 결정한다.

- 플레이어 속도와 충돌 반지름
- tick 간 경과 시간을 반영한 이동 거리
- 맵 경계와 장애물 충돌
- 이동 가능 여부와 방향
- 체포·수감 상태에서의 이동 잠금
- 입력 timeout 이후 자동 정지

UDP 입력은 다음 순서로 방어한다.

1. 로그인 세션으로 인증된 TCP 입장과 실제 로비 멤버십을 확인한다.
2. TCP 세션에서 별도의 movement token을 발급한다.
3. UDP 패킷의 플레이어 ID가 참여 중인 방과 연결되는지 확인한다.
4. token과 최초 등록 endpoint가 일치하는지 확인한다.
5. `NaN`, `Infinity`, 과도한 입력 벡터를 거부하거나 정규화한다.
6. sequence로 중복·역순 패킷을 제거한다.
7. 플레이어별 token bucket으로 UDP 수신률을 제한한다.

포트폴리오 표현 예시:

> 클라이언트 좌표 대신 입력만 수신하고 서버가 고정 tick에서 이동과 충돌을 계산하도록 설계했습니다. movement token, UDP endpoint, sequence, 유한수 검증과 rate limiting을 적용해 위조·재전송·비정상 입력이 게임 상태에 반영되지 않도록 했습니다.

### 기술적 특징 2 — 방 단위 single-consumer 동시성 모델

네트워크 수신부는 게임 상태를 직접 수정하지 않는다. 각 방이 bounded `Channel<RoomCommand>`를 소유하고, 여러 TCP/UDP producer가 `Join`, `Leave`, `Move` 명령을 기록한다. 방별 비동기 루프 한 개가 이를 순서대로 소비한다.

- `SingleReader=true`, `SingleWriter=false`인 bounded channel로 방별 backpressure 경계를 둔다.
- 입장과 퇴장은 순서가 중요하므로 즉시 처리한다.
- 같은 tick에 쌓인 이동은 플레이어별 최신 명령 하나로 coalescing한다.
- 방이 비면 유예 시간 후 loop와 channel을 정리한다.
- `ConcurrentDictionary`는 개별 컬렉션 접근을, single consumer는 복합 규칙의 처리 순서를 책임진다.

이 구조의 장점은 전역 lock 하나로 모든 방을 직렬화하지 않으면서도, 한 방 안의 복합 상태 전이를 이해하기 쉽게 유지하는 것이다. 특정 방의 부하나 오류를 다른 방과 격리하는 모델이기도 하다.

### 기술적 특징 3 — 주기별 게임 루프 분리

한 방의 loop 안에서도 업무 빈도에 따라 tick을 분리했다.

| 주기 | 처리 |
|---:|---|
| 50ms | command drain, 최신 입력 반영, 서버 이동 simulation |
| 100ms | 위치 snapshot 송신, 시야·체포·탈옥 규칙 |
| 1초 | countdown, 남은 시간, 승패, 전체 게임 상태 |

모든 입력을 즉시 브로드캐스트하지 않고, 서버가 송신 빈도를 통제하면서 최신 authoritative state만 전송한다. 이는 입력 수신률과 상태 배포률을 분리해 불필요한 fan-out을 줄인 설계다.

### 기술적 특징 4 — 서버 권위형 게임 규칙과 정보 필터링

게임 결과에 영향을 주는 규칙을 서버가 계산한다.

- 시야 거리와 90도 시야각을 이용한 상대 탐지
- 벽, 건물, 원형 장애물, 다각형 장애물과 선분 교차를 이용한 line-of-sight 차단
- 일정 시간 접촉을 유지해야 완료되는 체포
- 감옥 배치, 수감 시 이동 잠금, 구조자의 거리·시간 기반 탈옥 진행률
- 제한 시간과 전체 도둑 수감 여부에 따른 승패
- 같은 팀 또는 실제로 시야에 들어온 상대에게만 위치를 전송하는 recipient filtering
- 시야 밖 상대는 좌표나 ID 대신 거리 단계만 전달하는 근접 알림

이 부분은 게임 서버 포트폴리오에서 단순 채팅/동기화 프로젝트와 차별되는 핵심이다. 특히 **판정 권한과 정보 공개 범위를 서버가 통제한다**는 점을 함께 설명하는 것이 좋다.

### 기술적 특징 5 — 매칭과 방 lifecycle

- 경찰 2명, 도둑 4명의 역할 정원을 만족시키는 랜덤 매칭
- 6자리 코드 기반 커스텀 방
- 방장만 가능한 커스텀 게임 시작
- 역할 변경, 매칭 취소, 게임 종료와 재대결
- SignalR 연결 종료 후 10초 유예를 두고 최신 connection ID와 비교하는 재접속 처리
- 매칭 직후 이탈 시 게임 시작을 취소하고 rematching 상태로 복구
- 빈 커스텀 방 만료와 게임 loop 정리

재접속 처리에서 `connectionId → room/user`와 `room/user → latest connectionId`의 양방향 인덱스를 둔 점은 면접에서 설명하기 좋은 세부 설계다.

### 기술적 특징 6 — 측정 가능한 부하 테스트 환경

화면이 있는 앱을 여러 개 실행하지 않고, 실제 HTTP 로그인 → SignalR 랜덤 매칭 → TCP 입장 → UDP 이동 → 게임 종료 흐름을 수행하는 headless bot을 구현했다.

서버가 1초마다 기록하는 지표:

- UDP/TCP packets와 UDP bytes
- JSON serialization 횟수
- 연결·플레이어·방·Phase와 command queue 길이
- invalid, duplicate/late, rate-limited, dropped 패킷
- CPU, working set, GC allocation/pause
- lock contention, ThreadPool queue/thread
- bot 연결 성공과 실패 수

부하 스크립트는 60/300/600/900 bot을 반복 실행하고, 모든 예상 방이 `Playing`이며 실패 봇이 없는 구간만 eligible sample로 인정한다. 그중 트래픽이 높은 연속 20초 구간을 같은 규칙으로 집계한다.

### 성능 개선 결과

현재 README에는 baseline `32620197`과 결합 최적화 커밋 `a7441c4`의 동일 로컬 환경 비교가 다음과 같이 기록돼 있다.

| 부하 | CPU | Total PPS | UDP bytes/s | GC allocation | Working Set |
|---:|---:|---:|---:|---:|---:|
| 600 bots / 100 rooms | 13.85 → 1.90, **86.3% 감소** | 33,654 → 22,951, **31.8% 감소** | 6.14 → 2.08MB, **66.1% 감소** | 22.29 → 11.83MB/s, **46.9% 감소** | 908 → 397MB, **56.3% 감소** |
| 900 bots / 150 rooms | 13.89 → 2.93, **78.9% 감소** | 33,762 → 34,367, 1.8% 증가 | 6.22 → 3.13MB, **49.6% 감소** | 23.38 → 17.61MB/s, **24.7% 감소** | 1,197 → 523MB, **56.4% 감소** |

적용한 개선은 다음과 같다.

1. 전체 `Player` 대신 위치 동기화 전용 경량 DTO 전송
2. 입력 수신 때마다 broadcast하지 않고 방별 100ms send tick에서 최신 상태 전송
3. 한 tick에 쌓인 동일 플레이어 이동 입력 coalescing
4. 정지 중인 클라이언트의 이동 입력 송신 주기 감소

결과를 설명할 때는 “900명을 수용한다”가 아니라 다음처럼 표현해야 한다.

> 동일한 로컬 환경과 workload에서 네트워크 처리 경로의 before/after를 비교했습니다. 600 bot 구간에서 CPU 86.3%, UDP 전송량 66.1%, working set 56.3%를 줄였지만, 이 수치는 실제 서비스 수용 인원을 뜻하지 않습니다.

각 최적화가 줄이는 비용이 다르다는 해석도 중요하다.

- 경량 DTO: packet size와 serialization/GC 비용 감소
- send tick: server fan-out 횟수와 CPU 감소
- input coalescing: 수신 PPS가 아니라 서버 내부 상태 적용 비용 감소
- idle send-rate: 실제 플레이 패턴에서 불필요한 client-to-server 입력 감소

### 면접에서 강조할 문제 해결 이야기

1. “UDP가 빠르다”에서 끝내지 않고 인증, endpoint 고정, sequence, rate limit을 결합한 이유
2. `ConcurrentDictionary`만으로 복합 게임 규칙의 thread safety가 해결되지 않는 이유
3. 이동 입력은 coalescing해도 되지만 Join/Leave는 순서를 보존해야 하는 이유
4. 입력 수신 tick, simulation tick, snapshot tick을 분리한 이유와 지연/트래픽 trade-off
5. 900 bot에서 PPS가 소폭 증가했어도 CPU·메모리 개선을 별도 지표로 해석한 과정
6. 로컬 benchmark를 실제 CCU 수용량으로 일반화하지 않은 이유

### 제출 전 보강할 점

- 현재 저장소에는 README의 최종 수치를 재현한 raw CSV가 보존되어 있지 않다. 결과 CSV, 실행 환경의 CPU/OS/.NET 버전, commit hash를 저장소에 함께 남기는 것이 좋다.
- `docs/server_optimization_report.md`의 “Final combined result = TBD” 문구와 README의 최종 결합 수치가 충돌하므로 문서를 일치시켜야 한다.
- 서버 빌드는 성공했지만 `Microsoft.OpenApi 2.0.0`의 high severity 취약점 경고가 발생한다. 제출 전 의존성 업데이트 또는 영향 분석 기록이 필요하다.
- 로그인 세션은 현재 프로세스 메모리에 있으므로 서버 재시작·다중 인스턴스 환경의 세션 저장 전략은 후속 과제로 명시하는 편이 정확하다.
- 자동화된 게임 규칙 단위/통합 테스트보다 headless scenario test 비중이 크다. 이동 검증, Phase 전이, 체포·탈옥, 재접속을 deterministic test로 추가하면 더 강해진다.

---

## 2. CanvaSync

### 한 줄 소개

교수자의 PDF 위 필기 변경을 같은 강의방의 학생들에게 실시간 전파하고, 실시간 상태와 영속 상태를 분리해 관리하는 Blazor 기반 협업 필기 서비스다.

### 기술 스택

- C#, .NET 9, ASP.NET Core, Blazor Server/WebAssembly
- SignalR + MessagePack
- PostgreSQL + EF Core + `jsonb`
- Redis 또는 InMemory cache
- Azure Blob Storage
- SkiaSharp, PDFsharp, PDFtoImage
- Cookie authentication, BCrypt

### 기술적 특징 1 — 화면이 아닌 변경 이벤트 동기화

완성된 캔버스 이미지를 매번 전송하지 않고 도형 단위의 `Add`, `Update`, `End`, `Delete` 이벤트를 전송한다.

```text
사용자 입력
→ SkiaSharp Factor를 직렬화 가능한 FactorDto로 변환
→ SignalR MessagePack 메시지 전송
→ lecture:{lectureId} 그룹에 broadcast
→ 클라이언트 페이지 상태에 이벤트 반영
→ canvas와 필요한 thumbnail만 다시 렌더링
```

- 사각형, 원, 선, 텍스트는 box/paint/font 속성으로 변환한다.
- 자유곡선은 `SKPath`를 SVG path data로 변환해 전송한다.
- 연속 drag의 `Update`마다 thumbnail을 만들지 않고 `End` 또는 `Delete` 시점에 생성한다.
- SignalR JSON 대신 MessagePack을 사용한다.

게임 서버와 연결되는 역량은 “전체 상태를 계속 보내지 않고 상태 변화 이벤트를 모델링했다”는 점이다.

### 기술적 특징 2 — 동일 페이지의 동시 쓰기 직렬화

SignalR Hub는 요청마다 인스턴스가 달라질 수 있으므로, `lectureId:pageIndex`를 키로 하는 정적 `ConcurrentDictionary<string, SemaphoreSlim>`을 둔다. 같은 페이지의 read-modify-write 구간은 semaphore 하나만 통과시켜 컬렉션 손상과 lost update 가능성을 줄였다. 서로 다른 페이지는 독립적으로 처리할 수 있다.

브로드캐스트와 저장은 불변 DTO를 기준으로 `Task.WhenAll`에서 병렬 실행해, 네트워크 전파가 cache write를 불필요하게 기다리지 않게 했다.

### 기술적 특징 3 — 실시간 cache와 durable DB의 수명 주기 분리

| 데이터 | 저장소 | 역할 |
|---|---|---|
| 진행 중인 교수자 필기 | Redis(개발) 또는 InMemory(배포) | 빈번한 실시간 read/write |
| 저장된 사용자별 필기 | PostgreSQL `jsonb` | 재접속·재조회용 snapshot |
| 회원·강의·참여 관계 | PostgreSQL | 관계와 무결성 |
| PDF 원본 | private Azure Blob Storage | 대용량 binary object 관리 |

교수자 연결이 종료되면 cache의 현재 필기를 PostgreSQL에 저장하고, Redis 데이터에는 2시간 TTL을 설정해 늦게 남아 있는 학생에게 상태를 제공한 뒤 정리한다. 학생의 개인 필기는 교수자 필기와 분리해 저장한다.

이 설계는 게임 서버 관점에서 live session state와 persistent profile/history를 분리하는 사고와 연결할 수 있다.

### 기술적 특징 4 — DB 무결성과 경합 처리

DB가 애플리케이션의 불변식을 직접 보장하도록 다음 제약을 두었다.

- `Members.Name` unique index
- `Lectures.Code` unique index와 6자리 길이 제한
- `DrawingData(LectureId, MemberId)` unique index
- 강의·사용자 삭제 시 drawing snapshot cascade delete

6자리 강의 코드는 `RandomNumberGenerator`로 생성하고, 사전 조회 후 다른 요청이 먼저 같은 코드를 저장하는 race가 발생하면 unique violation을 확인해 재시도한다.

필기 저장은 일반적인 `SELECT → UPDATE/INSERT` 대신 다음 순서로 처리한다.

1. `(LectureId, MemberId)` 조건으로 `ExecuteUpdateAsync`
2. 변경된 행이 없으면 INSERT
3. 동시 INSERT의 unique conflict가 발생하면 UPDATE 재시도

읽기 화면은 `AsNoTracking()`과 projection을 사용해 불필요한 change tracking과 전체 graph 로딩을 줄였다.

### 기술적 특징 5 — PDF와 drawing overlay 분리

- 원본 PDF는 DB byte column이 아니라 Azure Blob Storage에 private blob으로 저장한다.
- 클라이언트는 PDF를 페이지 이미지로 변환해 SkiaSharp canvas의 배경으로 사용한다.
- 교수자 필기와 학생 개인 필기는 별도 factor layer로 관리한다.
- 다운로드 시 factor DTO를 투명 PDF overlay로 렌더링하고 원본 PDF 각 페이지 위에 합성한다.
- 출력 요청에서는 불필요한 base64 이미지 데이터를 제거해 multipart payload가 수십 MB로 커지는 문제를 방지한다.

### 기술적 특징 6 — 인증과 리소스 접근 경계

- 비밀번호는 BCrypt hash로 저장한다.
- 로그인 후 cookie에 `NameIdentifier` claim을 넣는다.
- lecture API는 request body의 member ID 대신 인증 claim에서 사용자 ID를 확정한다.
- 강의 조회·저장·삭제 전에 참여 여부 또는 host 여부를 확인한다.
- SignalR 연결 시 강의 접근 권한을 확인한 뒤 `lecture:{lectureId}` 그룹에 넣는다.

### 면접에서 강조할 문제 해결 이야기

1. 전체 캔버스 이미지가 아니라 도형 변경 이벤트를 동기화한 이유
2. `ConcurrentDictionary` 안에 mutable `List`가 있을 때 dictionary만으로 안전하지 않은 이유
3. semaphore의 범위를 전체 서비스가 아니라 페이지 단위로 잡은 이유
4. live cache와 PostgreSQL snapshot의 저장 시점을 분리한 이유
5. 사전 중복 조회만으로 6자리 코드 uniqueness를 보장할 수 없는 이유
6. DB upsert 경합을 unique constraint와 retry로 마무리한 과정

### 게임 서버 직무와의 연결

CanvaSync는 게임은 아니지만 다음 역량을 보완한다.

- 방/강의 단위 SignalR group broadcasting
- high-frequency live state와 durable state 분리
- 동시 이벤트의 순서와 race condition 처리
- DTO 기반 network serialization
- 접속/퇴장에 따른 상태 저장과 TTL lifecycle
- relational DB의 index, constraint, query pattern 설계

### 제출 전 보강할 점

- 현재 운영 환경은 Redis가 아니라 InMemory 구현을 사용하므로 다중 인스턴스에서 상태가 공유되지 않는다. “Redis로 수평 확장을 완료했다”라고 쓰면 안 되며, 분산 lock과 SignalR backplane까지 포함한 후속 설계로 제시해야 한다.
- 페이지 semaphore는 프로세스 내부에서만 유효하다. Redis 기반 다중 인스턴스 환경에서는 동일한 보장을 하지 못한다.
- `CanvasHub.SendDrawings`는 현재 연결 사용자가 교수자인지, payload의 lecture ID가 연결 시 승인된 강의와 같은지 다시 검증하지 않는다. 포트폴리오에서 완전한 서버 권위형 권한 모델이라고 주장하기 전에 보강해야 한다.
- factor index 기반 이벤트는 동시 add/delete와 재연결 상황에서 순서가 어긋날 수 있다. factor별 stable ID, operation sequence/version, idempotency 정책을 추가하면 게임 상태 동기화 역량과 더 잘 연결된다.
- `PdfController.GetPdf`는 인증 여부만 확인하고 해당 강의의 참여 권한은 확인하지 않는다. 리소스 단위 authorization을 일관되게 적용해야 한다.
- 자동 테스트 프로젝트가 보이지 않는다. Hub event ordering, DB unique race, host disconnect persistence를 통합 테스트로 추가하는 것이 좋다.
- 현재 머신에는 .NET 9 targeting pack이 없어 전체 빌드를 재검증하지 못했다. 제출 전 고정된 SDK/CI 환경에서 clean build를 남기는 것이 좋다.

---

## 3. LabelSpaceApp

### 한 줄 소개

라벨 제작 웹 서비스를 Android, iOS, Windows, Mac Catalyst의 네이티브 인쇄·파일 선택·Bluetooth 기능과 연결하는 .NET MAUI 기반 하이브리드 앱이다.

게임 서버 포트폴리오의 메인으로 두기보다는, **외부 시스템과의 프로토콜 경계와 실패 처리 역량**을 보여주는 보조 프로젝트로 1페이지 이내에 정리하는 것이 좋다.

### 기술 스택

- C#, .NET 10, .NET MAUI
- Android Classic Bluetooth SPP/RFCOMM
- iOS CoreBluetooth 계열 BLE(Shiny.BluetoothLE)
- Windows WebView2, Win32 print spooler P/Invoke
- Mac Catalyst WKWebView bridge
- TSPL raw label printing
- NUnit

### 기술적 특징

#### 하나의 공통 인터페이스와 플랫폼별 구현

`IBluetoothService`, `IDeviceScanner`, `IDesktopPrinterService`, `IWebViewBridge` 뒤에 플랫폼 구현을 두고 compile target별 DI로 연결했다.

- Android: Bluetooth Classic SPP socket, secure 연결 실패 시 insecure fallback
- iOS: BLE service/characteristic 탐색 후 writable characteristic에 chunk 전송
- Windows: 설치된 iDPRT driver allowlist만 노출하고 Win32 spooler에 RAW TSPL 전송
- Mac Catalyst: 웹 메시지 bridge를 제공하되 아직 native label printing은 명시적으로 unsupported 처리

#### WebView–native 메시지 프로토콜

- 웹은 `getPrinters`, `print` JSON 메시지를 전송한다.
- native processor가 request ID를 유지한 `printerList`, `printResult`, `nativeError` 응답을 돌려준다.
- Windows와 Mac은 `https://space.label.kr`의 scheme/host/port를 모두 확인해 신뢰한 origin의 메시지만 처리한다.
- JSON depth와 전체 메시지, 페이지, job 크기, 페이지 수를 제한한다.
- Base64를 검증한 뒤에만 raw TSPL byte로 변환한다.

#### 대용량·부분 전송과 실패 복구

- 모바일 인쇄는 semaphore로 동시에 한 작업만 허용한다.
- WebView의 print queue를 한 페이지씩 dequeue해 메모리와 장치 buffer 압력을 제한한다.
- Android는 1,024 byte, iOS BLE는 100 byte chunk로 나눠 전송한다.
- 페이지 이탈 시 cancellation을 전달하고 `finally`에서 Bluetooth 연결과 lock을 정리한다.
- Windows는 64KB 단위 `WritePrinter`의 실제 기록 byte 수를 확인해 partial write를 끝까지 처리한다.
- spool 과정 실패 시 `AbortPrinter`, 성공 시 `EndPagePrinter`와 `EndDocPrinter`로 resource lifecycle을 닫는다.
- `SafeHandle`과 pinned buffer의 `finally` 해제를 사용한다.

#### 운영 환경 처리

- network 상태 및 WebView navigation 실패를 감지해 offline page로 전환하고 재연결 시 reload한다.
- Android/iOS에서 웹의 file input을 camera, gallery, file picker와 연결한다.
- iOS signing과 store 배포 script, 플랫폼별 application ID와 runtime target을 관리한다.

#### 테스트

공통 desktop print message processor에 대해 다음을 NUnit으로 검증한다.

- printer list JSON 계약
- Base64 decode와 job name
- malformed JSON과 invalid Base64
- 동시 인쇄 거부
- 최대 페이지 수 제한
- printer service 실패 후 semaphore 해제

### 면접에서 강조할 문제 해결 이야기

1. 동일한 “Bluetooth 인쇄”라도 Android SPP와 iOS BLE의 통신 모델이 다른 점
2. 장치 buffer를 고려해 payload를 chunking하고 print queue를 직렬화한 이유
3. WebView 메시지가 로컬 장치 제어로 이어지므로 origin과 입력 크기를 검증한 이유
4. Win32 API의 partial write, unmanaged buffer, handle lifecycle을 안전하게 처리한 과정

### 제출 전 보강할 점

- git 이력상 여러 contributor가 있으므로 담당 범위와 본인이 구현한 부분을 명확히 적어야 한다.
- `myapp.keystore`가 git tracked file이다. 공개 저장소에 올리기 전에 실제 배포 key인지 확인하고, 민감한 key라면 즉시 폐기·재발급 후 history에서 제거해야 한다.
- 현재 로컬 `global.json`이 설치되지 않은 workload version `10.0.300`을 가리켜 테스트를 실행하지 못했다. CI 결과나 정상 SDK 환경의 test output을 별도 증빙으로 남기는 것이 좋다.
- Mac native printing과 Windows 실기 검증은 문서상 후속 과제다. 완료된 기능처럼 표현하지 않아야 한다.

---

## 세 프로젝트를 하나의 성장 서사로 연결하는 법

| 역량 | PolRob | CanvaSync | LabelSpaceApp |
|---|---|---|---|
| 실시간 상태 동기화 | TCP/UDP tick과 snapshot | SignalR drawing event | WebView/native request-response |
| 동시성 | 방별 single consumer | 페이지별 semaphore | 인쇄 작업 single-flight |
| 상태 수명 주기 | 방 생성·매칭·게임·종료 | cache → DB snapshot → TTL | scan/connect/print/disconnect |
| 보안 경계 | 세션·멤버십·movement token | cookie claim·lecture access | trusted origin·payload validation |
| 성능 | 60~900 bot 측정과 최적화 | event 전송, thumbnail/이미지 최적화 | chunk/queue 기반 장치 전송 |
| 실패 처리 | queue drop·rate limit·disconnect | unique conflict retry·cache fallback | cancellation·abort·resource cleanup |

추천 성장 서사는 다음과 같다.

> CanvaSync에서 실시간 이벤트와 공유 상태의 동시성 문제를 다뤘고, LabelSpaceApp에서 외부 장치와 프로토콜 경계의 실패 처리를 경험했습니다. 이를 PolRob에서는 서버 권위형 이동, 방 단위 게임 loop, TCP/UDP 분리와 부하 테스트까지 확장해 게임 서버 문제로 구체화했습니다.

---

## 포트폴리오 페이지 구성안

### 첫 페이지 — 개발자 소개

> C#/.NET을 중심으로 실시간 상태 동기화와 동시성 문제를 해결해 왔습니다. 기능 구현에서 멈추지 않고, 서버가 신뢰할 데이터의 경계를 정의하고 실제 workload를 재현해 CPU·네트워크·GC 지표로 개선 효과를 검증하는 개발자를 지향합니다.

핵심 키워드:

`C#` · `.NET` · `ASP.NET Core` · `TCP/UDP` · `SignalR` · `Server-Authoritative` · `Concurrency` · `PostgreSQL/Redis` · `Load Test`

### PolRob — 4~5페이지

1. 게임과 담당 범위, 전체 architecture
2. HTTP/SignalR/TCP/UDP 분리와 인증 흐름
3. room-scoped loop와 authoritative movement sequence
4. server-side rule과 visibility filtering
5. bot workload, 병목 가설, before/after 결과와 한계

### CanvaSync — 2~3페이지

1. drawing event와 SignalR group flow
2. 페이지 동시성, cache/DB lifecycle
3. DB constraint/upsert race와 PDF overlay

### LabelSpaceApp — 1페이지

플랫폼 추상화, WebView-native protocol, Bluetooth/Windows spooler의 안정성 처리만 선별한다.

### 마지막 페이지 — 회고와 다음 단계

- deterministic game rule tests
- UDP binary protocol과 serialization allocation 추가 최적화
- distributed session/cache/lock 설계
- packet loss, latency, jitter 환경에서 prediction/interpolation 품질 검증
- container/CI 기반 재현 가능한 benchmark

---

## 이력서용 압축 문장

### PolRob

- HTTP·SignalR·TCP·UDP를 데이터 성격에 따라 분리하고, 입력만 신뢰하는 서버 권위형 이동·충돌·체포·탈옥 로직 구현
- bounded `Channel<T>` 기반 room-scoped single-consumer loop로 방별 상태를 격리하고 이동 입력 coalescing 및 주기별 snapshot broadcast 적용
- 실제 매칭과 게임 프로토콜을 수행하는 60~900개 headless bot workload를 구축하고 CPU·PPS·UDP bytes·GC·working set 측정 자동화
- 동일 로컬 workload의 600 bot 구간에서 baseline 대비 CPU 86.3%, UDP bytes 66.1%, working set 56.3% 감소

### CanvaSync

- SignalR group과 MessagePack을 이용해 PDF 필기의 Add/Update/End/Delete event를 강의방 단위로 실시간 동기화
- 페이지별 `SemaphoreSlim`으로 동일 상태의 동시 변경을 직렬화하고 live cache, PostgreSQL `jsonb` snapshot, Azure Blob PDF의 수명 주기 분리
- unique constraint와 update-insert-retry 흐름으로 6자리 강의 코드 및 사용자별 drawing upsert의 동시 요청 race 처리

### LabelSpaceApp

- WebView 메시지를 Android/iOS Bluetooth 및 Windows RAW print spooler와 연결하는 MAUI 플랫폼 추상화 구현
- origin 검증, payload size 제한, 인쇄 single-flight, chunk 전송, cancellation과 native resource cleanup으로 장치 통신 안정성 강화

---

## 최종 체크리스트

- 수치에는 workload, 비교 commit, 실행 환경, 표본 선택 조건을 함께 적는다.
- “900명 동시 접속 지원” 대신 “900 bot workload에서 측정”이라고 적는다.
- 본인 담당 범위와 팀 규모를 프로젝트마다 별도로 적는다.
- 아직 구현되지 않은 Redis 운영 확장, Mac 인쇄, 분산 lock은 다음 단계로 분리한다.
- architecture diagram에는 기술 로고보다 데이터 흐름과 신뢰 경계를 표시한다.
- 코드 링크는 핵심 파일 2~4개만 선별하고, 면접에서 설명할 line을 미리 정한다.
- 공개 전 appsettings, keystore, signing 정보와 git history의 secret을 점검한다.
