using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace CameraCoop.Party
{
    public sealed class PartySceneDefinition
    {
        public PartySceneDefinition(PartyMode mode, string sceneName, string scenePath)
        {
            if (string.IsNullOrEmpty(sceneName)) throw new ArgumentException("Scene name is required.", nameof(sceneName));
            if (string.IsNullOrEmpty(scenePath)) throw new ArgumentException("Scene path is required.", nameof(scenePath));

            Mode = mode;
            SceneName = sceneName;
            ScenePath = scenePath;
        }

        public PartyMode Mode { get; }
        public string SceneName { get; }
        public string ScenePath { get; }
    }

    public static class PartySceneCatalog
    {
        public const string LobbySceneName = "RelayQuizOnline";
        public const string LobbyScenePath = "Assets/_CameraCoop/Scenes/RelayQuizOnline.unity";

        private static readonly PartySceneDefinition RelayCopy = new PartySceneDefinition(
            PartyMode.RelayCopy,
            "RelayCopy",
            "Assets/_CameraCoop/Scenes/RelayCopy.unity");

        private static readonly PartySceneDefinition MemoryCopy = new PartySceneDefinition(
            PartyMode.MemoryCopy,
            "MemoryCopy",
            "Assets/_CameraCoop/Scenes/MemoryCopy.unity");

        private static readonly PartySceneDefinition CoopMural = new PartySceneDefinition(
            PartyMode.CoopMural,
            "CoopMural",
            "Assets/_CameraCoop/Scenes/CoopMural.unity");

        private static readonly ReadOnlyCollection<PartySceneDefinition> Definitions =
            Array.AsReadOnly(new[] { RelayCopy, MemoryCopy, CoopMural });

        private static readonly ReadOnlyCollection<string> ScenePaths = Array.AsReadOnly(new[]
        {
            LobbyScenePath,
            RelayCopy.ScenePath,
            MemoryCopy.ScenePath,
            CoopMural.ScenePath
        });

        public static IReadOnlyList<string> BuildScenePaths => ScenePaths;

        public static bool TryGet(PartyMode mode, out PartySceneDefinition definition)
        {
            switch (mode)
            {
                case PartyMode.RelayCopy:
                    definition = Definitions[0];
                    return true;
                case PartyMode.MemoryCopy:
                    definition = Definitions[1];
                    return true;
                case PartyMode.CoopMural:
                    definition = Definitions[2];
                    return true;
                default:
                    definition = null;
                    return false;
            }
        }
    }
}
