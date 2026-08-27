# Phase 3e — 3D 이동 + 그림 도구 팔레트 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 플레이어가 3D 룸을 WASD로 돌아다니고, 이젤 옆 팔레트를 손으로 클릭해 색·두께·브러시·지우개를 바꾸며 그린다.

**Architecture:** 본체는 입력 모델 교체다. 현재 손 norm 좌표는 카메라와 무관하게 캔버스에 직결(`CanvasSurface.NormToWorld`)돼 있어 이동해도 그리는 위치가 안 따라오고 팔레트를 겨냥할 수단이 없다. 이를 **카메라 레이캐스트 조준**(신규 `HandPointer`)으로 바꾸면 이동·팔레트 클릭·드로잉이 한 경로로 통일된다. 레이 hit 지점을 캔버스 로컬로 역변환하면 그것이 곧 기존 norm [0,1]이므로 좌표계는 유지되고, 프로토콜은 스타일 필드 추가분(v2)만 바뀐다.

**Tech Stack:** Unity 6000.3.15f1 (URP, 새 Input System 전용), NUnit EditMode, Unity CLI (`unity cmd`), `PythonTracker/fake_hand.py`

**Spec:** `docs/11_phase3e_paint_tools.md` (docs/08 §3 프로토콜, docs/09 §4 함정, docs/10 §7 감점 이력도 함께 읽을 것)

**대상 씬:** `Assets/_CameraCoop/Scenes/Netplay3D.unity` 만. 나머지 3개(`NetplayTest`/`DrawingTest`/`HandTrackingTest`)는 무수정.

## Global Constraints

- Unity Editor 자동화는 `unity cmd`만 사용. 시작 전 `unity pipeline list`로 Server Reachable 확인 — 안 뜨면 Editor 창 포커스 필요: `(New-Object -ComObject WScript.Shell).AppActivate(<Unity PID>)`
- **`unity cmd eval`/`eval_file`은 코드를 메서드 본문에 감싼다 — `using` 지시문 금지, 전부 전체 이름**(`CameraCoop.Netplay.NetSession`). `Object`는 `UnityEngine.Object`로 명시. `eval_file` 파라미터는 `--path`가 아니라 **`--file`**
- **`--timeout`은 command 앞에 온다**: `unity cmd --timeout 180 run_tests --mode EditMode`
- **Play 중 `recompile` 금지**(domain reload NRE burst) — `editor_stop` 먼저. **컴파일 중 `run_tests` 금지** — `editor_status`의 `compiling:false` 확인 후
- **dirty 씬을 열어둔 채 `run_tests` 금지** — test-framework가 무조건 저장한다(docs/09 §4). 씬 수정 후에는 저장하거나 `git checkout --`로 되돌린 뒤 테스트
- instanceId는 Play 진입/domain reload마다 무효화 — 매번 `get_scene_hierarchy`로 재취득
- `capture_game_view --save_path`는 실제로 `Assets/` 밑에 저장 — 검증 후 `.meta`와 함께 삭제. `--width`/`--height` 반드시 명시(미지정 시 종횡비 왜곡). `--source screen`은 Play 전용
- URP 머티리얼 첫 렌더에서 Editor가 60~90초 blocking될 수 있다 — 재시작 말고 `editor_status` 폴링
- 코드 스타일: 기존 파일 규칙 준수 — 핫패스 LINQ 금지, 한글 주석 + docs 섹션 참조, 순수 로직은 `internal static` 클래스 분리, 테스트 namespace는 `CameraCoop.Tests`, 식별자는 영문
- **ProjectSettings 변경 금지**(레이어 추가 금지 — hit 구분은 컴포넌트 유무로 한다). 작업 시작 시 `ProjectSettings/ProjectSettings.asset`에 이미 uncommitted 변경이 있으니 건드리지 말 것
- 각 Task 끝 `git status`로 의도치 않은 파일 변경 확인
- 커밋 메시지: 본문 한글, 식별자는 영문 그대로

