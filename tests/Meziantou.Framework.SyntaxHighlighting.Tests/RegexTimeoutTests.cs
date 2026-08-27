using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.SyntaxHighlighting.Tests;

/// <summary>
/// Grammar patterns are matched against untrusted input. A pattern compiled without a match
/// timeout that backtracks catastrophically hangs the calling thread with no way to recover, so
/// every regex the engine builds must carry a finite timeout.
/// </summary>
public sealed class RegexTimeoutTests
{
    [Theory]
    [InlineData("bash")]
    [InlineData("bnf")]
    [InlineData("cpp")]
    [InlineData("csharp")]
    [InlineData("css")]
    [InlineData("dockerfile")]
    [InlineData("dos")]
    [InlineData("fsharp")]
    [InlineData("graphql")]
    [InlineData("html")]
    [InlineData("http")]
    [InlineData("ini")]
    [InlineData("javascript")]
    [InlineData("json")]
    [InlineData("less")]
    [InlineData("markdown")]
    [InlineData("msil")]
    [InlineData("nginx")]
    [InlineData("php")]
    [InlineData("powershell")]
    [InlineData("razor")]
    [InlineData("scss")]
    [InlineData("sql")]
    [InlineData("typescript")]
    [InlineData("urlencoded")]
    [InlineData("vbnet")]
    [InlineData("x86asm")]
    [InlineData("xml")]
    [InlineData("yaml")]
    public void EveryGrammarRegex_HasAFiniteMatchTimeout(string language)
    {
        var assembly = typeof(SyntaxHighlighter).Assembly;
        var registry = assembly.GetType("Meziantou.Framework.SyntaxHighlighting.Languages.LanguageRegistry", throwOnError: true)!;
        var root = registry.GetMethod("Get", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [language])!;

        var infinite = new List<string>();
        Collect(root, new HashSet<object>(ReferenceEqualityComparer.Instance), infinite);

        Assert.Empty(infinite);
    }

    [Fact]
    public void EveryBeginGuardRegex_HasAFiniteMatchTimeout()
    {
        var assembly = typeof(SyntaxHighlighter).Assembly;
        var guards = assembly.GetType("Meziantou.Framework.SyntaxHighlighting.Engine.BeginGuards", throwOnError: true)!;

        var regexes = guards
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method => method.ReturnType == typeof(Regex) && method.GetParameters().Length is 0)
            .Select(method => (Regex)method.Invoke(null, null)!)
            .ToList();

        Assert.NotEmpty(regexes);
        Assert.Empty(regexes.Where(regex => regex.MatchTimeout == Regex.InfiniteMatchTimeout).Select(regex => regex.ToString()));
    }

    private static void Collect(object? compiledMode, HashSet<object> visited, List<string> infinite)
    {
        if (compiledMode is null || !visited.Add(compiledMode))
            return;

        var type = compiledMode.GetType();
        foreach (var name in (string[])["BeginRe", "EndRe", "IllegalRe", "KeywordPatternRe"])
        {
            if (type.GetField(name)!.GetValue(compiledMode) is Regex regex && regex.MatchTimeout == Regex.InfiniteMatchTimeout)
            {
                infinite.Add($"{name}: {regex}");
            }
        }

        foreach (var child in (IEnumerable)type.GetField("Contains")!.GetValue(compiledMode)!)
        {
            Collect(child, visited, infinite);
        }

        Collect(type.GetField("Starts")!.GetValue(compiledMode), visited, infinite);
    }
}
