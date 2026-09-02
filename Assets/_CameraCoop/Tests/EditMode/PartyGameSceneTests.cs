using System;
using System.Linq;
using System.Reflection;
using CameraCoop.Party;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace CameraCoop.Tests
{
    public sealed class PartyGameSceneTests
    {
        [Test]
        public void CatalogScenesExistAndBuildSettingsStartWithExactCatalogOrder()
        {
            foreach (string scenePath in PartySceneCatalog.BuildScenePaths)
                Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath), Is.Not.Null, scenePath);

            string[] enabled = EditorBuildSettings.scenes.Where(item => item.enabled)
                .Select(item => item.path).ToArray();
            Assert.That(enabled.Take(PartySceneCatalog.BuildScenePaths.Count),
                Is.EqualTo(PartySceneCatalog.BuildScenePaths));
        }

        [Test]
        public void LobbyOwnsPersistentRuntimeAndFourPracticeStationsOnly()
        {
            Scene scene = EditorSceneManager.OpenScene(PartySceneCatalog.LobbyScenePath, OpenSceneMode.Single);
            GameObject runtimeRoot = scene.GetRootGameObjects().Single(item => item.name == "RuntimeRoot");
            GameObject lobbyRoot = scene.GetRootGameObjects().Single(item => item.name == "LobbyWorldRoot");
            PartyLobbyScenePort port = FindAll<PartyLobbyScenePort>(scene).Single();
            Assert.That(port.LobbyWorldRoot, Is.SameAs(lobbyRoot));
            Assert.That(port.ValidateBindings(out string error), Is.True, error);
            Assert.That(FindAll<PartyGameSceneAdapter>(scene), Is.Empty);
            Assert.That(FindAll<OnlineRelayQuizController>(scene), Has.Length.EqualTo(1));
            Assert.That(FindAll<PartyWorldController>(scene), Has.Length.EqualTo(1));
            Assert.That(FindAll<Camera>(scene), Has.Length.EqualTo(1));
            Assert.That(FindAll<EventSystem>(scene), Has.Length.EqualTo(1));
            Assert.That(Find(scene, "ModeSelectorRoot").activeSelf, Is.False);
            Assert.That(FindAll<Transform>(scene).Count(item =>
                item.name.StartsWith("PracticeEasel_", StringComparison.Ordinal)), Is.EqualTo(PartyRoster.Capacity));
            Assert.That(runtimeRoot.transform.IsChildOf(lobbyRoot.transform), Is.False);
        }

        [Test]
        public void LobbyControllerAcceptsDeferredGamePresentationBindings()
        {
            Scene scene = EditorSceneManager.OpenScene(PartySceneCatalog.LobbyScenePath, OpenSceneMode.Single);
            OnlineRelayQuizController controller = FindAll<OnlineRelayQuizController>(scene).Single();
            RelayQuizGallery gallery = FindAll<RelayQuizGallery>(scene).Single();

            Assert.That(gallery.IsReady, Is.False);
            Assert.That(gallery.ValidateRuntimeConfiguration(out string galleryError), Is.True, galleryError);
            Assert.That(controller.ValidateRuntimeConfiguration(out string controllerError), Is.True, controllerError);
        }
        [TestCase(PartyMode.RelayCopy)]
        [TestCase(PartyMode.MemoryCopy)]
        [TestCase(PartyMode.CoopMural)]
        public void GameSceneHasExactAdapterAndNoPersistentOwnerDuplicates(PartyMode mode)
        {
            Assert.That(PartySceneCatalog.TryGet(mode, out PartySceneDefinition definition), Is.True);
            Scene scene = EditorSceneManager.OpenScene(definition.ScenePath, OpenSceneMode.Single);
            PartyGameSceneAdapter[] adapters = FindAll<PartyGameSceneAdapter>(scene);
            Assert.That(adapters, Has.Length.EqualTo(1));
            Assert.That(adapters[0].Mode, Is.EqualTo(mode));
            Assert.That(adapters[0].ValidateBindings(out string error), Is.True, error);
            Assert.That(FindAll<Camera>(scene), Is.Empty);
            Assert.That(FindAll<EventSystem>(scene), Is.Empty);
            Assert.That(FindAll<OnlineRelayQuizController>(scene), Is.Empty);
            Assert.That(FindAll<PartyWorldController>(scene), Is.Empty);
            Assert.That(FindAll<TrackerLauncher>(scene), Is.Empty);
            Assert.That(FindAll<UdpHandReceiver>(scene), Is.Empty);
            Assert.That(FindAll<InputModeManager>(scene), Is.Empty);
            Assert.That(FindAll<PlayerController>(scene), Is.Empty);
            Assert.That(FindAll<DrawingController>(scene), Is.Empty);
            Assert.That(FindAll<ToolState>(scene), Is.Empty);
        }

        [TestCase(PartyMode.RelayCopy)]
        [TestCase(PartyMode.MemoryCopy)]
        public void PrivateScenesKeepThreeRemotePaperShellsGeometryOnly(PartyMode mode)
        {
            PartySceneCatalog.TryGet(mode, out PartySceneDefinition definition);
            Scene scene = EditorSceneManager.OpenScene(definition.ScenePath, OpenSceneMode.Single);
            GameObject[] shells = FindAll<Transform>(scene)
                .Where(item => item.name.StartsWith("RemotePaperShell_", StringComparison.Ordinal))
                .Select(item => item.gameObject).ToArray();
            Assert.That(shells, Has.Length.EqualTo(3));
            foreach (GameObject shell in shells)
            {
                Assert.That(shell.GetComponentInChildren<CanvasDrawingPresenter>(true), Is.Null);
                Assert.That(shell.GetComponentInChildren<CanvasSurface>(true), Is.Null);
                Assert.That(shell.GetComponentInChildren<HandCanvasInteractable>(true), Is.Null);
                Assert.That(shell.GetComponentInChildren<DrawingController>(true), Is.Null);
            }
        }

        [TestCase(PartyMode.RelayCopy)]
        [TestCase(PartyMode.MemoryCopy)]
        public void PrivateScenesBindThreeReadOnlyGallerySlotsAndResultViewPose(PartyMode mode)
        {
            PartySceneCatalog.TryGet(mode, out PartySceneDefinition definition);
            Scene scene = EditorSceneManager.OpenScene(definition.ScenePath, OpenSceneMode.Single);
            PartySceneBindings bindings = FindAll<PartyGameSceneAdapter>(scene).Single().Bindings;

            Assert.That(bindings.GalleryRoots, Has.Length.EqualTo(PartyRoster.Capacity - 1));
            Assert.That(bindings.GalleryPresenters, Has.Length.EqualTo(PartyRoster.Capacity - 1));
            Assert.That(bindings.GallerySurfaces, Has.Length.EqualTo(PartyRoster.Capacity - 1));
            Assert.That(bindings.ResultViewPose, Is.Not.Null);
            Assert.That(bindings.ResultViewPose.IsChildOf(bindings.ResultRoot.transform), Is.True);
            Assert.That(bindings.GallerySurfaces.All(surface =>
                surface.GetComponentInParent<HandCanvasInteractable>() == null), Is.True);
        }

        [TestCase(PartyMode.RelayCopy)]
        [TestCase(PartyMode.MemoryCopy)]
        public void PrivateResultGalleryFitsProductionCameraWithReadableSlotsAndReturnControl(PartyMode mode)
        {
            PartySceneCatalog.TryGet(mode, out PartySceneDefinition definition);
            Scene scene = EditorSceneManager.OpenScene(definition.ScenePath, OpenSceneMode.Single);
            PartySceneBindings bindings = FindAll<PartyGameSceneAdapter>(scene).Single().Bindings;
            bool wasActive = bindings.ResultRoot.activeSelf;
            GameObject cameraObject = new GameObject("ResultContractCamera", typeof(Camera));
            try
            {
                bindings.ResultRoot.SetActive(true);
                ResultPresentationIsolation isolation = bindings.ResultRoot.GetComponent<ResultPresentationIsolation>();
                Assert.That(isolation, Is.Not.Null);
                isolation.Apply();
                Assert.That(FindAll<Renderer>(scene).Where(renderer =>
                    !renderer.transform.IsChildOf(bindings.ResultRoot.transform)).All(renderer => !renderer.enabled),
                    Is.True);
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.fieldOfView = 76f;
                camera.aspect = 16f / 9f;
                camera.transform.SetPositionAndRotation(
                    bindings.ResultViewPose.TransformPoint(new Vector3(0f, 2.4f, 0f)),
                    bindings.ResultViewPose.rotation);

                Rect[] slotRects = bindings.GallerySurfaces
                    .Select(surface => ViewportRect(camera, surface.GetComponent<Renderer>().bounds)).ToArray();
                foreach (Rect rect in slotRects)
                {
                    Assert.That(rect.xMin, Is.GreaterThanOrEqualTo(0.05f));
                    Assert.That(rect.xMax, Is.LessThanOrEqualTo(0.95f));
                    Assert.That(rect.yMin, Is.GreaterThanOrEqualTo(0.15f));
                    Assert.That(rect.yMax, Is.LessThanOrEqualTo(0.88f));
                }
                for (int index = 1; index < slotRects.Length; index++)
                    Assert.That(slotRects[index - 1].xMax + 0.025f, Is.LessThan(slotRects[index].xMin));

                TextMesh[] slotLabels = FindAll<TextMesh>(scene)
                    .Where(label => label.transform.IsChildOf(bindings.ResultRoot.transform)
                        && label.text.StartsWith("PLAYER ", StringComparison.Ordinal)).ToArray();
                Assert.That(slotLabels, Has.Length.EqualTo(PartyRoster.Capacity - 1));
                Assert.That(slotLabels.All(label => label.characterSize >= 0.024f), Is.True);
                string expectedSubtitle = mode == PartyMode.MemoryCopy
                    ? "MEMORY COPY · 5 SECOND LOOK" : "RELAY COPY · PRIVATE HANDOFF";
                Assert.That(bindings.ResultRoot.GetComponentsInChildren<TextMesh>(true)
                    .Count(label => label.text == expectedSubtitle), Is.EqualTo(1));

                GameObject returnObject = Find(scene, "ReturnToLobby_ResultOnly");
                Assert.That(returnObject.transform.IsChildOf(bindings.ResultRoot.transform), Is.True);
                Assert.That(returnObject.GetComponent<Collider>(), Is.Not.Null);
                Assert.That(returnObject.GetComponent<WorldActionInteractable>().Action,
                    Is.EqualTo(PartyWorldAction.ReturnToLobby));
                TextMesh returnLabel = bindings.ResultRoot.GetComponentsInChildren<TextMesh>(true)
                    .Single(label => label.text == "HOST · RETURN TO LOBBY");
                Assert.That(returnLabel.characterSize, Is.GreaterThanOrEqualTo(0.022f));
                Assert.That(returnLabel.GetComponent<WorldLabelBillboard>(), Is.Not.Null);
                Transform returnSign = bindings.ResultRoot.transform.Find("ReturnToLobby_PlayerFacingSign");
                Assert.That(returnSign, Is.Not.Null);
                Assert.That(returnSign.GetComponent<Collider>(), Is.Null);
                Vector3 returnViewport = camera.WorldToViewportPoint(returnObject.GetComponent<Renderer>().bounds.center);
                Assert.That(returnViewport.z, Is.GreaterThan(0f));
                Assert.That(returnViewport.x, Is.InRange(0.2f, 0.8f));
                Assert.That(returnViewport.y, Is.InRange(0.08f, 0.4f));
            }
            finally
            {
                bindings.ResultRoot.SetActive(wasActive);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void CoopMuralHasFourPublicLayersAndNoPrivateReferenceOrGallery()
        {
            PartySceneCatalog.TryGet(PartyMode.CoopMural, out PartySceneDefinition definition);
            Scene scene = EditorSceneManager.OpenScene(definition.ScenePath, OpenSceneMode.Single);
            PartySceneBindings bindings = FindAll<PartyGameSceneAdapter>(scene).Single().Bindings;
            Assert.That(bindings.MuralLayerRoots, Has.Length.EqualTo(PartyRoster.Capacity));
            Assert.That(Find(scene, "AuthorizedReferenceSurface"), Is.Null);
            Assert.That(Find(scene, "ResultGalleryRoot"), Is.Null);
            Assert.That(Find(scene, "MuralFinalDisplay").activeSelf, Is.False);
        }

        [Test]
        public void AdapterRejectsDuplicateEventSystemInGameScene()
        {
            PartySceneCatalog.TryGet(PartyMode.MemoryCopy, out PartySceneDefinition definition);
            Scene scene = EditorSceneManager.OpenScene(definition.ScenePath, OpenSceneMode.Single);
            PartyGameSceneAdapter adapter = FindAll<PartyGameSceneAdapter>(scene).Single();
            GameObject duplicate = new GameObject("InjectedEventSystem", typeof(EventSystem));
            duplicate.transform.SetParent(adapter.Bindings.SceneRoot.transform, false);
            try
            {
                Assert.That(adapter.ValidateBindings(out string error), Is.False);
                StringAssert.Contains("EventSystem", error);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(duplicate);
            }
        }

        [Test]
        public void AdapterRejectsMissingPrivateReferenceSurface()
        {
            PartySceneCatalog.TryGet(PartyMode.RelayCopy, out PartySceneDefinition definition);
            Scene scene = EditorSceneManager.OpenScene(definition.ScenePath, OpenSceneMode.Single);
            PartyGameSceneAdapter adapter = FindAll<PartyGameSceneAdapter>(scene).Single();
            CanvasSurface original = adapter.Bindings.ReferenceSurface;
            try
            {
                adapter.Bindings.ReferenceSurface = null;
                Assert.That(adapter.ValidateBindings(out string error), Is.False);
                StringAssert.Contains("referenceSurface", error);
            }
            finally
            {
                adapter.Bindings.ReferenceSurface = original;
            }
        }

        [Test]
        public void LobbyValidatorRejectsInvalidBuildOrderAndRestoresOriginalSceneSetup()
        {
            EditorBuildSettingsScene[] originalBuildSettings = EditorBuildSettings.scenes;
            Scene relay = EditorSceneManager.OpenScene(PartySceneCatalog.BuildScenePaths[1], OpenSceneMode.Single);
            Scene memory = EditorSceneManager.OpenScene(PartySceneCatalog.BuildScenePaths[2], OpenSceneMode.Additive);
            SceneManager.SetActiveScene(memory);
            string expectedActivePath = memory.path;
            SceneSetup[] expectedSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                EditorBuildSettingsScene[] invalid = originalBuildSettings.ToArray();
                invalid[0] = new EditorBuildSettingsScene(PartySceneCatalog.LobbyScenePath, false);
                EditorBuildSettings.scenes = invalid;

                Assert.That(InvokeLobbyValidator(out string error), Is.False);
                StringAssert.Contains("Build Settings index 0", error);
                AssertSceneSetup(expectedSetup, EditorSceneManager.GetSceneManagerSetup());
                Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(expectedActivePath));
                Assert.That(SceneManager.GetSceneByPath(PartySceneCatalog.BuildScenePaths[1]).isLoaded, Is.True);
            }
            finally
            {
                EditorBuildSettings.scenes = originalBuildSettings;
            }
        }

        [Test]
        public void GameValidatorRestoresOriginalSceneSetupWhenInjectedSceneFails()
        {
            const string failingPath = "Assets/_CameraCoop/Scenes/__PartyValidatorFailureTest.unity";
            Scene failing = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("MissingAdapterRoot");
            Assert.That(EditorSceneManager.SaveScene(failing, failingPath), Is.True);

            Scene lobby = EditorSceneManager.OpenScene(PartySceneCatalog.LobbyScenePath, OpenSceneMode.Single);
            Scene relay = EditorSceneManager.OpenScene(PartySceneCatalog.BuildScenePaths[1], OpenSceneMode.Additive);
            SceneManager.SetActiveScene(relay);
            string expectedActivePath = relay.path;
            SceneSetup[] expectedSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                Assert.That(InvokeGameValidator(failingPath, PartyMode.RelayCopy, out string error), Is.False);
                StringAssert.Contains("exactly one PartyGameSceneAdapter", error);
                AssertSceneSetup(expectedSetup, EditorSceneManager.GetSceneManagerSetup());
                Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(expectedActivePath));
                Assert.That(SceneManager.GetSceneByPath(PartySceneCatalog.LobbyScenePath).isLoaded, Is.True);
            }
            finally
            {
                AssetDatabase.DeleteAsset(failingPath);
            }
        }

        [Test]
        public void GameValidatorEmptyStartupSetupLeavesOnlyLobbyLoadedAndActive()
        {
            EditorSceneManager.OpenScene(PartySceneCatalog.BuildScenePaths[1], OpenSceneMode.Single);
            Type type = Type.GetType("CameraCoop.EditorTools.PartyGameSceneValidator, Assembly-CSharp-Editor", true);
            MethodInfo method = type.GetMethod("RestoreOriginalSetupOrLobby",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null, "Validator must expose its empty-startup restoration boundary.");
            method.Invoke(null, new object[] { Array.Empty<SceneSetup>() });

            Scene active = SceneManager.GetActiveScene();
            Assert.That(active.path, Is.EqualTo(PartySceneCatalog.LobbyScenePath));
            Assert.That(active.isLoaded, Is.True);
            Assert.That(EditorSceneManager.GetSceneManagerSetup().Count(item => item.isLoaded), Is.EqualTo(1));
            Assert.That(active.isDirty, Is.False);
        }

        private static void AssertSceneSetup(SceneSetup[] expected, SceneSetup[] actual)
        {
            Assert.That(actual.Select(item => (item.path, item.isLoaded, item.isActive)),
                Is.EqualTo(expected.Select(item => (item.path, item.isLoaded, item.isActive))));
        }

        private static Rect ViewportRect(Camera camera, Bounds bounds)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            Vector3[] corners =
            {
                new Vector3(min.x, min.y, min.z), new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z), new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z)
            };
            Vector3 first = camera.WorldToViewportPoint(corners[0]);
            float xMin = first.x;
            float xMax = first.x;
            float yMin = first.y;
            float yMax = first.y;
            foreach (Vector3 corner in corners.Skip(1))
            {
                Vector3 point = camera.WorldToViewportPoint(corner);
                Assert.That(point.z, Is.GreaterThan(0f));
                xMin = Mathf.Min(xMin, point.x);
                xMax = Mathf.Max(xMax, point.x);
                yMin = Mathf.Min(yMin, point.y);
                yMax = Mathf.Max(yMax, point.y);
            }
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static bool InvokeLobbyValidator(out string error)
        {
            Type type = Type.GetType("CameraCoop.EditorTools.RelayQuizOnlineSceneValidator, Assembly-CSharp-Editor", true);
            object[] arguments = { null };
            bool result = (bool)type.GetMethod("TryValidate").Invoke(null, arguments);
            error = (string)arguments[0];
            return result;
        }

        private static bool InvokeGameValidator(string scenePath, PartyMode mode, out string error)
        {
            Type type = Type.GetType("CameraCoop.EditorTools.PartyGameSceneValidator, Assembly-CSharp-Editor", true);
            object[] arguments = { scenePath, mode, null };
            bool result = (bool)type.GetMethod("TryValidateScene").Invoke(null, arguments);
            error = (string)arguments[2];
            return result;
        }
        private static T[] FindAll<T>(Scene scene) where T : Component
        {
            return Resources.FindObjectsOfTypeAll<T>()
                .Where(item => item != null && item.gameObject.scene == scene).ToArray();
        }

        private static GameObject Find(Scene scene, string name)
        {
            Transform transform = FindAll<Transform>(scene).FirstOrDefault(item => item.name == name);
            return transform != null ? transform.gameObject : null;
        }
    }
}
