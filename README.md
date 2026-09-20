<div align="center">

<img width="1916" height="821" alt="PolRob - Police vs Robber" src="https://github.com/user-attachments/assets/e2b3b52f-91d4-499f-911c-a9c6e1b355a9" />

# PolRob

### 경찰과 도둑의 쫓고 쫓기는 실시간 추격전!

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

## 🎮 게임 소개

**장르** : 캐주얼 멀티플레이 게임  
**플랫폼** : Android · iOS  
**개발 인원** : 1인 개발

### 게임 방식

- **경찰 2명**은 제한 시간 5분 안에 모든 도둑을 체포해야 합니다.
- **도둑 4명**은 경찰을 피해 살아남고, 감옥에 갇힌 동료를 구출할 수 있습니다.
- 친구들과 방을 만들어 플레이하거나, 랜덤 매칭으로 바로 게임을 시작할 수 있습니다.

---

## ✨ 주요 특징

### 🎯 서버 중심의 이동과 판정

클라이언트는 조이스틱 입력만 전달하고, 실제 이동과 충돌은 서버가 계산합니다. 이를 통해 모든 플레이어에게 동일한 기준으로 게임 상태를 전달하고 잘못된 위치 값도 방지했습니다. 클라이언트에는 로컬 예측을 적용해 서버 응답을 기다리는 동안에도 조작이 자연스럽게 이어지도록 했습니다.

### 👀 시야·체포·탈옥

경찰의 거리와 시야각뿐 아니라 건물, 벽, 연못, 수풀 같은 장애물까지 반영해 도둑의 노출 여부를 판단합니다. 경찰이 일정 시간 접촉해야 체포가 완료되며, 남아 있는 도둑은 감옥으로 이동해 동료를 탈옥시킬 수 있습니다. 체포와 탈옥 진행 상황, 이동 제한, 최종 승패는 모두 서버에서 관리합니다.

### 🏠 방 단위 게임 처리

각 게임방에는 독립적인 명령 처리 흐름이 있으며, 이동과 게임 규칙을 들어온 순서대로 처리합니다. 여러 방이 동시에 실행되어도 서로의 상태에 영향을 주지 않도록 게임 데이터를 방 단위로 분리했습니다. 이동과 주요 이벤트는 필요한 주기에 맞춰 갱신해 불필요한 처리를 줄였습니다.

### 🔐 연결 전반의 사용자 검증

로그인에서 발급한 세션을 HTTP, 로비 연결, 게임 입장 과정에서 계속 검증합니다. 클라이언트가 보낸 사용자 정보를 그대로 신뢰하지 않고, 방 멤버십과 역할, 방장 권한을 서버의 데이터로 확인합니다. 게임 이동에 사용하는 연결도 별도의 토큰과 접속 정보를 확인하도록 구성했습니다.

### 🤝 랜덤 매칭과 커스텀 방

혼자 접속했을 때는 경찰 2명과 도둑 4명 구성에 맞춰 랜덤 매칭에 참여할 수 있습니다. 친구들과 플레이할 때는 6자리 코드로 방을 만들고 참가하며, 방장이 역할을 조정하거나 게임을 시작할 수 있습니다. 연결이 잠시 끊긴 플레이어의 재접속과 게임 종료 후 재대결도 지원합니다.

### 📊 실제 게임 흐름 기반 부하 테스트

화면을 직접 실행하지 않는 테스트용 봇을 만들어 로그인과 매칭, 이동, 게임 종료까지 실제 플레이 흐름을 반복했습니다. 최대 900개의 봇을 동시에 실행하며 서버와 네트워크 사용량을 확인했습니다. 그 결과 중복된 이동 입력을 합치고 전송 데이터와 패킷 수를 줄이는 방식으로 처리 구조를 개선했습니다.

---

## 🧩 구조

<div align="center">
  <img width="900" alt="PolRob 시스템 구성도" src="https://github.com/user-attachments/assets/4d60d211-ded0-47f3-8ea0-f756392ef3b5" />
</div>

클라이언트와 게임 서버를 직접 구현했으며, 게임의 성격에 맞춰 HTTP, SignalR, TCP, UDP 통신을 함께 사용했습니다.

---

<div align="center">

### Thank You

</div>
