# 09. Windows 인계 문서 — Phase 3a 이후 이어받기

> 작성 2026-08-27 · 기준 commit: `75b0af6` 다음 (이 문서 포함 commit)
> 배경: Intel Mac에서 Phase 1 웹캠 검증 → Phase 2 드로잉 → Phase 3a 네트워킹까지 완료했다. **이 Mac에는 Steam 클라이언트가 없어** Steam 런타임 검증(N-5)이 남았고, 나머지 작업을 Windows에서 이어받는다.

---

## 1. 지금 어디까지 됐나

| Phase | 상태 |
|---|---|
| 1. 손 추적 입력 | 완료. 웹캠 실기 검증 통과. **handedness는 Python이 좌우 반전 송신** (mediapipe 0.10.21 Intel Mac 실측 — §4 주의 참조) |
| 2. 드로잉 메카닉 | 완료. DoD D-1~D-7 통과, 재검출 스냅 가드 포함. 채점 9.9 |
| 3a. 온라인 4인 네트워킹 | **구현 완료, main merge됨.** Loopback 검증(N-1~N-3) 통과, EditMode **68/68**. Steam 경로는 컴파일·정적 검증만 (런타임 미검증) |

Phase 3a 채점 (N-6): **9.6/10** — 감점: client role 실행 커버리지 0 (-0.1), Unity 메모리 증가분 단정 불가+스냅샷 무상한 (-0.1), Steam 런타임 미실행 (-0.1), `lobby.SetData("hostId")` 죽은 줄 (-0.1)

### N-7 (웹캠 포함 10분 세션) — 조건부 통과, 재실행 권장

2026-08-27 Mac 실측: 세션 시작 10:31:28, 사용자 실드로잉 + 가짜 피어 2 + 중간 churn. **콘솔 에러 0건(전 구간), tracker 메모리 무누수(-2MB)**. 단 사용자가 도중 Play를 중단해 **연속 10분은 미보장** (최소 5분+ 확인). Windows에서 재실행 절차: NetplayTest Play → Host Loopback → 10분 자유 드로잉 → 콘솔 에러 0 + 프로세스 메모리 대조.

## 2. Windows 환경 구축

### 2-1. Python (웹캠 손 추적)

- Windows용 requirements는 `PythonTracker/requirements.txt` (mediapipe 1.0.1). `requirements-intel-mac.txt`는 Intel Mac 전용이다.
- venv 새로 생성: `python -m venv PythonTracker\.venv` → `pip install -r PythonTracker\requirements.txt`
- 모델 파일은 커밋돼 있음 (7,819,105 bytes — 크기 다르면 README URL로 재다운로드)

### 2-2. Unity

- **Unity 6000.3.15f1** 고정 (다른 버전은 URP 에셋 업그레이드 diff 발생)
- `com.unity.pipeline`은 manifest에 있어 자동 설치. Unity CLI 경로는 Windows에서 다름 — `where unity` 확인
- **프로젝트는 새 Input System 전용** (`activeInputHandler: 1`) — legacy `Input` API는 예외

### 2-3. Steam (N-5의 전제)

- Steam 클라이언트 설치 + 로그인 필요. `steam_appid.txt`(480, 커밋됨)가 프로젝트 루트에 있어 개발용 Init이 그대로 동작
- Facepunch.Steamworks **2.5.2** DLL 커밋됨: Windows Editor에서는 `Facepunch.Steamworks.Win64.dll` + `steam_api64.dll`이 활성 (Editor OS filter — macOS에서는 Posix+dylib). 추가 설치 없음
- smoke: `NetplayTest.unity` Play 전에 Editor eval 또는 콘솔에서 `SteamBootstrap.TryInit()` → `OK id=<SteamID64> name=<계정명>`이면 통과

## 3. 남은 작업 (이게 인계의 본체)

