using UnityEngine;
using UnityEngine.UI;

namespace CameraCoop.Netplay
{
    // 최소 로비 UI (docs/08 §5): Loopback/Steam host 시작, Clear, 피어 목록 표시.
    // 클라 join은 Steam overlay 초대 수락 경로 — 별도 버튼 없음 (SteamTransport.ConnectTo).
    public class NetplayUI : MonoBehaviour
    {
        [SerializeField] private NetSession session;
        [SerializeField] private DrawingController drawingController;
        [SerializeField] private Text statusText;

        private void OnEnable()
        {
            session.OnPlayersChanged += Refresh;
            session.OnCanvasCleared += HandleCleared;
            Refresh();
        }

        private void OnDisable()
        {
            session.OnPlayersChanged -= Refresh;
            session.OnCanvasCleared -= HandleCleared;
        }

        // Loopback 세션 시작 (버튼 배선). 가짜 피어는 Task 5의 검증 스크립트가 붙인다.
        public void OnClickHostLoopback()
        {
            if (session.IsRunning)
            {
                return;
            }
            session.StartSession(new LoopbackTransport(), "LocalHost");
            Refresh();
        }

        // Steam 세션 시작 (버튼 배선). 친구는 Steam overlay 초대로 참가 (docs/08 §5).
        public async void OnClickHostSteam()
        {
            if (session.IsRunning)
            {
                return;
            }
            if (!SteamBootstrap.TryInit())
            {
                if (statusText != null) { statusText.text = "Steam 미실행 — Steam 로그인 후 재시도"; }
                return;
            }
            try
            {
                SteamTransport transport = await SteamTransport.HostAsync(4);
                session.StartSession(transport, SteamBootstrap.LocalName);
                Steamworks.SteamFriends.OpenGameInviteOverlay(transport.LobbyId); // 로비 초대 overlay
                Refresh();
            }
            catch (System.Exception e)
            {
                if (statusText != null) { statusText.text = "Steam host 실패: " + e.Message; }
            }
        }

        public void OnClickClear()
        {
            session.SendClear(); // host 전용. 수신/발신 공통 정리는 HandleCleared에서
        }

        private void HandleCleared()
        {
            drawingController.ClearAll(); // 로컬 표시분도 함께 청소 (docs/08 §3 Clear 일관성)
        }

        private void Refresh()
        {
            if (statusText == null)
            {
                return;
            }
            if (!session.IsRunning)
            {
                statusText.text = "세션 없음 — Host Loopback을 누르세요";
                return;
            }
            var sb = new System.Text.StringBuilder();
            sb.Append(session.IsHost ? "[HOST] " : "[CLIENT] ").Append("players: ").Append(session.Players.Count).AppendLine();
            foreach (var pair in session.Players)
            {
                sb.Append("  ").Append(pair.Value.name).Append(" (#").Append(pair.Value.colorIndex).Append(")").AppendLine();
            }
            statusText.text = sb.ToString();
        }
    }
}
