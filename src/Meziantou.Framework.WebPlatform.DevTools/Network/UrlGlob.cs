namespace Meziantou.Framework.WebPlatform.DevTools.Network;

/// <summary>
/// The URL patterns of the interception of the requests (<c>Fetch.RequestPattern.urlPattern</c>): <c>*</c> matches any
/// sequence of characters, <c>?</c> matches one character, and a backslash escapes the next character.
/// </summary>
internal static class UrlGlob
{
    public static bool IsMatch(string pattern, string url) => IsMatch(pattern.AsSpan(), url.AsSpan());

    private static bool IsMatch(ReadOnlySpan<char> pattern, ReadOnlySpan<char> text)
    {
        // Iterative matching with backtracking to the last star (linear in most cases, quadratic at worst)
        var p = 0;
        var t = 0;
        var starPattern = -1;
        var starText = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length)
            {
                var c = pattern[p];
                if (c == '*')
                {
                    starPattern = p++;
                    starText = t;
                    continue;
                }

                if (c == '?')
                {
                    p++;
                    t++;
                    continue;
                }

                if (c == '\\' && p + 1 < pattern.Length)
                {
                    if (pattern[p + 1] == text[t])
                    {
                        p += 2;
                        t++;
                        continue;
                    }
                }
                else if (c == text[t])
                {
                    p++;
                    t++;
                    continue;
                }
            }

            if (starPattern < 0)
                return false;

            p = starPattern + 1;
            t = ++starText;
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }
}
