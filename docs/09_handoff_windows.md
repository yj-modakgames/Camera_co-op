# 09. Windows 인계 문서 — Phase 3a 이후 이어받기

> 작성 2026-08-27 · 기준 commit: `75b0af6` 다음 (이 문서 포함 commit)
> 배경: Intel Mac에서 Phase 1 웹캠 검증 → Phase 2 드로잉 → Phase 3a 네트워킹까지 완료했다. **이 Mac에는 Steam 클라이언트가 없어** Steam 런타임 검증(N-5)이 남았고, 나머지 작업을 Windows에서 이어받는다.

---

## 1. 지금 어디까지 됐나

| Phase | 상태 |
|---|---|
| 1. 손 추적 입력 | 완료. 웹캠 실기 검증 통과. **handedness는 Python이 좌우 반전 송신** (mediapipe 0.10.21 Intel Mac 실측 — §4 주의 참조) |
| 2. 드로잉 메카닉 | 완료. DoD D-1~D-7 통과, 재검출 스냅 가드 포함. 채점 9.9 |
| 3a. 온라인 4인 네트워킹 | **구현 완료, main merge됨.** Loopback 검증(N-1~N-3) 통과, EditMode **72/72** (2026-08-27 client role 4건 추가). Steam 경로는 컴파일·정적 검증만 (런타임 미검증) |
| 3d. 3D 월드 캔버스 (Netplay3D) | **구현 완료.** `CanvasSurface` norm→월드 매핑 + 3개 컴포넌트 optional 주입, `Netplay3D.unity` 신규 씬(빌드 씬 등록). EditMode **81/81**. Loopback W-2/W-3 PASS, 2D 씬 W-4 무회귀 PASS (docs/10 §5·§7) |

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
| 3b | 미니게임 프레임워크 설계 | **진입 조건 충족 (2026-08-27).** `LoopbackTransport(isHost:false, localPlayerId)` client 모드 + client role EditMode 4건 추가 — Hello 송신·Welcome 적용(플레이어+스냅샷 재생)·host 이탈 StopSession·커서 seq 게이트. 최종 리뷰 I-5 해소 |

**2026-08-27 Phase 3d 완료로 씬이 2개(`NetplayTest`, `Netplay3D`)가 됐다.** N-5(Steam 2인) 실검증은 이제 **`Netplay3D.unity`로 하는 것이 기본**이며(docs/10), 다음 Windows 빌드 갱신 시 함께 수행한다.

### 3b 설계 입력 (3a 최종 리뷰에서 park된 항목 — docs/08과 함께 읽을 것)

- 원격 스트로크에 순간이동 가드 미적용 (`StrokeLogic` 송신 측 재사용으로 해결 가능)
- 스냅샷(Welcome) 크기 무상한 — Steam 메시지 512KB 상한, 장시간 세션 후 늦은 참가 시 도달 가능 → 청크 분할
- 중계 경로 메시지 타입별 권한 검사 없음 (클라가 `TypeClear`/`Welcome`/`PeerLeft` 위조 가능 — 친구 로비라 수용 중, 경쟁 요소 생기면 화이트리스트 switch)
- relay socket이 로비 멤버십 미확인 (`OnConnecting` 무조건 Accept)
- 로컬 C키 Clear가 네트워크 우회 (게임 이벤트로 통합할 것)
- 핀치 판정이 `HandCursorController`와 `NetSession`에 중복 (Inspector 한쪽만 바꾸면 갈림)
- 비호스트 Clear 클릭 무피드백 / `lobby.SetData("hostId")` 죽은 줄

## 3-1. Windows 실동작 검증 결과 (2026-08-27)

설치 항목(§2)은 전부 확인 완료. 그 위에서 실제 구동을 처음으로 검증했다.