---

### Task 1 — 순수 로직 4종 + EditMode 테스트 (model: sonnet)

프로토콜·씬·MonoBehaviour를 건드리지 않는 순수 함수만 만든다. 이 Task 단독으로 테스트가 전부 통과해야 한다.

**Files:**
- Modify: `Assets/_CameraCoop/Scripts/Drawing/CanvasSurface.cs`
- Create: `Assets/_CameraCoop/Scripts/Input/PointerRouteLogic.cs`
- Create: `Assets/_CameraCoop/Scripts/Input/PlayerMoveLogic.cs`
- Create: `Assets/_CameraCoop/Scripts/Drawing/EraseLogic.cs`
- Modify: `Assets/_CameraCoop/Tests/EditMode/CanvasSurfaceTests.cs`
- Create: `Assets/_CameraCoop/Tests/EditMode/PointerRouteTests.cs`, `PlayerMoveTests.cs`, `EraseLogicTests.cs`

**Interfaces (Task 2~5가 이 시그니처에 의존한다 — 임의 변경 금지):**

```csharp
// CanvasSurface.cs — 기존 NormToWorld는 그대로 두고 역변환만 추가
public Vector2 WorldToNorm(Vector3 world);                                  // InverseTransformPoint + LocalToNorm
internal static Vector2 CanvasSurfaceLogic.LocalToNorm(Vector3 local);      // NormToLocal의 역함수 (z 무시)

// PointerRouteLogic.cs
internal static class PointerRouteLogic
{
    public enum HitKind { None, Canvas, Tool }
    public enum RouteAction { None, StartStroke, AppendStroke, EndStroke, ClickTool }
    public static RouteAction Decide(HitKind hit, StrokeLogic.PinchKind kind, bool isDrawing);
}

// PlayerMoveLogic.cs
internal static class PlayerMoveLogic
{
    // 로컬 입력(x=좌우, y=전후) + yaw(도) -> 월드 이동 델타. 대각선 속도 보정 포함
    public static Vector3 Step(Vector2 input, float yawDegrees, float speed, float deltaTime);
    // 사각 방 경계로 clamp. y는 건드리지 않는다
    public static Vector3 ClampToRoom(Vector3 position, Vector2 minXZ, Vector2 maxXZ);
    // pitch 누적값을 [-maxPitch, maxPitch]로 clamp
    public static float ClampPitch(float pitch, float maxPitch);
}

// EraseLogic.cs
internal static class EraseLogic
{
    // 점-선분 최소거리 <= radius (월드 단위). a==b(길이 0)도 안전해야 한다
    public static bool HitsSegment(Vector3 point, Vector3 a, Vector3 b, float radius);
}
```

- [x] **Step 1: 실패하는 테스트 작성** — 아래를 전부 덮을 것

  - `LocalToNorm`: `NormToLocal`과의 왕복 (norm (0,0)/(1,1)/(0.37,0.82) → local → norm 복원, 오차 1e-5)
  - `WorldToNorm`: `NormToWorld` 왕복 — transform에 position·scale·**rotation(euler 0,30,0)** 을 준 상태에서도 복원
  - `PointerRouteLogic.Decide` **분기표 전건**(아래 표 그대로):

    | kind | hit | isDrawing | 기대 |
    |---|---|---|---|
    | Start | Canvas | false | StartStroke |
    | Start | Tool | false | ClickTool |
    | Start | None | false | None |
    | Start | Tool | true | ClickTool |
    | Move | Canvas | true | AppendStroke |
    | Move | Tool | true | **EndStroke** (드래그가 캔버스를 벗어남) |
    | Move | None | true | **EndStroke** |
    | Move | Canvas | false | None (고아 Move) |
    | End | (any) | true | EndStroke |
    | End | (any) | false | None |

  - `PlayerMoveLogic.Step`: yaw 0에서 (0,1) → +Z, yaw 90에서 (0,1) → +X, 대각선 (1,1)의 크기가 `speed*dt`를 넘지 않음, 입력 0 → 델타 0
  - `ClampToRoom`: 안쪽 통과 / x·z 각각 초과 시 경계값 / y 보존
  - `ClampPitch`: 범위 안 통과, 위아래 초과 clamp
  - `HitsSegment`: 선분 위 점 hit / 반경 밖 miss / 선분 **끝점 너머**(투영 t<0, t>1)가 수직거리가 아니라 끝점 거리로 판정되는지 / 길이 0 선분에서 예외 없음

