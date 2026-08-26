# Phase 2 드로잉 메카닉 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 핀치 제스처로 3D 씬 고정 평면에 LineRenderer 스트로크를 그리는 로컬 드로잉 메카닉 (양손 동시, 손별 색, 키보드 전체 지우기).

**Architecture:** `HandCursorController`의 `OnPinchStart/Move/End` 이벤트를 새 `DrawingController`(MonoBehaviour)가 구독하고, 판정 로직은 순수 정적 클래스 `StrokeLogic`으로 분리한다. 기존 컴포넌트는 드로잉의 존재를 모른다 (단방향 참조). `HandCursorController`에는 lost 시 `OnPinchEnd` 미발행 계약 공백 수정 1건만 가한다.

**Tech Stack:** Unity 6000.3.15f1 · URP · 새 Input System 1.19.0 (전용 모드) · NUnit EditMode · Unity CLI (`unity cmd`)

**Spec:** `docs/07_phase2_drawing.md`

## Global Constraints

- Unity **6000.3.15f1** 고정. 씬/에셋 조작은 `unity cmd` (Unity CLI) 사용. Editor가 이미 실행 중이다 (`unity status`로 확인).
- **새 Input System 전용** (`ProjectSettings.asset` `activeInputHandler: 1`). legacy `UnityEngine.Input` API는 예외를 던지므로 사용 금지. 키보드는 `UnityEngine.InputSystem.Keyboard.current`.
- 식별자 English, 주석은 짧은 한국어 (기존 코드 스타일).
- 핫패스(프레임당 경로)에서 LINQ·문자열 연결·`GetComponent`/`Find` 금지. 참조는 `[SerializeField]` 직접 할당.
- `EventSystem`/`GraphicRaycaster`를 씬에 넣지 말 것 (docs/04 §1).
- 순수 로직은 MonoBehaviour 밖 정적 클래스로 분리해 EditMode 테스트 (docs/04 §5 패턴). `[assembly: InternalsVisibleTo("CameraCoop.Tests.EditMode")]`는 `HandCursorController.cs:7`에 이미 선언돼 있어 internal 접근 가능.
- 테스트 실행: `unity cmd run_tests --mode EditMode`. .cs 파일을 밖에서 만들었으면 먼저 `unity cmd recompile` 후 `unity cmd recompile_status`가 `completed`/`up_to_date`가 될 때까지 폴링.
- 커밋 메시지 끝에 다음 두 줄 필수:
  `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`
  `Claude-Session: https://claude.ai/code/session_01G2qzFDtBXdJwNyvJ3C33ia`
- 프로토콜 v1 무변경. `PythonTracker/`는 이 계획에서 건드리지 않는다.
- 기존 씬 `HandTrackingTest.unity`와 기존 테스트 30개는 무수정 (Task 2가 `ProtocolTests.cs`에 테스트 2개 추가만 한다). 매 Task 끝에 전체 EditMode 테스트가 pass여야 한다.

---

### Task 1: StrokeLogic 순수 로직 + 테스트

**Files:**
- Create: `Assets/_CameraCoop/Scripts/Drawing/StrokeLogic.cs`
- Test: `Assets/_CameraCoop/Tests/EditMode/DrawingTests.cs` (신규 파일)

**Interfaces:**
- Consumes: 없음 (UnityEngine.Vector3만 사용)
- Produces: `internal static class CameraCoop.StrokeLogic` —
  `enum PinchKind { Start, Move, End }` ·
  `enum StrokeAction { None, StartNew, EndThenStartNew, Append, End }` ·
  `StrokeAction Decide(bool hasActiveStroke, PinchKind kind)` ·
  `bool ShouldAppendPoint(bool hasLastPoint, Vector3 lastPoint, Vector3 newPoint, float minDistance)` ·
  `bool ShouldDiscardOnEnd(int pointCount)` — Task 3의 `DrawingController`가 이 시그니처 그대로 호출한다.

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/_CameraCoop/Tests/EditMode/DrawingTests.cs` 생성 (기존 `ProtocolTests.cs`의 스타일을 따른다):

```csharp
using CameraCoop;
using NUnit.Framework;
using UnityEngine;

namespace CameraCoop.Tests
{
    // docs/07_phase2_drawing.md §8 표의 StrokeLogic 순수 함수 테스트.
    public class DrawingTests
    {
        // ---- StrokeLogic.ShouldAppendPoint ----

