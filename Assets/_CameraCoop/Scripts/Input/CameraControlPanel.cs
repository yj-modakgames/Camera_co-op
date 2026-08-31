using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using System.Collections.Generic;

namespace CameraCoop
{
    public enum CameraConnectionState
    {
        Off,
        Starting,
        Receiving,
        External,
        Failed
    }

    [DefaultExecutionOrder(-90)]
    public class CameraControlPanel : MonoBehaviour
    {
        [SerializeField] private TrackerLauncher launcher;
        [SerializeField] private UdpHandReceiver receiver;
        [SerializeField] private InputModeManager inputModeManager;
        [SerializeField] private HandInputRouter handInputRouter;
        [SerializeField] private Button cameraButton;
        [SerializeField] private Text buttonLabel;
        [SerializeField] private Text statusLabel;
        [SerializeField] private bool autoStartCamera;
        [SerializeField] private int selectedCameraIndex;
        [SerializeField] private bool previewEnabled = true;
        [SerializeField] private Text selectedCameraLabel;
        [SerializeField, Min(1f)] private float connectionTimeoutSeconds = 15f;

        private static readonly Color NormalColor = new Color(0.10f, 0.20f, 0.32f, 1f);
        private static readonly Color HoverColor = new Color(0.16f, 0.31f, 0.46f, 1f);
        private static readonly Color PressedColor = new Color(0.07f, 0.15f, 0.24f, 1f);
        private static readonly Color DisabledColor = new Color(0.18f, 0.22f, 0.27f, 1f);

        private InputModeManager subscribedModes;
        private HandPacket discardedPacket;
        private bool initialized;
        private bool automaticStartupHandled;
        private bool processExitFailure;
        private bool ownsConnection;
        private bool stopFailed;
        private bool pointerDown;
        private bool pointerInside;
        private float startedAt;
        private string statusDetail;

        public CameraConnectionState State { get; private set; }
        public int SelectedCameraIndex { get { return selectedCameraIndex; } }
        public bool PreviewEnabled { get { return previewEnabled; } }
        public IReadOnlyList<int> AvailableCameraIndices { get { return availableCameraIndices; } }
        public string SelectedCameraStatus { get; private set; } = "카메라 목록을 조회하세요";
        private int[] availableCameraIndices = new int[0];

        public bool RefreshCameras()
        {
            if (launcher.IsRunning)
            {
                SelectedCameraStatus = "실행 중에는 카메라 목록을 변경할 수 없습니다";
                return false;
            }
            bool started = launcher.BeginDiscoverCameras();
            if (started)
            {
                SelectedCameraStatus = "카메라 목록 조회 중";
                UpdateCameraLabel();
            }
            else
            {
                SelectedCameraStatus = launcher.IsDiscovering ? "카메라 목록이 이미 조회 중입니다" :
                    (string.IsNullOrEmpty(launcher.LastDiscoveryError) ? "카메라 목록 조회를 시작할 수 없습니다" : launcher.LastDiscoveryError);
                UpdateCameraLabel();
            }
            return started;
        }

        public bool SelectCamera(int index)
        {
            if (index < 0 || launcher.IsRunning || !ContainsCamera(index)) return false;
            selectedCameraIndex = index;
            bool selected = launcher.SetCameraIndex(index);
            if (selected) {
                SelectedCameraStatus = "카메라 index " + index + " 선택됨";
                UpdateCameraLabel();
            }
            return selected;
        }

        public bool CycleCamera(int direction)
        {
            if (direction == 0 || launcher.IsRunning) return false;
            if (availableCameraIndices.Length == 0) return false;
            int next = CameraDeviceCatalog.NextIndex(availableCameraIndices, selectedCameraIndex, direction);
            return next >= 0 && SelectCamera(next);
        }

        public bool SelectPreview(bool enabled)
        {
            if (launcher.IsRunning) return false;
            previewEnabled = enabled;
            return launcher.SetPreviewEnabled(enabled);
        }

