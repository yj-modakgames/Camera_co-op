# Phase 3d — 3D 월드 캔버스 (Netplay3D) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Steam/Loopback 멀티플레이 드로잉을 3D 룸 + 월드 공간 캔버스에서 동작하게 한다.

**Architecture:** 와이어 프로토콜(network v1)은 이미 정규화 [0,1] 캔버스 좌표이므로 네트워크 계층 무수정. 신규 `CanvasSurface`(norm→월드 매핑) 1개를 만들고 기존 `DrawingController`/`RemotePresenter`/`HandCursorController`에 optional 주입 — 미할당이면 기존 화면 공간 동작 (NetplayTest 무회귀). 새 씬 `Netplay3D.unity`는 NetplayTest를 복제 후 3D 룸을 얹는다.

**Tech Stack:** Unity 6000.3.15f1 (URP, 새 Input System 전용), NUnit EditMode, Unity CLI (`unity cmd`)

**Spec:** `docs/10_phase3d_world_canvas.md` (승인됨. docs/08, docs/09도 함께 읽을 것)

## Global Constraints

- Unity Editor 자동화는 `unity cmd`만 사용 (바이너리 `C:\Users\yunji\AppData\Local\Unity\bin\unity.exe`). 시작 전 `unity pipeline list`로 Server Reachable 확인 — 안 뜨면 Editor 창 포커스 필요: `(New-Object -ComObject WScript.Shell).AppActivate(<Unity PID>)`
- **`unity cmd eval`/`eval_file`은 코드를 메서드 본문에 감싼다 — `using` 지시문 금지, 전부 전체 이름** (예: `CameraCoop.Netplay.NetSession`)
- **Play 모드 중 `recompile` 금지** (domain reload NRE burst) — 반드시 `editor_stop` 먼저. **컴파일 중 `run_tests` 금지** — `editor_status`의 `compiling:false` 확인 후 실행
- instanceId는 Play 진입/domain reload마다 무효화 — 매번 `get_scene_hierarchy`로 재취득
- `capture_game_view --save_path`는 실제로 `Assets/` 밑에 저장됨 — 검증 후 `.meta`와 함께 삭제
- 네트워크 계층(`NetProtocol`/`SteamTransport`/`LoopbackTransport`/`INetTransport`) 무수정. `NetSession`은 Task 2의 `ToNormalized` 1줄 위임 교체만
- 기존 씬 3개(`NetplayTest`/`DrawingTest`/`HandTrackingTest`) 무수정 — 각 Task 끝 `git status`로 확인
- 코드 스타일: 기존 파일 규칙 준수 — LINQ 금지(핫패스), 한글 주석 + docs 참조, 순수 로직은 static class 분리, 테스트 namespace는 `CameraCoop.Tests`
- 커밋 메시지: 본문 한글, 식별자(클래스명·파일명)는 영문 그대로

---

### Task 1: CanvasSurface + CanvasSurfaceLogic + EditMode 테스트

**Files:**
- Create: `Assets/_CameraCoop/Scripts/Drawing/CanvasSurface.cs`
- Create: `Assets/_CameraCoop/Tests/EditMode/CanvasSurfaceTests.cs`

**Interfaces:**
- Consumes: 없음
- Produces (Task 3·4가 사용): `CameraCoop.CanvasSurface : MonoBehaviour` — `Vector3 NormToWorld(Vector2 norm)`. `internal static CanvasSurfaceLogic.NormToLocal(Vector2 norm, float zOffset) : Vector3` (InternalsVisibleTo는 `HandCursorController.cs:7`에 이미 선언됨)

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/_CameraCoop/Tests/EditMode/CanvasSurfaceTests.cs`:

```csharp
using CameraCoop;
using NUnit.Framework;
using UnityEngine;

namespace CameraCoop.Tests
{
    // docs/10 §2 — norm [0,1] 좌상단 원점 -> 1x1 Quad 로컬/월드 매핑
    public class CanvasSurfaceTests
    {
        [Test]
        public void NormToLocal_Center_MapsToOrigin()
        {
            Vector3 local = CanvasSurfaceLogic.NormToLocal(new Vector2(0.5f, 0.5f), zOffset: -0.005f);
            Assert.AreEqual(0f, local.x, 1e-5f);
            Assert.AreEqual(0f, local.y, 1e-5f);
            Assert.AreEqual(-0.005f, local.z, 1e-5f);
        }

        [Test]
        public void NormToLocal_TopLeft_MapsToUpperLeftQuadCorner()
        {
            // norm (0,0) = 좌상단 -> 로컬 (-0.5, +0.5) (y 반전)
            Vector3 local = CanvasSurfaceLogic.NormToLocal(new Vector2(0f, 0f), zOffset: 0f);
            Assert.AreEqual(-0.5f, local.x, 1e-5f);
            Assert.AreEqual(0.5f, local.y, 1e-5f);
        }

        [Test]
        public void NormToLocal_BottomRight_MapsToLowerRightQuadCorner()
        {
            Vector3 local = CanvasSurfaceLogic.NormToLocal(new Vector2(1f, 1f), zOffset: 0f);
            Assert.AreEqual(0.5f, local.x, 1e-5f);
            Assert.AreEqual(-0.5f, local.y, 1e-5f);
        }