        [Test]
        public void ShouldAppendPoint_FirstPointAlwaysAppends()
        {
            Assert.IsTrue(StrokeLogic.ShouldAppendPoint(hasLastPoint: false, Vector3.zero, Vector3.zero, minDistance: 0.01f));
        }

        [Test]
        public void ShouldAppendPoint_BelowMinDistanceRejects()
        {
            var last = Vector3.zero;
            var next = new Vector3(0.005f, 0f, 0f);
            Assert.IsFalse(StrokeLogic.ShouldAppendPoint(hasLastPoint: true, last, next, minDistance: 0.01f));
        }

        [Test]
        public void ShouldAppendPoint_AtExactMinDistanceAppends()
        {
            var last = Vector3.zero;
            var next = new Vector3(0.01f, 0f, 0f);
            Assert.IsTrue(StrokeLogic.ShouldAppendPoint(hasLastPoint: true, last, next, minDistance: 0.01f));
        }

        [Test]
        public void ShouldAppendPoint_AboveMinDistanceAppends()
        {
            var last = Vector3.zero;
            var next = new Vector3(0.5f, 0f, 0f);
            Assert.IsTrue(StrokeLogic.ShouldAppendPoint(hasLastPoint: true, last, next, minDistance: 0.01f));
        }

        // ---- StrokeLogic.Decide (docs/07 §6 엣지 케이스 표) ----

        [Test]
        public void Decide_StartWithoutActive_StartsNew()
        {
            Assert.AreEqual(StrokeLogic.StrokeAction.StartNew, StrokeLogic.Decide(hasActiveStroke: false, StrokeLogic.PinchKind.Start));
        }

        [Test]
        public void Decide_StartWithActive_EndsThenStartsNew()
        {
            Assert.AreEqual(StrokeLogic.StrokeAction.EndThenStartNew, StrokeLogic.Decide(hasActiveStroke: true, StrokeLogic.PinchKind.Start));
        }

        [Test]
        public void Decide_MoveWithActive_Appends()
        {
            Assert.AreEqual(StrokeLogic.StrokeAction.Append, StrokeLogic.Decide(hasActiveStroke: true, StrokeLogic.PinchKind.Move));
        }

        [Test]
        public void Decide_MoveWithoutActive_None()
        {
            Assert.AreEqual(StrokeLogic.StrokeAction.None, StrokeLogic.Decide(hasActiveStroke: false, StrokeLogic.PinchKind.Move));
        }

        [Test]
        public void Decide_EndWithActive_Ends()
        {
            Assert.AreEqual(StrokeLogic.StrokeAction.End, StrokeLogic.Decide(hasActiveStroke: true, StrokeLogic.PinchKind.End));
        }

        [Test]
        public void Decide_EndWithoutActive_None()
        {
            Assert.AreEqual(StrokeLogic.StrokeAction.None, StrokeLogic.Decide(hasActiveStroke: false, StrokeLogic.PinchKind.End));
        }

        // ---- StrokeLogic.ShouldDiscardOnEnd ----

        [Test]
        public void ShouldDiscardOnEnd_ZeroPoints_True()
        {
            Assert.IsTrue(StrokeLogic.ShouldDiscardOnEnd(0));
        }

        [Test]
        public void ShouldDiscardOnEnd_OnePoint_True()
        {
            Assert.IsTrue(StrokeLogic.ShouldDiscardOnEnd(1));
        }

