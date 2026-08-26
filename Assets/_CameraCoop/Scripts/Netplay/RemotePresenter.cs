using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CameraCoop.Netplay
{
    // 원격 플레이어의 커서(uGUI)와 스트로크(LineRenderer)를 표시한다 (docs/08 §2).
    // 로컬 플레이어의 표시는 기존 HandCursorController/DrawingController가 담당 — 여기서는 원격만.
    public class RemotePresenter : MonoBehaviour
    {
        [SerializeField] private NetSession session;
        [SerializeField] private RectTransform cursorPrefab;   // HandCursor.prefab 재사용
        [SerializeField] private Canvas canvas;
        [SerializeField] private Camera drawCamera;
        [SerializeField, Min(0f)] private float planeDistance = 5.0f;   // DrawingController와 동일 값 유지
        [SerializeField, Min(0f)] private float lineWidth = 0.02f;
        [SerializeField] private Material lineMaterial;                  // StrokeLine.mat 공유
        [SerializeField] private Color[] playerPalette = new Color[]     // colorIndex 0~3 (docs/08 §3)
        {
            new Color(0.2f, 0.6f, 1f), new Color(1f, 0.6f, 0.1f),
            new Color(0.3f, 0.9f, 0.4f), new Color(0.9f, 0.3f, 0.8f)
        };
        [SerializeField, Min(0.05f)] private float cursorLostTimeout = 0.5f; // 무수신 fade (docs/08 §4)
        [SerializeField, Min(0f)] private float fadeDuration = 0.2f;

        private class RemoteCursor
        {
            public RectTransform rect;
            public CanvasGroup group;
            public Image image;
            public float lastSeen;
        }

        private readonly Dictionary<string, RemoteCursor> cursors = new Dictionary<string, RemoteCursor>();      // playerId|hand
        private readonly Dictionary<string, LineRenderer> strokeLines = new Dictionary<string, LineRenderer>();  // strokeId

        private void OnEnable()
        {
            session.OnRemoteCursor += HandleCursor;
            session.OnRemoteStrokeStart += HandleStrokeStart;
            session.OnRemoteStrokePoints += HandleStrokePoints;
            session.OnRemoteStrokeEnd += HandleStrokeEnd;
            session.OnCanvasCleared += HandleCleared;
        }

        private void OnDisable()
        {
            session.OnRemoteCursor -= HandleCursor;
            session.OnRemoteStrokeStart -= HandleStrokeStart;
            session.OnRemoteStrokePoints -= HandleStrokePoints;
            session.OnRemoteStrokeEnd -= HandleStrokeEnd;
            session.OnCanvasCleared -= HandleCleared;
        }

        private void Update()
        {
            // 무수신 커서 fade (기존 lostTimeout 패턴)
            foreach (KeyValuePair<string, RemoteCursor> pair in cursors)
            {
                RemoteCursor cursor = pair.Value;
                float target = (Time.unscaledTime - cursor.lastSeen) > cursorLostTimeout ? 0f : 1f;
                cursor.group.alpha = CursorStateLogic.StepAlpha(cursor.group.alpha, target, Time.deltaTime, fadeDuration);
            }
        }

        private Color ColorOf(string playerId)
        {
            PlayerInfo info;
            if (session.Players.TryGetValue(playerId, out info) && info.colorIndex >= 0 && info.colorIndex < playerPalette.Length)
            {
                return playerPalette[info.colorIndex];
            }
            return Color.white;
        }

        private void HandleCursor(string playerId, string hand, Vector2 norm, bool pinched)
        {
            string key = playerId + "|" + hand;
            RemoteCursor cursor;
            if (!cursors.TryGetValue(key, out cursor))
            {
                RectTransform rect = Instantiate(cursorPrefab, canvas.transform);
                rect.name = "RemoteCursor_" + key;
                cursor = new RemoteCursor
                {
                    rect = rect,
                    group = rect.GetComponent<CanvasGroup>(),
                    image = rect.GetComponent<Image>()
                };
                cursors[key] = cursor;
            }
            cursor.lastSeen = Time.unscaledTime;
            cursor.image.color = ColorOf(playerId);
            cursor.rect.position = HandScreenMapper.ToScreen(norm.x, norm.y, Screen.width, Screen.height);
            float scale = CursorStateLogic.Scale(pinched, 0.7f);
            cursor.rect.localScale = new Vector3(scale, scale, scale);
        }

        private void HandleStrokeStart(string strokeId, string playerId, Vector2 norm)
        {
            if (strokeLines.ContainsKey(strokeId))
            {
                return; // 멱등
            }
            var strokeObject = new GameObject("RemoteStroke_" + strokeId);
            strokeObject.transform.SetParent(transform, worldPositionStays: true);
            LineRenderer line = strokeObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.widthMultiplier = lineWidth;
            line.sharedMaterial = lineMaterial;
            line.numCapVertices = 4;
            line.numCornerVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            Color color = ColorOf(playerId);
            line.startColor = color;
            line.endColor = color;
            line.positionCount = 1;
            line.SetPosition(0, ToWorld(norm));
            strokeLines[strokeId] = line;
        }

        private void HandleStrokePoints(string strokeId, Vector2[] points)
        {
            LineRenderer line;
            if (!strokeLines.TryGetValue(strokeId, out line))
            {
                return;
            }
            int baseCount = line.positionCount;
            line.positionCount = baseCount + points.Length;
            for (int i = 0; i < points.Length; i++)
            {
                line.SetPosition(baseCount + i, ToWorld(points[i]));
            }
        }

        private void HandleStrokeEnd(string strokeId)
        {
            LineRenderer line;
            if (!strokeLines.TryGetValue(strokeId, out line))
            {
                return;
            }
            if (line.positionCount < 2)
            {
                strokeLines.Remove(strokeId);
                Destroy(line.gameObject); // 점 찍기 폐기 규칙 동일 (docs/08 §3)
            }
        }

        private void HandleCleared()
        {
            foreach (KeyValuePair<string, LineRenderer> pair in strokeLines)
            {
                if (pair.Value != null)
                {
                    Destroy(pair.Value.gameObject);
                }
            }
            strokeLines.Clear();
        }

        // 정규화 [0,1] (좌상단 원점) -> 화면 -> 드로잉 평면 월드 좌표
        private Vector3 ToWorld(Vector2 norm)
        {
            Vector2 screen = HandScreenMapper.ToScreen(norm.x, norm.y, Screen.width, Screen.height);
            return drawCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, planeDistance));
        }
    }
}
