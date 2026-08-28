# Camera Controls Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox syntax for tracking. No commits until the user's Phase 2 Step 5 gate.

**Goal:** RelayQuiz 오른쪽 위에서 사용자가 마우스로 캠을 켜고 끄며 실제 송신 연결 상태를 확인한다.

**Architecture:** 기존 TrackerLauncher가 자신이 시작한 프로세스만 관리한다. CameraControlPanel이 UDP 수신 상태와 시작 요청을 구분하고 자기 Button 영역의 마우스 입력만 처리한다. InputModeManager가 준비 중 이동 차단과 커서 표시를 전담하며 게임 컨텍스트를 덮어쓰지 않는다.

**Tech Stack:** Unity 6000.3, C#, 기존 uGUI/Input System, EditMode NUnit, Unity MCP. 패키지·Python 변경 없음.

---

## 1. Launcher 상태와 회귀

Files: `Assets/_CameraCoop/Scripts/Input/TrackerLauncher.cs`, new `Assets/_CameraCoop/Tests/EditMode/TrackerLauncherTests.cs`, new `Assets/_CameraCoop/Tests/Support/TrackerLauncherProbe.cs`.

- [x] 프로세스 시작 경계만 대체하는 probe로 실패 문구 유지·중복 시작 방지·예기치 않은 종료·소유 프로세스 종료를 검증하는 테스트를 먼저 작성한다. 기존 `OnClickToggle()`도 검증한다. 테스트에서 실제 Python을 실행하지 않는다.
- [x] Unity MCP EditMode에서 의도한 RED를 기록한다.
- [x] 기존 경로 탐색과 venv 실행을 유지하고 아래 공개 계약을 구현한다. 실패는 `LastError`와 기존 label에 남으며 다음 명시적 시작/종료 전에는 지우지 않는다. 기존 온라인 씬의 참조와 toggle 호출은 유지한다.

```csharp
public bool IsRunning { get; }
public string LastError { get; }
public bool StartTracker();
public void StopTracker();
public void RefreshStatus();
```

- [x] 프로세스 경계는 protected virtual `IsProcessRunning`, `TryLaunchProcess(out string error)`, `StopProcess()`로 제한한다. 실제 시작·종료 구현이 기본 경로이며 테스트 probe만 이를 대체한다.
- [x] RED 테스트의 GREEN과 기존 온라인 호출 계약을 확인한다.

## 2. 입력 권한

Files: `Assets/_CameraCoop/Scripts/Input/InputModeManager.cs`, `Assets/_CameraCoop/Tests/EditMode/InputModeTests.cs`.

- [x] 준비 중 Interact/포인터, 이동·룩·그리기·게임 손 UI·Tab 차단, 준비 해제 시 현재 컨텍스트 유지, focus/Blocked 우선, 기존 기본값 유지 테스트를 먼저 추가하고 RED를 기록한다.
- [x] 아래 계약으로 준비 overlay를 구현한다. requestedMode와 context를 따로 저장/복원하지 않고 현재 값을 유지한다. `SetContext`나 focus가 중간에 바뀌면 그 변경을 존중한다.

```csharp
public void SetCameraControlState(bool available, bool preparing);
public bool CanUseCameraMouse { get; }
```

- [x] available인 Interact에서만 포인터를 보여주고 Move 잠금은 유지한다. focus 상실/Blocked에서는 클릭을 받지 않는다. 권한 변경은 기존 `OnModeChanged`로 알려 캡처를 취소한다. 같은 값 반복 설정은 통지하지 않는다.
- [x] 입력 모드 전체 테스트를 다시 실행한다.

## 3. 카메라 패널

Files: new `Assets/_CameraCoop/Scripts/Input/CameraControlPanel.cs`, new `Assets/_CameraCoop/Tests/EditMode/CameraControlTests.cs`.

- [x] 꺼짐·시작 중·소유 수신·외부 수신·실패 상태와 시작 중 클릭 거부를 테스트한다. 최초 유효 패킷 없이 프로세스만 실행된 상태를 성공으로 표시하지 않는다. 빈 hands 패킷은 정상 수신이다.
- [x] Inspector에 Launcher, Receiver, InputModeManager, HandInputRouter, Button, 버튼 Text, 상태 Text를 직접 받는다. 15초 연결 대기 뒤 실패를 표시하며 자신이 시작한 프로세스만 종료한다.
- [x] `Update()`는 Receiver의 `LatestPacket != null && !IsServerLost`, Launcher 상태, unscaled time으로 연결 상태를 갱신한다. `Mouse.current`의 왼쪽 press/release만 읽고 자기 Button RectTransform 안에서 시작하고 끝난 클릭만 실행한다. 영역 이탈·모드/포커스/상태 변경은 누름을 취소한다.
- [x] 외부 수신이면 시작하지 않고 외부 실행 문구와 비활성 버튼을 표시한다. 종료/실패/단절 시 router 캡처 취소와 준비 권한을 적용하고 재연결 때 기존 open 재무장을 유지한다. 수동 종료 직전의 cached packet으로 수신 상태에 되돌아가지 않게 한다.
- [x] 카메라 밖 클릭, press 밖/release 안, Enter/Space 및 일반 A/B/C 우회 없음, 실패 문구 유지, 외부 프로세스 미종료, 재시도, timeout, reconnect를 검증한다.

## 4. 씬과 검증

Files: `Assets/_CameraCoop/Scenes/RelayQuiz.unity` through dedicated Unity MCP only; `docs/05_test_plan.md`, `docs/06_player_controller.md`, `docs/07_hand_interaction.md`, `.omo/evidence/phase2-camera-controls-verification.md`.

- [x] Play가 꺼진 상태에서만 씬 작업한다. InputRoot에 기존 Launcher와 새 패널을 연결하고 Overlay 오른쪽 위에 384×168 패널, 336×56 Button, 344×66 상태 Text를 배치한다. NanumGothic, 기존 패널 색과 여백을 사용한다.
- [x] 공유 InputSystemUIInputModule의 개별 10개 action을 null로 유지하고 카메라 Button도 Navigation.None/onClick 비움으로 저장한다. 실제 참조를 다시 읽어 확인한다.
- [x] `refresh_unity → read_console`과 targeted EditMode를 실행한다. 마지막 전체 실행은 기존 379건 중 알려진 native GraphicRaycaster 실패 4건과 새 실패를 분리해 기록한다. 기존 테스트를 약화하거나 skip하지 않는다.
- [x] Unity 창의 실제 정적 화면을 캡처해 직접 확인하고 두 독립 검토에서 배치·한글·상태 가독성을 확인한다. EditMode 화면은 실제 웹캠 동작의 증거로 쓰지 않는다.
- [x] 사용자에게 Play → 캠 켜기 → 수신 문구 → Tab/손 UI → 캠 끄기 → Play 종료 후 점유 해제 순서를 안내한다. 에이전트는 Play·카메라를 자동으로 실행하지 않는다.

Current gate: 85/85 targeted pass, 436/432/4 full suite. The latest RelayQuiz Full HD Off-state static capture passed both visual reviewers. Other resolutions and actual Play/camera validation remain with the user. Details: `.omo/evidence/phase2-camera-controls-verification.md`.
