using System;
using System.Linq;
using CameraCoop.Party;
using NUnit.Framework;

namespace CameraCoop.Tests
{
    public sealed class PartyModeCatalogTests
    {
        [Test]
        public void InitialCatalogContainsExactlyThreeApprovedModes()
        {
            PartyModeDefinition[] modes = PartyModeCatalog.All.ToArray();

            Assert.That(modes.Select(mode => mode.Id), Is.EqualTo(new[]
            {
                PartyMode.RelayCopy,
                PartyMode.MemoryCopy,
                PartyMode.CoopMural
            }));
            Assert.That(modes.Count(mode => mode.RequiredForInitialRelease), Is.EqualTo(1));
            Assert.That(modes.Single(mode => mode.RequiredForInitialRelease).Id, Is.EqualTo(PartyMode.RelayCopy));
            Assert.That(Enum.GetValues(typeof(PartyMode)).Length, Is.EqualTo(3));
        }

        [Test]
        public void RelayCopyKeepsPrivateReferenceVisibleWhileCopying()
        {
            PartyModeDefinition mode = PartyModeCatalog.Get(PartyMode.RelayCopy);

            Assert.That(mode.Inputs, Does.Contain(PartyModeInput.FistDrawing));
            Assert.That(mode.Inputs, Does.Contain(PartyModeInput.KeyboardAnswer));
            Assert.That(mode.ReferencePolicy, Is.EqualTo(PartyReferencePolicy.ContinuousWhileCopying));
            Assert.That(mode.CanvasVisibility, Is.EqualTo(PartyCanvasVisibility.PrivateToAuthorizedSlot));
            Assert.That(mode.WritePolicy, Is.EqualTo(PartyWritePolicy.ActiveSlotOnly));
        }

        [Test]
        public void MemoryCopyUsesTimedReferenceThenHidesIt()
        {
            PartyModeDefinition mode = PartyModeCatalog.Get(PartyMode.MemoryCopy);

            Assert.That(mode.ReferencePolicy, Is.EqualTo(PartyReferencePolicy.TimedThenHidden));
            Assert.That(mode.ReferenceSeconds, Is.GreaterThan(0f));
            Assert.That(mode.IsReferenceVisible(mode.ReferenceSeconds - 0.01f), Is.True);
            Assert.That(mode.IsReferenceVisible(mode.ReferenceSeconds), Is.False);
        }

        [Test]
        public void CoopMuralIsPublicAndWritableInRosterOrder()
        {
            PartyModeDefinition mode = PartyModeCatalog.Get(PartyMode.CoopMural);

            Assert.That(mode.Inputs, Is.EqualTo(new[] { PartyModeInput.FistDrawing }));
            Assert.That(mode.ReferencePolicy, Is.EqualTo(PartyReferencePolicy.PublicSharedCanvas));
            Assert.That(mode.CanvasVisibility, Is.EqualTo(PartyCanvasVisibility.PublicToParty));
            Assert.That(mode.WritePolicy, Is.EqualTo(PartyWritePolicy.SequentialRosterSlots));
        }

        [Test]
        public void SceneCatalogUsesUniqueLobbyFirstBuildOrder()
        {
            string[] paths = PartySceneCatalog.BuildScenePaths.ToArray();

            Assert.That(paths, Is.EqualTo(new[]
            {
                "Assets/_CameraCoop/Scenes/RelayQuizOnline.unity",
                "Assets/_CameraCoop/Scenes/RelayCopy.unity",
                "Assets/_CameraCoop/Scenes/MemoryCopy.unity",
                "Assets/_CameraCoop/Scenes/CoopMural.unity"
            }));
            Assert.That(paths.Distinct().Count(), Is.EqualTo(paths.Length));
            Assert.That(PartySceneCatalog.LobbySceneName, Is.EqualTo("RelayQuizOnline"));
            Assert.That(PartySceneCatalog.LobbyScenePath, Is.EqualTo(paths[0]));
        }