        [Test]
        public void ShouldDiscardOnEnd_TwoPoints_False()
        {
            Assert.IsFalse(StrokeLogic.ShouldDiscardOnEnd(2));
        }
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `unity cmd recompile` → `unity cmd recompile_status`가 completed가 될 때까지 폴링.
Expected: 컴파일 에러 — `StrokeLogic`이 존재하지 않음 (이것이 "실패하는 테스트" 단계다. `unity cmd console --tail 20`으로 CS0103/CS0246 에러 확인).

- [ ] **Step 3: 최소 구현 작성**

`Assets/_CameraCoop/Scripts/Drawing/StrokeLogic.cs` 생성:

```csharp
using UnityEngine;

namespace CameraCoop
{
    // 드로잉 스트로크 판정을 MonoBehaviour 밖으로 분리한 순수 함수 모음 (docs/07 §2, §8).
    internal static class StrokeLogic
    {
        // 구독자가 받은 핀치 이벤트 종류
        public enum PinchKind { Start, Move, End }

        // DrawingController가 취할 행동
        public enum StrokeAction { None, StartNew, EndThenStartNew, Append, End }

        // 활성 스트로크 유무 + 이벤트 종류 -> 행동 (docs/07 §6 엣지 케이스 표)
        public static StrokeAction Decide(bool hasActiveStroke, PinchKind kind)
        {
            switch (kind)
            {
                case PinchKind.Start:
                    return hasActiveStroke ? StrokeAction.EndThenStartNew : StrokeAction.StartNew;
                case PinchKind.Move:
                    return hasActiveStroke ? StrokeAction.Append : StrokeAction.None;
                case PinchKind.End:
                    return hasActiveStroke ? StrokeAction.End : StrokeAction.None;
                default:
                    return StrokeAction.None;
            }
        }

        // 새 점 추가 판정. 첫 점은 항상 추가, 이후는 최소 간격 이상일 때만 (~14Hz 입력의 근접점 필터).
        public static bool ShouldAppendPoint(bool hasLastPoint, Vector3 lastPoint, Vector3 newPoint, float minDistance)
        {
            if (!hasLastPoint)
            {
                return true;
            }
            return (newPoint - lastPoint).sqrMagnitude >= minDistance * minDistance;
        }

        // End 시 점 2개 미만 스트로크는 폐기 (점 찍기 미지원, docs/07 §6)
        public static bool ShouldDiscardOnEnd(int pointCount)
        {
            return pointCount < 2;
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `unity cmd recompile` → status 폴링 → `unity cmd run_tests --mode EditMode`
Expected: Total 43+, Failed 0 (기존 30 + 신규 13)

- [ ] **Step 5: Commit**

```bash
git add Assets/_CameraCoop/Scripts/Drawing/ Assets/_CameraCoop/Tests/EditMode/DrawingTests.cs Assets/_CameraCoop/Tests/EditMode/DrawingTests.cs.meta
git commit -m "feat: StrokeLogic 순수 판정 로직 + EditMode 테스트 13개 (docs/07 §2)"
```
(주의: Unity가 생성한 `.meta` 파일 — `Drawing/` 폴더, `StrokeLogic.cs`, `DrawingTests.cs` 각각의 .meta — 을 함께 커밋한다. `git status`로 누락 확인.)

---

### Task 2: HandCursorController lost 시 OnPinchEnd 발행 (계약 보장)

**Files:**
- Modify: `Assets/_CameraCoop/Scripts/Input/HandCursorController.cs` (§Update 69-74행, §UpdateHand 116-119행 부근)
- Modify: `Assets/_CameraCoop/Tests/EditMode/ProtocolTests.cs` (CursorStateLogic 섹션 끝에 테스트 2개 추가)
- Modify: `docs/04_unity_client.md` (§4 동작 명세 표에 행 1개 추가)

**Interfaces:**
- Consumes: `CursorStateLogic.DetermineEvent(bool wasPinched, bool nowPinched)` (기존, 무수정)
- Produces: 이벤트 계약 강화 — "모든 `OnPinchStart`는 반드시 `OnPinchEnd`로 닫힌다 (lost 포함)". Task 3의 `DrawingController`가 이 계약에 의존한다.

- [ ] **Step 1: 계약을 문서화하는 테스트 추가**

`ProtocolTests.cs`의 `// ---- CursorStateLogic ...` 섹션 마지막에 추가 (lost는 `nowPinched=false`로 취급한다는 계약):

```csharp
        // docs/07 §4: lost 시에도 Start-End 쌍 보장. lost == nowPinched false로 판정한다.
        [Test]
        public void DetermineEvent_PinchedThenLost_ReturnsEnd()
        {
            Assert.AreEqual(CursorStateLogic.PinchEvent.End, CursorStateLogic.DetermineEvent(wasPinched: true, nowPinched: false));
        }

        [Test]
        public void DetermineEvent_NotPinchedThenLost_ReturnsNone()
        {
            Assert.AreEqual(CursorStateLogic.PinchEvent.None, CursorStateLogic.DetermineEvent(wasPinched: false, nowPinched: false));
        }
```

- [ ] **Step 2: 테스트 실행 (이 2개는 즉시 통과한다 — 순수 함수는 이미 올바름)**

Run: `unity cmd recompile` → status 폴링 → `unity cmd run_tests --mode EditMode`
Expected: 전체 pass. 이 테스트의 역할은 회귀 방지가 아니라 lost 계약의 문서화다. 결함은 순수 함수가 아니라 호출부(controller)가 lost 경로에서 판정을 아예 건너뛰는 것이고, 그 수정이 Step 3이다.

- [ ] **Step 3: HandCursorController 수정**

`HandCursorController.cs`에 private 메서드 추가 (`FadeOut` 메서드 앞):

```csharp
        // lost 시에도 Start-End 쌍 계약 보장 (docs/07 §4): 핀치 중이던 손이 사라지면 End를 발행한다.
        private void EndPinchIfActive(HandCursorState state, string handedness)
        {
            if (CursorStateLogic.DetermineEvent(state.pinched, nowPinched: false) == CursorStateLogic.PinchEvent.End)
            {
                state.pinched = false;
                OnPinchEnd?.Invoke(handedness);
            }
        }
```

`Update()`의 serverLost 분기를 수정:

```csharp
            if (serverLost)
            {
                EndPinchIfActive(leftState, "Left");
                EndPinchIfActive(rightState, "Right");
                FadeOut(leftState);
                FadeOut(rightState);
                return; // lost 상태: 위치/핀치 갱신 스킵 (docs/04 §6)
            }
```

`UpdateHand()`의 미검출 분기를 수정:

```csharp
            if (!present)
            {
                EndPinchIfActive(state, handedness);
                return; // 손 미검출: 위치/핀치 갱신 스킵
            }
```

- [ ] **Step 4: docs/04 §4 동작 명세 표에 행 추가**

`docs/04_unity_client.md`의 §4 "동작 명세" 표 마지막에:

```markdown
| 핀치 중 lost | 손/서버 lost로 갱신을 스킵하기 전에 `OnPinchEnd` 발행 + pinched 해제. 모든 Start는 End로 닫힌다 (docs/07 §4) |
```

- [ ] **Step 5: 전체 테스트 통과 확인**

Run: `unity cmd recompile` → status 폴링 → `unity cmd run_tests --mode EditMode`
Expected: Total 45+, Failed 0

- [ ] **Step 6: Commit**

```bash
git add Assets/_CameraCoop/Scripts/Input/HandCursorController.cs Assets/_CameraCoop/Tests/EditMode/ProtocolTests.cs docs/04_unity_client.md
git commit -m "fix: 핀치 중 손/서버 lost 시 OnPinchEnd 발행 — Start-End 쌍 계약 보장 (docs/07 §4)"
```

---

### Task 3: DrawingController MonoBehaviour

**Files:**
- Create: `Assets/_CameraCoop/Scripts/Drawing/DrawingController.cs`
- Modify: `Assets/_CameraCoop/Scripts/CameraCoop.Runtime.asmdef` (references에 `"Unity.InputSystem"` 추가)

**Interfaces:**
- Consumes: `HandCursorController.OnPinchStart/OnPinchMove(Action<string, Vector2>)`, `OnPinchEnd(Action<string>)` · `StrokeLogic.Decide/ShouldAppendPoint/ShouldDiscardOnEnd` (Task 1 시그니처)
- Produces: `public class CameraCoop.DrawingController : MonoBehaviour` — SerializeField 이름: `cursorController`, `drawCamera`, `planeDistance`, `minPointDistance`, `lineWidth`, `lineMaterial`, `leftStrokeColor`, `rightStrokeColor`, `clearKey`. Task 4의 씬 배선 스크립트가 이 필드명을 그대로 쓴다.

- [ ] **Step 1: asmdef에 Input System 참조 추가**

`Assets/_CameraCoop/Scripts/CameraCoop.Runtime.asmdef`의 references를 다음으로 교체:

```json
    "references": [
        "UnityEngine.UI",
        "Unity.InputSystem"
    ],
```

- [ ] **Step 2: DrawingController.cs 작성 (전체 코드)**

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CameraCoop
{
    // 핀치 이벤트를 구독해 카메라 앞 고정 평면에 LineRenderer 스트로크를 그린다 (docs/07).
    // 기존 컴포넌트는 이 클래스의 존재를 모른다 — 참조는 단방향 (docs/01 §4).
    public class DrawingController : MonoBehaviour
    {
        [SerializeField] private HandCursorController cursorController;
        [SerializeField] private Camera drawCamera;
        [SerializeField] private float planeDistance = 5.0f;      // 카메라 -> 드로잉 평면 거리 (m)
        [SerializeField] private float minPointDistance = 0.01f;  // 점 추가 최소 간격 (월드 단위)
        [SerializeField] private float lineWidth = 0.02f;
        [SerializeField] private Material lineMaterial;           // vertex color를 곱하는 셰이더여야 함 (URP Particles/Unlit)
        [SerializeField] private Color leftStrokeColor = new Color(0.2f, 0.6f, 1f);   // 커서 색과 같은 계열
        [SerializeField] private Color rightStrokeColor = new Color(1f, 0.6f, 0.1f);
        [SerializeField] private Key clearKey = Key.C;            // 새 Input System 전용 (docs/07 §5)

        private class ActiveStroke
        {
            public LineRenderer line;
            public Vector3 lastPoint;
        }

        // 손별 진행 중 스트로크 ("Left"/"Right" 키). 확정분은 finishedStrokes로 이동.
        private readonly Dictionary<string, ActiveStroke> activeStrokes = new Dictionary<string, ActiveStroke>(2);
        private readonly List<GameObject> finishedStrokes = new List<GameObject>();

        private void OnEnable()
        {
            cursorController.OnPinchStart += HandlePinchStart;
            cursorController.OnPinchMove += HandlePinchMove;
            cursorController.OnPinchEnd += HandlePinchEnd;
        }

        private void OnDisable()
        {
            cursorController.OnPinchStart -= HandlePinchStart;
            cursorController.OnPinchMove -= HandlePinchMove;
            cursorController.OnPinchEnd -= HandlePinchEnd;
        }

        private void Update()
        {
            // Keyboard 부재(장치 없음) 방어. legacy Input API는 이 프로젝트에서 예외를 던진다.
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard[clearKey].wasPressedThisFrame)
            {
                ClearAll();
            }
        }

        private void HandlePinchStart(string handedness, Vector2 screenPos)
        {
            switch (StrokeLogic.Decide(activeStrokes.ContainsKey(handedness), StrokeLogic.PinchKind.Start))
            {
                case StrokeLogic.StrokeAction.EndThenStartNew: // 방어: 중복 Start (docs/07 §6)
                    FinishStroke(handedness);
                    BeginStroke(handedness, screenPos);
                    break;
                case StrokeLogic.StrokeAction.StartNew:
                    BeginStroke(handedness, screenPos);
                    break;
            }
        }

        private void HandlePinchMove(string handedness, Vector2 screenPos)
        {
            if (StrokeLogic.Decide(activeStrokes.ContainsKey(handedness), StrokeLogic.PinchKind.Move) != StrokeLogic.StrokeAction.Append)
            {
                return; // 고아 Move (docs/07 §6)
            }

            ActiveStroke stroke = activeStrokes[handedness];
            Vector3 point = ToPlanePoint(screenPos);
            if (!StrokeLogic.ShouldAppendPoint(hasLastPoint: true, stroke.lastPoint, point, minPointDistance))
            {
                return;
            }

            int count = stroke.line.positionCount;
            stroke.line.positionCount = count + 1;
            stroke.line.SetPosition(count, point);
            stroke.lastPoint = point;
        }

        private void HandlePinchEnd(string handedness)
        {
            if (StrokeLogic.Decide(activeStrokes.ContainsKey(handedness), StrokeLogic.PinchKind.End) == StrokeLogic.StrokeAction.End)
            {
                FinishStroke(handedness);
            }
        }

        // 화면 좌표 -> 카메라 앞 planeDistance 평면의 월드 좌표 (docs/07 §3)
        private Vector3 ToPlanePoint(Vector2 screenPos)
        {
            return drawCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, planeDistance));
        }

