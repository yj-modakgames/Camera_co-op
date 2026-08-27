using System;
using System.Collections.Generic;

namespace CameraCoop.Game
{
    // 단어장: 1줄 1단어 텍스트 → 무중복 랜덤 추출, 소진 시 재셔플 (docs/12 §1)
    public class WordBank
    {
        private readonly string[] _words;
        private readonly Random _random;
        private int[] _order;
        private int _cursor;

        // 줄 단위 파싱: Trim, 빈 줄 제거, 중복 제거(첫 등장 유지)
        public WordBank(string textContent, int seed)
        {
            _random = new Random(seed);
            _words = ParseUnique(textContent);
            _order = null;
            _cursor = 0;
        }

        public int Count
        {
            get { return _words.Length; }
        }

        // Count==0이면 null. 한 바퀴 안에서 중복 없음
        public string Next()
        {
            if (_words.Length == 0)
            {
                return null;
            }

            if (_order == null || _cursor >= _order.Length)
            {
                _order = Shuffle(_words.Length);
                _cursor = 0;
            }

            return _words[_order[_cursor++]];
        }

        private static string[] ParseUnique(string textContent)
        {
            if (string.IsNullOrEmpty(textContent))
            {
                return Array.Empty<string>();
            }

            string[] lines = textContent.Split('\n');
            var seen = new HashSet<string>();
            var result = new List<string>();
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0)
                {
                    continue;
                }
                if (seen.Add(line))
                {
                    result.Add(line);
                }
            }
            return result.ToArray();
        }

        // Fisher-Yates
        private int[] Shuffle(int n)
        {
            var order = new int[n];
            for (int i = 0; i < n; i++)
            {
                order[i] = i;
            }
            for (int i = n - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                int tmp = order[i];
                order[i] = order[j];
                order[j] = tmp;
            }
            return order;
        }
    }
}
