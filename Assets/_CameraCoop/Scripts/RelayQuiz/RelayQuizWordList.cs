using System;
using System.Collections.Generic;
using System.Text;
using CameraCoop.Game;
using UnityEngine;

namespace CameraCoop
{
    public enum RelayQuizDifficulty
    {
        Easy = 0,
        Medium = 1,
        Hard = 2
    }

    // 릴레이 제시어 데이터 schema (docs/09 §9). runtime 추출 cursor는 여기 두지 않는다.
    [CreateAssetMenu(fileName = "RelayQuizWords", menuName = "CameraCoop/Relay Quiz Word List")]
    public sealed class RelayQuizWordList : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string text;
            public RelayQuizDifficulty difficulty;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        public int Count { get { return entries != null ? entries.Length : 0; } }

        // 빈 단어와 정규화 기준 중복을 거부한다. 성공 시 WordBank가 읽는 줄 단위 text를 만든다.
        public bool TryBuildDeckText(out string deckText, out string error)
        {
            deckText = null;
            error = null;
            if (entries == null || entries.Length == 0)
            {
                error = "RelayQuizWordList has no entries.";
                return false;
            }

            var seen = new HashSet<string>();
            var builder = new StringBuilder();
            for (int i = 0; i < entries.Length; i++)
            {
                string text = entries[i].text;
                string normalized = GuessJudge.Normalize(text);
                if (normalized.Length == 0)
                {
                    error = "RelayQuizWordList entry " + i + " is empty after normalization.";
                    return false;
                }
                if (!seen.Add(normalized))
                {
                    error = "RelayQuizWordList entry " + i + " duplicates an earlier word.";
                    return false;
                }
                if (builder.Length > 0) builder.Append('\n');
                builder.Append(text.Trim());
            }
            deckText = builder.ToString();
            return true;
        }
    }
}
