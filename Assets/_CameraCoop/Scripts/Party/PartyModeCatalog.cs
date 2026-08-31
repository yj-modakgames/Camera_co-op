using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace CameraCoop.Party
{
    public enum PartyMode
    {
        RelayCopy = 0,
        MemoryCopy = 1,
        CoopMural = 2
    }

    public enum PartyModeInput
    {
        FistDrawing = 0,
        KeyboardAnswer = 1
    }

    public enum PartyReferencePolicy
    {
        ContinuousWhileCopying = 0,
        TimedThenHidden = 1,
        PublicSharedCanvas = 2
    }

    public enum PartyCanvasVisibility
    {
        PrivateToAuthorizedSlot = 0,
        PublicToParty = 1
    }

    public enum PartyWritePolicy
    {
        ActiveSlotOnly = 0,
        SequentialRosterSlots = 1
    }

    public sealed class PartyModeDefinition
    {
        private readonly ReadOnlyCollection<PartyModeInput> inputs;

        internal PartyModeDefinition(
            PartyMode id,
            bool requiredForInitialRelease,
            PartyModeInput[] inputs,
            PartyReferencePolicy referencePolicy,
            float referenceSeconds,
            PartyCanvasVisibility canvasVisibility,
            PartyWritePolicy writePolicy)
        {
            Id = id;
            RequiredForInitialRelease = requiredForInitialRelease;
            this.inputs = Array.AsReadOnly((PartyModeInput[])inputs.Clone());
            ReferencePolicy = referencePolicy;
            ReferenceSeconds = referenceSeconds;
            CanvasVisibility = canvasVisibility;
            WritePolicy = writePolicy;
        }

        public PartyMode Id { get; }
        public bool RequiredForInitialRelease { get; }
        public IReadOnlyList<PartyModeInput> Inputs => inputs;
        public PartyReferencePolicy ReferencePolicy { get; }
        public float ReferenceSeconds { get; }
        public PartyCanvasVisibility CanvasVisibility { get; }
        public PartyWritePolicy WritePolicy { get; }

        public bool IsReferenceVisible(float elapsedSeconds)
        {
            if (float.IsNaN(elapsedSeconds) || float.IsInfinity(elapsedSeconds) || elapsedSeconds < 0f)
            {
                return false;
            }

            return ReferencePolicy != PartyReferencePolicy.TimedThenHidden || elapsedSeconds < ReferenceSeconds;
        }
    }

    public static class PartyModeCatalog
    {
        private static readonly PartyModeDefinition RelayCopy = new PartyModeDefinition(
            PartyMode.RelayCopy,
            true,
            new[] { PartyModeInput.FistDrawing, PartyModeInput.KeyboardAnswer },
            PartyReferencePolicy.ContinuousWhileCopying,
            0f,
            PartyCanvasVisibility.PrivateToAuthorizedSlot,
            PartyWritePolicy.ActiveSlotOnly);

        private static readonly PartyModeDefinition MemoryCopy = new PartyModeDefinition(
            PartyMode.MemoryCopy,
            false,
            new[] { PartyModeInput.FistDrawing, PartyModeInput.KeyboardAnswer },
            PartyReferencePolicy.TimedThenHidden,
            5f,
            PartyCanvasVisibility.PrivateToAuthorizedSlot,
            PartyWritePolicy.ActiveSlotOnly);

        private static readonly PartyModeDefinition CoopMural = new PartyModeDefinition(
            PartyMode.CoopMural,
            false,
            new[] { PartyModeInput.FistDrawing },
            PartyReferencePolicy.PublicSharedCanvas,
            0f,
            PartyCanvasVisibility.PublicToParty,
            PartyWritePolicy.SequentialRosterSlots);

        private static readonly ReadOnlyCollection<PartyModeDefinition> Modes = Array.AsReadOnly(new[]
        {
            RelayCopy,
            MemoryCopy,
            CoopMural
        });

        public static IReadOnlyList<PartyModeDefinition> All => Modes;

        public static PartyModeDefinition Get(PartyMode id)
        {
            switch (id)
            {
                case PartyMode.RelayCopy: return RelayCopy;
                case PartyMode.MemoryCopy: return MemoryCopy;
                case PartyMode.CoopMural: return CoopMural;
                default: throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown party mode.");
            }
        }

        public static bool TryGet(PartyMode id, out PartyModeDefinition definition)
        {
            switch (id)
            {
                case PartyMode.RelayCopy: definition = RelayCopy; return true;
                case PartyMode.MemoryCopy: definition = MemoryCopy; return true;
                case PartyMode.CoopMural: definition = CoopMural; return true;
                default: definition = null; return false;
            }
        }
    }
}
