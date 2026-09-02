using System;
using System.Collections.Generic;
using System.Linq;
using CameraCoop.Party;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CameraCoop.EditorTools
{
    public static partial class PartyGameSceneBuilder
    {
        private static GameObject BuildWritablePaper(Transform root, PartyMode mode, Palette palette,
            out CanvasSurface surface, out HandCanvasInteractable interactable)
        {
            Transform paperRoot = Group("AuthorizedLocalPaper", root);
            Vector3 position = mode == PartyMode.CoopMural ? new Vector3(0f, 1.9f, 4.8f)
                : new Vector3(0f, 1.9f, 2.4f);
            GameObject paper = Quad("WritablePaperSurface", paperRoot, position,
                new Vector3(4.8f, 3f, 1f), palette.Paper, Quaternion.Euler(0f, 180f, 0f));
            surface = paper.AddComponent<CanvasSurface>();
            interactable = paper.AddComponent<HandCanvasInteractable>();
            SetField(interactable, "canvasSurface", surface);
            FrameAt(paperRoot, "WritablePaperFrame", position + new Vector3(0f, 0f, 0.1f),
                new Vector2(5.05f, 3.25f), palette.Accent, Quaternion.Euler(0f, 180f, 0f));
            Label(mode == PartyMode.CoopMural ? "ACTIVE OWNER LAYER" : "YOUR PRIVATE PAPER",
                paperRoot, position + new Vector3(0f, 2f, 0f), 0.28f, Color.white);
            paperRoot.gameObject.SetActive(false);
            return paperRoot.gameObject;
        }

        private static void BuildRemotePaperShells(Transform root, PartyMode mode, Palette palette)
        {
            if (mode == PartyMode.CoopMural) return;
            Transform shells = Group("RemoteBlankPaperShells", root);
            for (int slot = 1; slot < PartyRoster.Capacity; slot++)
            {
                float x = -6f + (slot - 1) * 6f;
                Transform shell = Group("RemotePaperShell_" + slot, shells);
                Quad("BlankGeometryOnly", shell, new Vector3(x, 1.4f, 6.8f),
                    new Vector3(3.5f, 2.1f, 1f), palette.Paper, Quaternion.Euler(0f, 180f, 0f));
                Label("PLAYER " + (slot + 1) + " · PRIVATE", shell,
                    new Vector3(x, 2.8f, 6.7f), 0.2f, Color.white);
            }
        }

        private static void BuildTools(Transform root, Palette palette, out Transform rack,
            out PhysicalPaintTool paintTool, out PhysicalBrush[] brushes, out HandInteractable rackStation)
        {
            Transform tools = Group("DrawingTools", root);
            paintTool = tools.gameObject.AddComponent<PhysicalPaintTool>();
            paintTool.MaxInteractionDistance = 12f;
            GameObject rackObject = Cube("BrushRack", tools, new Vector3(-8.8f, 0.55f, -6.4f),
                new Vector3(3.4f, 1.1f, 1.1f), palette.Dark);
            rack = rackObject.transform;
            PhysicalToolStation rackComponent = rackObject.AddComponent<PhysicalToolStation>();
            rackComponent.SetConfiguration(paintTool, PhysicalToolStation.StationKind.Rack, 0);
            rackStation = rackComponent;

            GameObject brushObject = Cylinder("PhysicalBrush", tools, new Vector3(-8.8f, 1.5f, -6.4f),
                new Vector3(0.13f, 0.75f, 0.13f), palette.Red);
            brushObject.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            PhysicalBrush brush = brushObject.AddComponent<PhysicalBrush>();
            paintTool.RegisterBrush(brush);
            brushes = new[] { brush };
            paintTool.SetDockAnchor(rack);

            Material[] paints = { palette.Red, palette.Blue, palette.Green, palette.Yellow };
            for (int index = 0; index < paints.Length; index++)
            {
                GameObject station = Cylinder("PaintStation_" + index, tools,
                    new Vector3(-5.7f + index * 1.15f, 0.35f, -6.4f), new Vector3(0.48f, 0.3f, 0.48f), paints[index]);
                station.AddComponent<PhysicalToolStation>()
                    .SetConfigurationAndReturn(paintTool, PhysicalToolStation.StationKind.Paint, index);
            }
            for (int index = 0; index < 3; index++)
            {
                GameObject station = Cube("WidthStation_" + index, tools,
                    new Vector3(0f + index * 1.2f, 0.35f, -6.4f), new Vector3(0.9f, 0.7f, 0.9f), palette.Accent);
                PhysicalToolStation component = station.AddComponent<PhysicalToolStation>();
                component.SetConfiguration(paintTool, PhysicalToolStation.StationKind.Width, index);
                Label(index == 0 ? "THIN" : index == 1 ? "MID" : "WIDE", station.transform,
                    new Vector3(0f, 0.7f, 0f), 0.17f, Color.white, true);
            }
            GameObject eraser = Cube("EraserStation", tools, new Vector3(4.3f, 0.35f, -6.4f),
                new Vector3(1.25f, 0.7f, 0.9f), palette.Paper);
            PhysicalToolStation eraserComponent = eraser.AddComponent<PhysicalToolStation>();
            eraserComponent.SetConfiguration(paintTool, PhysicalToolStation.StationKind.Eraser, 0);
            Label("ERASER", eraser.transform, new Vector3(0f, 0.7f, 0f), 0.17f, Color.black, true);
            Label("PICK UP BRUSH · PAINT · WIDTH · ERASER", tools,
                new Vector3(-2f, 1.55f, -6.4f), 0.25f, Color.white);
        }

        private static WorldActionInteractable BuildPrivateModePresentation(Transform root, PartyMode mode, Palette palette,
            PartySceneBindings bindings)
        {
            Transform reference = Group(mode == PartyMode.MemoryCopy ? "FiveSecondObservationPedestal" : "ContinuousReferencePedestal", root);
            Vector3 referencePosition = mode == PartyMode.MemoryCopy ? new Vector3(-6.5f, 2f, 1.4f) : new Vector3(-7.5f, 2f, 2f);
            GameObject referenceSurfaceObject = Quad("AuthorizedReferenceSurface", reference, referencePosition,
                new Vector3(4.2f, 2.6f, 1f), palette.Paper, Quaternion.Euler(0f, 180f, 0f));
            bindings.ReferenceSurface = referenceSurfaceObject.AddComponent<CanvasSurface>();
            referenceSurfaceObject.SetActive(false);
            bindings.ReferencePresenter = Presenter("AuthorizedReferencePresenter", reference, palette);
            Label(mode == PartyMode.MemoryCopy ? "LOOK · HIDES AT 5.0s" : "REFERENCE · ACTIVE SLOT ONLY",
                reference, referencePosition + new Vector3(0f, 1.75f, 0f), 0.27f, Color.white);

            GameObject result = new GameObject("ResultGalleryRoot");
            result.transform.SetParent(root, false);
            result.AddComponent<ResultPresentationIsolation>();
            result.SetActive(false);
            bindings.GalleryRoots = new GameObject[PartyRoster.Capacity - 1];
            bindings.GalleryPresenters = new CanvasDrawingPresenter[PartyRoster.Capacity - 1];
            bindings.GallerySurfaces = new CanvasSurface[PartyRoster.Capacity - 1];
            Cube("ResultBackdrop", result.transform, new Vector3(0f, 2.35f, 3.55f),
                new Vector3(11.5f, 5.15f, 0.2f), palette.Dark);
            for (int index = 0; index < PartyRoster.Capacity - 1; index++)
            {
                Vector3 galleryPosition = new Vector3(-3.2f + index * 3.2f, 2.2f, 3.3f);
                GameObject frame = new GameObject("ReadOnlyResultSlot_" + index);
                frame.transform.SetParent(result.transform, false);
                GameObject surfaceObject = Quad("ReadOnlyResultSurface_" + index, frame.transform, galleryPosition,
                    new Vector3(2.6f, 3f, 1f), palette.Paper, Quaternion.Euler(0f, 180f, 0f));
                Label("PLAYER " + (index + 1) + " RESULT", frame.transform,
                    galleryPosition + new Vector3(0f, 1.85f, -0.15f), 0.34f, Color.white);
                bindings.GalleryRoots[index] = frame;
                bindings.GallerySurfaces[index] = surfaceObject.AddComponent<CanvasSurface>();
                bindings.GalleryPresenters[index] = Presenter("ResultGalleryPresenter_" + index, frame.transform, palette);
            }
            Label("FINAL RESULT GALLERY", result.transform, new Vector3(0f, 4.75f, 3.25f), 0.42f, Color.white);
            Label(mode == PartyMode.MemoryCopy ? "MEMORY COPY · 5 SECOND LOOK" : "RELAY COPY · PRIVATE HANDOFF",
                result.transform, new Vector3(0f, 4.28f, 3.2f), 0.27f, Color.white);
            bindings.ResultViewPose = Marker("ResultViewPose", result.transform,
                new Vector3(0f, 0f, -1.5f), 0f);
            GameObject returnObject = Cube("ReturnToLobby_ResultOnly", result.transform,
                new Vector3(0f, 0.05f, 2.1f), new Vector3(3.4f, 0.4f, 1f), palette.Yellow);
            WorldActionInteractable returnAction = returnObject.AddComponent<WorldActionInteractable>();
            SetField(returnAction, "action", PartyWorldAction.ReturnToLobby);
            GameObject returnSign = Cube("ReturnToLobby_PlayerFacingSign", result.transform,
                new Vector3(0f, 0.55f, 2.2f), new Vector3(4.2f, 0.5f, 0.12f), palette.Yellow);
            UnityEngine.Object.DestroyImmediate(returnSign.GetComponent<Collider>());
            Label("HOST · RETURN TO LOBBY", result.transform, new Vector3(0f, 0.55f, 2.1f), 0.28f, Color.black);
            bindings.ResultRoot = result;
            return returnAction;
        }

        private static WorldActionInteractable BuildMural(Transform root, Palette palette, PartySceneBindings bindings)
        {
            Transform board = Group("PublicMuralBoard", root);
            Cube("MuralBack", board, new Vector3(0f, 2.3f, 7.8f), new Vector3(11f, 4.7f, 0.3f), palette.Dark);
            Quad("PublicBoardSurface", board, new Vector3(0f, 2.3f, 7.6f),
                new Vector3(10.4f, 4.2f, 1f), palette.Paper, Quaternion.Euler(0f, 180f, 0f));
            bindings.MuralLayerRoots = new GameObject[PartyRoster.Capacity];
            bindings.MuralLayerPresenters = new CanvasDrawingPresenter[PartyRoster.Capacity];
            bindings.MuralLayerSurfaces = new CanvasSurface[PartyRoster.Capacity];
            for (int slot = 0; slot < PartyRoster.Capacity; slot++)
            {
                GameObject layer = new GameObject("PublicOwnerLayer_" + slot);
                layer.transform.SetParent(board, false);
                layer.transform.position = new Vector3(0f, 2.3f, 7.55f - slot * 0.01f);
                layer.transform.localScale = new Vector3(10.4f, 4.2f, 1f);
                CanvasSurface surface = layer.AddComponent<CanvasSurface>();
                CanvasDrawingPresenter presenter = layer.AddComponent<CanvasDrawingPresenter>();
                ConfigurePresenter(presenter, palette);
                bindings.MuralLayerRoots[slot] = layer;
                bindings.MuralLayerPresenters[slot] = presenter;
                bindings.MuralLayerSurfaces[slot] = surface;
            }
            Label("SEQUENTIAL OWNER: 1 → 2 → 3 → 4", board,
                new Vector3(0f, 5.2f, 7.45f), 0.3f, Color.white);
            Transform indicators = Group("ActiveSlotIndicators", board);
            for (int slot = 0; slot < PartyRoster.Capacity; slot++)
                Cube("ActiveSlotIndicator_" + slot, indicators,
                    new Vector3(-3.6f + slot * 2.4f, 0.25f, 7.2f), new Vector3(1.5f, 0.35f, 0.6f),
                    slot == 0 ? palette.Red : slot == 1 ? palette.Blue : slot == 2 ? palette.Green : palette.Yellow);

            GameObject final = new GameObject("MuralFinalDisplay");
            final.transform.SetParent(root, false);
            final.SetActive(false);
            GameObject returnObject = Cube("ReturnToLobby_ResultOnly", final.transform,
                new Vector3(0f, 0.55f, -1f), new Vector3(3.5f, 0.9f, 1.2f), palette.Yellow);
            WorldActionInteractable returnAction = returnObject.AddComponent<WorldActionInteractable>();
            SetField(returnAction, "action", PartyWorldAction.ReturnToLobby);
            Label("HOST · RETURN TO LOBBY", returnObject.transform, new Vector3(0f, 0.75f, 0f), 0.2f, Color.black, true);
            bindings.ResultRoot = final;
            return returnAction;
        }
    }
}
