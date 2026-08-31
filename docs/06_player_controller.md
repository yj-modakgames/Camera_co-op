# 06. 플레이어 컨트롤 설계 — 로컬 릴레이

> 작성: 2026-08-28 · 상태: **Step 1 구현·자동 검증·사용자 Play 통과, Step 2 진행 승인**
> 승인된 Phase 0 방향: 기존 컨트롤러 재사용, Tab 모드 전환, CharacterController, 로컬 전용 씬.
> §1은 Phase 0 기준선이며 §2~6은 승인된 설계다. Step 1 구현·검증 범위는 §8에 기록했다. 이후 손 입력·드로잉의 구현 상태는 docs/07·08, 아직 미구현인 릴레이 흐름은 docs/09를 따른다.

## 1. 범위와 기존 구현

- 목표는 스튜디오 이동과 갤러리 감상용 WASD·마우스 룩, 손 조작 중 고정 시점이다.
- 기존 `Assets/_CameraCoop/Scripts/Input/PlayerController.cs`를 확장한다. 요청 초안의 `Scripts/Player/PlayerController.cs`는 중복 생성하지 않는다.
- 현재 컨트롤러는 우클릭 유지 시 회전하고, XZ 사각 경계로 위치를 제한한다. CharacterController와 모드 상태는 아직 없다.
- 기존 온라인 씬의 우클릭·경계 제한 동작은 보존한다. 로컬 씬만 새 제어 프로필을 사용한다.
- 점프는 `ModalFirstPerson` 로컬 이동에 포함한다. 달리기, 웅크리기, 물리 밀기, 온라인 이동 동기화는 이번 범위에서 제외한다.

| 예정 파일 | 책임 |
|---|---|
| `Assets/_CameraCoop/Scripts/Input/PlayerController.cs` 수정 | 기존 이동 계산 재사용, 로컬 CharacterController 이동·시점 적용 |
| `Assets/_CameraCoop/Scripts/Input/PlayerMoveLogic.cs` 유지 | 카메라 yaw 기준 이동·대각선 정규화 계산 |
| `Assets/_CameraCoop/Scripts/Input/InputModeManager.cs` 신규 | 모드·게임 컨텍스트·포커스로 입력 권한 계산 |
| `Assets/_CameraCoop/Scripts/Input/InputFocus.cs` 재사용 | 현재 정답 입력창의 타이핑 여부 |
| `Assets/_CameraCoop/Scenes/RelayQuiz.unity` 신규 | Step 1 그레이박스부터 Step 4 릴레이까지 같은 로컬 씬 확장 |

`PlayerControlProfile`은 `Legacy`와 `ModalFirstPerson`으로 구분한다. 새 직렬화 필드의 기본값은 `Legacy`여서 기존 씬의 배선과 동작이 유지된다. `ModalFirstPerson`은 CharacterController와 InputModeManager를 반드시 직접 할당한다. 누락 시 명확한 오류와 함께 로컬 입력을 중지하며 Legacy로 조용히 전환하지 않는다.

## 2. 이동 구현 선택

| 선택 | 평가 |
|---|---|
| **CharacterController 채택** | 캡슐 충돌·벽 미끄러짐이 필요하고 외력 반응은 필요하지 않다. 이동량을 직접 제어하기 쉽다. |
| Rigidbody | 물리 힘·관성·밀기에는 적합하지만 이번 방 이동에는 불필요하다. |

WASD는 플레이어 yaw 기준 수평 이동이다. 대각선 길이를 정규화하고 `moveSpeed × deltaTime`을 적용한다. 마우스 delta는 픽셀 단위 감도로 yaw와 pitch에 적용하며 deltaTime을 다시 곱하지 않는다. pitch만 제한한다. 로컬 프로필에서는 우클릭을 요구하지 않는다.

