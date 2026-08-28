using System;
using UnityEngine;
using UnityEngine.UI;

namespace CameraCoop
{
    public sealed class HandToolPalette : MonoBehaviour
    {
        [Serializable]
        public sealed class Binding
        {
            [SerializeField] private HandButtonInteractable button;
            [SerializeField] private ToolButton toolButton;
            [SerializeField] private Graphic selectedMarker;

            private Action<HandClickContext> clickHandler;

            public bool IsComplete => button != null && toolButton != null && selectedMarker != null;
            public Graphic SelectedMarker => selectedMarker;

            public void Subscribe(ToolState state)
            {
                clickHandler = _ => state.Apply(toolButton);
                button.OnHandClick += clickHandler;
            }

            public void Unsubscribe()
            {
                if (clickHandler != null)
                {
                    button.OnHandClick -= clickHandler;
                    clickHandler = null;
                }
            }

            public void Refresh(ToolState state)
            {
                selectedMarker.enabled = toolButton.Kind == ToolKind.Color ? toolButton.Index == state.CurrentColorIndex
                    : toolButton.Kind == ToolKind.Width ? toolButton.Index == state.CurrentWidthIndex
                    : toolButton.Kind == ToolKind.Brush ? toolButton.Index == state.CurrentBrushIndex
                    : state.CurrentMode == ToolState.Mode.Erase;
            }
        }

        [SerializeField] private ToolState toolState;
        [SerializeField] private Binding[] bindings;

        private bool initialized;
        private bool subscribed;

        private void Awake()
        {
            if (initialized)
            {
                return;
            }
            if (toolState == null || bindings == null || bindings.Length == 0)
            {
                DisableForMissingReferences();
                return;
            }
            for (int index = 0; index < bindings.Length; index++)
            {
                if (bindings[index] == null || !bindings[index].IsComplete)
                {
                    DisableForMissingReferences();
                    return;
                }
                bindings[index].SelectedMarker.raycastTarget = false;
            }
            initialized = true;
            Refresh();
        }

        private void OnEnable()
        {
            if (!initialized || subscribed)
            {
                return;
            }
            for (int index = 0; index < bindings.Length; index++)
            {
                bindings[index].Subscribe(toolState);
            }
            toolState.OnChanged += Refresh;
            subscribed = true;
            Refresh();
        }

        private void OnDisable()
        {
            if (!subscribed)
            {
                return;
            }
            toolState.OnChanged -= Refresh;
            for (int index = 0; index < bindings.Length; index++)
            {
                bindings[index].Unsubscribe();
            }
            subscribed = false;
        }

        private void Refresh()
        {
            if (!initialized)
            {
                return;
            }
            for (int index = 0; index < bindings.Length; index++)
            {
                bindings[index].Refresh(toolState);
            }
        }

        private void DisableForMissingReferences()
        {
            Debug.LogError("HandToolPalette requires ToolState and complete HandButtonInteractable, ToolButton, and selected marker bindings.", this);
            enabled = false;
        }
    }
}
