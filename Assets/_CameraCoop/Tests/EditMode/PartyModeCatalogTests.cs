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
    }
}
