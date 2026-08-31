using System.Collections.Generic;
using System.Linq;
using CameraCoop.Party;
using NUnit.Framework;
using UnityEngine;

namespace CameraCoop.Tests
{
    public sealed class PartyLobbyTests
    {
        private const string Host = "steam-host";
        private PartyLobby lobby;

        [SetUp]
        public void SetUp()
        {
            lobby = new PartyLobby("session-a", Host, "Host", PartyTestLayout.Create(), 1f);
        }

        [Test]
        public void RosterUsesFixedSlotsAndRejectsDuplicateAndFifthPlayer()
        {
            Assert.That(lobby.TryJoin("p1", "One", out int slot1, out PartyJoinRejection first), Is.True);
            Assert.That(slot1, Is.EqualTo(1));
            Assert.That(first, Is.EqualTo(PartyJoinRejection.None));
            Assert.That(lobby.TryJoin("p1", "Duplicate", out _, out PartyJoinRejection duplicate), Is.False);
            Assert.That(duplicate, Is.EqualTo(PartyJoinRejection.DuplicateIdentity));
            JoinRemainingPlayers();

            Assert.That(lobby.TryJoin("p5", "Five", out _, out PartyJoinRejection full), Is.False);
            Assert.That(full, Is.EqualTo(PartyJoinRejection.Full));
            Assert.That(lobby.OccupiedCount, Is.EqualTo(PartyRoster.Capacity));
        }

        [Test]
        public void OnlyHostCanSelectModeDuringSetup()
        {
            lobby.TryJoin("p1", "One", out _, out _);
            int generation = lobby.RosterGeneration;

            Assert.That(lobby.TrySelectMode("p1", generation, PartyMode.MemoryCopy), Is.False);
            Assert.That(lobby.SelectedMode, Is.EqualTo(PartyMode.RelayCopy));
            Assert.That(lobby.TrySelectMode(Host, generation, PartyMode.MemoryCopy), Is.True);
            Assert.That(lobby.SelectedMode, Is.EqualTo(PartyMode.MemoryCopy));
            Assert.That(lobby.TrySelectMode(Host, generation - 1, PartyMode.CoopMural), Is.False);
        }

        [Test]
        public void OwnFreshHandAndConnectedCameraMustDwellOnOwnPad()
        {
            lobby.TryJoin("p1", "One", out int slot, out _);
            int generation = lobby.RosterGeneration;
            PartyHandPresence ownPad = PartyHandPresence.FreshAt(PartyTestLayout.ReadyPosition(slot));
            PartyHandPresence wrongPad = PartyHandPresence.FreshAt(PartyTestLayout.ReadyPosition(0));

            lobby.UpdateReadiness("p1", generation, true, wrongPad, PartyHandPresence.Missing, 1f);
            Assert.That(lobby.IsReady(slot), Is.False);
            lobby.UpdateReadiness("p1", generation, true, ownPad, PartyHandPresence.Missing, 0.6f);
            Assert.That(lobby.IsReady(slot), Is.False);
            lobby.UpdateReadiness("p1", generation, true, ownPad, PartyHandPresence.Missing, 0.4f);
            Assert.That(lobby.IsReady(slot), Is.True);
        }

        [Test]
        public void TwoHandsFromSamePlayerAdvanceOneDwellOnly()
        {
            lobby.TryJoin("p1", "One", out int slot, out _);
            int generation = lobby.RosterGeneration;
            PartyHandPresence hand = PartyHandPresence.FreshAt(PartyTestLayout.ReadyPosition(slot));

            lobby.UpdateReadiness("p1", generation, true, hand, hand, 0.5f);

            Assert.That(lobby.IsReady(slot), Is.False);
            Assert.That(lobby.ReadyCount, Is.Zero);
        }

        [Test]
        public void HandLeaveOrCameraLossImmediatelyClearsReady()
        {
            lobby.TryJoin("p1", "One", out int slot, out _);
            int generation = lobby.RosterGeneration;
            PartyHandPresence hand = PartyHandPresence.FreshAt(PartyTestLayout.ReadyPosition(slot));
            lobby.UpdateReadiness("p1", generation, true, hand, PartyHandPresence.Missing, 1f);
            Assert.That(lobby.IsReady(slot), Is.True);

            lobby.UpdateReadiness("p1", generation, true, PartyHandPresence.Missing, PartyHandPresence.Missing, 0f);
            Assert.That(lobby.IsReady(slot), Is.False);
            lobby.UpdateReadiness("p1", generation, true, hand, PartyHandPresence.Missing, 1f);
            lobby.UpdateReadiness("p1", generation, false, hand, PartyHandPresence.Missing, 0f);
            Assert.That(lobby.IsReady(slot), Is.False);
        }

        [Test]
        public void RosterGenerationChangeClearsEveryReadyAndRejectsStaleInput()
        {
            ReadyHost();
            int staleGeneration = lobby.RosterGeneration;
            Assert.That(lobby.IsReady(0), Is.True);

            lobby.TryJoin("p1", "One", out int slot, out _);

            Assert.That(lobby.IsReady(0), Is.False);
            PartyHandPresence hand = PartyHandPresence.FreshAt(PartyTestLayout.ReadyPosition(slot));
            Assert.That(lobby.UpdateReadiness("p1", staleGeneration, true, hand, PartyHandPresence.Missing, 1f), Is.False);
            Assert.That(lobby.IsReady(slot), Is.False);
        }

