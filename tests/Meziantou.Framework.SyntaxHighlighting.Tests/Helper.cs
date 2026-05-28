global using static Meziantou.Framework.SyntaxHighlighting.Tests.Helper;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public static class Helper
{
    public static void AssertHighlighter(string language, string code, string expected, [CallerMemberName] string testName = "")
    {
        var result = SyntaxHighlighter.Highlight(code, language);
        HighlighterPreviewFixture.Current?.Add(language, testName, code, result);
        Assert.Equal(expected, result);
    }
}