- [x] **Step 2: 테스트 실행 → 컴파일 에러 또는 fail 확인** (`unity cmd --timeout 180 run_tests --mode EditMode`)
- [x] **Step 3: 구현** — 전부 struct 반환, 힙 할당 없음
- [x] **Step 4: 전체 테스트 통과** — 기존 81건 + 신규분. **결과 수치를 그대로 인용해 보고**

**Verification:** EditMode 전건 pass. `git status`에 `.unity`·`ProjectSettings` 변경 없음.

---

### Task 2 — HandPointer / ToolState / ToolButton (model: sonnet)

MonoBehaviour 3개를 만든다. 아직 아무도 구독하지 않으므로 씬 동작은 변하지 않는다 — 컴파일 green과 Inspector 노출까지가 이 Task다.

**Files:**
- Create: `Assets/_CameraCoop/Scripts/Input/HandPointer.cs`
- Create: `Assets/_CameraCoop/Scripts/Drawing/ToolState.cs`
- Create: `Assets/_CameraCoop/Scripts/Drawing/ToolButton.cs`

**Interfaces (Task 3~5가 의존):**

```csharp
// ToolButton.cs — 팔레트 버튼 1개. 데이터만 들고 있고 아무 동작도 하지 않는다
public enum ToolKind { Color, Width, Brush, Eraser }
public class ToolButton : MonoBehaviour { public ToolKind Kind { get; } public int Index { get; } }

// ToolState.cs
[Serializable] public class BrushDef { public string name; public Material material; public float widthScale; public float alpha; }
public class ToolState : MonoBehaviour
{
    public enum Mode { Draw, Erase }
    public Mode CurrentMode { get; }
    public Color CurrentColor { get; }     // palette[colorIndex]에 브러시 alpha 적용된 최종 색
    public float CurrentWidth { get; }     // widths[widthIndex] * brush.widthScale (월드 단위)
    public int CurrentBrushIndex { get; }
    public Material CurrentMaterial { get; }
    public float EraseRadius { get; }      // [SerializeField], 기본 0.05
    public event Action OnChanged;
    public void Apply(ToolButton button);  // Color/Brush 클릭은 Erase 모드를 Draw로 되돌린다
}

// HandPointer.cs
public class HandPointer : MonoBehaviour
{
    public event Action<string, Vector2, Vector3> OnCanvasStrokeStart;  // hand, norm, world
    public event Action<string, Vector2, Vector3> OnCanvasStrokeMove;
    public event Action<string> OnCanvasStrokeEnd;
    public event Action<Vector3> OnCanvasErase;                         // world (Erase 모드일 때 Start/Move 대신 발행)
    public event Action<ToolButton> OnToolClicked;
}
```

- [x] **Step 1: ToolButton + ToolState**
  - `ToolState`의 `[SerializeField]`: `Color[] palette`(6), `float[] widths`(3, 기본 0.01/0.02/0.045), `BrushDef[] brushes`(3), `eraseRadius`(0.05). 인덱스는 배열 길이로 clamp — 범위 밖 `ToolButton.Index`는 예외 대신 무시 + `Debug.LogWarning` 1회
  - `Apply`에서 상태가 실제로 바뀔 때만 `OnChanged` 발행