        private bool ContainsCamera(int index)
        {
            return System.Array.IndexOf(availableCameraIndices, index) >= 0;
        }

        private void UpdateCameraLabel()
        {
            if (selectedCameraLabel != null) selectedCameraLabel.text = SelectedCameraStatus;
            if (statusLabel != null && State == CameraConnectionState.Off) statusLabel.text = SelectedCameraStatus;
        }

        private void PollCameraDiscovery()
        {
            int[] indices;
            string error;
            if (!launcher.TryConsumeDiscoveryResult(out indices, out error)) return;
            if (indices.Length == 0)
            {
                availableCameraIndices = new int[0];
                SelectedCameraStatus = error;
                UpdateCameraLabel();
                return;
            }
            availableCameraIndices = indices;
            if (!ContainsCamera(selectedCameraIndex)) selectedCameraIndex = indices[0];
            launcher.SetCameraIndex(selectedCameraIndex);
            SelectedCameraStatus = "카메라 index " + selectedCameraIndex + " 선택됨";
            UpdateCameraLabel();
        }

        private void OnEnable()
        {
            if (!ValidateReferences())
            {
                enabled = false;
                return;
            }
            if (subscribedModes != null) subscribedModes.OnModeChanged -= HandleModeChanged;
            subscribedModes = inputModeManager;
            subscribedModes.OnModeChanged += HandleModeChanged;
            string retainedFailure = State == CameraConnectionState.Failed ? statusDetail : null;
            bool alreadyStarting = initialized && ownsConnection && State == CameraConnectionState.Starting;
            initialized = true;
            ownsConnection = launcher.IsRunning;
            stopFailed = stopFailed && ownsConnection;
            if (!alreadyStarting) startedAt = Time.unscaledTime;
            statusDetail = null;
            if (stopFailed || (!ownsConnection && !string.IsNullOrEmpty(retainedFailure)))
                SetState(CameraConnectionState.Failed, retainedFailure ?? "캠 종료 실패\n다시 눌러 종료하세요");
            else
                SetState(ownsConnection ? CameraConnectionState.Starting : CameraConnectionState.Off);
            RefreshConnection(Time.unscaledTime);
        }

        private void Start()
        {
            if (Application.isPlaying) TryAutomaticStartup(Time.unscaledTime);
        }

        internal void TryAutomaticStartup(float now)
        {
            if (!initialized || !autoStartCamera || automaticStartupHandled) return;
            automaticStartupHandled = true;
            RefreshConnection(now);
            if (launcher.IsRunning || HasFreshPacket()) return;
            StartCamera(now);
        }

        private void Update()
        {
            RefreshConnection(Time.unscaledTime);
            PollCameraDiscovery();
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                CancelPointer();
                return;
            }
            ProcessPointer(mouse.position.ReadValue(), mouse.leftButton.wasPressedThisFrame,
                mouse.leftButton.wasReleasedThisFrame, Time.unscaledTime);
        }

        internal void RefreshConnection(float now)
        {
            if (!initialized) return;
            launcher.RefreshStatus();
            if (stopFailed)
            {
                if (!launcher.IsRunning)
                {
                    ownsConnection = false;
                    stopFailed = false;
                    DiscardCurrentPacket();
                    SetState(CameraConnectionState.Off);
                }
                return;
            }
            if (ownsConnection)
            {
                if (!launcher.IsRunning)
                {
                    ownsConnection = false;
                    processExitFailure = true;
                    DiscardCurrentPacket();
                    SetState(CameraConnectionState.Failed,
                        string.IsNullOrEmpty(launcher.LastError) ? "송신기가 종료되었습니다" : launcher.LastError);
                }
                else if (HasFreshPacket())
                {
                    SetState(CameraConnectionState.Receiving);
                }
                else if (State == CameraConnectionState.Receiving)
                {
                    SetState(CameraConnectionState.Failed, "송신 연결 끊김\n캠을 다시 켜거나 연결을 확인하세요");
                }
                else if (State == CameraConnectionState.Starting && now - startedAt >= connectionTimeoutSeconds)
                {
                    launcher.StopTrackerAfterTimeout();
                    stopFailed = launcher.IsRunning;
                    ownsConnection = stopFailed;
                    DiscardCurrentPacket();
                    SetState(CameraConnectionState.Failed, launcher.LastError);
                }
                return;
            }
            if (HasFreshPacket())
            {
                processExitFailure = false;
                SetState(CameraConnectionState.External);
            }
            else if (State == CameraConnectionState.External)
            {
                SetState(CameraConnectionState.Failed, "외부 송신 연결 끊김\n실행한 터미널을 확인하세요");
            }
            else if (processExitFailure && !string.IsNullOrEmpty(launcher.LastError))
            {
                SetState(CameraConnectionState.Failed, launcher.LastError);
            }
        }

