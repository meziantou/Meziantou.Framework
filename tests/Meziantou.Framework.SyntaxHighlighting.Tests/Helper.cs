global using static Meziantou.Framework.SyntaxHighlighting.Tests.Helper;
using System.Reflection;
using System.Runtime.CompilerServices;
using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages;

namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public static class Helper
{
    public static void AssertHighlighter(string language, string code, string expected, [CallerMemberName] string testName = "")
    {
        var result = HighlightWithFallbackDetection(code, language, out var isFallback);
        HighlighterPreviewFixture.Current?.Add(language, testName, code, result);
        Assert.False(isFallback, $"Highlighting '{language}' was abandoned (regex timeout or no progress), so the result is plain text.");
        Assert.Equal(expected, result);
    }

    /// <summary>Same as <see cref="SyntaxHighlighter.Highlight"/>, but also reports whether the result is the plain-text fallback.</summary>
    public static string HighlightWithFallbackDetection(string code, string language, out bool isFallback)
    {
        return Tokenizer.Highlight(code, LanguageRegistry.Get(language), HighlightOptions.Default, out isFallback);
    }

    /// <summary>
    /// One identifier per grammar, named after the grammar's class (<c>Languages.CSharp</c> → <c>csharp</c>), so theories
    /// that must cover every grammar pick up a new one without being edited.
    /// </summary>
    public static TheoryData<string> Grammars()
    {
        var data = new TheoryData<string>();
        foreach (var language in GetGrammarIdentifiers())
        {
            data.Add(language);
        }

        return data;
    }

    internal static string[] GetGrammarIdentifiers()
    {
        return
        [
            .. typeof(SyntaxHighlighter).Assembly.GetTypes()
                .Where(type => type.Namespace == "Meziantou.Framework.SyntaxHighlighting.Languages" && type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.PropertyType == typeof(CompiledMode))
                .Select(type => type.Name.ToLowerInvariant())
                .Order(StringComparer.Ordinal),
        ];
    }
}