| # | 작업 | 방법 |
|---|---|---|
| N-5 | **실 Steam 2인 상호 드로잉** | 기기 2대 (Windows + Mac 또는 노트북), 서로 다른 Steam 계정·친구 관계. A: NetplayTest Play → **Host Steam** → Steam overlay로 친구 초대. B: 게임 실행 중 초대 수락 → 자동 join (`SteamFriends.OnGameLobbyJoinRequested` 배선됨). 확인: 상호 커서·스트로크 실시간 표시, 늦은 참가 스냅샷, host 종료 시 클라 세션 종료 |
| N-7 | 10분 Loopback 세션 재실행 | §1 절차 |
| 3b | 미니게임 프레임워크 설계 | **진입 조건: `LoopbackTransport`에 client 모드 추가 + client role EditMode/통합 테스트** (최종 리뷰 I-5 — Welcome 적용·Hello 송신·host 이탈 StopSession·client seq 게이트가 현재 무커버) |

### 3b 설계 입력 (3a 최종 리뷰에서 park된 항목 — docs/08과 함께 읽을 것)

- 원격 스트로크에 순간이동 가드 미적용 (`StrokeLogic` 송신 측 재사용으로 해결 가능)
- 스냅샷(Welcome) 크기 무상한 — Steam 메시지 512KB 상한, 장시간 세션 후 늦은 참가 시 도달 가능 → 청크 분할
- 중계 경로 메시지 타입별 권한 검사 없음 (클라가 `TypeClear`/`Welcome`/`PeerLeft` 위조 가능 — 친구 로비라 수용 중, 경쟁 요소 생기면 화이트리스트 switch)
- relay socket이 로비 멤버십 미확인 (`OnConnecting` 무조건 Accept)
- 로컬 C키 Clear가 네트워크 우회 (게임 이벤트로 통합할 것)
- 핀치 판정이 `HandCursorController`와 `NetSession`에 중복 (Inspector 한쪽만 바꾸면 갈림)
- 비호스트 Clear 클릭 무피드백 / `lobby.SetData("hostId")` 죽은 줄

## 4. 알려진 함정 (Mac에서 실증된 것)

- **Play 모드 중 `unity cmd recompile` 금지** — domain reload로 비직렬화 필드 null → 프레임당 NRE burst. 반드시 editor_stop 먼저
- **컴파일 중 run_tests 금지** — stale 어셈블리 결과 반환. `editor_status`의 `compiling:false` 확인 후 실행
- **handedness 반전 (docs/02 §2)**: Python이 좌우 반전해 송신하는 것은 mediapipe **0.10.21** 실측 기반. Windows의 mediapipe 1.0.1에서 라벨 방향이 다를 수 있다 — **웹캠 연결 후 첫 작업으로 양손 색 확인** (왼손=파랑, 오른손=주황). 반대면 `hand_tracker.py`의 반전 1줄 제거 + docs/02 갱신
- Unity CLI: instanceId는 Play 진입/domain reload마다 무효화. eval에서 `Object`는 `UnityEngine.Object`로 명시 (모호성 컴파일 에러)
- `capture_game_view --save_path`는 프로젝트 루트 상대 경로만, 실제로는 `Assets/` 밑에 저장됨 — 검증 후 .meta와 함께 삭제
- pipeline test-runner가 timeout으로 취소되면 editor update 펌프가 죽어 모든 CLI 명령이 timeout될 수 있다 — Editor 재시작으로 복구 (Mac에서 1회 발생)

## 5. 문서 지도

- `docs/02_protocol.md` — 로컬 UDP 프로토콜 v1 (단일 진실 원천)
- `docs/07_phase2_drawing.md` — 드로잉 메카닉 spec + DoD 결과
- `docs/08_netplay.md` — 네트워킹 spec (network v1 프로토콜·아키텍처·DoD)
- `docs/superpowers/plans/` — Phase 2·3a 구현 계획 (Task 단위 기록)
- 구현과 문서가 다르면: 문서 갱신 → 승인 → 코드 반영 (docs/05 §4)