| 대상 | 결과 | 증거 |
|---|---|---|
| Python venv | OK | mediapipe 1.0.1 / cv2 5.0.0 / Python 3.14.4, 모델 7,819,105 bytes |
| handedness 방향 | OK, 반전 유지 | 양손 30프레임 (§4 참조) |
| 웹캠 → UDP → 커서 | OK | 실드로잉 캡처, 왼손=파랑·오른손=주황 일치 |
| 핀치 드로잉 (Phase 2) | OK | 동일 캡처 |
| Loopback 세션 (Phase 3a) | OK | `[HOST] players: 3` (LocalHost#0/Alice#1/Bob#2), 원격 스트로크 렌더 |
| UI 버튼 | **수정 후 OK** | GraphicRaycaster 누락으로 전부 무반응이었다 (아래 §4) |
| Steam Init (Editor·빌드) | OK | id=76561199085658857, 빌드 로그 `Setting breakpad minidump AppID = 480` |
| Windows 빌드 | OK | Succeeded, errors 0. `Builds/CameraCoop/` |
| EditMode | 72/72 | client role 4건 추가분 포함 |

미검증으로 남은 것: **N-5 (Steam 2인)** — Steam 계정 2개가 필요해 보류. `Builds/CameraCoop_Steam2p.zip`(41MB)에 빌드 + 안내문 + `fake_hand.py`를 묶어 뒀다.

## 4. 알려진 함정 (Mac에서 실증된 것)

- **Play 모드 중 `unity cmd recompile` 금지** — domain reload로 비직렬화 필드 null → 프레임당 NRE burst. 반드시 editor_stop 먼저
- **컴파일 중 run_tests 금지** — stale 어셈블리 결과 반환. `editor_status`의 `compiling:false` 확인 후 실행
- ~~**handedness 반전 (docs/02 §2)**~~ **2026-08-27 Windows 검증 완료 — 코드 변경 불필요.** mediapipe 1.0.1도 0.10.21과 동일하게 flip 후 raw 라벨이 실제 손과 반대다 (양손 동시 검출 30프레임: 실제 왼손→raw `Right`, 실제 오른손→raw `Left`, score 0.975). `hand_tracker.py:181`의 반전 1줄 유지. 단, **한 손만** 올리면 라벨이 한쪽으로 고정되는 현상이 있으니 검증은 반드시 양손으로 할 것
- Unity CLI: instanceId는 Play 진입/domain reload마다 무효화. eval에서 `Object`는 `UnityEngine.Object`로 명시 (모호성 컴파일 에러)
- `capture_game_view --save_path`는 프로젝트 루트 상대 경로만, 실제로는 `Assets/` 밑에 저장됨 — 검증 후 .meta와 함께 삭제
- **UI 버튼이 조용히 안 눌리면 Canvas의 `GraphicRaycaster`를 먼저 확인**한다. 없으면 EventSystem이 포인터 이벤트를 UI로 전달하지 못하는데 에러도 로그도 남지 않는다. 2026-08-27 NetplayTest에서 실제로 발생 (Editor에서도 처음부터 안 눌렸다). 검증: eval에서 `EventSystem.current.RaycastAll`로 버튼이 잡히는지 본다
- **`unity cmd eval`은 코드를 메서드 본문에 감싸므로 `using` 지시문을 쓸 수 없다.** 전부 전체 이름(`CameraCoop.Netplay.NetSession`)으로 적는다. 또한 Roslyn 컴파일이 메인 스레드를 ~0.5초 멈추므로, eval에서 읽은 시간 기반 값(`Time.realtimeSinceStartup` 차이 등)은 그만큼 낡게 나온다 — `UdpHandReceiver.IsServerLost`가 정상 수신 중에도 true로 보이는 것이 그 예다. 시간 판정은 eval 대신 `capture_game_view`로 확인할 것
- pipeline test-runner가 timeout으로 취소되면 editor update 펌프가 죽어 모든 CLI 명령이 timeout될 수 있다 — Editor 재시작으로 복구 (Mac에서 1회 발생)
- **`unity cmd`의 `--timeout`은 command **앞**에 와야 한다.** 뒤에 두면 `run_tests`의 인자로 넘어가 CLI 기본값 30초에 끊긴다. 올바른 형태: `unity cmd --timeout 180 run_tests --mode EditMode` (2026-08-27 Phase 3d 실측)
- `eval_file`의 파라미터는 `--path`가 아니라 **`--file`**이다 (2026-08-27 Phase 3d 실측)
- `capture_game_view --source screen`은 **Play 모드 전용**이다. Play가 아닐 때는 `--source camera`를 쓴다 (단 `--source camera`는 Screen Space - Overlay UI가 안 잡힌다, 2026-08-27 Phase 3d 실측)
- `capture_game_view`에 `--width`/`--height`를 안 주면 실제 Game view 해상도를 목표 크기로 **비균일 확대**해 종횡비가 왜곡된다 (2026-08-27 실측: 500×462 → 1280×720, 가로 1.65배). 크기를 명시하거나 `UnityEditor.PlayModeWindow.SetCustomRenderingResolution`으로 먼저 해상도를 고정할 것
- URP 머티리얼을 처음 렌더할 때 Editor가 60~90초 blocking될 수 있다 — **재시작 불필요**, `editor_status` 폴링으로 복구된다 (timeout을 진짜 멈춤으로 오판하지 말 것, 2026-08-27 Phase 3d 실측)
- 원격 merge로 유입된 `Assets/_CameraCoop/Editor/MacBuild.cs`가 macOS 전용 API(`UnityEditor.OSXStandalone`)를 조건 없이 참조해 Windows에서 CS0234로 **Editor 어셈블리 전체**가 깨진 적이 있다 (`run_tests`가 stale 79 반환) — `#if UNITY_EDITOR_OSX`로 가드해 해결 (commit `bd24577`)
- **`run_tests`가 "이미 Editor에서 열려 있는(active) 씬"을 `EditorSceneManager.OpenScene(path, OpenSceneMode.Additive)`로 다시 여는 테스트(`NetplaySceneTests`)를 포함하면, 그 씬의 오브젝트 transform이 실제로 변형돼 디스크에 저장될 수 있다.** 2026-08-27 Phase 3d Task 6 실측: `Netplay3D.unity`가 Editor의 active 씬인 상태에서 `run_tests`를 돌리자 `DrawCanvas`의 로컬 z가 `0 → -0.04`로 바뀌어 파일까지 저장됐다(`get_scene_hierarchy`의 `isActive:true`로 원인 확인). `run_tests` 전에 `unity cmd open_scene --path <테스트 대상이 아닌 다른 씬>`으로 active 씬을 바꿔두면 재현되지 않는다 — 재발생 시 `git status`로 `*.unity` diff를 확인하고 `git checkout --` 로 되돌릴 것

## 5. 문서 지도

- `docs/02_protocol.md` — 로컬 UDP 프로토콜 v1 (단일 진실 원천)
- `docs/07_phase2_drawing.md` — 드로잉 메카닉 spec + DoD 결과
- `docs/08_netplay.md` — 네트워킹 spec (network v1 프로토콜·아키텍처·DoD)
- `docs/superpowers/plans/` — Phase 2·3a 구현 계획 (Task 단위 기록)
- 구현과 문서가 다르면: 문서 갱신 → 승인 → 코드 반영 (docs/05 §4)
