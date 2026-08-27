using System.Collections.Generic;
using CameraCoop.Game;
using NUnit.Framework;

namespace CameraCoop.Tests
{
    // docs/12 §1 — 단어장: 무중복 랜덤 추출 + 소진 시 재셔플
    public class WordBankTests
    {
        [Test]
        public void Parse_TrimsBlankLinesAndDuplicates()
        {
            var bank = new WordBank("사과\n\n 바나나 \n사과\n포도", seed: 1);
            Assert.AreEqual(3, bank.Count);
        }

        [Test]
        public void Next_OneFullCycle_NoDuplicates_CoversWholeSet()
        {
            var bank = new WordBank("사과\n바나나\n포도", seed: 1);
            var drawn = new HashSet<string>();
            for (int i = 0; i < bank.Count; i++)
            {
                string word = bank.Next();
                Assert.IsNotNull(word);
                Assert.IsTrue(drawn.Add(word), "한 바퀴 안에서 중복 없음: " + word);
            }
            Assert.AreEqual(3, drawn.Count);
            Assert.IsTrue(drawn.Contains("사과"));
            Assert.IsTrue(drawn.Contains("바나나"));
            Assert.IsTrue(drawn.Contains("포도"));
        }

        [Test]
        public void Next_AfterExhaustion_Reshuffles_ReturnsSetMember()
        {
            var bank = new WordBank("사과\n바나나\n포도", seed: 1);
            for (int i = 0; i < bank.Count; i++)
            {
                bank.Next();
            }
            string extra = bank.Next();
            Assert.IsNotNull(extra);
            Assert.IsTrue(extra == "사과" || extra == "바나나" || extra == "포도");
        }

        [Test]
        public void Next_SameSeed_ProducesSameOrder()
        {
            const string text = "사과\n바나나\n포도\n수박\n딸기";
            var bankA = new WordBank(text, seed: 42);
            var bankB = new WordBank(text, seed: 42);
            for (int i = 0; i < 8; i++)
            {
                Assert.AreEqual(bankA.Next(), bankB.Next());
            }
        }

        [Test]
        public void Next_EmptyContent_ReturnsNull()
        {
            var bank = new WordBank("", seed: 1);
            Assert.IsNull(bank.Next());
        }
    }
}