        [Test]
        public void NormToWorld_AppliesPositionAndScale()
        {
            var go = new GameObject("canvas");
            try
            {
                go.transform.position = new Vector3(1f, 2f, 3f);
                go.transform.localScale = new Vector3(2f, 4f, 1f);
                var surface = go.AddComponent<CanvasSurface>();
                // norm (0,0) 좌상단 -> 로컬 (-0.5, +0.5, -0.005) -> 스케일·이동 적용
                Vector3 world = surface.NormToWorld(new Vector2(0f, 0f));
                Assert.AreEqual(1f - 1f, world.x, 1e-4f);   // 1 + (-0.5 * 2)
                Assert.AreEqual(2f + 2f, world.y, 1e-4f);   // 2 + (0.5 * 4)
                Assert.AreEqual(3f - 0.005f, world.z, 1e-4f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void NormToWorld_AppliesRotation()
        {
            var go = new GameObject("canvas");
            try
            {
                go.transform.rotation = Quaternion.Euler(0f, 180f, 0f); // 뒤집으면 x 부호 반전
                var surface = go.AddComponent<CanvasSurface>();
                Vector3 world = surface.NormToWorld(new Vector2(0f, 0.5f)); // 로컬 (-0.5, 0, -0.005)
                Assert.AreEqual(0.5f, world.x, 1e-4f);
                Assert.AreEqual(0f, world.y, 1e-4f);
                Assert.AreEqual(0.005f, world.z, 1e-4f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `unity cmd recompile` (Play 모드 아님을 `editor_status`로 먼저 확인) → 컴파일 에러 (CanvasSurface 미존재) 확인.

- [ ] **Step 3: 구현 작성**

`Assets/_CameraCoop/Scripts/Drawing/CanvasSurface.cs`:

```csharp
using UnityEngine;

namespace CameraCoop
{
    // 월드 공간 캔버스 평면 (docs/10 §2). 1x1 Quad에 부착 — transform 스케일이 곧 캔버스 크기.
    // 정규화 [0,1] 좌상단 원점 좌표(docs/02 §3)를 캔버스 표면 위 월드 좌표로 매핑한다.
    // 소비자(DrawingController/RemotePresenter/HandCursorController)는 이 컴포넌트를 optional 참조 — 역방향 참조 없음.
    public class CanvasSurface : MonoBehaviour
    {
        [SerializeField] private float surfaceOffset = -0.005f; // 로컬 z. Quad 정면(-Z) 쪽으로 띄워 z-fighting 방지

        public Vector3 NormToWorld(Vector2 norm)
        {
            return transform.TransformPoint(CanvasSurfaceLogic.NormToLocal(norm, surfaceOffset));
        }
    }

    // 매핑 수식을 MonoBehaviour 밖으로 분리한 순수 함수 (docs/04 §5 테스트 가능 설계)
    internal static class CanvasSurfaceLogic
    {
        // norm [0,1] 좌상단 원점 -> 1x1 Quad 로컬 좌표 (중심 원점, y 반전)
        public static Vector3 NormToLocal(Vector2 norm, float zOffset)
        {
            return new Vector3(norm.x - 0.5f, 0.5f - norm.y, zOffset);
        }
    }
}
```

- [ ] **Step 4: 통과 확인**

Run: `unity cmd recompile` → 에러 0 → `editor_status`로 `compiling:false` 확인 → `unity cmd run_tests --mode EditMode --timeout 120`
Expected: 기존 72 + 신규 5 = **77 pass**

- [ ] **Step 5: Commit**

```bash
git add Assets/_CameraCoop/Scripts/Drawing/CanvasSurface.cs* Assets/_CameraCoop/Tests/EditMode/CanvasSurfaceTests.cs*
git commit -m "feat: CanvasSurface — norm→월드 캔버스 매핑 (docs/10 §2)"
```
(`.meta` 파일 포함 확인)

---

### Task 2: HandScreenMapper.ToNormalized + NetSession 위임 교체

**Files:**
- Modify: `Assets/_CameraCoop/Scripts/Net/HandData.cs:76-82` (HandScreenMapper)
- Modify: `Assets/_CameraCoop/Scripts/Netplay/NetSession.cs:509-513` (ToNormalized)
- Modify: `Assets/_CameraCoop/Tests/EditMode/CanvasSurfaceTests.cs` (왕복 테스트 추가)

**Interfaces:**
- Consumes: 없음
- Produces (Task 3이 사용): `CameraCoop.HandScreenMapper.ToNormalized(Vector2 screenPos, float screenW, float screenH) : Vector2` — `ToScreen`의 역함수 (y 반전 포함)

- [ ] **Step 1: 실패하는 테스트 추가**

`CanvasSurfaceTests.cs` 클래스 끝에 추가:

```csharp
        // ---- HandScreenMapper 왕복 (docs/10 §2 — screen↔norm 단일 진실 원천) ----

        [Test]
        public void HandScreenMapper_RoundTrip_IsIdentity()
        {
            var norm = new Vector2(0.25f, 0.75f);
            Vector2 screen = HandScreenMapper.ToScreen(norm.x, norm.y, 1920f, 1080f);
            Vector2 back = HandScreenMapper.ToNormalized(screen, 1920f, 1080f);
            Assert.AreEqual(norm.x, back.x, 1e-5f);
            Assert.AreEqual(norm.y, back.y, 1e-5f);
        }

        [Test]
        public void HandScreenMapper_ToNormalized_FlipsY()
        {
            // 화면 좌하단 원점 (0,0) -> norm 좌상단 원점이므로 (0,1)
            Vector2 norm = HandScreenMapper.ToNormalized(Vector2.zero, 1920f, 1080f);
            Assert.AreEqual(0f, norm.x, 1e-5f);
            Assert.AreEqual(1f, norm.y, 1e-5f);
        }
```

- [ ] **Step 2: 실패 확인**

Run: `unity cmd recompile` → 컴파일 에러 (ToNormalized 미존재) 확인.

- [ ] **Step 3: 구현**

`HandData.cs`의 `HandScreenMapper`에 추가 (기존 ToScreen 아래):

```csharp
        // 화면 픽셀(좌하단 원점) → 정규화 [0,1](좌상단 원점). ToScreen의 역함수.
        public static Vector2 ToNormalized(Vector2 screenPos, float screenW, float screenH)
        {
            return new Vector2(screenPos.x / screenW, 1f - screenPos.y / screenH);
        }
```

`NetSession.cs`의 `ToNormalized` 본문을 위임으로 교체 (중복 제거 — docs/10 §2):

```csharp
        // 화면 픽셀 -> 정규화 (송신은 항상 정규화 좌표, docs/08 §3). 변환 수식은 HandScreenMapper가 단일 진실 원천.
        private Vector2 ToNormalized(Vector2 screenPos)
        {
            return HandScreenMapper.ToNormalized(screenPos, Screen.width, Screen.height);
        }
```

- [ ] **Step 4: 통과 확인**

Run: recompile → `compiling:false` → `unity cmd run_tests --mode EditMode --timeout 120`
Expected: **79 pass** (77 + 2)

- [ ] **Step 5: Commit**

```bash
git add Assets/_CameraCoop/Scripts/Net/HandData.cs Assets/_CameraCoop/Scripts/Netplay/NetSession.cs Assets/_CameraCoop/Tests/EditMode/CanvasSurfaceTests.cs
git commit -m "refactor: screen↔norm 변환을 HandScreenMapper로 단일화 + ToNormalized 추가"
```

---

### Task 3: DrawingController / RemotePresenter / HandCursorController에 canvasSurface 주입

**Files:**
- Modify: `Assets/_CameraCoop/Scripts/Drawing/DrawingController.cs` (필드 1 + ToPlanePoint 분기)
- Modify: `Assets/_CameraCoop/Scripts/Netplay/RemotePresenter.cs` (필드 1 + ToWorld·커서 위치 분기)
- Modify: `Assets/_CameraCoop/Scripts/Input/HandCursorController.cs` (필드 2 + 커서 표시 위치 분기)

**Interfaces:**
- Consumes: Task 1 `CanvasSurface.NormToWorld(Vector2)`, Task 2 `HandScreenMapper.ToNormalized(Vector2, float, float)`
- Produces (Task 4가 씬에서 배선): serialized 필드 `canvasSurface` (3개 컴포넌트 공통), `projectionCamera` (HandCursorController만)

**불변 계약 (전부 유지 — 위반 시 STOP):**
- `OnPinchStart/Move` 이벤트의 `screenPos`는 기존 `HandScreenMapper.ToScreen` 값 그대로 — `NetSession.ToNormalized` 왕복 보존
- `ShouldSplitStroke` 판정은 screenPos 기반 그대로
- `canvasSurface` 미할당 시 세 컴포넌트 모두 기존 동작과 바이트 단위 동일 경로

- [ ] **Step 1: DrawingController 수정**

필드 추가 (`lineMaterial` 필드 아래):

```csharp
        [SerializeField] private CanvasSurface canvasSurface;    // 할당 시 월드 캔버스에 그림 (docs/10 §2). 미할당 = 기존 카메라 평면
```

`ToPlanePoint` 교체:

```csharp
        // 화면 좌표 -> 드로잉 표면 월드 좌표. canvasSurface 할당 시 월드 캔버스(docs/10 §2), 미할당 시 카메라 앞 평면(docs/07 §3)
        private Vector3 ToPlanePoint(Vector2 screenPos)
        {
            if (canvasSurface != null)
            {
                return canvasSurface.NormToWorld(HandScreenMapper.ToNormalized(screenPos, Screen.width, Screen.height));
            }
            return drawCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, planeDistance));
        }
```

- [ ] **Step 2: RemotePresenter 수정**

필드 추가 (`lineMaterial` 필드 아래):

```csharp
        [SerializeField] private CanvasSurface canvasSurface;    // 할당 시 월드 캔버스에 표시 (docs/10 §2). 미할당 = 기존 카메라 평면
```

`HandleCursor`의 위치 설정 줄(`cursor.rect.position = ...`) 교체:

```csharp
            cursor.rect.position = canvasSurface != null
                ? (Vector2)drawCamera.WorldToScreenPoint(canvasSurface.NormToWorld(norm))
                : HandScreenMapper.ToScreen(norm.x, norm.y, Screen.width, Screen.height);
```

`ToWorld` 교체:

```csharp
        // 정규화 [0,1] (좌상단 원점) -> 월드 좌표. canvasSurface 할당 시 월드 캔버스, 미할당 시 카메라 평면
        private Vector3 ToWorld(Vector2 norm)
        {
            if (canvasSurface != null)
            {
                return canvasSurface.NormToWorld(norm);
            }
            Vector2 screen = HandScreenMapper.ToScreen(norm.x, norm.y, Screen.width, Screen.height);
            return drawCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, planeDistance));
        }
```

- [ ] **Step 3: HandCursorController 수정**

필드 추가 (`fadeDuration` 필드 아래):

```csharp
        [SerializeField] private CanvasSurface canvasSurface;    // 할당 시 커서를 월드 캔버스 투영 위치에 표시 (docs/10 §2)
        [SerializeField] private Camera projectionCamera;        // canvasSurface 사용 시 필수 (WorldToScreenPoint용)
```

`UpdateHand`의 위치 설정부 교체 — 기존:

```csharp
            Vector3 tip = hand.GetLandmark(8); // index tip
            Vector2 screenPos = HandScreenMapper.ToScreen(tip.x, tip.y, Screen.width, Screen.height);
            state.cursor.position = screenPos;
```

교체 후 (**이벤트용 screenPos는 불변** — 표시 위치만 분기):

```csharp
            Vector3 tip = hand.GetLandmark(8); // index tip
            Vector2 screenPos = HandScreenMapper.ToScreen(tip.x, tip.y, Screen.width, Screen.height);
            // 표시 위치만 월드 캔버스 투영으로 분기. 이벤트의 screenPos는 기존 값 유지 (NetSession 왕복 계약, docs/10 §2)
            Vector2 displayPos = screenPos;
            if (canvasSurface != null && projectionCamera != null)
            {
                displayPos = projectionCamera.WorldToScreenPoint(canvasSurface.NormToWorld(new Vector2(tip.x, tip.y)));
            }
            state.cursor.position = displayPos;
```

- [ ] **Step 4: 컴파일 + 전체 테스트 + 무회귀 확인**

Run: recompile → 에러 0 → `compiling:false` → `run_tests` → **79 pass**
`git status`로 씬 파일(`*.unity`) 무변경 확인.

- [ ] **Step 5: Commit**

```bash
git add Assets/_CameraCoop/Scripts/Drawing/DrawingController.cs Assets/_CameraCoop/Scripts/Netplay/RemotePresenter.cs Assets/_CameraCoop/Scripts/Input/HandCursorController.cs
git commit -m "feat: 드로잉·커서 표시 계층에 CanvasSurface optional 주입 (docs/10 §2)"
```

---

### Task 4: Netplay3D 씬 구성 (eval_file)

**Files:**
- Create: `Assets/_CameraCoop/Scenes/Netplay3D.unity` (NetplayTest 복제 후 3D 룸 추가)
- Create: `Assets/_CameraCoop/Materials/CanvasWhite.mat`, `RoomFloor.mat`, `RoomWall.mat`, `EaselWood.mat`
- Modify: `ProjectSettings/EditorBuildSettings.asset` (씬 등록 — eval의 EditorBuildSettings API로)
- 씬 구성 스크립트는 scratchpad에만 (커밋 금지)

**Interfaces:**
- Consumes: Task 1 `CanvasSurface`, Task 3의 serialized 필드 `canvasSurface`/`projectionCamera`
- Produces: Task 5가 Play할 `Netplay3D.unity`

- [ ] **Step 1: Editor 상태 확인**

`unity cmd editor_status` — Play 모드면 `editor_stop`. `compiling:false` 확인.

- [ ] **Step 2: 씬 구성 스크립트 실행**

scratchpad에 `build_netplay3d.cs`로 저장 후 `unity cmd eval_file --path <절대경로>` 실행. **`using` 지시문 금지 — 전체 이름만** (eval은 메서드 본문 래핑):

```csharp
// 1) NetplayTest 열고 Netplay3D로 저장
var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/_CameraCoop/Scenes/NetplayTest.unity");
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, "Assets/_CameraCoop/Scenes/Netplay3D.unity");
scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/_CameraCoop/Scenes/Netplay3D.unity");

// 2) 머티리얼 생성 (URP Lit)
var litShader = UnityEngine.Shader.Find("Universal Render Pipeline/Lit");
System.Func<string, UnityEngine.Color, float, UnityEngine.Material> makeMat = (name, color, smooth) => {
    string path = "Assets/_CameraCoop/Materials/" + name + ".mat";
    var existing = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(path);
    if (existing != null) { return existing; }
    var m = new UnityEngine.Material(litShader);
    m.SetColor("_BaseColor", color);
    m.SetFloat("_Smoothness", smooth);
    UnityEditor.AssetDatabase.CreateAsset(m, path);
    return m;
};
var canvasMat = makeMat("CanvasWhite", new UnityEngine.Color(0.96f, 0.96f, 0.93f), 0.1f);
var floorMat  = makeMat("RoomFloor",  new UnityEngine.Color(0.35f, 0.32f, 0.30f), 0.2f);
var wallMat   = makeMat("RoomWall",   new UnityEngine.Color(0.55f, 0.58f, 0.62f), 0.05f);
var woodMat   = makeMat("EaselWood",  new UnityEngine.Color(0.45f, 0.32f, 0.20f), 0.15f);

// 3) 룸 프리미티브
System.Func<UnityEngine.PrimitiveType, string, UnityEngine.Vector3, UnityEngine.Vector3, UnityEngine.Material, UnityEngine.GameObject> makePrim =
    (type, name, pos, scale, mat) => {
        var go = UnityEngine.GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.position = pos;
        go.transform.localScale = scale;
        go.GetComponent<UnityEngine.Renderer>().sharedMaterial = mat;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<UnityEngine.Collider>()); // 물리 불필요 (YAGNI)
        return go;
    };
var room = new UnityEngine.GameObject("Room");
makePrim(UnityEngine.PrimitiveType.Plane, "Floor", new UnityEngine.Vector3(0f, 0f, 0f), new UnityEngine.Vector3(2f, 1f, 2f), floorMat).transform.SetParent(room.transform);
makePrim(UnityEngine.PrimitiveType.Cube, "BackWall", new UnityEngine.Vector3(0f, 2.5f, 1.2f), new UnityEngine.Vector3(12f, 5f, 0.1f), wallMat).transform.SetParent(room.transform);
makePrim(UnityEngine.PrimitiveType.Cube, "LeftWall", new UnityEngine.Vector3(-6f, 2.5f, -4f), new UnityEngine.Vector3(0.1f, 5f, 10.5f), wallMat).transform.SetParent(room.transform);
makePrim(UnityEngine.PrimitiveType.Cube, "RightWall", new UnityEngine.Vector3(6f, 2.5f, -4f), new UnityEngine.Vector3(0.1f, 5f, 10.5f), wallMat).transform.SetParent(room.transform);

// 4) 이젤 + 캔버스 Quad (16:9 — 웹캠·화면 norm 비율과 동일, docs/10 §3)
var easel = new UnityEngine.GameObject("Easel");
easel.transform.SetParent(room.transform);
makePrim(UnityEngine.PrimitiveType.Cube, "Backboard", new UnityEngine.Vector3(0f, 1.5f, 0.04f), new UnityEngine.Vector3(2.5f, 1.45f, 0.05f), woodMat).transform.SetParent(easel.transform);
makePrim(UnityEngine.PrimitiveType.Cube, "LegLeft",  new UnityEngine.Vector3(-1.1f, 0.7f, 0.04f), new UnityEngine.Vector3(0.08f, 1.4f, 0.08f), woodMat).transform.SetParent(easel.transform);
makePrim(UnityEngine.PrimitiveType.Cube, "LegRight", new UnityEngine.Vector3(1.1f, 0.7f, 0.04f), new UnityEngine.Vector3(0.08f, 1.4f, 0.08f), woodMat).transform.SetParent(easel.transform);
var canvasGo = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Quad);
canvasGo.name = "DrawCanvas";
canvasGo.transform.SetParent(easel.transform);
canvasGo.transform.position = new UnityEngine.Vector3(0f, 1.5f, 0f);   // Quad 정면은 -Z — 카메라 쪽
canvasGo.transform.localScale = new UnityEngine.Vector3(2.4f, 1.35f, 1f);
canvasGo.GetComponent<UnityEngine.Renderer>().sharedMaterial = canvasMat;
UnityEngine.Object.DestroyImmediate(canvasGo.GetComponent<UnityEngine.Collider>());
var surface = canvasGo.AddComponent<CameraCoop.CanvasSurface>();

// 5) 카메라 재배치 (캔버스 정면 고정, docs/10 §3)
var cam = UnityEngine.Camera.main;
cam.transform.position = new UnityEngine.Vector3(0f, 1.5f, -1.6f);
cam.transform.rotation = UnityEngine.Quaternion.identity;
cam.clearFlags = UnityEngine.CameraClearFlags.Skybox;

// 6) Directional Light 확보 (없으면 생성)
if (UnityEngine.Object.FindFirstObjectByType<UnityEngine.Light>() == null)
{
    var lightGo = new UnityEngine.GameObject("Directional Light");
    var light = lightGo.AddComponent<UnityEngine.Light>();
    light.type = UnityEngine.LightType.Directional;
    light.intensity = 1.0f;
    lightGo.transform.rotation = UnityEngine.Quaternion.Euler(50f, -30f, 0f);
}

// 7) 배선: canvasSurface / projectionCamera (SerializedObject — prefab/씬 저장 반영)
System.Action<UnityEngine.Component, string, UnityEngine.Object> wire = (comp, field, value) => {
    var so = new UnityEditor.SerializedObject(comp);
    so.FindProperty(field).objectReferenceValue = value;
    so.ApplyModifiedProperties();
};
var drawing = UnityEngine.Object.FindFirstObjectByType<CameraCoop.DrawingController>();
var presenter = UnityEngine.Object.FindFirstObjectByType<CameraCoop.Netplay.RemotePresenter>();
var cursorCtrl = UnityEngine.Object.FindFirstObjectByType<CameraCoop.HandCursorController>();
wire(drawing, "canvasSurface", surface);
wire(presenter, "canvasSurface", surface);
wire(cursorCtrl, "canvasSurface", surface);
wire(cursorCtrl, "projectionCamera", cam);

// 8) 빌드 씬 등록 + 저장
var scenes = new System.Collections.Generic.List<UnityEditor.EditorBuildSettingsScene>(UnityEditor.EditorBuildSettings.scenes);
bool already = false;
foreach (var s in scenes) { if (s.path == "Assets/_CameraCoop/Scenes/Netplay3D.unity") { already = true; } }
if (!already) { scenes.Add(new UnityEditor.EditorBuildSettingsScene("Assets/_CameraCoop/Scenes/Netplay3D.unity", true)); }
UnityEditor.EditorBuildSettings.scenes = scenes.ToArray();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
UnityEditor.AssetDatabase.SaveAssets();
return "Netplay3D built";
```

(람다의 타입 추론이 eval 래퍼에서 문제되면 `System.Func` 선언을 풀어 일반 문장으로 펼쳐라 — 동작이 같으면 형태는 자유.)

- [ ] **Step 3: 배선 검증**

```bash
unity cmd get_scene_hierarchy
unity cmd get_serialized_fields --target <DrawingController 오브젝트> --component DrawingController
unity cmd get_serialized_fields --target <HandCursorController 오브젝트> --component HandCursorController
```
Expected: `Room/Easel/DrawCanvas` 존재, `canvasSurface`·`projectionCamera` non-null. (`--target` 경로 거부 시 find_gameobjects로 instanceId)

- [ ] **Step 4: 육안 확인**

`unity cmd capture_game_view --source screen --save_path Temp/netplay3d_scene.png` → 이미지를 Read로 열어 확인: 캔버스가 화면 중앙 대부분 차지, 룸·이젤·조명 정상. 확인 후 `Assets/Temp/` 밑 파일 + `.meta` 삭제.

- [ ] **Step 5: 무회귀 확인 + Commit**

`git status` — `NetplayTest.unity` 무변경 확인 (변경 시 STOP — OpenScene 순서 실수).

```bash
git add Assets/_CameraCoop/Scenes/Netplay3D.unity* Assets/_CameraCoop/Materials ProjectSettings/EditorBuildSettings.asset
git commit -m "feat: Netplay3D 씬 — 3D 룸 + 이젤 월드 캔버스, 빌드 씬 등록 (docs/10 §3)"
```

---

### Task 5: Loopback 통합 검증 (W-2/W-3) + 2D 무회귀 (W-4)

**Files:**
- 없음 (검증 전용. eval 스크립트는 scratchpad에만 — 커밋 금지)

**Interfaces:**
- Consumes: Task 4의 Netplay3D.unity 전부
- Produces: W-2/W-3/W-4 검증 보고 + 스크린샷 + 콘솔 에러 카운트

절차는 Phase 3a plan Task 5 (`docs/superpowers/plans/2026-08-26-phase3a-netplay.md:1588-1706`)와 동일 패턴 — 씬만 Netplay3D, 판정에 "캔버스 평면 위" 확인 추가.

- [ ] **Step 1: Play 진입 + Loopback 세션 + 가짜 피어**

```bash
unity cmd open_scene --path Assets/_CameraCoop/Scenes/Netplay3D.unity
unity cmd editor_play
```

eval (전체 이름만):

```csharp
var ui = UnityEngine.Object.FindFirstObjectByType<CameraCoop.Netplay.NetplayUI>();
ui.OnClickHostLoopback();
var session = UnityEngine.Object.FindFirstObjectByType<CameraCoop.Netplay.NetSession>();
var tf = session.GetType().GetField("transport", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var lb = (CameraCoop.Netplay.LoopbackTransport)tf.GetValue(session);
var p1 = lb.AddFakePeer("fake-1", "P1");
var p2 = lb.AddFakePeer("fake-2", "P2");
var p3 = lb.AddFakePeer("fake-3", "P3");
p1.Send(CameraCoop.Netplay.NetProtocol.Encode(CameraCoop.Netplay.NetProtocol.TypeHello, "fake-1", new CameraCoop.Netplay.HelloPayload { name = "P1" }));
p2.Send(CameraCoop.Netplay.NetProtocol.Encode(CameraCoop.Netplay.NetProtocol.TypeHello, "fake-2", new CameraCoop.Netplay.HelloPayload { name = "P2" }));
p3.Send(CameraCoop.Netplay.NetProtocol.Encode(CameraCoop.Netplay.NetProtocol.TypeHello, "fake-3", new CameraCoop.Netplay.HelloPayload { name = "P3" }));
p1.Send(CameraCoop.Netplay.NetProtocol.Encode(CameraCoop.Netplay.NetProtocol.TypeStrokeStart, "fake-1", new CameraCoop.Netplay.StrokeStartPayload { strokeId = "fake-1:0", hand = "Right", x = 0.2f, y = 0.2f }));
p1.Send(CameraCoop.Netplay.NetProtocol.Encode(CameraCoop.Netplay.NetProtocol.TypeStrokePoints, "fake-1", new CameraCoop.Netplay.StrokePointsPayload { strokeId = "fake-1:0", xy = new float[] { 0.3f, 0.3f, 0.4f, 0.35f, 0.5f, 0.4f } }));
p1.Send(CameraCoop.Netplay.NetProtocol.Encode(CameraCoop.Netplay.NetProtocol.TypeStrokeEnd, "fake-1", new CameraCoop.Netplay.StrokeEndPayload { strokeId = "fake-1:0" }));
p2.Send(CameraCoop.Netplay.NetProtocol.Encode(CameraCoop.Netplay.NetProtocol.TypeStrokeStart, "fake-2", new CameraCoop.Netplay.StrokeStartPayload { strokeId = "fake-2:0", hand = "Left", x = 0.6f, y = 0.6f }));
p2.Send(CameraCoop.Netplay.NetProtocol.Encode(CameraCoop.Netplay.NetProtocol.TypeStrokePoints, "fake-2", new CameraCoop.Netplay.StrokePointsPayload { strokeId = "fake-2:0", xy = new float[] { 0.7f, 0.65f, 0.8f, 0.7f } }));
p2.Send(CameraCoop.Netplay.NetProtocol.Encode(CameraCoop.Netplay.NetProtocol.TypeStrokeEnd, "fake-2", new CameraCoop.Netplay.StrokeEndPayload { strokeId = "fake-2:0" }));
p3.Send(CameraCoop.Netplay.NetProtocol.Encode(CameraCoop.Netplay.NetProtocol.TypeCursor, "fake-3", new CameraCoop.Netplay.CursorPayload { hand = "Right", x = 0.5f, y = 0.4f, pinched = false, seq = 1 }));
return "4p session + strokes sent";
```

(주의: `FakePeer.Send`가 현재 API — 3a plan 문서의 `SendToHost`는 구버전 명칭이다. `LoopbackTransport.cs:25` 확인.)

- [ ] **Step 2: W-2 판정 — 원격 표시가 캔버스 평면 위에 있는가**

2초 후 eval:

```csharp
var lines = UnityEngine.Object.FindObjectsByType<UnityEngine.LineRenderer>(UnityEngine.FindObjectsSortMode.None);
int remote = 0;
int onCanvas = 0;
foreach (var l in lines)
{
    if (!l.gameObject.name.StartsWith("RemoteStroke_")) { continue; }
    remote++;
    UnityEngine.Vector3 p = l.GetPosition(0);
    if (UnityEngine.Mathf.Abs(p.z) < 0.05f) { onCanvas++; } // 캔버스 평면 z≈0 (기존 카메라 평면이면 z≈3.4)
}
int cursors = 0;
foreach (var img in UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Image>(UnityEngine.FindObjectsSortMode.None))
{
    if (img.gameObject.name.StartsWith("RemoteCursor_")) { cursors++; }
}
var s = UnityEngine.Object.FindFirstObjectByType<CameraCoop.Netplay.NetSession>();
return "remoteStrokes=" + remote + " onCanvas=" + onCanvas + " remoteCursors=" + cursors + " players=" + s.Players.Count;
```
Expected: `remoteStrokes=2 onCanvas=2 remoteCursors>=1 players=4`. 미달 시 STOP — `unity cmd console --tail 20 --level error` 확인 후 보고.

스크린샷: `unity cmd capture_game_view --source screen --save_path Temp/netplay3d_loopback.png` → Read로 열어 4색 표시가 **캔버스 Quad 위에** 있는지 육안 확인.

- [ ] **Step 3: W-3 판정 — 늦은 참가 스냅샷 + 피어 이탈**

늦은 참가 (3a plan과 동일 — 같은 eval 안에서 `lb.Tick()` 직접 호출로 즉시 처리):

```csharp
var s = UnityEngine.Object.FindFirstObjectByType<CameraCoop.Netplay.NetSession>();
var tf = s.GetType().GetField("transport", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var lb = (CameraCoop.Netplay.LoopbackTransport)tf.GetValue(s);
var late = lb.AddFakePeer("fake-late", "Late");
late.Send(CameraCoop.Netplay.NetProtocol.Encode(CameraCoop.Netplay.NetProtocol.TypeHello, "fake-late", new CameraCoop.Netplay.HelloPayload { name = "Late" }));
lb.Tick();
if (late.Received.Count == 0) { return "FAIL: no welcome"; }
var env = CameraCoop.Netplay.NetProtocol.Decode(late.Received[0]);
var welcome = CameraCoop.Netplay.NetProtocol.DecodePayload<CameraCoop.Netplay.WelcomePayload>(env);
return "welcome type=" + env.type + " players=" + welcome.players.Length + " snapshot=" + welcome.snapshot.Length;
```
Expected: `type=Welcome players=5 snapshot=2`

피어 이탈:

```csharp
var s = UnityEngine.Object.FindFirstObjectByType<CameraCoop.Netplay.NetSession>();
var tf = s.GetType().GetField("transport", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var lb = (CameraCoop.Netplay.LoopbackTransport)tf.GetValue(s);
int before = s.Players.Count;
lb.RemoveFakePeer("fake-1");
int after = s.Players.Count;
var lines = UnityEngine.Object.FindObjectsByType<UnityEngine.LineRenderer>(UnityEngine.FindObjectsSortMode.None);
int remote = 0;
foreach (var l in lines) { if (l.gameObject.name.StartsWith("RemoteStroke_")) { remote++; } }
return "players " + before + "->" + after + " remoteStrokesPreserved=" + remote;
```
Expected: players 감소 1, `remoteStrokesPreserved=2` (스트로크 보존)

- [ ] **Step 4: 콘솔 에러 + 종료**

```bash
unity cmd get_console_logs --severity error --limit 10
unity cmd editor_stop
```
Expected: 에러 0.

- [ ] **Step 5: W-4 — NetplayTest 2D 무회귀 smoke**

```bash
unity cmd open_scene --path Assets/_CameraCoop/Scenes/NetplayTest.unity
unity cmd editor_play
```

eval — Loopback host + 가짜 스트로크 1개 후, RemoteStroke의 z가 **기존 카메라 평면(≈5)**인지 확인:

```csharp
var ui = UnityEngine.Object.FindFirstObjectByType<CameraCoop.Netplay.NetplayUI>();
ui.OnClickHostLoopback();
var session = UnityEngine.Object.FindFirstObjectByType<CameraCoop.Netplay.NetSession>();
var tf = session.GetType().GetField("transport", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var lb = (CameraCoop.Netplay.LoopbackTransport)tf.GetValue(session);
var p1 = lb.AddFakePeer("fake-1", "P1");
p1.Send(CameraCoop.Netplay.NetProtocol.Encode(CameraCoop.Netplay.NetProtocol.TypeHello, "fake-1", new CameraCoop.Netplay.HelloPayload { name = "P1" }));
p1.Send(CameraCoop.Netplay.NetProtocol.Encode(CameraCoop.Netplay.NetProtocol.TypeStrokeStart, "fake-1", new CameraCoop.Netplay.StrokeStartPayload { strokeId = "fake-1:0", hand = "Right", x = 0.2f, y = 0.2f }));
p1.Send(CameraCoop.Netplay.NetProtocol.Encode(CameraCoop.Netplay.NetProtocol.TypeStrokePoints, "fake-1", new CameraCoop.Netplay.StrokePointsPayload { strokeId = "fake-1:0", xy = new float[] { 0.3f, 0.3f, 0.4f, 0.35f } }));
lb.Tick();
var lines = UnityEngine.Object.FindObjectsByType<UnityEngine.LineRenderer>(UnityEngine.FindObjectsSortMode.None);
foreach (var l in lines)
{
    if (l.gameObject.name.StartsWith("RemoteStroke_"))
    {
        return "2d stroke z=" + l.GetPosition(0).z + " camZ=" + UnityEngine.Camera.main.transform.position.z;
    }
}
return "FAIL: no remote stroke";
```
Expected: `z ≈ camZ + 5` (planeDistance=5인 기존 카메라 평면 경로 유지 — 캔버스 평면 z≈0이 나오면 회귀, STOP). 이후 `editor_stop`, `Assets/Temp/` 캡처 파일 + `.meta` 삭제.

- [ ] **Step 6: 보고**

결과 수치 전부 (players/strokes/onCanvas/z값/에러 수) + 스크린샷 경로 보고. **커밋 없음.**

---

### Task 6: 문서 갱신 + 최종 확인

**Files:**
- Modify: `docs/10_phase3d_world_canvas.md` (상태·DoD 결과 기입)
- Modify: `docs/09_handoff_windows.md` (§3 남은 작업 표에 Netplay3D 씬 추가 언급 1줄)

**Interfaces:**
- Consumes: Task 5의 검증 결과 수치
- Produces: 완결된 문서

- [ ] **Step 1: docs/10 헤더의 상태를 "구현 완료"로 갱신하고 §5 표에 실제 결과 기입** (테스트 수, W-2/W-3 수치, 스크린샷 여부)

- [ ] **Step 2: docs/09 §3 표 아래에 1줄 추가**: Netplay3D 씬이 생겼고 Steam 2인 3D 검증은 빌드 갱신 시 수행한다는 메모

- [ ] **Step 3: 최종 전체 테스트**

`unity cmd run_tests --mode EditMode --timeout 120` → **79 pass** 재확인. `git status`로 의도치 않은 변경 없음 확인.

- [ ] **Step 4: Commit**

```bash
git add docs/10_phase3d_world_canvas.md docs/09_handoff_windows.md
git commit -m "docs: Phase 3d 구현 완료 — DoD 결과 기입"
```
