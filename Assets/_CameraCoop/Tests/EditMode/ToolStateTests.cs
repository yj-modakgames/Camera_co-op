using System.Collections.Generic;
using System.Reflection;
using CameraCoop;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CameraCoop.Tests
{
    // docs/11 §2 — 팔레트 클릭이 도구 상태에 반영되는 규칙.
    // ToolButton의 kind/index는 private [SerializeField]라 SerializedObject로 세팅한다 (NetplaySceneTests와 같은 API).
    public class ToolStateTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < spawned.Count; i++)
            {
                if (spawned[i] != null)
                {
                    Object.DestroyImmediate(spawned[i]);
                }
            }
            spawned.Clear();
        }

        private ToolState MakeState()
        {
            var go = new GameObject("toolstate");
            spawned.Add(go);
            return go.AddComponent<ToolState>();
        }

        private ToolState MakeBoundState(HandPointer handPointer, ToolButton[] buttons)
        {
            var go = new GameObject("toolstate");
            go.SetActive(false);
            spawned.Add(go);
            ToolState state = go.AddComponent<ToolState>();
            var so = new SerializedObject(state);
            SerializedProperty handPointerProperty = so.FindProperty("handPointer");
            SerializedProperty buttonsProperty = so.FindProperty("buttons");
            Assert.IsNotNull(handPointerProperty, "ToolState는 HandPointer.OnToolClicked 구독용 handPointer 필드가 필요하다");
            Assert.IsNotNull(buttonsProperty, "ToolState는 선택 표시용 buttons 필드가 필요하다");
            handPointerProperty.objectReferenceValue = handPointer;
            buttonsProperty.arraySize = buttons.Length;
            for (int i = 0; i < buttons.Length; i++)
            {
                buttonsProperty.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return state;
        }

        private void InvokeLifecycle(ToolState state, string methodName)
        {
            MethodInfo method = typeof(ToolState).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "ToolState." + methodName + "가 필요하다");
            method.Invoke(state, null);
        }

        private HandPointer MakeHandPointer()
        {
            var go = new GameObject("handpointer");
            go.SetActive(false);
            spawned.Add(go);
            return go.AddComponent<HandPointer>();
        }

        private ToolButton MakeButton(ToolKind kind, int index)
        {
            var go = new GameObject("button");
            spawned.Add(go);
            ToolButton button = go.AddComponent<ToolButton>();
            var so = new SerializedObject(button);
            so.FindProperty("kind").enumValueIndex = (int)kind;
            so.FindProperty("index").intValue = index;
            so.ApplyModifiedPropertiesWithoutUndo();
            Assert.AreEqual(kind, button.Kind, "ToolButton.kind 세팅 실패");
            Assert.AreEqual(index, button.Index, "ToolButton.index 세팅 실패");
            return button;
        }

        private void RaiseToolClick(HandPointer handPointer, ToolButton button)
        {
            FieldInfo eventField = typeof(HandPointer).GetField("OnToolClicked", BindingFlags.Instance | BindingFlags.NonPublic);
            var callback = eventField.GetValue(handPointer) as System.Action<ToolButton>;
            callback?.Invoke(button);
        }

        private static System.Type RequiredRuntimeType(string fullName)
        {
            System.Type type = typeof(ToolState).Assembly.GetType(fullName);
            Assert.IsNotNull(type, fullName + " 타입이 필요하다");
            return type;
        }

        private static PropertyInfo RequiredProperty(System.Type type, string name)
        {
            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(property, type.Name + "." + name + " 읽기 전용 API가 필요하다");
            Assert.IsTrue(property.CanRead && !property.CanWrite, type.Name + "." + name + "는 읽기 전용이어야 한다");
            return property;
        }

        private static FieldInfo RequiredField(System.Type type, string name)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, type.Name + "." + name + " 직접 Inspector 참조가 필요하다");
            return field;
        }

        private static void InvokePrivate(object instance, string methodName)
        {
            MethodInfo method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, instance.GetType().Name + "." + methodName + "가 필요하다");
            method.Invoke(instance, null);
        }

        private Image MakeFeedbackGraphic(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            spawned.Add(go);
            go.transform.SetParent(parent, false);
            Image graphic = go.GetComponent<Image>();
            graphic.raycastTarget = false;
            graphic.enabled = false;
            return graphic;
        }

        [Test]
        public void Defaults_AreDrawMode_WithMiddleWidth()
        {
            ToolState state = MakeState();
            Assert.AreEqual(ToolState.Mode.Draw, state.CurrentMode);
            Assert.AreEqual(0.02f, state.CurrentWidth, 1e-5f);  // widths[1] * Pen(1.0)
            Assert.AreEqual(1f, state.CurrentColor.a, 1e-5f);    // Pen alpha
            Assert.AreEqual(0.05f, state.EraseRadius, 1e-5f);
        }

        [Test]
        public void Apply_Width_ChangesWidthOnly()
        {
            ToolState state = MakeState();
            Color before = state.CurrentColor;
            state.Apply(MakeButton(ToolKind.Width, 2));
            Assert.AreEqual(0.045f, state.CurrentWidth, 1e-5f);
            Assert.AreEqual(before, state.CurrentColor);
        }

        [Test]
        public void Apply_Brush_ScalesWidthAndAlpha()
        {
            ToolState state = MakeState();
            state.Apply(MakeButton(ToolKind.Brush, 1)); // Marker: widthScale 2.2, alpha 0.55
            Assert.AreEqual(1, state.CurrentBrushIndex);
            Assert.AreEqual(0.02f * 2.2f, state.CurrentWidth, 1e-5f);
            Assert.AreEqual(0.55f, state.CurrentColor.a, 1e-5f);
        }

        [Test]
        public void Apply_Eraser_ThenColor_ReturnsToDrawMode()
        {
            ToolState state = MakeState();
            state.Apply(MakeButton(ToolKind.Eraser, 0));
            Assert.AreEqual(ToolState.Mode.Erase, state.CurrentMode);

            state.Apply(MakeButton(ToolKind.Color, 3));
            Assert.AreEqual(ToolState.Mode.Draw, state.CurrentMode, "색을 고르면 그리기 모드로 돌아와야 한다");
        }

        [Test]
        public void Apply_Color_ChangesRgb_KeepsBrushAlpha()
        {
            ToolState state = MakeState();
            Color first = state.CurrentColor;
            state.Apply(MakeButton(ToolKind.Color, 4));
            Color second = state.CurrentColor;
            Assert.AreNotEqual(first, second);
            Assert.AreEqual(1f, second.a, 1e-5f);
        }

        [Test]
        public void Apply_OutOfRangeIndex_IsIgnored()
        {
            ToolState state = MakeState();
            float width = state.CurrentWidth;
            state.Apply(MakeButton(ToolKind.Width, 99)); // 경고만 남기고 무시 (예외 금지)
            Assert.AreEqual(width, state.CurrentWidth, 1e-5f);
        }

        [Test]
        public void OnChanged_FiresOnlyWhenSomethingActuallyChanges()
        {
            ToolState state = MakeState();
            int fired = 0;
            state.OnChanged += () => fired++;

            state.Apply(MakeButton(ToolKind.Color, 2));
            Assert.AreEqual(1, fired, "색이 바뀌면 1회");

            state.Apply(MakeButton(ToolKind.Color, 2));
            Assert.AreEqual(1, fired, "같은 색 재클릭은 발행 없음");

            state.Apply(MakeButton(ToolKind.Eraser, 0));
            Assert.AreEqual(2, fired, "모드 전환은 발행");

            state.Apply(MakeButton(ToolKind.Eraser, 0));
            Assert.AreEqual(2, fired, "같은 모드 재클릭은 발행 없음");

            state.Apply(MakeButton(ToolKind.Color, 2));
            Assert.AreEqual(3, fired, "색은 같지만 Erase에서 Draw로 돌아오므로 발행");
        }

        [Test]
        public void Apply_NullButton_DoesNotThrow()
        {
            ToolState state = MakeState();
            Assert.DoesNotThrow(() => state.Apply(null));
        }

        [Test]
        public void OnEnable_ToolClickAppliesAndOnDisableUnsubscribes()
        {
            HandPointer handPointer = MakeHandPointer();
            ToolButton firstColor = MakeButton(ToolKind.Color, 1);
            ToolButton secondColor = MakeButton(ToolKind.Color, 4);
            ToolState state = MakeBoundState(handPointer, new[] { firstColor, secondColor });
            InvokeLifecycle(state, "OnEnable");

            RaiseToolClick(handPointer, firstColor);
            Assert.AreEqual(0.85f, state.CurrentColor.r, 1e-5f, "OnToolClicked는 활성 ToolState에 반영되어야 한다");

            InvokeLifecycle(state, "OnDisable");
            RaiseToolClick(handPointer, secondColor);
            Assert.AreEqual(0.85f, state.CurrentColor.r, 1e-5f, "비활성 ToolState는 OnToolClicked 구독을 해제해야 한다");
        }

        [Test]
        public void Apply_SelectionMovesColorWidthBrushAndEraserWithoutAccumulatingOffsets()
        {
            HandPointer handPointer = MakeHandPointer();
            ToolButton color0 = MakeButton(ToolKind.Color, 0);
            ToolButton color2 = MakeButton(ToolKind.Color, 2);
            ToolButton width1 = MakeButton(ToolKind.Width, 1);
            ToolButton width2 = MakeButton(ToolKind.Width, 2);
            ToolButton brush0 = MakeButton(ToolKind.Brush, 0);
            ToolButton brush1 = MakeButton(ToolKind.Brush, 1);
            ToolButton eraser = MakeButton(ToolKind.Eraser, 0);
            ToolButton[] buttons = { color0, color2, width1, width2, brush0, brush1, eraser };
            Vector3[] originalPositions =
            {
                new Vector3(1f, 2f, 3f), new Vector3(2f, 3f, 4f), new Vector3(3f, 4f, 5f), new Vector3(4f, 5f, 6f),
                new Vector3(5f, 6f, 7f), new Vector3(6f, 7f, 8f), new Vector3(7f, 8f, 9f)
            };
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].transform.localPosition = originalPositions[i];
            }
            ToolState state = MakeBoundState(handPointer, buttons);
            InvokeLifecycle(state, "OnEnable");

            Assert.AreEqual(originalPositions[0].z - 0.02f, color0.transform.localPosition.z, 1e-5f, "기본 색 선택 표시");
            Assert.AreEqual(originalPositions[2].z - 0.02f, width1.transform.localPosition.z, 1e-5f, "기본 두께 선택 표시");
            Assert.AreEqual(originalPositions[4].z - 0.02f, brush0.transform.localPosition.z, 1e-5f, "기본 브러시 선택 표시");
            Assert.AreEqual(originalPositions[6], eraser.transform.localPosition, "기본 지우개는 선택되지 않는다");

            state.Apply(color2);
            state.Apply(width2);
            state.Apply(brush1);
            state.Apply(eraser);
            Assert.AreEqual(originalPositions[6].z - 0.02f, eraser.transform.localPosition.z, 1e-5f, "지우개 선택 표시");
            state.Apply(color2);

            Assert.AreEqual(originalPositions[0], color0.transform.localPosition, "이전 색은 기준 위치로 돌아와야 한다");
            Assert.AreEqual(originalPositions[1].z - 0.02f, color2.transform.localPosition.z, 1e-5f, "같은 색을 다시 적용해도 z가 누적되지 않아야 한다");
            Assert.AreEqual(originalPositions[2], width1.transform.localPosition, "이전 두께는 기준 위치로 돌아와야 한다");
            Assert.AreEqual(originalPositions[3].z - 0.02f, width2.transform.localPosition.z, 1e-5f, "선택 두께 표시");
            Assert.AreEqual(originalPositions[4], brush0.transform.localPosition, "이전 브러시는 기준 위치로 돌아와야 한다");
            Assert.AreEqual(originalPositions[5].z - 0.02f, brush1.transform.localPosition.z, 1e-5f, "선택 브러시 표시");
            Assert.AreEqual(originalPositions[6], eraser.transform.localPosition, "색 선택으로 Draw 모드가 되면 지우개가 돌아와야 한다");
        }

        [Test]
        public void PaletteReadOnlyApi_ReportsSelectionsAndBrushMaterialLookup()
        {
            ToolState state = MakeState();
            System.Type type = typeof(ToolState);
            PropertyInfo colorIndex = RequiredProperty(type, "CurrentColorIndex");
            PropertyInfo widthIndex = RequiredProperty(type, "CurrentWidthIndex");
            PropertyInfo brushCount = RequiredProperty(type, "BrushCount");
            MethodInfo materialLookup = type.GetMethod("GetBrushMaterial", BindingFlags.Instance | BindingFlags.Public);

            Assert.IsNotNull(materialLookup, "ToolState.GetBrushMaterial(int)가 필요하다");
            Assert.AreEqual(typeof(Material), materialLookup.ReturnType);
            Assert.AreEqual(0, colorIndex.GetValue(state));
            Assert.AreEqual(1, widthIndex.GetValue(state));
            Assert.AreEqual(3, brushCount.GetValue(state));

            state.Apply(MakeButton(ToolKind.Color, 4));
            state.Apply(MakeButton(ToolKind.Width, 2));
            Assert.AreEqual(4, colorIndex.GetValue(state));
            Assert.AreEqual(2, widthIndex.GetValue(state));
            Assert.IsNull(materialLookup.Invoke(state, new object[] { -1 }), "범위 밖 브러시는 null로 거부해야 한다");
            Assert.IsNull(materialLookup.Invoke(state, new object[] { 3 }), "마지막 브러시 다음 인덱스는 null이어야 한다");
        }

        [Test]
        public void Slider_DownAndHoldClampWidthAndExternalToolChangeSynchronizesNativeValue()
        {
            System.Type adapterType = RequiredRuntimeType("CameraCoop.HandSliderInteractable");
            Assert.IsTrue(typeof(HandInteractable).IsAssignableFrom(adapterType), "슬라이더는 HandInteractable이어야 한다");

            var root = new GameObject("slider", typeof(RectTransform), typeof(Slider));
            root.SetActive(false);
            spawned.Add(root);
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(100f, 24f);
            Slider slider = root.GetComponent<Slider>();
            CanvasGroup canvasGroup = root.AddComponent<CanvasGroup>();
            Component component = root.AddComponent(adapterType);
            ToolState state = MakeState();
            ToolButton[] widths =
            {
                MakeButton(ToolKind.Width, 0), MakeButton(ToolKind.Width, 1), MakeButton(ToolKind.Width, 2)
            };
            RequiredField(adapterType, "targetSlider").SetValue(component, slider);
            RequiredField(adapterType, "toolState").SetValue(component, state);
            RequiredField(adapterType, "widthButtons").SetValue(component, widths);
            RequiredField(adapterType, "hoverGraphic").SetValue(component, MakeFeedbackGraphic(root.transform, "hover"));
            RequiredField(adapterType, "pressedGraphic").SetValue(component, MakeFeedbackGraphic(root.transform, "pressed"));
            root.SetActive(true);
            InvokePrivate(component, "Awake");
            InvokePrivate(component, "OnEnable");

            HandInteractable adapter = component as HandInteractable;
            Assert.IsNotNull(adapter);
            Assert.IsFalse(adapter.RequiresInside, "슬라이더는 영역 밖 hold도 양끝으로 제한해야 한다");
            Assert.IsTrue(adapter.Exclusive, "슬라이더는 최초로 누른 손 하나만 소유해야 한다");
            Assert.AreEqual(0f, slider.minValue);
            Assert.AreEqual(2f, slider.maxValue);
            Assert.IsTrue(slider.wholeNumbers);

            HandInputSample down = new HandInputSample("Left", new Vector2(-100f, 0f), 1, 1, 0f, true, true);
            adapter.Press(down, new Vector3(-100f, 0f, 0f), new HandClickContext("Left", 1, 1));
            Assert.AreEqual(0, RequiredProperty(typeof(ToolState), "CurrentWidthIndex").GetValue(state));
            HandInputSample held = new HandInputSample("Left", new Vector2(100f, 0f), 2, 2, 0f, true, true);
            adapter.Hold(held, new Vector3(100f, 0f, 0f));
            Assert.AreEqual(2, RequiredProperty(typeof(ToolState), "CurrentWidthIndex").GetValue(state));
            Assert.IsTrue(adapter.Release(new HandInputSample("Left", new Vector2(100f, 0f), 3, 3, 0f, true, false), Vector3.zero));

            state.Apply(widths[1]);
            Assert.AreEqual(1f, slider.value, "외부 ToolState 변경은 slider를 갱신해야 한다");

            adapter.Press(down, new Vector3(-100f, 0f, 0f), new HandClickContext("Left", 1, 4));
            adapter.Cancel(held, new Vector3(100f, 0f, 0f));
            Assert.AreEqual(0, RequiredProperty(typeof(ToolState), "CurrentWidthIndex").GetValue(state), "취소는 마지막 위치를 두께 변경으로 반영하면 안 된다");

            canvasGroup.interactable = false;
            Assert.IsFalse(adapter.IsAvailable, "비상호작용 CanvasGroup 아래 slider는 손 입력을 받으면 안 된다");
        }

        [Test]
        public void Palette_HandClickAppliesOnceAndExternalToolChangesRefreshSelectedMarkers()
        {
            System.Type paletteType = RequiredRuntimeType("CameraCoop.HandToolPalette");
            var root = new GameObject("palette");
            root.SetActive(false);
            spawned.Add(root);
            Component palette = root.AddComponent(paletteType);
            ToolState state = MakeState();
            ToolButton color0 = MakeButton(ToolKind.Color, 0);
            ToolButton color2 = MakeButton(ToolKind.Color, 2);
            color0.gameObject.SetActive(false);
            color2.gameObject.SetActive(false);
            HandButtonInteractable firstButton = color0.gameObject.AddComponent<HandButtonInteractable>();
            HandButtonInteractable secondButton = color2.gameObject.AddComponent<HandButtonInteractable>();
            Image firstMarker = MakeFeedbackGraphic(color0.transform, "first marker");
            Image secondMarker = MakeFeedbackGraphic(color2.transform, "second marker");

            FieldInfo bindingsField = RequiredField(paletteType, "bindings");
            System.Type bindingType = bindingsField.FieldType.GetElementType();
            Assert.IsNotNull(bindingType, "bindings는 직렬화 가능한 배열이어야 한다");
            System.Array bindings = System.Array.CreateInstance(bindingType, 2);
            object firstBinding = System.Activator.CreateInstance(bindingType);
            object secondBinding = System.Activator.CreateInstance(bindingType);
            RequiredField(bindingType, "button").SetValue(firstBinding, firstButton);
            RequiredField(bindingType, "toolButton").SetValue(firstBinding, color0);
            RequiredField(bindingType, "selectedMarker").SetValue(firstBinding, firstMarker);
            RequiredField(bindingType, "button").SetValue(secondBinding, secondButton);
            RequiredField(bindingType, "toolButton").SetValue(secondBinding, color2);
            RequiredField(bindingType, "selectedMarker").SetValue(secondBinding, secondMarker);
            bindings.SetValue(firstBinding, 0);
            bindings.SetValue(secondBinding, 1);
            RequiredField(paletteType, "toolState").SetValue(palette, state);
            bindingsField.SetValue(palette, bindings);
            Behaviour paletteBehaviour = palette as Behaviour;
            Assert.IsNotNull(paletteBehaviour, "HandToolPalette는 Behaviour여야 한다");
            paletteBehaviour.enabled = true;
            InvokePrivate(palette, "Awake");
            root.SetActive(true);
            InvokePrivate(palette, "OnEnable");

            Assert.IsTrue(firstMarker.enabled);
            Assert.IsFalse(secondMarker.enabled);
            int changes = 0;
            state.OnChanged += () => changes++;
            FieldInfo clickEvent = typeof(HandButtonInteractable).GetField("OnHandClick", BindingFlags.Instance | BindingFlags.NonPublic);
            var click = clickEvent.GetValue(secondButton) as System.Action<HandClickContext>;
            click.Invoke(new HandClickContext("Right", 1, 1));

            Assert.AreEqual(2, RequiredProperty(typeof(ToolState), "CurrentColorIndex").GetValue(state));
            Assert.AreEqual(1, changes, "팔레트 클릭은 ToolState.Apply를 한 번만 호출해야 한다");
            Assert.IsFalse(firstMarker.enabled);
            Assert.IsTrue(secondMarker.enabled);

            state.Apply(color0);
            Assert.IsTrue(firstMarker.enabled, "외부 ToolState 변경도 선택 표시를 갱신해야 한다");
            Assert.IsFalse(secondMarker.enabled);
        }
    }
}
