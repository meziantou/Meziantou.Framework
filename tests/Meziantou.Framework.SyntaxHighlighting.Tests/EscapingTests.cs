using System.Diagnostics.CodeAnalysis;
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
    [GeneratedRegex("""\G(?:</span>|<span class="[A-Za-z0-9_\- ]*">)""", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 10_000)]
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

        // A character outside the BMP where grammars match a single UTF-16 unit (an escape, a character literal, a
        // variable sigil), so a boundary falls between its two halves.
        "\\\U0001F600",
        "$\U0001F600",
        "?\U0001F600",
        "#\U0001F600 x",
        "'\U0001F600'",
    ];

    [Theory]
    [MemberData(nameof(Grammars), MemberType = typeof(Helper))]
    public void Highlight_TextNeverEscapesTheEmittersTags(string language)
    {
        foreach (var input in GetInputs())
        {
            var html = SyntaxHighlighter.Highlight(input, language);
            var text = StripEmitterTags(html, input, language);

            Assert.Equal(input, WebUtility.HtmlDecode(text));
        }
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Generating test inputs, and the fixed seed keeps the cases reproducible.")]
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
        var depth = 0;
        var index = 0;
        while (index < html.Length)
        {
            if (html[index] is '<')
            {
                var match = EmitterTag().Match(html, index);
                Assert.True(match.Success, $"Unescaped '<' in the output for language '{language}' and input '{input}': {html}");

                depth += match.ValueSpan is "</span>" ? -1 : 1;
                if (depth < 0)
                    Assert.Fail($"A </span> closes no <span> in the output for language '{language}' and input '{input}': {html}");

                index += match.Length;
                continue;
            }

            // Markup between the two halves of a surrogate pair makes the output invalid UTF-16, which a strict encoder
            // rejects and a lenient one turns into two replacement characters.
            if (char.IsLowSurrogate(html[index]) && text.Length > 0 && char.IsHighSurrogate(text[^1]) && !char.IsHighSurrogate(html[index - 1]))
                Assert.Fail($"A tag splits a surrogate pair in the output for language '{language}' and input '{input}': {html}");

            text.Append(html[index]);
            index++;
        }

        if (depth != 0)
            Assert.Fail($"{depth} <span> are never closed in the output for language '{language}' and input '{input}': {html}");

        return text.ToString();
    }
}
