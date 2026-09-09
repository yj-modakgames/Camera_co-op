using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace CameraCoop.Party
{
    public enum PartyMode
    {
        RelayCopy = 0,
        MemoryCopy = 1,
        CoopMural = 2,
        PictureTelephone = 3,
        DrawingWordChain = 4
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
            string displayName,
            string description,
            bool requiredForInitialRelease,
            PartyModeInput[] inputs,
            PartyReferencePolicy referencePolicy,
            float referenceSeconds,
            PartyCanvasVisibility canvasVisibility,
            PartyWritePolicy writePolicy)
        {
            Id = id;
            DisplayName = displayName;
            Description = description;
            RequiredForInitialRelease = requiredForInitialRelease;
            this.inputs = Array.AsReadOnly((PartyModeInput[])inputs.Clone());
            ReferencePolicy = referencePolicy;
            ReferenceSeconds = referenceSeconds;
            CanvasVisibility = canvasVisibility;
            WritePolicy = writePolicy;
        }

        public PartyMode Id { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public bool RequiredForInitialRelease { get; }
        public IReadOnlyList<PartyModeInput> Inputs => inputs;
        public PartyReferencePolicy ReferencePolicy { get; }
        public float ReferenceSeconds { get; }
        public PartyCanvasVisibility CanvasVisibility { get; }
        public PartyWritePolicy WritePolicy { get; }
        public int ResultDrawingCount => Id == PartyMode.PictureTelephone ? 2
            : Id == PartyMode.DrawingWordChain ? 4 : Id == PartyMode.CoopMural ? 0 : PartyRoster.Capacity - 1;
        public bool UsesSlotDrawingBoards => Id == PartyMode.RelayCopy || Id == PartyMode.DrawingWordChain;

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
            "RELAY COPY",
            "Copy the previous player's drawing in order.",
            true,
            new[] { PartyModeInput.FistDrawing, PartyModeInput.KeyboardAnswer },
            PartyReferencePolicy.ContinuousWhileCopying,
            0f,
            PartyCanvasVisibility.PrivateToAuthorizedSlot,
            PartyWritePolicy.ActiveSlotOnly);

        private static readonly PartyModeDefinition MemoryCopy = new PartyModeDefinition(
            PartyMode.MemoryCopy,
            "MEMORY COPY",
            "Memorize the picture for five seconds, then draw it.",
            false,
            new[] { PartyModeInput.FistDrawing, PartyModeInput.KeyboardAnswer },
            PartyReferencePolicy.TimedThenHidden,
            5f,
            PartyCanvasVisibility.PrivateToAuthorizedSlot,
            PartyWritePolicy.ActiveSlotOnly);

        private static readonly PartyModeDefinition CoopMural = new PartyModeDefinition(
            PartyMode.CoopMural,
            "CO-OP MURAL",
            "Take turns adding one public layer to a shared mural.",
            false,
            new[] { PartyModeInput.FistDrawing },
            PartyReferencePolicy.PublicSharedCanvas,
            0f,
            PartyCanvasVisibility.PublicToParty,
            PartyWritePolicy.SequentialRosterSlots);

        private static readonly PartyModeDefinition PictureTelephone = new PartyModeDefinition(
            PartyMode.PictureTelephone,
            "PICTURE TELEPHONE",
            "Draw, describe, draw the description, then make the final guess.",
            false,
            new[] { PartyModeInput.FistDrawing, PartyModeInput.KeyboardAnswer },
            PartyReferencePolicy.ContinuousWhileCopying,
            0f,
            PartyCanvasVisibility.PrivateToAuthorizedSlot,
            PartyWritePolicy.ActiveSlotOnly);

        private static readonly PartyModeDefinition DrawingWordChain = new PartyModeDefinition(
            PartyMode.DrawingWordChain,
            "DRAWING WORD CHAIN",
            "Draw a four-picture word chain, then privately name your own picture.",
            false,
            new[] { PartyModeInput.FistDrawing, PartyModeInput.KeyboardAnswer },
            PartyReferencePolicy.ContinuousWhileCopying,
            0f,
            PartyCanvasVisibility.PrivateToAuthorizedSlot,
            PartyWritePolicy.SequentialRosterSlots);

        private static readonly ReadOnlyCollection<PartyModeDefinition> Modes = Array.AsReadOnly(new[]
        {
            RelayCopy,
            MemoryCopy,
            CoopMural,
            PictureTelephone,
            DrawingWordChain
        });

        public static IReadOnlyList<PartyModeDefinition> All => Modes;

        public static PartyModeDefinition Get(PartyMode id)
        {
            switch (id)
            {
                case PartyMode.RelayCopy: return RelayCopy;
                case PartyMode.MemoryCopy: return MemoryCopy;
                case PartyMode.CoopMural: return CoopMural;
                case PartyMode.PictureTelephone: return PictureTelephone;
                case PartyMode.DrawingWordChain: return DrawingWordChain;
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
                case PartyMode.PictureTelephone: definition = PictureTelephone; return true;
                case PartyMode.DrawingWordChain: definition = DrawingWordChain; return true;
                default: definition = null; return false;
            }
        }
    }
}
