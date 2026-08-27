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
        [SerializeField] private Material lineMaterial;                  // StrokeLine.mat 공유 (스타일 누락 시 폴백)
        [SerializeField] private Material[] brushMaterials;              // 브러시 인덱스 -> 머티리얼 (docs/11 §3)
        [SerializeField] private CanvasSurface canvasSurface;    // 할당 시 월드 캔버스에 표시 (docs/10 §2). 미할당 = 기존 카메라 평면
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
            if (canvasSurface != null && drawCamera == null)
            {
                Debug.LogError("[RemotePresenter] canvasSurface는 할당됐는데 drawCamera가 없다 — 원격 표시가 화면 좌표로 폴백해 잉크와 어긋난다 (docs/10 §2)");
            }
            session.OnRemoteCursor += HandleCursor;
            session.OnRemoteStrokeStart += HandleStrokeStart;
            session.OnRemoteStrokeErased += HandleStrokeErased;
            session.OnRemoteStrokePoints += HandleStrokePoints;
            session.OnRemoteStrokeEnd += HandleStrokeEnd;
            session.OnCanvasCleared += HandleCleared;
        }

        private void OnDisable()
        {
            session.OnRemoteCursor -= HandleCursor;
            session.OnRemoteStrokeStart -= HandleStrokeStart;
            session.OnRemoteStrokeErased -= HandleStrokeErased;
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
            cursor.rect.position = (canvasSurface != null && drawCamera != null)
                ? (Vector2)drawCamera.WorldToScreenPoint(canvasSurface.NormToWorld(norm))
                : HandScreenMapper.ToScreen(norm.x, norm.y, Screen.width, Screen.height);
            float scale = CursorStateLogic.Scale(pinched, 0.7f);
            cursor.rect.localScale = new Vector3(scale, scale, scale);
        }

        private void HandleStrokeStart(string strokeId, string playerId, Vector2 norm, StrokeStyle style)
        {
            if (strokeLines.ContainsKey(strokeId))
            {
                return; // 멱등
            }
            var strokeObject = new GameObject("RemoteStroke_" + strokeId);
            strokeObject.transform.SetParent(transform, worldPositionStays: true);
            LineRenderer line = strokeObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.numCapVertices = 4;
            line.numCornerVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            // 스타일이 실려 있으면 그대로 재생한다. width <= 0 = 스타일 없음(구버전/스냅샷) -> 플레이어 색 폴백 (docs/11 §3)
            bool hasStyle = style.width > 0f;
            Color color = hasStyle ? ColorPack.FromInt(style.color) : ColorOf(playerId);
            line.widthMultiplier = hasStyle ? style.width : lineWidth;
            Material material = hasStyle ? BrushMaterial(style.brush) : lineMaterial;
            line.sharedMaterial = material != null ? material : lineMaterial;
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

        // 브러시 인덱스 -> 공유 머티리얼. 범위 밖이면 null을 돌려 호출부가 기본 머티리얼로 폴백한다.
        private Material BrushMaterial(int brush)
        {
            if (brushMaterials == null || brush < 0 || brush >= brushMaterials.Length)
            {
                return null;
            }
            return brushMaterials[brush];
        }

        // 원격 지우개 (docs/11 §3). 없는 id는 무시 — 멱등
        private void HandleStrokeErased(string strokeId)
        {
            LineRenderer line;
            if (!strokeLines.TryGetValue(strokeId, out line))
            {
                return;
            }
            strokeLines.Remove(strokeId);
            if (line != null)
            {
                Destroy(line.gameObject);
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
    }
}
