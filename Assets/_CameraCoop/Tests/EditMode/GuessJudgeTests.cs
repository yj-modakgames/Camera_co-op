using CameraCoop.Game;
using NUnit.Framework;

namespace CameraCoop.Tests
{
    // docs/12 §1 — 정답 정규화·판정 (상태 없음)
    public class GuessJudgeTests
    {
        [Test]
        public void Normalize_Null_ReturnsEmpty()
        {
            Assert.AreEqual("", GuessJudge.Normalize(null));
        }

        [Test]
        public void Normalize_TrimsSurroundingWhitespace()
        {
            Assert.AreEqual("사과", GuessJudge.Normalize("  사과 "));
        }

        [Test]
        public void Normalize_RemovesInnerWhitespace()
        {
            Assert.AreEqual("사과", GuessJudge.Normalize("사 과"));
        }

        [Test]
        public void Normalize_TrimsTabsAndNewlines()
        {
            Assert.AreEqual("소방차", GuessJudge.Normalize("\t소방차\n"));
        }

        [Test]
        public void Normalize_LowersInvariantCase()
        {
            Assert.AreEqual("apple", GuessJudge.Normalize("Apple"));
        }

        [Test]
        public void Normalize_RemovesFullWidthSpace()
        {
            Assert.AreEqual("사과", GuessJudge.Normalize("사　과"));
        }

        [Test]
        public void IsMatch_ExactSame_True()
        {
            Assert.IsTrue(GuessJudge.IsMatch("사과", "사과"));
        }

        [Test]
        public void IsMatch_WhitespaceOnlyDifference_True()
        {
            Assert.IsTrue(GuessJudge.IsMatch("사과", " 사 과 "));
        }

        [Test]
        public void IsMatch_Superstring_False()
        {
            Assert.IsFalse(GuessJudge.IsMatch("사과", "사과나무"));
        }

        [Test]
        public void IsMatch_EmptyGuess_False()
        {
            Assert.IsFalse(GuessJudge.IsMatch("사과", ""));
        }

        [Test]
        public void IsMatch_BothEmpty_False()
        {
            Assert.IsFalse(GuessJudge.IsMatch("", ""));
        }

        [Test]
        public void IsMatch_NullAnswer_False()
        {
            Assert.IsFalse(GuessJudge.IsMatch(null, "사과"));
        }
    }
}