CharacterController의 수평 이동과 중력을 함께 처리한다. `Space`의 rising edge에서 grounded일 때만 `sqrt(jumpHeight × -2 × gravity)` impulse를 적용한다. 공중 재점프와 held key 반복 발동은 허용하지 않는다. 천장 충돌은 상승 속도를 제거하고, 착지 시 하강 속도를 초기화한다. Interact와 Blocked, 타이핑 중에는 점프를 포함한 이동 입력을 적용하지 않는다. 바닥과 구조물은 정적 collider로 두어 버튼이나 구조물이 통로를 막으며, 플레이어는 점프로 넘을 수 있다. 바닥은 유효한 캡슐 위치로 배치한다.

선택 근거: [Unity 6.3 CharacterController](https://docs.unity3d.com/6000.3/Documentation/Manual/class-CharacterController.html).

## 3. 입력 모드와 게임 컨텍스트

`InputMode`는 `Move`, `Interact` 두 개다. 추가적인 게임 제한은 모드를 늘리지 않고 `InputContext`로 전달한다.

| InputContext | 유효 모드 | WASD·룩 | 손 UI | 캔버스 | Tab |
|---|---|---|---|---|---|
| `Explore` | Move 또는 Interact | Move에서 허용 | Interact에서 허용 | 금지 | 허용 |
| `UiOnly` | Interact 강제 | 금지 | 현재 화면의 활성 대상만 | 금지 | 금지 |
| `Drawing` | Interact 강제 | 금지 | 팔레트·undo·clear·완료 | 현재 작업 캔버스만 | 금지 |
| `Blocked` | Interact 강제 | 금지 | 금지 | 금지 | 금지 |

- Step 1과 갤러리는 Explore, Step 2 버튼 시험은 UiOnly, Step 3 드로잉 시험은 Drawing을 사용한다.
- 릴레이 Setup·Handover·WordReveal·ObservePrevious·Guessing·Reveal은 UiOnly다. 상태별 버튼 표시 여부는 [릴레이 설계](09_relay_quiz_mode.md)가 결정한다.
- 처음 Explore에 들어갈 때는 Move로 시작한다. 강제 컨텍스트에 들어가면 요청 모드는 Interact로 정리한다. Gallery에 진입할 때에는 명시적으로 Move를 요청한다.
- 손이 사라져도 Explore의 이동을 자동 차단하지 않는다. 릴레이의 유효 손 부재·타이머 일시정지는 RelayQuizController가 관리한다.

### 단일 권한 계산

| 예정 API | 계약 |
|---|---|
| `SetContext(InputContext context)` | 현재 단계의 입력 범위 변경. 변경 전에 활성 캡처 종료 |
| `RequestMode(InputMode mode)` | Explore에서만 수용. 타이핑·포커스 상실 중에는 거부 |
| `CurrentMode`, `CurrentContext` | 표현용 읽기 전용 상태 |
| `CanMove`, `CanLook`, `CanUseHandUi`, `CanDraw`, `CanToggleMode` | 컨텍스트·모드·앱 포커스·InputFocus로 계산한 읽기 전용 권한 |
| `OnModeChanged` | HUD·커서·라우터에 변경 통지 |

판정 우선순위는 앱 포커스 상실 → Blocked → 타이핑 → 게임 컨텍스트 → Explore 모드다. `InputFocus.IsTyping`은 이동·룩·Tab·드로잉을 모두 막되 손 제출 버튼은 막지 않는다. 컨트롤러는 `ApplyLook`보다 먼저 이 권한을 검사한다. 아래 카메라 전용 예외를 제외하면 `Blocked`의 모든 게임 입력은 닫힌다.

로컬 씬에는 `GameSession`과 `NetSession`을 넣지 않는다. 기존 `GameSession.UpdateGates()`와 새 모드 관리자가 같은 `StrokesEnabled`를 덮어쓰는 구성을 금지한다. 로컬에서는 HandInputRouter만 계산된 `CanDraw`를 HandPointer에 반영한다. 온라인 게이트는 기존 소유자를 유지한다.

### 전환 순서

1. Tab·컨텍스트 변경·포커스 이벤트를 판정한다. 타이핑 중 Tab은 아무 동작도 하지 않는다.
2. 손 라우터가 생긴 Step 2부터 `CancelAll(reason)`으로 눌림과 캔버스 캡처를 닫는다. 클릭 성공 이벤트는 만들지 않는다.
3. 권한·커서 잠금·HUD를 함께 바꾼다. 다음 권한 계산 전에는 이전 권한으로 이동·그리기를 실행하지 않는다.
4. 손마다 새 수용 패킷에서 0.10초 이상 벌린 상태를 확인한 뒤 새 핀치를 받는다. 자세한 조건은 [손 인터랙션](07_hand_interaction.md) §3을 따른다.

Step 1에는 아직 HandInputRouter가 없으므로 모드 이벤트와 플레이어 권한까지만 구현·검증한다. Step 2에서 캡처 취소 연결을 추가한다.

## 4. 커서·키보드 정책

| 상황 | Cursor.lockState | Cursor.visible | 손 커서 |
|---|---|---|---|
| Move | Locked | false | 숨김 |
| Interact | None | false | 추적된 손 표시 |
| 앱 포커스 상실 | None | true | 숨김, 입력 취소 |
| 포커스 복귀 | None | false | 손 재무장 후 표시; 릴레이는 손 `계속` 확인 필요 |

커서 잠금 상태를 게임 모드의 원인으로 사용하지 않는다. Editor나 OS가 잠금을 해제하면 이동·룩을 멈추고 안전한 Interact로 바꾼다. Explore에서는 이후 Tab으로 Move에 다시 들어간다. 릴레이는 가림·일시정지 복구 규칙을 우선한다.

| 허용 입력 | 용도 |
|---|---|
| W/A/S/D | Move에서만 이동 |
| 마우스 이동 | Move에서만 시점 회전 |
| Tab | Explore에서만 Move ↔ Interact |
| 정답 텍스트·편집 키·한글 IME | 손으로 포커스한 답변창 안에서만 입력·편집 |
| 손 호버·핀치 | 모든 버튼, 입력창 포커스, 제출, 팔레트, undo, clear, 준비·재시작 |

로컬 게임의 마우스 클릭, 키보드 버튼 탐색, Enter 제출·포커스, C 클리어, Escape 메뉴는 제공하지 않는다. IME 확정에 쓰이는 Enter는 정답 제출로 해석하지 않는다. Editor 자체의 Play 종료와 OS 창 조작은 게임 입력 범위 밖이다.

Unity는 UI 입력과 게임 입력을 자동으로 상호 배제하지 않는다. [Input System UI 지원](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.19/manual/UISupport.html#distinguishing-between-ui-and-game-input)의 모드 분리 원칙을 적용한다.

## 5. Inspector 파라미터

아래 값은 로컬 프로필의 초기 설계값이다. 실제 감도·위치·시야는 사용자 Play 검증 후 문서 변경과 승인을 거쳐 조정한다.

| 대상 / 필드 | 기본값 | 설명 |
|---|---|---|
| PlayerController / `controlProfile` | 로컬: ModalFirstPerson | 기존 씬은 Legacy 유지 |
| `playerCamera`, `characterController`, `inputModeManager` | 직접 할당 | 동일 PlayerRig의 카메라·캡슐·권한 관리자 |
| `moveSpeed` | 3 m/s | 수평 이동 속도 |
| `lookSensitivity` | 0.12 deg/pixel | 마우스 감도 |
| `maxPitch` | 80° | 위·아래 제한 |
| `gravity` | -20 m/s² | 바닥 접촉용 |
| CharacterController / height, radius, center | 1.8, 0.3, (0, 0.9, 0) | PlayerRig 원점은 발바닥 |
| skinWidth, stepOffset, slopeLimit | 0.03, 0.3, 45° | 충돌 여유·턱·경사 |
| PlayerCamera / localPosition, FOV | (0, 1.6, 0), 60° | 기본 시점 |
| InputModeManager / `toggleKey` | Tab | 허용 모드 키 |
| `initialContext`, `initialMode` | Explore, Move | Step 1 기본; 릴레이 시작은 Setup이 UiOnly로 변경 |

기존 `minXZ`, `maxXZ`는 Legacy에서만 사용한다. 로컬 이동은 방의 Collider로 제한한다. 두 제한 방식을 동시에 적용하지 않는다.

## 6. 그레이박스와 Inspector 할당

예정 씬의 루트는 `Studio`, `PlayerRig`, `InputRoot`, `OverlayRoot`, `DrawingRoot`, `RelayRoot`다. Step 1에서는 방·플레이어·모드 HUD와 향후 이젤 위치만 만든다.

- 방 내부는 약 12×10m, 벽 높이 3m다. 바닥과 벽에는 정적 BoxCollider를 둔다.
- 작업 캔버스는 3.6×2.4m, 중심 (0, 1.8, 3.8), `WorkPose`는 (0, 0, 1.1)·yaw=0이다. 캔버스 local +Z는 관찰자 반대쪽, 잉크 offset은 -Z 쪽이다. 캔버스 변환은 [드로잉 설계](08_drawing_canvas.md)를 따른다.
- `GalleryPose`는 (0, 0, -0.5)·yaw=180이다. 갤러리 캔버스 3개의 예정 중심은 x=-3/0/3, y=1.8, z=-4이며 +Z 쪽 관찰자를 향하게 한다. 표시는 같은 3:2 비율의 2.4×1.6m다. 실제 record 수만 왼쪽부터 표시한다.
- 갤러리 공간은 이동 통로를 비워둔다. 그리기·열람 중에는 이동하지 않고, 갤러리에서만 둘러본다.
- PlayerRig에는 CharacterController와 기존 PlayerController를 부착하고 카메라는 자식으로 둔다.
- InputRoot의 InputModeManager를 PlayerController에 연결한다. 모드 HUD는 `이동 · Tab: 손 조작` / `손 조작 · Tab: 이동`을 표시한다. 강제 상태에서는 Tab 안내를 숨긴다.
- Step 2부터 HandInputRouter의 InputModeManager 참조와 모드 변경 구독을 연결한다.

Step 4에는 PlayerController의 예정 `PlaceAt(Transform pose)`를 연결한다. RelayQuizController가 차폐·Blocked 상태에서만 WorkPose 또는 GalleryPose로 배치하며 pitch·수직 속도·이전 mouse delta를 초기화한다. CharacterController 이동 제약이 순간 배치를 방해하지 않도록 배치 구간에서만 비활성화 후 복구한다. 이 이동 자체를 정보 차단 수단으로 간주하지 않는다. Step 1에는 위치 marker까지만 준비한다.

씬·오브젝트·참조는 Unity MCP의 전용 도구로 작성한다. `.unity` 텍스트 편집과 `execute_code`는 금지한다. MCP가 특정 참조를 설정하지 못하면 누락된 오브젝트·컴포넌트·필드 이름을 보고하고 사용자 Inspector 할당을 요청한다. 런타임 Find로 대체하지 않는다.

## 7. Step 1 완료 기준

- [x] 기존 이동 계산 테스트를 유지하고, 모드·컨텍스트·타이핑·포커스 권한 표를 `Assets/_CameraCoop/Tests/EditMode/InputModeTests.cs`에서 검증한다.
- [x] `RelayQuiz.unity`에서 참조 누락이 없고 기존 씬의 Legacy 프로필이 유지된다.
- [x] `refresh_unity → read_console`로 컴파일 오류와 새 경고를 확인한다.
- [x] 사용자에게 WASD·대각선·벽 충돌·pitch 제한·Tab·포커스 복귀·HUD 체크리스트를 제공한다.
- [x] 사용자 Play 결과를 기록하고 Step 2 승인을 받았다. 에이전트가 Play를 대신 실행하지 않았다.

공통 검증 절차와 실행 기록은 [검증 계획](05_test_plan.md) §6~7을 따른다. 2026-08-28 사용자가 “Play 확인했어. 다 괜찮아 다음 단계 진행해”로 통과와 Step 2 진행을 확인했다.

## 8. Step 1 구현 기록

- 기존 `PlayerController.cs`를 확장하고 `InputModeManager.cs`를 추가했다. `Legacy = 0` 기본값과 기존 온라인 씬 파일은 유지했다.
- 로컬 씬의 카메라·CharacterController·InputModeManager와 HUD의 `modeLabel`을 MCP로 직접 할당했다. 추가로 사용자가 할당해야 할 참조는 없다.
- `RequestMode`는 수용 여부를 `bool`로 반환한다. `OnModeChanged`는 `Action<InputMode>`이며 컨텍스트·포커스 변경도 통지한다. 관리자 실행 순서는 -100이다.
- 방과 충돌체, WorkPose·GalleryPose, 작업·갤러리 위치의 빈 표면만 준비했다. HandInputRouter, 실제 드로잉·릴레이, `PlaceAt`은 구현하지 않았다.
- HUD는 기존 uGUI의 Text와 Image다. 한글 표시에 [NanumGothic Regular](https://github.com/google/fonts/tree/main/ofl/nanumgothic)를 사용하고 `Assets/_CameraCoop/Fonts/OFL.txt`를 함께 보관했다. 패키지를 추가하지 않았다.
- 관련 테스트 56건과 최종 전체 EditMode 287건이 통과했다. 정적 화면은 에이전트가 확인했고, 실제 Play는 사용자가 통과를 보고했다. Step 2 진행 승인을 받았다.

## 10. Grounded jump 최종 구현 — 2026-08-31

`ModalFirstPerson`에서 `Space`를 grounded jump로 사용한다. `CanMove`와 `CanLook` 권한은 기존 `InputModeManager` 계약을 따르며, `Blocked`·타이핑·앱 포커스 상실에서는 jump request를 거부한다. `CharacterController.isGrounded`가 확인된 rising edge만 소비하므로 키를 누르고 있는 동안 재발동하지 않는다. `jumpHeight`는 Inspector 설정값이며 기본값은 1.5m다. 관련 결과는 [검증 계획](05_test_plan.md) §11-4와 [최종 runtime QA](../.omo/evidence/world-labels-jump-runtime-qa-20260831/final-selective21-manual-qa.md)에 기록한다.

## 9. 캠 시작 컨트롤의 입력 예외 (승인 완료)

[손 UI 설계 §10](07_hand_interaction.md#10-캠-시작-버튼-보완안-승인-완료)의 카메라 준비 화면과 카메라 컨트롤 전용 마우스 클릭 예외를 2026-08-28 사용자가 승인했다. 준비 동안 Interact·이동/룩 차단을 적용하며, Interact에서는 해당 컨트롤에 접근할 마우스 포인터를 표시한다. Move 잠금, 키보드 허용 범위, 일반 게임 UI의 손 전용 정책은 유지한다. 카메라 컨트롤이 없는 기존 씬의 기본 동작은 바꾸지 않는다.

구현 API는 `SetCameraControlState(available, preparing)`와 `CanUseCameraMouse`다. 초기 꺼짐·시작 중·실패 재시도는 준비 화면으로 처리하고 실제 수신 뒤 해제한다. 카메라 패널의 왼쪽 클릭은 앱 포커스가 있고 `Interact`일 때만 허용하며, `Blocked`에서도 카메라가 아직 수신 중이 아니고 `IsCameraPreparing`인 동안에만 복구용으로 허용한다. 수신 중 `Blocked`에서는 카메라 mouse도 거부한다. 준비 중 컨텍스트가 바뀌거나 포커스를 잃으면 그 변경을 존중한다. 기존 모드 28건과 새 카메라 권한 16건을 합친 44건이 통과했다. 전체 결과와 사용자 확인 대기는 손 UI 설계 §11을 따른다.

> 계약 보완일: 2026-08-28.
