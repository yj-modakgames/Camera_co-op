using CameraCoop.Game;
using NUnit.Framework;
using UnityEngine;

namespace CameraCoop.Tests
{
    // docs/12 §3 — 게임 프로토콜 payload JsonUtility 왕복
    public class GameProtocolTests
    {
        [Test]
        public void RoundBeginPayload_RoundTrips()
        {
            var original = new RoundBeginPayload
            {
                round = 2,
                totalRounds = 8,
                activeId = "p1",
                wordLen = 3,
                introSec = 3f,
                durationSec = 90f
            };
            var back = JsonUtility.FromJson<RoundBeginPayload>(JsonUtility.ToJson(original));
            Assert.AreEqual(original.round, back.round);
            Assert.AreEqual(original.totalRounds, back.totalRounds);
            Assert.AreEqual(original.activeId, back.activeId);
            Assert.AreEqual(original.wordLen, back.wordLen);
            Assert.AreEqual(original.introSec, back.introSec, 1e-6f);
            Assert.AreEqual(original.durationSec, back.durationSec, 1e-6f);
        }

        [Test]
        public void RoundEndPayload_RoundTrips_IncludingParallelArrays()
        {
            var original = new RoundEndPayload
            {
                word = "사과",
                playerIds = new[] { "p1", "p2", "p3" },
                scores = new[] { 150, 50, 0 },
                reason = 1
            };
            var back = JsonUtility.FromJson<RoundEndPayload>(JsonUtility.ToJson(original));
            Assert.AreEqual(original.word, back.word);
            Assert.AreEqual(original.reason, back.reason);
            Assert.AreEqual(original.playerIds.Length, back.playerIds.Length);
            for (int i = 0; i < original.playerIds.Length; i++)
            {
                Assert.AreEqual(original.playerIds[i], back.playerIds[i]);
                Assert.AreEqual(original.scores[i], back.scores[i]);
            }
        }

        [Test]
        public void GameStateSyncPayload_RoundTrips()
        {
            var original = new GameStateSyncPayload
            {
                phase = 2,
                gameId = GameMsg.GuessGameId,
                mode = 1,
                round = 3,
                totalRounds = 8,
                activeId = "p2",
                wordLen = 4,
                remainingSec = 42.5f,
                playerIds = new[] { "p1", "p2" },
                scores = new[] { 200, 100 }
            };
            var back = JsonUtility.FromJson<GameStateSyncPayload>(JsonUtility.ToJson(original));
            Assert.AreEqual(original.phase, back.phase);
            Assert.AreEqual(original.gameId, back.gameId);
            Assert.AreEqual(original.mode, back.mode);
            Assert.AreEqual(original.round, back.round);
            Assert.AreEqual(original.totalRounds, back.totalRounds);
            Assert.AreEqual(original.activeId, back.activeId);
            Assert.AreEqual(original.wordLen, back.wordLen);
            Assert.AreEqual(original.remainingSec, back.remainingSec, 1e-6f);
            Assert.AreEqual(original.playerIds.Length, back.playerIds.Length);
            Assert.AreEqual(original.playerIds[0], back.playerIds[0]);
            Assert.AreEqual(original.scores[0], back.scores[0]);
            Assert.AreEqual(original.scores[1], back.scores[1]);
        }

        [Test]
        public void WordAssignPayload_RoundTrips_KoreanText()
        {
            var original = new WordAssignPayload { word = "소방차" };
            var back = JsonUtility.FromJson<WordAssignPayload>(JsonUtility.ToJson(original));
            Assert.AreEqual("소방차", back.word);
        }
    }
}