        internal void ProcessPointer(Vector2 position, bool pressed, bool released, float now)
        {
            if (!initialized || !inputModeManager.CanUseCameraMouse)
            {
                CancelPointer();
                return;
            }
            if (cameraButton == null || !cameraButton.IsActive() || !cameraButton.IsInteractable())
            {
                CancelPointer();
                return;
            }
            // 공유 UI 모듈을 켜지 않고 이 카메라 버튼 영역만 검사한다.
            pointerInside = RectTransformUtility.RectangleContainsScreenPoint((RectTransform)cameraButton.transform, position, null);
            if (!pointerInside) pointerDown = false;
            if (pressed) pointerDown = pointerInside;
            bool clicked = released && pointerDown && pointerInside;
            if (released) pointerDown = false;
            ApplyButtonColor();
            if (clicked) ToggleCamera(now);
        }

        private void ToggleCamera(float now)
        {
            automaticStartupHandled = true;
            if (State == CameraConnectionState.Receiving || stopFailed)
            {
                StopOwnedTracker();
                SetState(stopFailed ? CameraConnectionState.Failed : CameraConnectionState.Off,
                    stopFailed ? "캠 종료 실패\n다시 눌러 종료하세요" : string.Empty);
                return;
            }
            if (launcher.IsRunning)
            {
                StopOwnedTracker();
                if (stopFailed)
                {
                    SetState(CameraConnectionState.Failed, "캠 종료 실패\n다시 눌러 종료하세요");
                    return;
                }
            }
            if (HasFreshPacket())
            {
                SetState(CameraConnectionState.External);
                return;
            }
            StartCamera(now);
        }

        private void StartCamera(float now)
        {
            processExitFailure = false;
            startedAt = now;
            ownsConnection = launcher.StartTracker();
            if (ownsConnection)
            {
                SetState(CameraConnectionState.Starting);
            }
            else
            {
                SetState(CameraConnectionState.Failed,
                    string.IsNullOrEmpty(launcher.LastError) ? "캠 실행 실패" : launcher.LastError);
            }
        }

        private bool HasFreshPacket()
        {
            return receiver.LatestPacket != null && !ReferenceEquals(receiver.LatestPacket, discardedPacket) && !receiver.IsServerLost;
        }

        private void StopOwnedTracker()
        {
            launcher.StopTracker();
            stopFailed = launcher.IsRunning;
            ownsConnection = stopFailed;
            DiscardCurrentPacket();
        }

        private void DiscardCurrentPacket()
        {
            discardedPacket = receiver.LatestPacket;
        }

