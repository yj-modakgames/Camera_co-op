# 08. Phase 3a 설계 — 온라인 4인 네트워킹 기반 (Netplay)

> 작성 2026-08-26 · 상태: **구현 완료** (main `75b0af6`. DoD: N-1~N-4 통과, N-7 조건부 통과, N-5는 Steam 미설치로 이연 — docs/09 인계. 채점 9.6/10)
> 전제: Phase 2 완료 (드로잉 메카닉, main `7bc8f86`). 제품 방향: **4인 온라인 협동 미니게임 컬렉션** (릴레이 그림 맞추기, 색칠하기 등 — 게임 규칙은 3b/3c에서).
> Phase 분해: **3a 네트워킹 기반 (이 문서)** → 3b 미니게임 프레임워크 → 3c 첫 미니게임.

## 1. 목표와 범위

Steam 로비로 모인 **최대 4인**이 한 캔버스에서 서로의 손 커서와 스트로크를 실시간으로 본다. 게임 규칙 없음 — "함께 그려지는 상태"까지가 3a다.

- 스택: **Facepunch.Steamworks** (Steam Lobby + Steam Sockets/SDR relay). 출시 스택과 동일 (사용자 결정 — NGO/Photon/FishNet 배제)
- topology: **host 중계 star** — 전원이 host에만 연결, host가 이벤트를 rebroadcast. 정본 순서 = host의 rebroadcast 순서 (풀 메시는 검토 후 배제: 연결 관리 4배 대비 1 hop 지연 이득이 체감 불가)
- 로컬 입력 파이프라인 (Python→UDP→Unity)은 **무수정** — 네트워크 계층은 `HandCursorController` 이벤트를 추가 구독만 한다 (docs/01 §4 약속 유지)

### 명시적 배제 (3b+/Phase 4+)

host migration, 텍스트/음성 채팅, 관전 모드, 재접속 상태 복구(스냅샷은 신규 참가 시에만), 바이너리 직렬화(JSON 시작 — 측정 후 필요 시에만), 게임 규칙 일체, Steam 도전과제/클라우드/리치 프레즌스.

## 2. 아키텍처

```
Assets/_CameraCoop/Scripts/Netplay/
  INetTransport.cs     — transport 추상화: 연결·해제·송수신·피어 join/leave 이벤트
  SteamTransport.cs    — Facepunch: Lobby 생성/참가/초대 + Steam Sockets (reliable/unreliable 채널)
  LoopbackTransport.cs — 가짜 피어 시뮬레이션 (일상 개발·자동 검증용, Steam 불필요)
  NetProtocol.cs       — 메시지 정의 + JSON 직렬화/역직렬화 + seq 폐기 판정 (순수 — EditMode 테스트 대상)
  NetSession.cs        — 세션 상태 머신 (Idle→Lobby→InGame), 피어 목록, host 중계, 스냅샷 송수신
  RemotePresenter.cs   — 원격 커서 표시(플레이어 색) + 원격 스트로크 재생 (MonoBehaviour)
```

- 참조 방향: `NetSession → HandCursorController` (이벤트 구독), `NetSession → INetTransport`, `RemotePresenter → NetSession`. 기존 컴포넌트는 netplay의 존재를 모른다.
- 송신 흐름: 로컬 `OnPinchStart/Move/End` + 커서 위치 → `NetSession`이 프로토콜 메시지로 변환·송신. **로컬 화면 표시는 기존 `DrawingController`가 그대로 담당** (로컬 반응성은 네트워크와 무관).
- 수신 흐름: transport → `NetSession` 역직렬화·중계 → `RemotePresenter`가 원격 커서·스트로크 표시.

## 3. 프로토콜 (network v1)

envelope: `{ "v": 1, "type": "<메시지명>", "sender": "<playerId>", "payload": { ... } }`
playerId = SteamID 문자열 (loopback에서는 `"fake-1"` 등). 좌표는 전부 **정규화 [0,1], 원점 좌상단** (docs/02 §3와 동일 좌표계 — 해상도 독립).

| 메시지 | 채널 | payload | 비고 |
|---|---|---|---|
| `Hello` | reliable | `{ name }` | 참가 직후 host에게 |
| `Welcome` | reliable | `{ players[], snapshot }` | host→신규. snapshot = 확정 스트로크 리스트 |
| `CursorUpdate` | **unreliable** | `{ hand, x, y, pinched, seq }` | ~15Hz. 수신 측은 (playerId, hand)별 마지막 seq 이하 폐기 (`PacketFilter` 패턴 재사용) |
| `StrokeStart` | reliable ordered | `{ strokeId, hand, x, y }` | strokeId = `"{playerId}:{로컬카운터}"` — 전역 유일 |
| `StrokePoints` | reliable ordered | `{ strokeId, points[] }` | **100ms 묶음 배치** (점당 1메시지 금지) |
| `StrokeEnd` | reliable ordered | `{ strokeId }` | 점 2개 미만 폐기 규칙은 각 수신 측에서 동일 적용 |
| `ClearCanvas` | reliable ordered | `{}` | host 경유 rebroadcast가 정본 |
| `PeerJoined` / `PeerLeft` | reliable | `{ playerId, name }` | host가 브로드캐스트 |