- [x] **Step 2: HandPointer**
  - `[SerializeField]`: `HandCursorController cursorController`, `Camera aimCamera`, `CanvasSurface canvasSurface`, `ToolState toolState`, `float maxDistance`(기본 20)
  - `OnEnable`/`OnDisable`에서 `cursorController.OnPinchStart/Move/End` **대칭 구독**(기존 파일들과 같은 패턴)
  - 핀치 이벤트 → `Physics.Raycast(aimCamera.ScreenPointToRay(screenPos), out hit, maxDistance)` **1회**
    → `hit.collider.GetComponentInParent<ToolButton>()`가 있으면 `HitKind.Tool`,
      아니면 `GetComponentInParent<CanvasSurface>()`가 있으면 `HitKind.Canvas`, 그 외·미스는 `None`
    → `PointerRouteLogic.Decide(hit, kind, isDrawing[hand])`로 행동 결정 후 이벤트 발행
  - 손별 `isDrawing` 상태는 `Dictionary<string,bool>(2)`. `StartStroke`/`EndStroke` 시 갱신
  - `toolState.CurrentMode == Erase`면 `StartStroke`/`AppendStroke`를 `OnCanvasErase(hit.point)`로 바꿔 발행하고 `OnCanvasStrokeStart/Move`는 발행하지 않는다
  - norm은 `canvasSurface.WorldToNorm(hit.point)` (Task 1)
  - **캐싱**: `Camera.main`·`GetComponent` 런타임 조회 금지, 전부 `[SerializeField]`. `RaycastHit`는 struct라 할당 없음
  - **null 가드**: `aimCamera`/`canvasSurface`/`toolState`/`cursorController` 중 하나라도 미할당이면 `Awake`에서 `Debug.LogError` 1회 + `enabled = false`(조용한 실패 금지 — docs/10 §7의 `drawCamera` 가드 누락 재발 방지)
- [x] **Step 3: 컴파일 확인** — `unity cmd recompile` 후 `get_console_logs --severity error` 0건

**Verification:** 콘솔 에러·경고 0. 씬 파일 변경 0(`git status`). 기존 테스트 여전히 전건 pass.

---

### Task 3 — DrawingController 도구 반영 + 지우개, HandCursorController 죽은 분기 삭제 (model: opus)

입력원을 `HandCursorController` → `HandPointer`로 갈아끼운다. 이 Task부터 씬 동작이 바뀐다.

**Files:**
- Modify: `Assets/_CameraCoop/Scripts/Drawing/DrawingController.cs`
- Modify: `Assets/_CameraCoop/Scripts/Input/HandCursorController.cs`
- Modify/Create: EditMode 테스트(아래 Step 5)

**Interfaces (Task 4가 의존):**

```csharp
// DrawingController
public event Action<int> OnLocalStrokeStarted;  // 새 스트로크의 localId (NetSession이 전역 strokeId와 매핑)
public event Action<int> OnLocalStrokeErased;   // 지운 로컬 스트로크의 localId
```

- [x] **Step 1: 입력원 교체**
  - `cursorController` 필드 제거, `[SerializeField] HandPointer handPointer` + `[SerializeField] ToolState toolState` 추가
  - 구독을 `handPointer.OnCanvasStrokeStart/Move/End` + `OnCanvasErase`로 교체(대칭 해제 유지)
  - 좌표는 **월드를 직접 받는다** — `ToPlanePoint(screenPos)`와 `canvasSurface`·`drawCamera`·`planeDistance` 분기 삭제(이제 `HandPointer`가 hit 지점을 준다)
  - `ShouldSplitStroke`(재검출 스냅 가드)는 screen 거리 기준이었다 → 월드 거리 기준으로 바꾸고 임계값을 `[SerializeField] maxSegmentWorldDistance`(기본 = 캔버스 폭의 0.25 ≈ 0.6)로 노출. **가드를 없애지 말 것** — 손 재검출 시 점프 선을 막는 장치다(docs/07 §6)