        [Test]
        public void SceneCatalogResolvesEverySupportedModeToItsExactScene()
        {
            PartyMode[] modes = { PartyMode.RelayCopy, PartyMode.MemoryCopy, PartyMode.CoopMural };
            string[] expectedNames = { "RelayCopy", "MemoryCopy", "CoopMural" };

            for (int index = 0; index < modes.Length; index++)
            {
                Assert.That(PartySceneCatalog.TryGet(modes[index], out PartySceneDefinition definition), Is.True);
                Assert.That(definition.Mode, Is.EqualTo(modes[index]));
                Assert.That(definition.SceneName, Is.EqualTo(expectedNames[index]));
                Assert.That(definition.ScenePath, Is.EqualTo(PartySceneCatalog.BuildScenePaths[index + 1]));
            }
        }

        [Test]
        public void PartySceneCatalog_RejectsUndefinedMode()
        {
            Assert.That(PartySceneCatalog.TryGet((PartyMode)99, out PartySceneDefinition definition), Is.False);
            Assert.That(definition, Is.Null);
        }

        [Test]
        public void TransitionPhaseAcceptsOnlyTheFiveDefinedValues()
        {
            Assert.That(PartyTransitionPhaseRules.IsDefined(PartyTransitionPhase.Lobby), Is.True);
            Assert.That(PartyTransitionPhaseRules.IsDefined(PartyTransitionPhase.SelectingMode), Is.True);
            Assert.That(PartyTransitionPhaseRules.IsDefined(PartyTransitionPhase.LoadingGame), Is.True);
            Assert.That(PartyTransitionPhaseRules.IsDefined(PartyTransitionPhase.InGame), Is.True);
            Assert.That(PartyTransitionPhaseRules.IsDefined(PartyTransitionPhase.ReturningToLobby), Is.True);
            Assert.That(PartyTransitionPhaseRules.IsDefined((PartyTransitionPhase)99), Is.False);
        }

        [Test]
        public void TransitionKeyEqualityUsesSessionRosterAndTransitionGenerations()
        {
            PartyTransitionKey first = new PartyTransitionKey("session-a", 3, 7);
            PartyTransitionKey same = new PartyTransitionKey("session-a", 3, 7);
            PartyTransitionKey differentSession = new PartyTransitionKey("session-b", 3, 7);
            PartyTransitionKey differentRoster = new PartyTransitionKey("session-a", 4, 7);
            PartyTransitionKey differentTransition = new PartyTransitionKey("session-a", 3, 8);

            Assert.That(first, Is.EqualTo(same));
            Assert.That(first == same, Is.True);
            Assert.That(first != differentSession, Is.True);
            Assert.That(first != differentRoster, Is.True);
            Assert.That(first != differentTransition, Is.True);
            Assert.That(first.GetHashCode(), Is.EqualTo(same.GetHashCode()));
        }

        [Test]
        public void InvalidTransitionKeyIsRejectedWithoutFallbackIdentity()
        {
            Assert.That(PartyTransitionKey.TryCreate(string.Empty, 3, 7, out PartyTransitionKey emptySession), Is.False);
            Assert.That(emptySession.IsValid, Is.False);
            Assert.That(PartyTransitionKey.TryCreate("session-a", -1, 7, out PartyTransitionKey negativeRoster), Is.False);
            Assert.That(negativeRoster.IsValid, Is.False);
            Assert.That(PartyTransitionKey.TryCreate("session-a", 3, -1, out PartyTransitionKey negativeTransition), Is.False);
            Assert.That(negativeTransition.IsValid, Is.False);
        }

        [Test]
        public void SceneLoadFailureValuesRemainStableForSerializedReports()
        {
            Assert.That((int)PartySceneLoadFailure.None, Is.EqualTo(0));
            Assert.That((int)PartySceneLoadFailure.InvalidScenePath, Is.EqualTo(1));
            Assert.That((int)PartySceneLoadFailure.LoadFailed, Is.EqualTo(2));
            Assert.That((int)PartySceneLoadFailure.MissingAdapter, Is.EqualTo(3));
            Assert.That((int)PartySceneLoadFailure.ModeMismatch, Is.EqualTo(4));
            Assert.That((int)PartySceneLoadFailure.ActivationFailed, Is.EqualTo(5));
            Assert.That((int)PartySceneLoadFailure.UnloadFailed, Is.EqualTo(6));
            Assert.That((int)PartySceneLoadFailure.Timeout, Is.EqualTo(7));
            Assert.That((int)PartySceneLoadFailure.InvalidTransition, Is.EqualTo(8));
        }
    }
}
