using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.SyntaxHighlighting.Tests;

/// <summary>
/// The security-critical invariant of the highlighter: the only markup in the output is the
/// emitter's own <c>&lt;span&gt;</c> tags, and everything else is the input, HTML-escaped. If
/// input text could ever escape that, the highlighter would be an HTML injection vector for
/// anyone rendering untrusted code. The golden tests cover this incidentally; this states it
/// directly and checks it against hostile and randomized input.
/// </summary>
public sealed partial class EscapingTests
{
    // \G so the match is anchored at the scan position rather than the start of the string.
    [GeneratedRegex("""\G(</span>|<span class="[A-Za-z0-9_\- ]*">)""")]
    private static partial Regex EmitterTag();

    private static readonly string[] HostileInputs =
    [
        "<script>alert(1)</script>",
        "\" onload=\"alert(1)",
        "</span><script>x</script>",
        "<!--<script>-->",
        "&lt;script&gt;",
        "'\"><img src=x onerror=alert(1)>",
        "</span></span></span>",
        "<span class=\"hljs-keyword\">",
        "&#x27;&amp;",
        "&",
        "<",
        ">",
        "\"",
        "'",
    ];

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
    public void Highlight_TextNeverEscapesTheEmittersTags(string language)
    {
        foreach (var input in GetInputs())
        {
            var html = SyntaxHighlighter.Highlight(input, language);
            var text = StripEmitterTags(html, input, language);

            Assert.Equal(input, WebUtility.HtmlDecode(text));
        }
    }

    private static IEnumerable<string> GetInputs()
    {
        foreach (var hostile in HostileInputs)
            yield return hostile;

        // Deterministic pseudo-random inputs over an alphabet weighted towards the characters
        // that delimit modes, so the fuzzing reaches unusual tokenizer states.
        const string Alphabet = "<>&\"'/\\{}()[]@#$%^*-=+:;,.`~|! \t\n\rabzABZ019_é中\U0001F600";
        var random = new Random(20260827);
        for (var i = 0; i < 200; i++)
        {
            var length = random.Next(1, 60);
            var builder = new StringBuilder(length);
            for (var j = 0; j < length; j++)
                builder.Append(Alphabet[random.Next(Alphabet.Length)]);

            yield return builder.ToString();
        }
    }

    private static string StripEmitterTags(string html, string input, string language)
    {
        var text = new StringBuilder(html.Length);
        var index = 0;
        while (index < html.Length)
        {
            if (html[index] is '<')
            {
                var match = EmitterTag().Match(html, index);
                Assert.True(match.Success, $"Unescaped '<' in the output for language '{language}' and input '{input}': {html}");
                index += match.Length;
                continue;
            }

            text.Append(html[index]);
            index++;
        }

        return text.ToString();
    }
}