        private void BeginStroke(string handedness, Vector2 screenPos)
        {
            var strokeObject = new GameObject("Stroke_" + handedness);
            strokeObject.transform.SetParent(transform, worldPositionStays: true);

            LineRenderer line = strokeObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.widthMultiplier = lineWidth;
            line.sharedMaterial = lineMaterial;
            line.numCapVertices = 4;    // ~14Hz 입력의 각짐 완화 (docs/07 §3)
            line.numCornerVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            Color color = string.Equals(handedness, "Left", System.StringComparison.Ordinal) ? leftStrokeColor : rightStrokeColor;
            line.startColor = color;
            line.endColor = color;

            Vector3 point = ToPlanePoint(screenPos);
            line.positionCount = 1;
            line.SetPosition(0, point);

            activeStrokes[handedness] = new ActiveStroke { line = line, lastPoint = point };
        }

        private void FinishStroke(string handedness)
        {
            ActiveStroke stroke = activeStrokes[handedness];
            activeStrokes.Remove(handedness);

            if (StrokeLogic.ShouldDiscardOnEnd(stroke.line.positionCount))
            {
                Destroy(stroke.line.gameObject); // 점 찍기 미지원 (docs/07 §6)
                return;
            }
            finishedStrokes.Add(stroke.line.gameObject);
        }