- [x] **Step 2: 도구 반영**
  - `leftStrokeColor`/`rightStrokeColor`/`lineWidth` 하드코딩 제거 → `BeginStroke`에서 `toolState.CurrentColor`/`CurrentWidth`/`CurrentMaterial` 사용
  - 스트로크는 시작 시점의 도구 값을 **고정**한다(그리는 도중 색이 바뀌어도 진행 중 선은 안 바뀐다)
  - `lineMaterial` 필드는 브러시 머티리얼 미할당 대비 폴백으로만 유지
- [x] **Step 3: 지우개 + localId**
  - 완료 스트로크를 `localId`·점 목록과 함께 보관하도록 `finishedStrokes` 확장. 점은 `List<Vector3>`로 직접 보관할 것 — `LineRenderer.GetPositions` 호출은 할당이 생긴다
  - `HandleErase(Vector3 world)`: 완료 스트로크를 **최근 것부터** 훑어 `EraseLogic.HitsSegment(world, p[i], p[i+1], toolState.EraseRadius)` 첫 hit **1개만** 파괴하고 `OnLocalStrokeErased(localId)` 발행. 진행 중 스트로크는 대상 아님
  - `// ponytail: 선형 스캔 O(스트로크×점). 수백 스트로크에서 체감되면 그때 공간 분할` 주석 명시
  - `ClearAll`은 목록·매핑을 함께 비운다
- [x] **Step 4: HandCursorController 정리**
  - `canvasSurface`/`projectionCamera` 필드와 `UpdateHand`의 투영 분기 **삭제**. 커서는 항상 `screenPos`(레이 원점)에 둔다
  - 이 분기를 덮던 테스트가 있으면 함께 정리하되, `HandScreenMapper.ToNormalized` 왕복 테스트는 `NetSession`이 계속 쓰므로 **남긴다**
- [x] **Step 5: 테스트** — 지우개 선택(여러 스트로크 중 닿은 것만 사라짐), localId 단조성, 도구 변경이 진행 중 스트로크에 영향 없음. MonoBehaviour 의존부는 순수 로직으로 밀어내 `[Test]`로 덮고, 불가능하면 `[UnityTest]`
- [x] **Step 6: 전체 테스트 통과 + 콘솔 에러 0**

**Verification:** EditMode 전건 pass(수치 인용). `Netplay3D.unity`는 아직 배선 전이라 Play 시 드로잉이 동작하지 않을 수 있다 — **정상**이며 Task 5에서 배선한다. 단 콘솔 NRE는 0이어야 한다(Task 2의 미할당 LogError 1회는 허용).

---

### Task 4 — 프로토콜 v2 + NetSession + RemotePresenter (model: opus)

**Files:**
- Modify: `Assets/_CameraCoop/Scripts/Netplay/NetProtocol.cs`, `NetSession.cs`, `RemotePresenter.cs`
- Modify: `Assets/_CameraCoop/Tests/EditMode/ProtocolTests.cs`, `NetplayTests.cs`

- [x] **Step 1: 프로토콜**
  - `Version = 2`
  - `StrokeStartPayload`에 `public int color; public float width; public int brush;` 추가(color = packed `0xRRGGBB`)
  - `StrokeSnapshot`에 같은 3필드 추가
  - `TypeStrokeErase = "StrokeErase"` + `[Serializable] class StrokeErasePayload { public string strokeId; }`
  - `ColorPack.ToInt(Color)` / `.FromInt(int)` 순수 함수 + 왕복 테스트(0x000000·0xFFFFFF 양 끝값 포함, 오차 ≤ 1/255)