- 스트로크 색: playerId 기반 팔레트 (4인 고정 4색) — 3a에서는 손별 색 대신 **플레이어별 색** (게임 정체성). 로컬 `DrawingController`의 좌청/우주황은 로컬 전용 표시로 유지하되, 3b에서 통합 여부 재검토.
- 게임 이벤트(턴, 제시어, 정답 등)는 3b에서 `type`만 추가 — envelope·채널 규칙 동일.

## 4. 엣지 케이스

| 상황 | 처리 |
|---|---|
| host 이탈 | 전원 세션 종료 → 로비 화면 복귀 (host migration 배제) |
| 일반 피어 이탈 | 커서 제거, 확정 스트로크 보존, 진행 중 스트로크 강제 End |
| 늦은 참가 | `Welcome.snapshot`으로 캔버스 재생 후 실시간 합류 |
| 로컬 손 lost | `CursorUpdate` 송신 정지 → 수신 측은 0.5초 무수신 시 해당 커서 fade (기존 lostTimeout 패턴) |
| 커서 역전/유실 | seq 폐기. 유실은 다음 update가 덮는다 |
| 중복 `StrokeStart` (같은 strokeId) | 멱등 — 이미 존재하면 무시 |
| Steam 미실행/로비 실패 | 명확한 에러 UI + Loopback 모드 안내 (조용한 실패 금지) |

## 5. 씬과 UI

`Scenes/NetplayTest.unity` — 기존 씬 무수정.

- 최소 로비 UI: Host 버튼 / Join(친구 목록·초대 수락) / 피어 목록 텍스트 / Loopback 모드 토글
- 인게임: 공유 캔버스 (Phase 2 구성 재사용) + 원격 커서 2~3개 + 플레이어 색 범례
- 이 씬에는 UI 입력이 필요하므로 **EventSystem 허용** (docs/04 §1의 금지는 커서 전용 씬에 대한 것 — 이 문서로 명시 갱신)

## 6. 테스트

| 층 | 방법 |
|---|---|
| EditMode (순수) | `NetProtocol` 직렬화 왕복 / strokeId 유일성·파싱 / 커서 seq 폐기 / 스냅샷 병합 멱등성 |
| Loopback 통합 | 가짜 피어 3명이 스크립트된 커서·스트로크 재생 → 캔버스에 4인분 표시 자동 검증 (이 Mac 한 대로 가능) |
| 실 Steam | AppID 480 (`steam_appid.txt`), **두 번째 기기 + 별도 Steam 계정 필요** — 2인 상호 드로잉 수동 검증. 기기 확보 시점은 사용자와 조율 |

## 7. 의존성

- Facepunch.Steamworks DLL + native 라이브러리 (`Assets/Plugins/`: macOS `libsteam_api.dylib`, Windows `steam_api64.dll`) — repo 커밋
- 개발용 `steam_appid.txt` = 480 (프로젝트 루트에 커밋 — 공개 테스트 ID라 무해. 출시 AppID 발급 시 교체)
- Steam 클라이언트 실행 + 로그인 필요 (SteamTransport 경로만. Loopback은 불필요)

## 8. DoD

| # | 기준 | 확인 방법 |
|---|---|---|
| N-1 | Loopback 4인: 가짜 피어 3 + 로컬 1의 커서·스트로크가 한 캔버스에 색 구분 표시 | 자동 (eval 검증 + 육안) |
| N-2 | 늦은 참가 스냅샷: 그려진 캔버스에 가짜 피어가 늦게 합류해도 전체 재생 | 자동 |
| N-3 | 피어 이탈: 커서 제거 + 스트로크 보존 + 진행 중 스트로크 종료 | 자동 |
| N-4 | EditMode 테스트 전체 pass (기존 48 + 신규) | `unity cmd run_tests` |
| N-5 | 실 Steam 2인: 로비 생성→초대→참가→상호 드로잉 | 수동 (두 번째 기기 확보 시) |
| N-6 | QUALITY_CHECKLIST ≥ 9.0 | 채점 보고 |
| N-7 | 웹캠 입력 포함 10분 세션 에러·누수 없음 (Loopback) | 콘솔 + 메모리 |
