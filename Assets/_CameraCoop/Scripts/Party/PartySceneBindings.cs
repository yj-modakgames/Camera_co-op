using UnityEngine;

namespace CameraCoop.Party
{
    [System.Serializable]
    public sealed class PartySceneBindings
    {
        [SerializeField] private PartyMode mode;
        [SerializeField] private GameObject sceneRoot;
        [SerializeField] private Transform[] slotSpawns;
        [SerializeField] private BoxCollider[] slotZones;
        [SerializeField] private Transform[] slotDocks;
        [SerializeField] private Transform carryAnchor;
        [SerializeField] private WorldActionInteractable[] actions;
        [SerializeField] private GameObject[] avatarRoots;
        [SerializeField] private RemoteAvatarPresenter[] avatarPresenters;
        [SerializeField] private GameObject writablePaperRoot;
        [SerializeField] private CanvasSurface writableSurface;
        [SerializeField] private CanvasDrawingPresenter referencePresenter;
        [SerializeField] private CanvasSurface referenceSurface;
        [SerializeField] private GameObject resultRoot;
        [SerializeField] private CanvasDrawingPresenter galleryPresenter;
        [SerializeField] private CanvasSurface gallerySurface;
        [SerializeField] private Transform toolRack;
        [SerializeField] private PhysicalPaintTool physicalPaintTool;
        [SerializeField] private PhysicalBrush[] brushes;
        [SerializeField] private HandInteractable[] toolStations;
        [SerializeField] private GameObject[] muralLayerRoots;
        [SerializeField] private CanvasDrawingPresenter[] muralLayerPresenters;
        [SerializeField] private CanvasSurface[] muralLayerSurfaces;

        public PartyMode Mode { get => mode; set => mode = value; }
        public GameObject SceneRoot { get => sceneRoot; set => sceneRoot = value; }
        public Transform[] SlotSpawns { get => slotSpawns; set => slotSpawns = value; }
        public BoxCollider[] SlotZones { get => slotZones; set => slotZones = value; }
        public Transform[] SlotDocks { get => slotDocks; set => slotDocks = value; }
        public Transform CarryAnchor { get => carryAnchor; set => carryAnchor = value; }
        public WorldActionInteractable[] Actions { get => actions; set => actions = value; }
        public GameObject[] AvatarRoots { get => avatarRoots; set => avatarRoots = value; }
        public RemoteAvatarPresenter[] AvatarPresenters { get => avatarPresenters; set => avatarPresenters = value; }
        public GameObject WritablePaperRoot { get => writablePaperRoot; set => writablePaperRoot = value; }
        public CanvasSurface WritableSurface { get => writableSurface; set => writableSurface = value; }
        public CanvasDrawingPresenter ReferencePresenter { get => referencePresenter; set => referencePresenter = value; }
        public CanvasSurface ReferenceSurface { get => referenceSurface; set => referenceSurface = value; }
        public GameObject ResultRoot { get => resultRoot; set => resultRoot = value; }
        public CanvasDrawingPresenter GalleryPresenter { get => galleryPresenter; set => galleryPresenter = value; }
        public CanvasSurface GallerySurface { get => gallerySurface; set => gallerySurface = value; }
        public Transform ToolRack { get => toolRack; set => toolRack = value; }
        public PhysicalPaintTool PhysicalPaintTool { get => physicalPaintTool; set => physicalPaintTool = value; }
        public PhysicalBrush[] Brushes { get => brushes; set => brushes = value; }
        public HandInteractable[] ToolStations { get => toolStations; set => toolStations = value; }
        public GameObject[] MuralLayerRoots { get => muralLayerRoots; set => muralLayerRoots = value; }
        public CanvasDrawingPresenter[] MuralLayerPresenters { get => muralLayerPresenters; set => muralLayerPresenters = value; }
        public CanvasSurface[] MuralLayerSurfaces { get => muralLayerSurfaces; set => muralLayerSurfaces = value; }
    }
}
