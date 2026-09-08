using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CameraCoop;
using CameraCoop.Party;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CameraCoop.Tests
{
    // 로비 소품 배치 감사. 소품끼리 XZ로 겹치거나, 지형이 방 안으로 들어오거나,
    // 장식이 기능 소품 앞을 막으면 여기서 걸린다. 눈으로 보는 대신 bounds로 잰다.
    public sealed class RelayQuizOnlineLobbyLayoutTests
    {
        private const float RoomHalfX = 14f;
        private const float RoomHalfZ = 8f;
        private const float DecorClearance = 1.0f;
        private static readonly Vector2 SpawnXZ = new Vector2(0f, -7.2f);
        private const float SpawnClearRadius = 2f;

        // 감사 대상 소품의 이름 접두. 표지판(Sign_)·라벨(Label_)·액자(Frame)·Quad 면·마커는
        // 부피가 없거나 통행을 막지 않으므로 소품이 아니다.
        // 아바타(AvatarBody_)도 뺀다 — 몸통이 SkinnedMeshRenderer라 여기서 재는 MeshRenderer는
        // 손 본에 달린 붓뿐이고, 본 스케일 때문에 bounds가 160 m로 나온다. 소품이 아니라 캐릭터다.
        private static readonly string[] AuditedPrefixes =
        {
            "BayRug_", "ReadyPad_", "Action_", "LobbyCounter_", "LobbyBarrel_",
            "CameraDesk_", "SupplyBench_", "PhysicalBrush_", "PaintPot_", "WidthControl_",
            "JumpStep_", "Decor_"
        };

        // 접두가 다른 소품을 삼키지 않도록 이름 하나짜리는 정확히 일치시킨다
        // (예전엔 "LobbyDesk"가 LobbyDeskProps를 통째로 먹어 그 안의 스툴·컵이 측정되지 않았다).
        private static readonly string[] AuditedNames =
        {
            "LobbyDesk", "CameraMonitor", "BrushRack", "EraserStation",
            "CurrentInkStand", "CurrentInkChip", "ReferencePanelBack", "ScratchBoardClear"
        };

        private static bool IsAudited(string name)
        {
            return AuditedNames.Contains(name)
                || AuditedPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));
        }

        private SceneSetup[] originalSetup;
        private Scene lobby;

        [SetUp]
        public void SetUp()
        {
            originalSetup = EditorSceneManager.GetSceneManagerSetup();
            lobby = EditorSceneManager.OpenScene(PartySceneCatalog.LobbyScenePath, OpenSceneMode.Single);
        }

        [TearDown]
        public void TearDown()
        {
            if (originalSetup != null && originalSetup.Length > 0
                && originalSetup.All(item => !string.IsNullOrEmpty(item.path)))
            {
                EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
                return;
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [Test]
        public void PropsDoNotOverlapOnTheGroundPlane()
        {
            Prop[] props = CollectProps();
            Assert.That(props, Has.Length.GreaterThan(30), "감사 대상 소품을 못 찾았다. 이름 규칙이 바뀌었는지 확인할 것.");

            var failures = new List<string>();
            for (int a = 0; a < props.Length; a++)
            for (int b = a + 1; b < props.Length; b++)
            {
                if (props[a].Group == props[b].Group) continue;
                float overlapX = Overlap(props[a].MinX, props[a].MaxX, props[b].MinX, props[b].MaxX);
                float overlapZ = Overlap(props[a].MinZ, props[a].MaxZ, props[b].MinZ, props[b].MaxZ);
                if (overlapX <= 0f || overlapZ <= 0f) continue;
                failures.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0} {1} <-> {2} {3} : x {4:0.00} m, z {5:0.00} m",
                    props[a].Name, props[a].Rect(), props[b].Name, props[b].Rect(), overlapX, overlapZ));
            }

            Assert.That(failures, Is.Empty, () => "소품 " + failures.Count + "쌍이 XZ 평면에서 겹친다:\n"
                + string.Join("\n", failures) + "\n\n[전체 소품 bounds]\n" + Dump(props));
        }

        [Test]
        public void TerrainStaysOutsideTheRoom()
        {
            Transform terrain = Find("Terrain");
            var failures = new List<string>();
            foreach (MeshRenderer renderer in terrain.GetComponentsInChildren<MeshRenderer>(true))
            {
                Bounds bounds = renderer.bounds;
                // 방 바닥(Floor 윗면 y=0) 아래에 묻힌 평원·지면 패치는 보이지도 통행을 막지도 않는다.
                if (bounds.max.y <= 0.001f) continue;
                float overlapX = Overlap(bounds.min.x, bounds.max.x, -RoomHalfX, RoomHalfX);
                float overlapZ = Overlap(bounds.min.z, bounds.max.z, -RoomHalfZ, RoomHalfZ);
                if (overlapX <= 0f || overlapZ <= 0f) continue;
                failures.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0} x[{1:0.00},{2:0.00}] z[{3:0.00},{4:0.00}] : 방 안으로 x {5:0.00} m, z {6:0.00} m 침범",
                    Path(renderer.transform, terrain), bounds.min.x, bounds.max.x, bounds.min.z, bounds.max.z,
                    overlapX, overlapZ));
            }
            Assert.That(failures, Is.Empty, () => "지형이 방 경계(±14, ±8) 안으로 들어왔다:\n"
                + string.Join("\n", failures));
        }

        [Test]
        public void DecorKeepsOneMeterFromFunctionalProps()
        {
            Prop[] props = CollectProps();
            Prop[] decor = props.Where(item => item.Name.StartsWith("Decor_", StringComparison.Ordinal)).ToArray();
            Prop[] functional = props.Where(item => item.Functional).ToArray();
            Assert.That(decor, Is.Not.Empty);
            Assert.That(functional, Has.Length.GreaterThan(10));

            var failures = new List<string>();
            foreach (Prop item in decor)
            foreach (Prop target in functional)
            {
                float gap = RectDistance(item, target);
                if (gap >= DecorClearance) continue;
                failures.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1} <-> {2} {3} : {4:0.00} m",
                    item.Name, item.Rect(), target.Name, target.Rect(), gap));
            }
            Assert.That(failures, Is.Empty, () => "장식이 기능 소품에서 1 m 안으로 붙었다:\n"
                + string.Join("\n", failures));
        }

        [Test]
        public void SpawnCircleStaysClear()
        {
            Prop[] props = CollectProps()
                .Where(item => item.Functional || item.Name.StartsWith("Decor_", StringComparison.Ordinal))
                .ToArray();
            var failures = new List<string>();
            foreach (Prop item in props)
            {
                float gap = PointDistance(item, SpawnXZ);
                if (gap >= SpawnClearRadius) continue;
                failures.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1} : spawn까지 {2:0.00} m",
                    item.Name, item.Rect(), gap));
            }
            Assert.That(failures, Is.Empty, () => "spawn (0, -7.2) 반경 2 m 안에 소품이 있다:\n"
                + string.Join("\n", failures));
        }

        private sealed class Prop
        {
            public string Name;
            public string Group;
            public bool Functional;
            public float MinX;
            public float MaxX;
            public float MinZ;
            public float MaxZ;

            public string Rect()
            {
                return string.Format(CultureInfo.InvariantCulture, "x[{0:0.00},{1:0.00}] z[{2:0.00},{3:0.00}]",
                    MinX, MaxX, MinZ, MaxZ);
            }
        }

        private Prop[] CollectProps()
        {
            Transform world = Find("LobbyWorldRoot");
            var props = new List<Prop>();
            Collect(world, props);
            return props.ToArray();
        }

        private static void Collect(Transform node, List<Prop> props)
        {
            foreach (Transform child in node)
            {
                // 지형은 별도 테스트가 본다.
                if (child.name == "Studio") { Collect(child, props); continue; }
                if (child.name == "Terrain") continue;
                if (!IsAudited(child.name))
                {
                    Collect(child, props);
                    continue;
                }
                Prop prop = Measure(child);
                if (prop != null) props.Add(prop);
            }
        }

        private static Prop Measure(Transform root)
        {
            bool first = true;
            Bounds bounds = default;
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                // 라벨은 소품이 아니다. 물체 위에 떠 있을 뿐이라 발자국에 넣으면 안 된다.
                if (renderer.GetComponent<TextMesh>() != null) continue;
                if (first) { bounds = renderer.bounds; first = false; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (first) return null;
            return new Prop
            {
                Name = root.name,
                Group = GroupOf(root.name),
                Functional = root.GetComponentInChildren<HandInteractable>(true) != null
                    || root.name.StartsWith("JumpStep_", StringComparison.Ordinal),
                MinX = bounds.min.x, MaxX = bounds.max.x, MinZ = bounds.min.z, MaxZ = bounds.max.z
            };
        }

        // 같은 기능 묶음 안의 겹침은 의도된 것이다 (버튼이 상판 위에, 붓이 rack 위에).
        // 묶음이 다르면 겹치면 안 된다.
        private static string GroupOf(string name)
        {
            if (name.StartsWith("BayRug_", StringComparison.Ordinal)
                || name.StartsWith("ReadyPad_", StringComparison.Ordinal))
                return "Bay" + name[name.Length - 1];
            switch (name)
            {
                case "LobbyDesk":
                case "Action_Host":
                case "Action_Invite":
                case "Action_Leave":
                    return "Counter";
                case "CameraMonitor":
                case "Action_CameraRefresh":
                case "Action_CameraPrevious":
                case "Action_CameraNext":
                case "Action_CameraPreview":
                    return "CameraStation";
                case "SupplyBench_0":
                case "BrushRack":
                    return "BrushBench";
                case "SupplyBench_1":
                case "CurrentInkStand":
                case "CurrentInkChip":
                    return "PaintBench";
            }
            if (name.StartsWith("LobbyCounter_", StringComparison.Ordinal)
                || name.StartsWith("LobbyBarrel_", StringComparison.Ordinal))
                return "Counter";
            if (name.StartsWith("CameraDesk_", StringComparison.Ordinal)) return "CameraStation";
            if (name.StartsWith("PhysicalBrush_", StringComparison.Ordinal)) return "BrushBench";
            if (name.StartsWith("PaintPot_", StringComparison.Ordinal)) return "PaintBench";
            return name;
        }

        private static float Overlap(float minA, float maxA, float minB, float maxB)
        {
            return Mathf.Min(maxA, maxB) - Mathf.Max(minA, minB);
        }

        private static float RectDistance(Prop a, Prop b)
        {
            float dx = Mathf.Max(0f, Mathf.Max(a.MinX - b.MaxX, b.MinX - a.MaxX));
            float dz = Mathf.Max(0f, Mathf.Max(a.MinZ - b.MaxZ, b.MinZ - a.MaxZ));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static float PointDistance(Prop a, Vector2 point)
        {
            float dx = Mathf.Max(0f, Mathf.Max(a.MinX - point.x, point.x - a.MaxX));
            float dz = Mathf.Max(0f, Mathf.Max(a.MinZ - point.y, point.y - a.MaxZ));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static string Dump(Prop[] props)
        {
            var text = new StringBuilder();
            foreach (Prop prop in props.OrderBy(item => item.Name, StringComparer.Ordinal))
                text.Append(prop.Name).Append(' ').Append(prop.Rect())
                    .Append(prop.Functional ? " [기능]" : string.Empty).Append('\n');
            return text.ToString();
        }

        private static string Path(Transform node, Transform stopAt)
        {
            var parts = new List<string>();
            for (Transform current = node; current != null && current != stopAt; current = current.parent)
                parts.Add(current.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        private Transform Find(string name)
        {
            Transform match = lobby.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(item => item.name == name);
            Assert.That(match, Is.Not.Null, name + " is missing from " + PartySceneCatalog.LobbyScenePath);
            return match;
        }
    }
}