- [x] **Step 2: NetSession 송신**
  - 핀치 구독을 `handPointer.OnCanvasStrokeStart/Move/End`로 교체 — **norm을 직접 받으므로 내부 screen→norm 변환 호출부를 삭제**한다. 커서 송신(`CursorUpdate`)은 `HandCursorController` 구독 그대로 유지(캔버스 밖에서도 커서는 보여야 한다)
  - `StrokeStart` 송신 시 `toolState`의 현재 값을 packed color·width·brush로 동봉
  - `localId → strokeId` 매핑(`Dictionary<int,string>`) 유지. `drawingController.OnLocalStrokeErased` 구독 → 매핑 조회 후 `StrokeErase` 송신(reliable ordered) + 자기 `strokes` 목록에서 제거. 매핑에 없으면 조용히 넘기지 말고 `Debug.LogWarning` 1회
  - `strokes` 스냅샷 항목에도 스타일 3필드를 채운다
- [x] **Step 3: NetSession 수신 + 중계**
  - `TypeStrokeErase` 수신: host면 rebroadcast(기존 `TypeClear` 경로와 동일 취급), 자기 `strokes`에서 제거, `OnRemoteStrokeErased(strokeId)` 발행
  - `OnRemoteStrokeStart`에 스타일을 실어 보낸다. 인자가 5개를 넘으면 `[Serializable] StrokeStyle` 구조체로 묶을 것
- [x] **Step 4: RemotePresenter**
  - 수신 스타일로 색·두께·머티리얼 적용. 브러시 인덱스는 `Material[]`로 매핑하고 범위 밖이면 기본 머티리얼 폴백
  - 스타일이 비어 있으면(`width <= 0`) 기존 `playerPalette`·`lineWidth` 폴백 유지
  - `OnRemoteStrokeErased` 구독 → 해당 `strokeId` 오브젝트 파괴. 없으면 무시(멱등)
- [x] **Step 5: 테스트** — v2 왕복 직렬화, `StrokeErase` 인코딩/디코딩, v1 envelope가 폐기되는지, 스냅샷 스타일 보존, erase 멱등성(같은 id 2회)
- [x] **Step 6: 전체 테스트 통과 + 콘솔 에러 0**

**Verification:** EditMode 전건 pass(수치 인용). `INetTransport`/`SteamTransport`/`LoopbackTransport`는 **무수정**이어야 한다 — `git diff --stat`로 확인.

---

### Task 5 — PlayerController + Netplay3D 씬 배선 (model: sonnet)

**Files:**
- Create: `Assets/_CameraCoop/Scripts/Input/PlayerController.cs`
- Modify: `Assets/_CameraCoop/Scenes/Netplay3D.unity` (+ 신규 머티리얼 몇 개)

- [x] **Step 1: PlayerController**
  - 새 Input System 전용: `Keyboard.current[Key.W]` 등, `Mouse.current.delta`/`rightButton`. legacy `Input` API는 이 프로젝트에서 예외를 던진다
  - WASD → `PlayerMoveLogic.Step` → `ClampToRoom`(Task 1). **CharacterController·Rigidbody 쓰지 않는다** — 씬에 collider가 없고 사각 방이라 clamp로 충분하다. `// ponytail: 사각 방 전용 clamp, 장애물이 생기면 CharacterController로 승격` 주석
  - 우클릭 **홀드 중에만** 마우스 룩(yaw는 Player, pitch는 자식 Camera, `ClampPitch(±80)`). 커서 잠금(`Cursor.lockState`)은 쓰지 않는다 — 로비 UI 버튼을 계속 눌러야 한다
  - `Keyboard.current`/`Mouse.current` null 가드(장치 없음)
  - `[SerializeField]`: `moveSpeed`(기본 3), `lookSensitivity`, `minXZ`/`maxXZ`, `maxPitch`
