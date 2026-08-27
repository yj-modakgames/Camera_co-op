using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CameraCoop.Tests
{
    // NetplayTest 씬 UI 클릭 배선 회귀 가드 (docs/08 §5).
    // GraphicRaycaster가 빠지면 EventSystem이 Graphic을 맞히지 못해 Button.onClick이 조용히 죽는다.
    public class NetplaySceneTests
    {
        private const string ScenePath = "Assets/_CameraCoop/Scenes/NetplayTest.unity";

        [Test]
        public void NetplayScene_ButtonsAreClickable()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                GameObject[] roots = scene.GetRootGameObjects();
                Canvas canvas = roots.SelectMany(r => r.GetComponentsInChildren<Canvas>(true)).FirstOrDefault();
                Assert.IsNotNull(canvas, "Canvas 없음");
                Assert.IsNotNull(canvas.GetComponent<GraphicRaycaster>(), "Canvas에 GraphicRaycaster 없음 — UI raycast 불가");
                Assert.IsNotNull(roots.SelectMany(r => r.GetComponentsInChildren<EventSystem>(true)).FirstOrDefault(), "EventSystem 없음");

                Button[] buttons = roots.SelectMany(r => r.GetComponentsInChildren<Button>(true)).ToArray();
                Assert.IsNotEmpty(buttons, "Button 없음");
                foreach (Button button in buttons)
                {
                    Assert.IsTrue(button.interactable, button.name + " interactable=false");
                    Assert.AreEqual(1, button.onClick.GetPersistentEventCount(), button.name + " onClick 미배선");
                    Assert.IsTrue(button.targetGraphic != null && button.targetGraphic.raycastTarget, button.name + " targetGraphic raycastTarget 꺼짐");
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