        private void ClearAll()
        {
            for (int i = 0; i < finishedStrokes.Count; i++)
            {
                if (finishedStrokes[i] != null)
                {
                    Destroy(finishedStrokes[i]);
                }
            }
            finishedStrokes.Clear();

            foreach (KeyValuePair<string, ActiveStroke> pair in activeStrokes)
            {
                Destroy(pair.Value.line.gameObject);
            }
            activeStrokes.Clear(); // 이후 Move/End는 고아로 무시된다 (docs/07 §6)
        }
    }
}
```

- [ ] **Step 3: 컴파일 + 전체 테스트 통과 확인**

Run: `unity cmd recompile` → status 폴링 → `unity cmd run_tests --mode EditMode`
Expected: Task 2까지의 테스트 전체 pass, 신규 컴파일 에러 0 (`unity cmd console --tail 10 --level error`로 확인)

- [ ] **Step 4: Commit**

```bash
git add Assets/_CameraCoop/Scripts/Drawing/DrawingController.cs Assets/_CameraCoop/Scripts/Drawing/DrawingController.cs.meta Assets/_CameraCoop/Scripts/CameraCoop.Runtime.asmdef
git commit -m "feat: DrawingController — 핀치 이벤트 구독 LineRenderer 드로잉 (docs/07 §3)"
```

---

### Task 4: 머티리얼 + DrawingTest 씬 구성

**Files:**
- Create: `Assets/_CameraCoop/Materials/StrokeLine.mat` (URP Particles/Unlit — LineRenderer vertex color 지원)
- Create: `Assets/_CameraCoop/Materials/Backdrop.mat` (URP Unlit, 어두운 색)
- Create: `Assets/_CameraCoop/Scenes/DrawingTest.unity` (`HandTrackingTest.unity` 복사 후 확장)
- Create: `Assets/_CameraCoop/Editor/` 하위 임시 배선 스크립트는 만들지 않는다 — `unity cmd eval_file`로 실행하고 스크립트 파일은 scratchpad에 둔다 (프로젝트에 커밋 금지)

**Interfaces:**
- Consumes: Task 3의 `DrawingController` SerializeField 이름들
- Produces: Play 가능한 `DrawingTest.unity` — Task 5의 검증 대상

- [ ] **Step 1: 셰이더 확인 + 머티리얼 생성**

```bash
unity cmd list_shaders --filter "Particles/Unlit" --limit 5
```
Expected: `Universal Render Pipeline/Particles/Unlit`이 목록에 있고 isSupported true. (없으면 STOP — 사람에게 보고. URP Unlit은 vertex color를 무시하므로 대체 불가.)

```bash
unity cmd create_folder --path Assets/_CameraCoop/Materials
unity cmd create_asset --path Assets/_CameraCoop/Materials/StrokeLine.mat --type Material --shader "Universal Render Pipeline/Particles/Unlit"
unity cmd create_asset --path Assets/_CameraCoop/Materials/Backdrop.mat --type Material --shader "Universal Render Pipeline/Unlit"
unity cmd set_material_properties --material Assets/_CameraCoop/Materials/Backdrop.mat --properties '{"_BaseColor": [0.08, 0.08, 0.1, 1.0]}'
```
(`set_material_properties`의 정확한 파라미터명은 `unity cmd --list`로 확인. 실패 시 eval로 대체: `AssetDatabase.LoadAssetAtPath<Material>(...).SetColor("_BaseColor", new Color(0.08f,0.08f,0.1f,1f))` + `AssetDatabase.SaveAssets()`)

- [ ] **Step 2: 씬 복사 + 열기**

```bash
unity cmd copy_asset --asset Assets/_CameraCoop/Scenes/HandTrackingTest.unity --destination Assets/_CameraCoop/Scenes/DrawingTest.unity --confirm true
unity cmd open_scene --path Assets/_CameraCoop/Scenes/DrawingTest.unity
```

- [ ] **Step 3: 배선 스크립트 작성 + 실행**

scratchpad에 `wire_drawing_scene.cs`를 만들고 `unity cmd eval_file --file <절대경로>`로 실행:

```csharp
// DrawingTest.unity 배선: DrawingController 추가 + 참조 연결 + backdrop 생성 + 저장
var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
if (scene.name != "DrawingTest") { return "WRONG SCENE: " + scene.name; }

