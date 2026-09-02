using CameraCoop.Party;
using NUnit.Framework;
using UnityEngine;

namespace CameraCoop.Tests
{
    public class HandInteractableHighlightTests
    {
        private const string BaseColor = "_BaseColor";

        private GameObject root;
        private Material material;

        [SetUp]
        public void SetUp()
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            material.SetColor(BaseColor, new Color(0.2f, 0.2f, 0.2f, 1f));
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
            if (material != null) Object.DestroyImmediate(material);
        }

        [Test]
        public void HoverBrightensTheTargetAndExitRestoresIt()
        {
            WorldActionInteractable interactable = NewButton();
            Color idle = CurrentColor();

            interactable.HoverEnter(Sample("Right"), Vector3.zero);
            Color hovered = CurrentColor();
            Assert.That(hovered.r, Is.GreaterThan(idle.r), "hover는 대상을 밝게 만들어야 조준이 보인다");

            interactable.HoverExit(Sample("Right"), Vector3.zero);
            Assert.That(CurrentColor().r, Is.EqualTo(idle.r).Within(0.0001f));
        }

        [Test]
        public void PressIsBrighterThanHoverAndReleaseFallsBackToHover()
        {
            WorldActionInteractable interactable = NewButton();
            interactable.HoverEnter(Sample("Right"), Vector3.zero);
            Color hovered = CurrentColor();

            interactable.Press(Sample("Right"), Vector3.zero, default);
            Color pressed = CurrentColor();
            Assert.That(pressed.r, Is.GreaterThan(hovered.r), "press는 hover보다 더 밝아야 눌린 것이 보인다");

            interactable.Release(Sample("Right"), Vector3.zero);
            Assert.That(CurrentColor().r, Is.EqualTo(hovered.r).Within(0.0001f));
        }

        [Test]
        public void CancelClearsHighlightSoAStuckHandDoesNotLeaveItLit()
        {
            WorldActionInteractable interactable = NewButton();
            interactable.HoverEnter(Sample("Left"), Vector3.zero);
            interactable.Press(Sample("Left"), Vector3.zero, default);
            Color idle = new Color(0.2f, 0.2f, 0.2f, 1f);

            interactable.Cancel(Sample("Left"), Vector3.zero);
            Assert.That(CurrentColor().r, Is.EqualTo(idle.r).Within(0.0001f));
        }

        [Test]
        public void OneHandLeavingKeepsTheHighlightWhileTheOtherStillHovers()
        {
            WorldActionInteractable interactable = NewButton();
            interactable.HoverEnter(Sample("Left"), Vector3.zero);
            interactable.HoverEnter(Sample("Right"), Vector3.zero);
            Color hovered = CurrentColor();

            interactable.HoverExit(Sample("Left"), Vector3.zero);
            Assert.That(CurrentColor().r, Is.EqualTo(hovered.r).Within(0.0001f));
        }

        private WorldActionInteractable NewButton()
        {
            root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.GetComponent<MeshRenderer>().sharedMaterial = material;
            return root.AddComponent<WorldActionInteractable>();
        }

        private Color CurrentColor()
        {
            var block = new MaterialPropertyBlock();
            MeshRenderer renderer = root.GetComponent<MeshRenderer>();
            renderer.GetPropertyBlock(block);
            Color applied = block.GetColor(BaseColor);
            // 블록이 비어 있으면 material 원본이 그대로 보인다.
            return applied.a <= 0f ? material.GetColor(BaseColor) : applied;
        }

        private static HandInputSample Sample(string handedness)
        {
            return new HandInputSample(handedness, Vector2.zero, 1, 1, 0f, true, false);
        }
    }
}
