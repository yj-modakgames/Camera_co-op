using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CameraCoop.Party;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CameraCoop.EditorTools
{
    public static partial class RelayQuizOnlineSceneBuilder
    {
        private static PresentationLayout BuildPresentation(Context context, CoreReferences core)
        {
            var layout = new PresentationLayout();
            Transform reference = Group("ReferenceHowToPanel", core.WorldRoot.transform);
            Cube("ReferencePanelBack", reference, new Vector3(-13.72f, 2f, -0.5f), new Vector3(0.12f, 3.2f, 6.2f), context.Dark);
            Label("REFERENCE / HOW TO", reference, new Vector3(-13.5f, 3.25f, -0.5f), 0.34f, Color.white, false,
                Quaternion.Euler(0f, 90f, 0f));
            GameObject previewSurfaceObject = Quad("ReferenceSurface", reference, new Vector3(-13.58f, 2f, -0.8f),
                new Vector3(4.2f, 2.5f, 1f), context.Paper, Quaternion.Euler(0f, 90f, 0f));
            layout.PreviewSurface = previewSurfaceObject.AddComponent<CanvasSurface>();
            GameObject previewPresenterObject = new GameObject("ReferenceDrawingPresenter");
            previewPresenterObject.transform.SetParent(reference, false);
            layout.PreviewPresenter = previewPresenterObject.AddComponent<CanvasDrawingPresenter>();
            ConfigurePresenter(layout.PreviewPresenter, context);
            Label("Pinch release: select   Fist: draw   Open hand: rearm", reference,
                new Vector3(-13.48f, 0.6f, -0.5f), 0.18f, Color.white, false, Quaternion.Euler(0f, 90f, 0f));

            Transform mural = Group("CoopMuralBoard", core.WorldRoot.transform);
            Cube("MuralBack", mural, new Vector3(13.72f, 2f, -0.2f), new Vector3(0.12f, 3.7f, 7.4f), context.Dark);
            GameObject muralSurfaceObject = Quad("CoopMuralSurface", mural, new Vector3(13.57f, 2f, -0.2f),
                new Vector3(6.4f, 3.2f, 1f), context.Paper, Quaternion.Euler(0f, -90f, 0f));
            CanvasSurface muralSurface = muralSurfaceObject.AddComponent<CanvasSurface>();
            layout.MuralRoots = new GameObject[4];
            layout.MuralPresenters = new CanvasDrawingPresenter[4];
            layout.MuralSurfaces = new CanvasSurface[4];
            for (int slot = 0; slot < 4; slot++)
            {
                GameObject layer = new GameObject("MuralPresenterLayer_" + slot);
                layer.transform.SetParent(mural, false);
                CanvasDrawingPresenter presenter = layer.AddComponent<CanvasDrawingPresenter>();
                ConfigurePresenter(presenter, context);
                layout.MuralRoots[slot] = layer;
                layout.MuralPresenters[slot] = presenter;
                layout.MuralSurfaces[slot] = muralSurface;
            }
            Label("COOP MURAL · PUBLIC LAYERS", mural, new Vector3(13.48f, 3.45f, -0.2f), 0.34f,
                Color.white, false, Quaternion.Euler(0f, -90f, 0f));

            Transform galleryRoot = Group("ReadOnlyGallery", core.WorldRoot.transform);
            layout.GalleryRoots = new GameObject[3];
            layout.GalleryPresenters = new CanvasDrawingPresenter[3];
            layout.GallerySurfaces = new CanvasSurface[3];
            float[] xs = { -3f, 0f, 3f };
            for (int index = 0; index < 3; index++)
            {
                GameObject frame = new GameObject("GalleryFrame_" + index);
                frame.transform.SetParent(galleryRoot, false);
                GameObject surfaceObject = Quad("GallerySurface_" + index, frame.transform,
                    new Vector3(xs[index], 2f, -7.72f), new Vector3(2.45f, 1.65f, 1f), context.Paper,
                    Quaternion.Euler(0f, 180f, 0f));
                layout.GallerySurfaces[index] = surfaceObject.AddComponent<CanvasSurface>();
                layout.GalleryPresenters[index] = frame.AddComponent<CanvasDrawingPresenter>();
                ConfigurePresenter(layout.GalleryPresenters[index], context);
                FrameAt(frame.transform, "GalleryBorder_" + index, new Vector3(xs[index], 2f, -7.6f),
                    new Vector2(2.7f, 1.9f), index == 0 ? context.Red : index == 1 ? context.Blue : context.Green,
                    Quaternion.Euler(0f, 180f, 0f));
                Label("GALLERY " + (index + 1), frame.transform, new Vector3(xs[index], 3.1f, -7.45f), 0.24f, Color.white,
                    false, Quaternion.Euler(0f, 180f, 0f));
                layout.GalleryRoots[index] = frame;
            }

            Transform qaRoot = Group("QAViewpoints", core.WorldRoot.transform);
            Transform lobby = Marker("QA_Lobby", qaRoot, new Vector3(0f, 2.4f, -7.2f), 0f);
            Transform privateView = Marker("QA_PrivateZone", qaRoot, new Vector3(-9f, 1.65f, 2.35f), 0f);
            Transform galleryView = Marker("QA_Gallery", qaRoot, new Vector3(0f, 2.2f, -4.7f), 180f);
            layout.LobbyPose = lobby;
            layout.GalleryPose = galleryView;
            lobby.gameObject.SetActive(false);
            privateView.gameObject.SetActive(false);
            galleryView.gameObject.SetActive(false);
            return layout;
        }

        private static void FinalizeLobbySplit(Context context, CoreReferences core, PartyLayout party)
        {
            core.WorldRoot.name = "LobbyWorldRoot";
            GameObject lobbyRoot = core.WorldRoot;

            GameObject modeSelector = Find(context.Scene, "ModePedestals");
            if (modeSelector == null) throw new InvalidOperationException("ModePedestals is required.");
            modeSelector.name = "ModeSelectorRoot";
            modeSelector.SetActive(false);

            GameObject tutorial = Find(context.Scene, "ReferenceHowToPanel");
            if (tutorial != null)
            {
                tutorial.name = "GestureTutorialStation";
                CanvasDrawingPresenter presenter = tutorial.GetComponentInChildren<CanvasDrawingPresenter>(true);
                CanvasSurface surface = tutorial.GetComponentInChildren<CanvasSurface>(true);
                if (presenter != null) UnityEngine.Object.DestroyImmediate(presenter.gameObject);
                if (surface != null)
                {
                    GameObject surfaceObject = surface.gameObject;
                    UnityEngine.Object.DestroyImmediate(surface);
                    surfaceObject.name = "GestureTutorialBoard";
                }
                TextMesh heading = tutorial.GetComponentsInChildren<TextMesh>(true)
                    .FirstOrDefault(item => item.text == "REFERENCE / HOW TO");
                if (heading != null) heading.text = "HAND GESTURE TUTORIAL";
            }

            DestroyNamed(context.Scene, "ReadOnlyGallery");
            DestroyNamed(context.Scene, "CoopMuralBoard");
            core.Gallery.Release();
            for (int index = 0; index < 3; index++)
            {
                DestroyNamed(context.Scene, "PrivacyDivider_" + index);
                DestroyNamed(context.Scene, "RemotePaperShell_" + (index + 1));
            }

            BuildLobbyPracticeWall(context, lobbyRoot.transform, out GameObject[] practiceRoots,
                out CanvasDrawingPresenter[] practicePresenters, out CanvasSurface[] practiceSurfaces);
            BuildJumpTutorial(context, lobbyRoot.transform);

            GameObject studio = Find(context.Scene, "Studio");
            if (studio != null) studio.transform.SetParent(lobbyRoot.transform, true);

            GameObject runtimeRoot = new GameObject("RuntimeRoot");
            GameObject onlineRuntime = Find(context.Scene, "OnlinePartyRuntime");
            if (onlineRuntime == null) throw new InvalidOperationException("OnlinePartyRuntime is required.");
            onlineRuntime.transform.SetParent(runtimeRoot.transform, true);

            GameObject[] roots = context.Scene.GetRootGameObjects();
            foreach (GameObject root in roots)
            {
                if (root == lobbyRoot || root == runtimeRoot) continue;
                root.transform.SetParent(runtimeRoot.transform, true);
            }

            PartyLobbyScenePort lobbyPort = onlineRuntime.GetComponent<PartyLobbyScenePort>();
            if (lobbyPort == null) lobbyPort = onlineRuntime.AddComponent<PartyLobbyScenePort>();
            lobbyPort.Configure(lobbyRoot, party.Spawns, practiceRoots, practicePresenters, practiceSurfaces,
                party.AvatarRoots.Select(item => item.gameObject).ToArray(), party.RemotePresenters);

            OnlineRelayQuizController online = onlineRuntime.GetComponent<OnlineRelayQuizController>();
            SetField(online, "lobbyScenePort", lobbyPort);
            SetField(online, "workCanvasRoot", null);
            SetField(online, "previewPresenter", null);
            SetField(online, "previewSurface", null);
        }

        private static void BuildLobbyPracticeWall(Context context, Transform parent, out GameObject[] layerRoots,
            out CanvasDrawingPresenter[] presenters, out CanvasSurface[] surfaces)
        {
            Transform root = Group("PublicPracticeEasels", parent);
            Label("PUBLIC PRACTICE WALL · FOUR LIVE EASELS", root,
                new Vector3(0f, 4.1f, 7.45f), 0.34f, Color.white);
            layerRoots = new GameObject[PartyRoster.Capacity];
            presenters = new CanvasDrawingPresenter[PartyRoster.Capacity];
            surfaces = new CanvasSurface[PartyRoster.Capacity];
            Material[] colors = { context.Red, context.Blue, context.Green, context.Yellow };
            for (int slot = 0; slot < PartyRoster.Capacity; slot++)
            {
                float x = -8.1f + slot * 5.4f;
                GameObject easel = new GameObject("PracticeEasel_" + slot);
                easel.transform.SetParent(root, false);
                GameObject surfaceObject = Quad("PracticeSurface_" + slot, easel.transform,
                    new Vector3(x, 2.25f, 7.55f), new Vector3(4.2f, 2.6f, 1f), context.Paper,
                    Quaternion.Euler(0f, 180f, 0f));
                surfaces[slot] = surfaceObject.AddComponent<CanvasSurface>();
                presenters[slot] = easel.AddComponent<CanvasDrawingPresenter>();
                ConfigurePresenter(presenters[slot], context);
                FrameAt(easel.transform, "PracticeFrame_" + slot, new Vector3(x, 2.25f, 7.42f),
                    new Vector2(4.45f, 2.85f), colors[slot], Quaternion.Euler(0f, 180f, 0f));
                Label("P" + (slot + 1) + " PRACTICE", easel.transform,
                    new Vector3(x, 3.9f, 7.35f), 0.22f, Color.white);
                layerRoots[slot] = easel;
            }
        }

        private static void BuildJumpTutorial(Context context, Transform parent)
        {
            Transform root = Group("JumpObstaclePath", parent);
            Label("MOVE · JUMP · FOLLOW THE STEPPING PATH", root,
                new Vector3(7.7f, 2.4f, -1.2f), 0.23f, Color.white);
            for (int index = 0; index < 6; index++)
            {
                float height = 0.18f + (index % 3) * 0.24f;
                Cube("JumpStep_" + index, root, new Vector3(5.2f + index * 1.15f, height * 0.5f, -0.4f + index * 0.65f),
                    new Vector3(0.9f, height, 0.9f), index % 2 == 0 ? context.Accent : context.Blue);
            }
        }

        private static void DestroyNamed(Scene scene, string name)
        {
            GameObject item = Find(scene, name);
            if (item != null) UnityEngine.Object.DestroyImmediate(item);
        }
    }
}
