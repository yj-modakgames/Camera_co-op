using System;
using UnityEngine;

namespace CameraCoop.Party
{
    public sealed class PartyLobbyScenePort : MonoBehaviour
    {
        [SerializeField] private GameObject lobbyWorldRoot;
        [SerializeField] private Transform[] slotSpawns;
        [SerializeField] private GameObject[] practiceLayerRoots;
        [SerializeField] private CanvasDrawingPresenter[] practiceLayerPresenters;
        [SerializeField] private CanvasSurface[] practiceLayerSurfaces;
        [SerializeField] private GameObject[] avatarRoots;
        [SerializeField] private RemoteAvatarPresenter[] avatarPresenters;

        public GameObject LobbyWorldRoot => lobbyWorldRoot;
        public Transform[] SlotSpawns => slotSpawns;
        public GameObject[] PracticeLayerRoots => practiceLayerRoots;
        public CanvasDrawingPresenter[] PracticeLayerPresenters => practiceLayerPresenters;
        public CanvasSurface[] PracticeLayerSurfaces => practiceLayerSurfaces;
        public GameObject[] AvatarRoots => avatarRoots;
        public RemoteAvatarPresenter[] AvatarPresenters => avatarPresenters;

        public void Configure(GameObject worldRoot, Transform[] spawns, GameObject[] layerRoots,
            CanvasDrawingPresenter[] layerPresenters, CanvasSurface[] layerSurfaces,
            GameObject[] lobbyAvatarRoots, RemoteAvatarPresenter[] lobbyAvatarPresenters)
        {
            lobbyWorldRoot = worldRoot ?? throw new ArgumentNullException(nameof(worldRoot));
            slotSpawns = Clone(spawns, nameof(spawns));
            practiceLayerRoots = Clone(layerRoots, nameof(layerRoots));
            practiceLayerPresenters = Clone(layerPresenters, nameof(layerPresenters));
            practiceLayerSurfaces = Clone(layerSurfaces, nameof(layerSurfaces));
            avatarRoots = Clone(lobbyAvatarRoots, nameof(lobbyAvatarRoots));
            avatarPresenters = Clone(lobbyAvatarPresenters, nameof(lobbyAvatarPresenters));
        }

        public bool ValidateBindings(out string error)
        {
            if (lobbyWorldRoot == null) return Fail("lobbyWorldRoot is required.", out error);
            if (lobbyWorldRoot.scene != gameObject.scene) return Fail("lobbyWorldRoot must belong to the bootstrap Scene.", out error);
            if (!Validate(slotSpawns, "slotSpawns", 4, out error)) return false;
            if (!Validate(practiceLayerRoots, "practiceLayerRoots", 4, out error)) return false;
            if (!Validate(practiceLayerPresenters, "practiceLayerPresenters", 4, out error)) return false;
            if (!Validate(practiceLayerSurfaces, "practiceLayerSurfaces", 4, out error)) return false;
            if (!Validate(avatarRoots, "avatarRoots", 4, out error)) return false;
            if (!Validate(avatarPresenters, "avatarPresenters", 3, out error)) return false;
            error = string.Empty;
            return true;
        }

        public void SetLobbyVisible(bool visible)
        {
            if (!ValidateBindings(out string error)) throw new InvalidOperationException(error);
            lobbyWorldRoot.SetActive(visible);
        }

        private bool Validate<T>(T[] values, string name, int count, out string error) where T : UnityEngine.Object
        {
            if (values == null || values.Length != count) return Fail(name + "[" + count + "] is required.", out error);
            for (int index = 0; index < values.Length; index++)
            {
                UnityEngine.Object value = values[index];
                if (value == null) return Fail(name + "[" + index + "] is required.", out error);
                GameObject item = value is GameObject go ? go : ((Component)value).gameObject;
                if (item.scene != lobbyWorldRoot.scene || !IsInside(lobbyWorldRoot.transform, item.transform))
                    return Fail(name + "[" + index + "] must belong to lobbyWorldRoot.", out error);
            }
            error = string.Empty;
            return true;
        }

        private static T[] Clone<T>(T[] values, string name) => values != null ? (T[])values.Clone() : throw new ArgumentNullException(name);
        private static bool IsInside(Transform root, Transform value) => value == root || value.IsChildOf(root);
        private static bool Fail(string value, out string error) { error = value; return false; }
    }
}
