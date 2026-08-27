using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using CameraCoop.Netplay;

namespace CameraCoop.Tests
{
    // Netplay 씬 UI 클릭 배선 회귀 가드 (docs/08 §5, docs/09 §4).
    // GraphicRaycaster가 빠지면 EventSystem이 Graphic을 맞히지 못해 Button.onClick이 조용히 죽는다.
    // Netplay3D는 NetplayTest 복제본이라 같은 함정을 물려받는다 → 두 씬 모두 검사.
    public class NetplaySceneTests
    {
        // 이미 Editor에 열려 있는 씬은 다시 열지 않는다. OpenScene(Additive)로 재차 열면 그 씬을
        // reload하고, 뒤이은 CloseScene(scene, true)이 사용자의 active 씬을 닫아버린다 (Task 7 진단).
        // 열지 않았으면 닫지도 않는다. 두 테스트가 이 가드를 공유한다.
        private static Scene OpenSceneIfNeeded(string scenePath, out bool alreadyOpen)
        {
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            alreadyOpen = scene.IsValid() && scene.isLoaded;
            if (!alreadyOpen)
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            }
            return scene;
        }

        private static void CloseSceneIfOpened(Scene scene, bool alreadyOpen)
        {
            if (!alreadyOpen)
            {
                EditorSceneManager.CloseScene(scene, true); // 열지 않았으면 닫지도 않는다
            }
        }

        [TestCase("Assets/_CameraCoop/Scenes/NetplayTest.unity")]
        [TestCase("Assets/_CameraCoop/Scenes/Netplay3D.unity")]
        public void NetplayScene_ButtonsAreClickable(string scenePath)
        {
            string tag = "[" + System.IO.Path.GetFileNameWithoutExtension(scenePath) + "] ";
            bool alreadyOpen;
            Scene scene = OpenSceneIfNeeded(scenePath, out alreadyOpen);
            try
            {
                GameObject[] roots = scene.GetRootGameObjects();
                Canvas canvas = roots.SelectMany(r => r.GetComponentsInChildren<Canvas>(true)).FirstOrDefault();
                Assert.IsNotNull(canvas, tag + "Canvas 없음");
                Assert.IsNotNull(canvas.GetComponent<GraphicRaycaster>(), tag + "Canvas에 GraphicRaycaster 없음 — UI raycast 불가");
                Assert.IsNotNull(roots.SelectMany(r => r.GetComponentsInChildren<EventSystem>(true)).FirstOrDefault(), tag + "EventSystem 없음");

                Button[] buttons = roots.SelectMany(r => r.GetComponentsInChildren<Button>(true)).ToArray();
                Assert.IsNotEmpty(buttons, tag + "Button 없음");
                foreach (Button button in buttons)
                {
                    Assert.IsTrue(button.interactable, tag + button.name + " interactable=false");
                    Assert.AreEqual(1, button.onClick.GetPersistentEventCount(), tag + button.name + " onClick 미배선");
                    Assert.IsTrue(button.targetGraphic != null && button.targetGraphic.raycastTarget, tag + button.name + " targetGraphic raycastTarget 꺼짐");
                }
            }
            finally
            {
                CloseSceneIfOpened(scene, alreadyOpen);
            }
        }

        // Phase 3d 배선 회귀 가드 (docs/10_phase3d_world_canvas.md §2·§3).
        // Netplay3D만 검사한다 — NetplayTest는 canvasSurface가 의도적으로 null(2D 레거시 경로)이라
        // 이 테스트에 포함하면 항상 실패한다.
        [Test]
        public void Netplay3DScene_WorldCanvasIsWired()
        {
            const string scenePath = "Assets/_CameraCoop/Scenes/Netplay3D.unity";
            bool alreadyOpen;
            Scene scene = OpenSceneIfNeeded(scenePath, out alreadyOpen);
            try
            {
                GameObject[] roots = scene.GetRootGameObjects();

                CanvasSurface surface = roots.SelectMany(r => r.GetComponentsInChildren<CanvasSurface>(true)).FirstOrDefault();
                Assert.IsNotNull(surface, "Netplay3D에 CanvasSurface 컴포넌트가 없다");

                DrawingController drawing = roots.SelectMany(r => r.GetComponentsInChildren<DrawingController>(true)).FirstOrDefault();
                Assert.IsNotNull(drawing, "Netplay3D에 DrawingController가 없다");
                AssertFieldAssigned(drawing, "canvasSurface", "DrawingController.canvasSurface 미배선 — 로컬 드로잉이 화면 좌표로 폴백한다");

                RemotePresenter presenter = roots.SelectMany(r => r.GetComponentsInChildren<RemotePresenter>(true)).FirstOrDefault();
                Assert.IsNotNull(presenter, "Netplay3D에 RemotePresenter가 없다");
                AssertFieldAssigned(presenter, "canvasSurface", "RemotePresenter.canvasSurface 미배선 — 원격 표시가 화면 좌표로 폴백한다");
                AssertFieldAssigned(presenter, "drawCamera", "RemotePresenter.drawCamera 미배선 — 원격 커서가 화면 좌표로 폴백한다");

                HandCursorController cursor = roots.SelectMany(r => r.GetComponentsInChildren<HandCursorController>(true)).FirstOrDefault();
                Assert.IsNotNull(cursor, "Netplay3D에 HandCursorController가 없다");
                AssertFieldAssigned(cursor, "canvasSurface", "HandCursorController.canvasSurface 미배선 — 커서가 화면 좌표로 폴백한다");
                AssertFieldAssigned(cursor, "projectionCamera", "HandCursorController.projectionCamera 미배선 — 커서가 화면 좌표로 폴백해 잉크와 어긋난다");
            }
            finally
            {
                CloseSceneIfOpened(scene, alreadyOpen);
            }
        }

        // private [SerializeField] 필드는 SerializedObject로만 검사 가능하다 (EditMode 전용, Task 4와 동일 API).
        private static void AssertFieldAssigned(Object component, string fieldName, string failureMessage)
        {
            SerializedObject so = new SerializedObject(component);
            SerializedProperty prop = so.FindProperty(fieldName);
            Assert.IsNotNull(prop, "필드를 찾을 수 없다: " + fieldName);
            Assert.IsNotNull(prop.objectReferenceValue, failureMessage);
        }
    }
}