        private void SetState(CameraConnectionState state, string detail = "")
        {
            if (State == state && statusDetail == detail) return;
            State = state;
            statusDetail = detail;
            CancelPointer();
            handInputRouter.CancelAll(HandCancelReason.TrackingLost);
            bool receiving = state == CameraConnectionState.Receiving || state == CameraConnectionState.External;
            inputModeManager.SetCameraControlState(true, !receiving);
            if (cameraButton != null)
                cameraButton.interactable = state != CameraConnectionState.Starting && state != CameraConnectionState.External;
            statusLabel.color = receiving ? new Color(0.62f, 0.89f, 0.75f) :
                state == CameraConnectionState.Failed ? new Color(1f, 0.70f, 0.62f) : new Color(0.79f, 0.84f, 0.90f);
            switch (state)
            {
                case CameraConnectionState.Starting:
                    SetButtonLabel("시작 중…");
                    statusLabel.text = "카메라 연결 대기 중\n첫 패킷을 기다리고 있습니다";
                    break;
                case CameraConnectionState.Receiving:
                    SetButtonLabel("캠 끄기");
                    statusLabel.text = "송신 수신 중\n손을 카메라에 보여주세요";
                    break;
                case CameraConnectionState.External:
                    SetButtonLabel("외부 캠 사용 중");
                    statusLabel.text = "외부 송신 수신 중\n종료는 실행한 터미널에서";
                    break;
                case CameraConnectionState.Failed:
                    SetButtonLabel(stopFailed ? "캠 끄기 재시도" : "캠 다시 켜기");
                    statusLabel.text = SummarizeFailure(detail);
                    break;
                default:
                    SetButtonLabel("캠 켜기");
                    statusLabel.text = "꺼짐\n버튼을 눌러 카메라를 켜세요";
                    break;
            }
            ApplyButtonColor();
        }

        private static string SummarizeFailure(string detail)
        {
            string[] lines = (detail ?? string.Empty).Split('\n');
            for (int i = 0; i < Mathf.Min(2, lines.Length); i++)
            {
                lines[i] = lines[i].Trim();
                int length = 0;
                int width = 0;
                while (length < lines[i].Length && length < 31)
                {
                    int nextWidth = lines[i][length] >= 0x1100 ? 2 : 1;
                    if (width + nextWidth > 40) break;
                    width += nextWidth;
                    length++;
                }
                if (length < lines[i].Length) lines[i] = lines[i].Substring(0, length) + "…";
            }
            return lines.Length > 1 ? lines[0] + "\n" + lines[1] : lines[0];
        }

        private void SetButtonLabel(string value)
        {
            if (buttonLabel != null) buttonLabel.text = value;
        }

        private void HandleModeChanged(InputMode mode) => CancelPointer();

        private void CancelPointer()
        {
            pointerDown = false;
            pointerInside = false;
            ApplyButtonColor();
        }

        private void ApplyButtonColor()
        {
            if (cameraButton == null || cameraButton.targetGraphic == null) return;
            cameraButton.targetGraphic.color = !cameraButton.IsInteractable() ? DisabledColor :
                pointerDown ? PressedColor : pointerInside ? HoverColor : NormalColor;
        }

        private bool ValidateReferences()
        {
            if (launcher == null) return MissingReference(nameof(launcher));
            if (receiver == null) return MissingReference(nameof(receiver));
            if (inputModeManager == null) return MissingReference(nameof(inputModeManager));
            if (handInputRouter == null) return MissingReference(nameof(handInputRouter));
            if (cameraButton == null) return MissingReference(nameof(cameraButton));
            if (buttonLabel == null) return MissingReference(nameof(buttonLabel));
            if (statusLabel == null) return MissingReference(nameof(statusLabel));
            if (float.IsNaN(connectionTimeoutSeconds) || float.IsInfinity(connectionTimeoutSeconds) || connectionTimeoutSeconds < 1f)
            {
                Debug.LogError("CameraControlPanel: connectionTimeoutSeconds must be finite and at least 1 second.", this);
                return false;
            }
            return true;
        }

        private bool MissingReference(string field)
        {
            Debug.LogError("CameraControlPanel: " + field + " is unassigned.", this);
            return false;
        }

        private void OnDisable()
        {
            if (launcher != null) launcher.CancelDiscoverCameras();
            if (subscribedModes != null) subscribedModes.OnModeChanged -= HandleModeChanged;
            subscribedModes = null;
            if (!initialized) return;
            initialized = false;
            CancelPointer();
            handInputRouter.CancelAll(HandCancelReason.ComponentDisabled);
            inputModeManager.SetCameraControlState(false, false);
            if (ownsConnection) StopOwnedTracker();
        }
    }
}
