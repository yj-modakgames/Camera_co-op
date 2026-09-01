using System;
using UnityEngine;

namespace CameraCoop.Party
{
    public sealed class PartyLobbyScenePort : MonoBehaviour
    {
        [SerializeField] private GameObject lobbyWorldRoot;
        [SerializeField] private Transform[] slotSpawns;

        public GameObject LobbyWorldRoot => lobbyWorldRoot;
        public Transform[] SlotSpawns => slotSpawns;

        public void Configure(GameObject worldRoot, Transform[] spawns)
        {
            lobbyWorldRoot = worldRoot != null ? worldRoot : throw new ArgumentNullException(nameof(worldRoot));
            slotSpawns = spawns != null ? (Transform[])spawns.Clone() : throw new ArgumentNullException(nameof(spawns));
        }

        public bool ValidateBindings(out string error)
        {
            if (lobbyWorldRoot == null) return Fail("lobbyWorldRoot is required.", out error);
            if (lobbyWorldRoot.scene != gameObject.scene)
                return Fail("lobbyWorldRoot must belong to the bootstrap Scene.", out error);
            if (slotSpawns == null || slotSpawns.Length != PartyRoster.Capacity)
                return Fail("slotSpawns[4] is required.", out error);
            for (int slot = 0; slot < slotSpawns.Length; slot++)
            {
                Transform spawn = slotSpawns[slot];
                if (spawn == null) return Fail("slotSpawns[" + slot + "] is required.", out error);
                if (spawn.gameObject.scene != lobbyWorldRoot.scene || !IsInside(lobbyWorldRoot.transform, spawn))
                    return Fail("slotSpawns[" + slot + "] must belong to lobbyWorldRoot.", out error);
            }
            error = string.Empty;
            return true;
        }

        public void SetLobbyVisible(bool visible)
        {
            if (!ValidateBindings(out string error)) throw new InvalidOperationException(error);
            lobbyWorldRoot.SetActive(visible);
        }

        private static bool IsInside(Transform root, Transform value)
        {
            return value == root || value.IsChildOf(root);
        }

        private static bool Fail(string value, out string error)
        {
            error = value;
            return false;
        }
    }
}