        [Test]
        public void StartRequiresFourReadyPlayersAndHostPedestalActivation()
        {
            JoinRemainingPlayers();
            ReadyAllPlayers();
            int generation = lobby.RosterGeneration;
            PartyHandPresence start = PartyHandPresence.FreshAt(new Vector3(0f, 1f, 8.5f));

            Assert.That(lobby.TryActivateStartPedestal("p1", generation, start, out _), Is.False);
            Assert.That(lobby.TryActivateStartPedestal(Host, generation, PartyHandPresence.Missing, out _), Is.False);
            Assert.That(lobby.TryActivateStartPedestal(Host, generation, start, out PartyStartSnapshot snapshot), Is.True);
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(lobby.State, Is.EqualTo(PartyLobbyState.Started));
            Assert.That(lobby.IsRosterLocked, Is.True);
        }

        [Test]
        public void StartedRosterRejectsLateJoinAndSnapshotCannotObserveLaterMutation()
        {
            JoinRemainingPlayers();
            lobby.TrySelectMode(Host, lobby.RosterGeneration, PartyMode.CoopMural);
            ReadyAllPlayers();
            PartyHandPresence start = PartyHandPresence.FreshAt(new Vector3(0f, 1f, 8.5f));
            lobby.TryActivateStartPedestal(Host, lobby.RosterGeneration, start, out PartyStartSnapshot snapshot);
            string[] identities = snapshot.Roster.Slots.Select(slot => slot.Identity).ToArray();

            Assert.That(lobby.TryJoin("late", "Late", out _, out PartyJoinRejection rejection), Is.False);
            Assert.That(rejection, Is.EqualTo(PartyJoinRejection.RosterLocked));
            Assert.That(lobby.TryDisconnect("p2"), Is.True);
            Assert.That(snapshot.Mode, Is.EqualTo(PartyMode.CoopMural));
            Assert.That(snapshot.Roster.Slots.Select(slot => slot.Identity), Is.EqualTo(identities));
            Assert.That(snapshot.Roster.Slots.Single(slot => slot.Identity == "p2").Connected, Is.True);
        }

        [Test]
        public void LifecycleResetPublishesAllCacheClearScopesAndReturnsToSetup()
        {
            JoinRemainingPlayers();
            ReadyAllPlayers();
            PartyHandPresence start = PartyHandPresence.FreshAt(new Vector3(0f, 1f, 8.5f));
            lobby.TryActivateStartPedestal(Host, lobby.RosterGeneration, start, out _);
            var resets = new List<PartyLifecycleReset>();
            lobby.LifecycleReset += resets.Add;

            Assert.That(lobby.TryResetToSetup("p1"), Is.False);
            Assert.That(lobby.TryResetToSetup(Host), Is.True);

            Assert.That(resets, Has.Count.EqualTo(1));
            Assert.That(resets[0].Scopes, Is.EqualTo(PartyResetScope.AllRuntimeCaches));
            Assert.That(resets[0].Scopes.HasFlag(PartyResetScope.SecretCache), Is.True);
            Assert.That(resets[0].Scopes.HasFlag(PartyResetScope.DrawingCache), Is.True);
            Assert.That(resets[0].Scopes.HasFlag(PartyResetScope.ToolOwnership), Is.True);
            Assert.That(resets[0].Scopes.HasFlag(PartyResetScope.CanvasPlacementCache), Is.True);
            Assert.That(lobby.State, Is.EqualTo(PartyLobbyState.Setup));
            Assert.That(lobby.IsRosterLocked, Is.False);
            Assert.That(lobby.ReadyCount, Is.Zero);
            Assert.That(lobby.SelectedMode, Is.EqualTo(PartyMode.RelayCopy));
        }

        private void JoinRemainingPlayers()
        {
            if (lobby.OccupiedCount < 2) lobby.TryJoin("p1", "One", out _, out _);
            if (lobby.OccupiedCount < 3) lobby.TryJoin("p2", "Two", out _, out _);
            if (lobby.OccupiedCount < 4) lobby.TryJoin("p3", "Three", out _, out _);
        }

        private void ReadyAllPlayers()
        {
            int generation = lobby.RosterGeneration;
            string[] identities = { Host, "p1", "p2", "p3" };
            for (int slot = 0; slot < identities.Length; slot++)
            {
                PartyHandPresence hand = PartyHandPresence.FreshAt(PartyTestLayout.ReadyPosition(slot));
                Assert.That(lobby.UpdateReadiness(identities[slot], generation, true, hand, PartyHandPresence.Missing, 1f), Is.True);
            }
        }

        private void ReadyHost()
        {
            PartyHandPresence hand = PartyHandPresence.FreshAt(PartyTestLayout.ReadyPosition(0));
            lobby.UpdateReadiness(Host, lobby.RosterGeneration, true, hand, PartyHandPresence.Missing, 1f);
        }
    }
}
