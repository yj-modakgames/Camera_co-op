using System.Linq;
using CameraCoop.Party;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CameraCoop.Tests
{
    // 로비는 실외 외계 행성이다. 벽이 다시 생기거나, 지형이 손 raycast를 막거나,
    // 에셋 재질이 마젠타로 깨지면 여기서 걸린다.
    public sealed class RelayQuizOnlineLobbyTerrainTests
    {
        private SceneSetup[] originalSetup;
        private Scene lobby;

        [SetUp]
        public void SetUp()
        {
            originalSetup = EditorSceneManager.GetSceneManagerSetup();
            // Scene은 테스트마다 한 번만 연다. 조회마다 다시 열면 앞서 찾은 Transform이 파괴된다.
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
        public void StudioHasNoWallsOrPillarsAndKeepsTheFloorCollider()
        {
            Transform studio = Find("Studio");
            string[] walls = { "NorthWall", "SouthWall", "WestWall", "EastWall" };
            foreach (Transform item in studio.GetComponentsInChildren<Transform>(true))
            {
                Assert.That(walls, Does.Not.Contain(item.name), "실외 로비에 벽이 다시 생겼다: " + item.name);
                Assert.That(item.name.StartsWith("RoomPillar_"), Is.False, "기둥이 다시 생겼다: " + item.name);
            }

            Transform floor = studio.Cast<Transform>().Single(item => item.name == "Floor");
            var collider = floor.GetComponent<BoxCollider>();
            Assert.That(collider, Is.Not.Null, "Floor의 BoxCollider가 사라지면 플레이어가 바닥을 뚫는다.");
            Assert.That(collider.bounds.max.y, Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void TerrainAndDecorAreSolidBackdropWithoutColliders()
        {
            Transform terrain = Find("Terrain");
            Transform decor = Find("LobbyDecor");
            Assert.That(terrain.childCount, Is.GreaterThanOrEqualTo(20));
            Assert.That(terrain.GetComponentsInChildren<Collider>(true), Is.Empty,
                "지형에 collider가 있으면 HandInputRouter가 가장 가까운 hit 하나만 보고 station을 놓친다.");
            Assert.That(decor.GetComponentsInChildren<Collider>(true), Is.Empty,
                "장식 collider가 기능 소품 앞을 가린다 (사용자 보고 2026-09-04).");

            Transform[] props = decor.Cast<Transform>().Where(item => item.name.StartsWith("Decor_")).ToArray();
            Assert.That(props, Has.Length.GreaterThanOrEqualTo(7));
            foreach (Transform prop in props)
                Assert.That(prop.GetComponentInChildren<MeshRenderer>(true), Is.Not.Null, prop.name);
        }

        [Test]
        public void TerrainRenderersHaveNoMissingOrErrorMaterials()
        {
            foreach (MeshRenderer renderer in Find("Terrain").GetComponentsInChildren<MeshRenderer>(true))
            foreach (Material material in renderer.sharedMaterials)
            {
                Assert.That(material, Is.Not.Null, renderer.name + " has an empty material slot.");
                Assert.That(material.shader.name, Is.Not.EqualTo("Hidden/InternalErrorShader"),
                    renderer.name + " renders magenta.");
            }
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