- [x] **Step 2: 씬 — 리그와 collider**
  - `Player` 빈 오브젝트 생성(위치 = 기존 Camera 위치) → 기존 `Camera`를 그 자식으로 이동(월드 위치 유지). `PlayerController` 부착 + 카메라 배선
  - `DrawCanvas`에 `MeshCollider` 추가
  - 방 경계는 `LeftWall`/`RightWall`/`BackWall` 실좌표를 `get_scene_hierarchy`로 읽어 안쪽 여유 0.5m로 설정 — **추측 금지, 실측값 기입**
- [x] **Step 3: 씬 — 팔레트 13개**
  - 이젤 오른쪽에 `Palette` 트레이(Cube) + 버튼 Cube 13개: 색 6 / 두께 3 / 브러시 3 / 지우개 1. 각각 `BoxCollider` + `ToolButton`(Kind·Index 설정)
  - 버튼은 시작 위치에서 **레이가 실제로 닿는 높이·각도**여야 한다 — 배치 후 `capture_game_view`로 육안 확인
  - 색 버튼 머티리얼은 `ToolState.palette`와 같은 색으로. 머티리얼은 공유(`sharedMaterial`), 버튼마다 인스턴스 생성 금지
- [x] **Step 4: 씬 — 컴포넌트 배선**
  - `HandPointer` 오브젝트 생성 + 4개 참조 배선, `ToolState` 부착 + palette·widths·brushes 채우기
  - `DrawingController`: `handPointer`·`toolState` 배선, 죽은 참조 정리
  - `NetSession`: `handPointer`·`toolState`·`drawingController` 배선
  - `HandCursorController`: 삭제된 필드의 잔여 참조 없음 확인
  - 배선 검증은 `SerializedObject`로 **각 필드 non-null을 하나씩 출력**해 증거로 남길 것(Phase 3d Task 4 방식)
- [x] **Step 5: 씬 저장 → 그 다음 테스트**(dirty 씬 + `run_tests` 금지 — Global Constraints)

**Verification:** `git status`에 `Netplay3D.unity`와 신규 에셋만. 다른 씬 3개 변경 0. `NetplaySceneTests` 포함 EditMode 전건 pass.

---

### Task 6 — 통합 검증 P-2 ~ P-7 (model: opus)

docs/11 §6의 DoD를 실제로 실행해 증거를 남긴다. **여기서 추측 금지** — 실행 출력과 캡처만 인용한다. 지난 Phase 채점의 최대 감점(-0.30)이 "로컬 드로잉 경로 실행 검증 0건"이었으므로 P-2/P-3은 필수 통과 항목이다.

**Files:**
- Modify: `PythonTracker/fake_hand.py`
- Create: 검증용 eval 스크립트(스크래치 디렉터리, 커밋하지 않음)

- [x] **Step 1: fake_hand.py에 고정 좌표 모드 추가**
  - `--target x,y --pinch-hold N` 형태로 **지정 norm 좌표에 손을 고정하고 N초간 핀치 유지**하는 모드. 기존 원 궤적 모드는 그대로 둔다
  - stdlib만. 파일 하단 `selfcheck()`에 새 모드의 assert 추가(패킷 스키마·좌표 범위)
  - 팔레트 버튼을 겨냥하려면 화면 좌표를 알아야 한다 → Play 중 eval에서 `aimCamera.WorldToScreenPoint(button.position)` → `HandScreenMapper.ToNormalized`로 **norm을 계산해 `--target`에 넣는다**
