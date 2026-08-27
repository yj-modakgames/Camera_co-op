using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CameraCoop.Tests
{
    // Netplay 씬 UI 클릭 배선 회귀 가드 (docs/08 §5, docs/09 §4).
    // GraphicRaycaster가 빠지면 EventSystem이 Graphic을 맞히지 못해 Button.onClick이 조용히 죽는다.
    // Netplay3D는 NetplayTest 복제본이라 같은 함정을 물려받는다 → 두 씬 모두 검사.
    public class NetplaySceneTests
    {
        [TestCase("Assets/_CameraCoop/Scenes/NetplayTest.unity")]
        [TestCase("Assets/_CameraCoop/Scenes/Netplay3D.unity")]
        public void NetplayScene_ButtonsAreClickable(string scenePath)
        {
            string tag = "[" + System.IO.Path.GetFileNameWithoutExtension(scenePath) + "] ";
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
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
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
