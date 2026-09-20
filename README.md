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

| 장르 | 플랫폼 | 개발 인원 |
|:---:|:---:|:---:|
| 캐주얼 멀티플레이 | Android · iOS | 1인 개발 |

### 게임 방식

- **경찰 2명**은 제한 시간 5분 안에 모든 도둑을 체포해야 합니다.
- **도둑 4명**은 경찰을 피해 살아남고, 감옥에 갇힌 동료를 구출할 수 있습니다.
- 친구들과 방을 만들어 플레이하거나, 랜덤 매칭으로 바로 게임을 시작할 수 있습니다.

---

## ✨ 주요 특징

<table>
  <tr>
    <td width="50%" valign="top">
      <h3>🚨 서로 다른 두 역할</h3>
      <p>경찰 2명과 도둑 4명이 각자의 목표로 한 판을 플레이합니다.</p>
    </td>
    <td width="50%" valign="top">
      <h3>👀 시야와 탈옥</h3>
      <p>건물과 수풀로 경찰의 시야를 피하고, 붙잡힌 팀원은 감옥에서 구출할 수 있습니다.</p>
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <h3>🎯 서버 중심 판정</h3>
      <p>서버가 이동과 게임 판정을 처리하고, 클라이언트 예측으로 조작 지연을 줄였습니다.</p>
    </td>
    <td width="50%" valign="top">
      <h3>🤝 친구 초대와 랜덤 매칭</h3>
      <p>6자리 코드로 친구를 초대하거나 랜덤 매칭으로 바로 참여할 수 있습니다.</p>
    </td>
  </tr>
</table>

---

## 🧩 구성

<div align="center">
  <img width="900" alt="PolRob 시스템 구성도" src="https://github.com/user-attachments/assets/4d60d211-ded0-47f3-8ea0-f756392ef3b5" />
</div>

클라이언트와 게임 서버를 직접 구현했으며, 게임의 성격에 맞춰 HTTP, SignalR, TCP, UDP 통신을 함께 사용했습니다.

---

<div align="center">

### Thank You

</div>