- [x] **Step 2: P-2 로컬 드로잉** — Play → fake_hand 원 궤적 → 스트로크가 생기고 첫 점의 캔버스 로컬 z가 `surfaceOffset`(-0.005)과 일치하는지 eval로 검산. `capture_game_view --width 1280 --height 720`
- [x] **Step 3: P-3 팔레트 클릭** — 색 버튼 겨냥 핀치 → `ToolState.CurrentColor` 변경 확인 → 그 뒤 그은 선의 `LineRenderer.startColor`가 그 색인지. 두께·브러시도 같은 방식. 지우개 버튼 → `CurrentMode == Erase`
- [x] **Step 4: P-4 지우개** — 스트로크 3개를 그린 뒤 가운데 것 위에서 지우개 핀치 → 그것만 사라지고 2개 보존(개수와 남은 오브젝트 이름 인용)
- [x] **Step 5: P-5 이동** — eval로 키보드를 흉내낼 수 없으면 `InputSystem` 가상 장치를 쓰거나 이동 메서드를 직접 호출한다. 이동 후에도 조준·드로잉이 캔버스에 맞는지, 경계 밖으로 못 나가는지 확인
- [x] **Step 6: P-6 Loopback 동기화** — 가짜 피어 스트로크가 **그쪽 색·두께**로 재생되는지, `StrokeErase` 수신 시 사라지는지, 늦은 참가 `Welcome.snapshot`에 스타일이 실리는지
- [ ] **Step 7: P-7 무회귀** — `NetplayTest.unity` Play → Loopback smoke → 콘솔 에러 0
- [ ] **Step 8: 정리** — `Assets/` 밑에 생긴 캡처 파일을 `.meta`와 함께 삭제, `git status` 클린 확인
- [x] **Step 9: docs/11 §6 DoD 표에 결과 기입**(PASS/FAIL + 인용 근거)

**Verification:** P-2~P-7 전건 PASS. 실패 항목이 있으면 해당 Task로 되돌아가 수정 후 재실행 — **부분 통과로 넘기지 않는다.**

---

### Task 7 — QUALITY_CHECKLIST 채점 + 문서 마감 (model: opus)

- [ ] **Step 1:** `QUALITY_CHECKLIST.md` 전 항목 채점. **채점 원칙 준수** — 추측 만점 금지, 감점 사유 먼저 탐색, 성능은 코드 분석/측정 근거, 검증은 실제 실행 결과 인용
- [ ] **Step 2:** 9.0 미만이면 코드 개선 → 재채점(점수 이력 기록: 예 8.4 → 9.1)
- [ ] **Step 3:** `docs/11_phase3e_paint_tools.md`에 §7 채점 섹션 추가(항목별 점수표 / 총점 / 판단 근거 / 구현 방식 선택 이유 / 감점 요인 및 개선 방안), 상태를 "구현 완료"로 갱신
- [ ] **Step 4:** `docs/09_handoff_windows.md` §1 상태 표에 Phase 3e 행 추가, 이번 Phase로 해소된 park 항목(좌우 손 색 통합 등)을 정정
- [ ] **Step 5:** commit — 본문 한글, 식별자 영문

**Verification:** 총점 ≥ 9.0. 문서와 코드 불일치 0.

---

## 주의: 이 Phase에서 깨지기 쉬운 것

1. **`NetSession`이 두 입력원을 구독한다** — 스트로크는 `HandPointer`(norm 직접), 커서는 `HandCursorController`(screenPos→norm). 둘을 섞지 말 것. 커서까지 `HandPointer`로 옮기면 캔버스를 안 겨냥할 때 커서가 사라진다.
2. **`Physics.Raycast`는 핀치 이벤트 안에서만** — `Update`에서 매 프레임 쏘지 않는다.
3. **머티리얼 인스턴스 누수** — `renderer.material`은 인스턴스를 복제한다. 팔레트 버튼·스트로크 모두 `sharedMaterial`을 쓸 것(기존 `DrawingController`도 그렇게 돼 있다).
4. **씬 저장 타이밍** — Task 5에서 씬을 만진 뒤 저장 없이 `run_tests`를 돌리면 test-framework가 임의 시점의 dirty 상태를 저장해 버린다(docs/09 §4 실증).
5. **프로토콜 v2 = 기존 빌드와 비호환** — `Builds/CameraCoop*`의 기존 플레이어와는 접속되지 않는다. N-5(Steam 2인)는 양쪽 다 새 빌드로 해야 한다.
