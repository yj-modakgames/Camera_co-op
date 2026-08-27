using System.Text;

namespace CameraCoop.Game
{
    // 정답 정규화·판정 (docs/12 §1). 상태 없음.
    internal static class GuessJudge
    {
        // null→"". Trim 후 char.IsWhiteSpace 전부 제거, ToLowerInvariant
        public static string Normalize(string raw)
        {
            if (raw == null)
            {
                return "";
            }

            string trimmed = raw.Trim();
            var sb = new StringBuilder(trimmed.Length);
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (!char.IsWhiteSpace(c))
                {
                    sb.Append(c);
                }
            }
            return sb.ToString().ToLowerInvariant();
        }

        // Normalize 양쪽 적용 후 완전 일치. 어느 쪽이든 정규화 결과가 빈 문자열이면 false
        public static bool IsMatch(string answer, string guess)
        {
            string normAnswer = Normalize(answer);
            string normGuess = Normalize(guess);
            if (normAnswer.Length == 0 || normGuess.Length == 0)
            {
                return false;
            }
            return normAnswer == normGuess;
        }
    }
}