var handTracking = GameObject.Find("/HandTracking");
var cursorController = handTracking.GetComponent<CameraCoop.HandCursorController>();
var camera = GameObject.Find("/Camera").GetComponent<Camera>();

var drawing = handTracking.GetComponent<CameraCoop.DrawingController>();
if (drawing == null) { drawing = handTracking.AddComponent<CameraCoop.DrawingController>(); }

var lineMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/_CameraCoop/Materials/StrokeLine.mat");
var so = new UnityEditor.SerializedObject(drawing);
so.FindProperty("cursorController").objectReferenceValue = cursorController;
so.FindProperty("drawCamera").objectReferenceValue = camera;
so.FindProperty("lineMaterial").objectReferenceValue = lineMat;
so.ApplyModifiedPropertiesWithoutUndo();

// backdrop: 드로잉 평면(카메라 앞 5m)보다 0.5m 뒤의 어두운 Quad
var existing = GameObject.Find("/Backdrop");
if (existing == null)
{
    var backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
    backdrop.name = "Backdrop";
    Object.DestroyImmediate(backdrop.GetComponent<Collider>()); // 콜라이더 불필요
    backdrop.transform.position = camera.transform.position + camera.transform.forward * 5.5f;
    backdrop.transform.rotation = camera.transform.rotation;
    backdrop.transform.localScale = new Vector3(14f, 8f, 1f);
    var backdropMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/_CameraCoop/Materials/Backdrop.mat");
    backdrop.GetComponent<MeshRenderer>().sharedMaterial = backdropMat;
}

UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
return "OK";
```
Expected: `OK`. (backdrop의 5.5f는 `planeDistance` 기본값 5.0f + 0.5f. planeDistance 기본값을 바꾸면 함께 바꾼다.)

- [ ] **Step 4: 배선 검증**

```bash
unity cmd get_serialized_fields --target /HandTracking --component DrawingController
```
Expected: `cursorController`/`drawCamera`/`lineMaterial`이 null 아님, `planeDistance` 5, `clearKey` C. (`--target`이 hierarchyPath를 받지 않으면 `unity cmd find_gameobjects --name HandTracking`으로 instanceId를 얻어 사용.)

- [ ] **Step 5: 전체 테스트 + 씬 무결성 확인**

Run: `unity cmd run_tests --mode EditMode` → 전체 pass.
`unity cmd open_scene --path Assets/_CameraCoop/Scenes/HandTrackingTest.unity` 후 `git status`로 기존 씬이 변경되지 않았는지 확인 (변경돼 있으면 STOP — 사람에게 보고).

- [ ] **Step 6: Commit**

```bash
git add Assets/_CameraCoop/Materials Assets/_CameraCoop/Scenes/DrawingTest.unity Assets/_CameraCoop/Scenes/DrawingTest.unity.meta
git commit -m "feat: DrawingTest 씬 + 스트로크/배경 머티리얼 (docs/07 §7)"
```
(Materials 폴더 .meta 포함 — `git status`로 누락 확인.)

---

### Task 5: 합성 송신기 통합 검증

**Files:**
- 없음 (검증만. 산출물은 보고와 스크린샷)

**Interfaces:**
- Consumes: Task 4의 `DrawingTest.unity`, `PythonTracker/fake_hand.py` (기존, 무수정), `PythonTracker/.venv`
- Produces: 검증 보고 — 스트로크 생성/종료/clear 동작 증거. 웹캠 육안 DoD(D-1~D-4)는 이 Task의 범위가 아니다 (사용자 협조 필요 — 메인 세션이 진행).

- [ ] **Step 1: Play 진입 + 합성 송신**

```bash
unity cmd open_scene --path Assets/_CameraCoop/Scenes/DrawingTest.unity
unity cmd editor_play
PythonTracker/.venv/bin/python PythonTracker/fake_hand.py 20 &
sleep 12
```
(fake_hand.py는 pinch 값을 진동시키므로 스트로크가 생겼다 끊긴다. 20초간 두 손 패킷 송신.)

- [ ] **Step 2: 스트로크 생성 검증 (eval)**

```bash
unity cmd eval --code "
var lines = Object.FindObjectsByType<LineRenderer>(FindObjectsSortMode.None);
int total = lines.Length; int multi = 0; int maxPts = 0;
foreach (var l in lines) { if (l.positionCount >= 2) multi++; if (l.positionCount > maxPts) maxPts = l.positionCount; }
return $\"lines={total} multiPoint={multi} maxPoints={maxPts}\";
"
```
Expected: `lines >= 2` (좌/우 각각 최소 1개), `multiPoint >= 2`, `maxPoints >= 3`. 미달이면 STOP — 이벤트 배선 또는 투영 문제. `unity cmd console --tail 20 --level error`로 원인 확인 후 사람에게 보고.

- [ ] **Step 3: 스크린샷 증거**

```bash
unity cmd capture_game_view --source screen --save_path Temp/drawing_test.png
```
(경로는 프로젝트 루트 기준 상대 경로만 허용된다. `Assets/Temp/`에 저장되면 검증 후 파일과 .meta를 삭제한다.)

- [ ] **Step 4: 서버 단절 + clear 검증**

```bash
pkill -f fake_hand.py
sleep 2
unity cmd eval --code "
var lines = Object.FindObjectsByType<LineRenderer>(FindObjectsSortMode.None);
return $\"after-kill lines={lines.Length}\";
"
```
Expected: 단절 후에도 확정 스트로크는 남아 있다 (lines >= 2 유지 — S6 계약: 드로잉은 lost와 무관하게 보존). lost 시 활성 스트로크가 정상 종료됐는지는 콘솔 에러 0건으로 간접 확인.

clear 검증 (Keyboard 합성 입력은 CLI로 불가하므로 eval로 ClearAll을 리플렉션 호출):

```bash
unity cmd eval --code "
var dc = Object.FindFirstObjectByType<CameraCoop.DrawingController>();
dc.GetType().GetMethod(\"ClearAll\", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(dc, null);
return \"cleared\";
"
sleep 1
unity cmd eval --code "
var lines = Object.FindObjectsByType<LineRenderer>(FindObjectsSortMode.None);
return $\"after-clear lines={lines.Length}\";
"
```
Expected: `after-clear lines=0`. (clearKey 키 입력 자체의 육안 확인은 웹캠 세션에서 D-3으로 수행.)

- [ ] **Step 5: 종료 + 정리 + 보고**

```bash
unity cmd editor_stop
unity cmd get_console_logs --severity error --limit 10
rm -f Assets/Temp/drawing_test.png Assets/Temp/drawing_test.png.meta && rmdir Assets/Temp 2>/dev/null; rm -f Assets/Temp.meta
```
Expected: 에러 0건 (capture 경로 실수 등 자기 유발 에러 제외). 결과 요약 보고: 스트로크 수, maxPoints, 단절 후 보존, clear 동작, 에러 유무. **커밋 없음** (검증 Task).

---

## 계획 외 잔여 작업 (메인 세션 담당)

- 웹캠 육안 DoD: D-1(핀치 그리기), D-2(양손 색), D-3(clear 키), D-4(핀치 중 손 이탈 → 점프 없음), D-7(5분) — 사용자 협조 필요
- QUALITY_CHECKLIST 채점 (D-6) 및 docs/07 §9 DoD 표 결과 기록
