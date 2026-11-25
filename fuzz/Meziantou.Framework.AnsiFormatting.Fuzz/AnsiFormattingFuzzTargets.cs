using System.Globalization;
using System.Text.RegularExpressions;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.AnsiFormatting.Fuzz;

/// <summary>
/// <see cref="AnsiTextProcessor.RemoveAnsiSequences(string)"/> and its span overload agree, only remove characters, and
/// return the input instance when they remove nothing. They remove something only when
/// <see cref="AnsiTextProcessor.ContainsAnsiSequences(string)"/> (string and span) is <see langword="true"/>, and always then
/// for a text without OSC sequences. <see cref="AnsiTextProcessor.ParseTextWithAnsiStyles"/> only removes
/// characters, agrees with <c>RemoveAnsiSequences</c> on a text without OSC sequences, and its runs cover the text without
/// gaps, each run not empty and with a style other than the style of the previous run.
/// </summary>
public static partial class AnsiFormattingFuzzTargets
{
    private const char EscapeCharacter = '\u001b';

    private static readonly string[] Tokens =
    [
        "\u001b", "\u001b[", "\u001b]", "\u0007", "\u001b\\", "[", "]", ";", ":", "m", "K", "H", "A", "?", "?25l", "?25h", " ",
        "0", "1", "3", "4", "7", "22", "23", "24", "27", "31", "39", "42", "49", "97", "107", "38", "48", "5", "2", "255", "256",
        "38;5;", "48;5;", "38;2;", "48;2;", "38:5:", "38:2:", "38:2::", "4:3", "99999999999", "2147483647", "2147483648", "\u0000", "\u001b[0m", "\u001b[m", "8;;", "\u001b]8;;", "0;title",
    ];

    // Lines written by command-line tools: a logger, OSC 8 hyperlinks, syntax-highlighted code
    private static readonly string[] RealWorldTexts =
    [
        "\u001b[40m\u001b[32minfo\u001b[39m\u001b[22m\u001b[49m: Microsoft.Hosting.Lifetime[14]\u001b[0m Now listening on: \u001b[1mhttp://localhost:5000\u001b[22m \u001b[90m(12 ms)\u001b[0m \u001b[1;31mfailed\u001b[0m",
        "\u001b[32m✔\u001b[0m Build succeeded\u001b[K See \u001b]8;;https://example.com/build/42/report.html\u001b\\the report\u001b]8;;\u001b\\ and \u001b]8;;https://example.com/build/42/logs\u0007the logs\u001b]8;;\u0007",
        "\u001b[38;2;249;38;114mpublic\u001b[0m\u001b[38;2;248;248;242m \u001b[0m\u001b[38;2;102;217;239mstatic\u001b[0m\u001b[3;38;2;253;151;31mvalue\u001b[0m",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("ansi-remove", Remove) { Seeds = GetSeeds, Dictionary = Tokens },
        new("ansi-parse", Parse) { Seeds = GetSeeds, Dictionary = Tokens },
    ];

    private static void Remove(ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(data);

        var result = AnsiTextProcessor.RemoveAnsiSequences(text);
        FuzzAssert.Equal(result, AnsiTextProcessor.RemoveAnsiSequences(text.AsSpan()), "The string and span overloads of RemoveAnsiSequences disagree");
        CheckSubsequence(result, text, "RemoveAnsiSequences");

        var contains = AnsiTextProcessor.ContainsAnsiSequences(text);
        FuzzAssert.Equal(contains, AnsiTextProcessor.ContainsAnsiSequences(text.AsSpan()), "The string and span overloads of ContainsAnsiSequences disagree");
        if (result.Length != text.Length)
        {
            FuzzAssert.True(contains, "RemoveAnsiSequences removed a sequence ContainsAnsiSequences did not find");
        }
        else
        {
            FuzzAssert.True(ReferenceEquals(result, text), "RemoveAnsiSequences does not return the input when it removes nothing");

            // RemoveAnsiSequences keeps the rest of the text after an OSC sequence without terminator, where ContainsAnsiSequences goes on
            if (!text.Contains("\u001b]", StringComparison.Ordinal))
            {
                FuzzAssert.False(contains, "ContainsAnsiSequences found a sequence RemoveAnsiSequences did not remove");
            }
        }
    }

    private static void Parse(ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(data);

        var parsed = AnsiTextProcessor.ParseTextWithAnsiStyles(text);
        CheckSubsequence(parsed.Text, text, "ParseTextWithAnsiStyles");

        // RemoveAnsiSequences keeps the rest of the text after an OSC sequence without terminator, where ParseTextWithAnsiStyles goes on
        if (!text.Contains("\u001b]", StringComparison.Ordinal))
        {
            FuzzAssert.Equal(AnsiTextProcessor.RemoveAnsiSequences(text), parsed.Text, "ParseTextWithAnsiStyles and RemoveAnsiSequences disagree on the text");
        }

        if (!text.Contains(EscapeCharacter, StringComparison.Ordinal))
        {
            FuzzAssert.Equal(text, parsed.Text, "A text without escape character is not kept as-is");
        }

        var expectedStart = 0;
        AnsiTextProcessor.AnsiStyle? previousStyle = null;
        foreach (var run in parsed.Runs)
        {
            FuzzAssert.Equal(expectedStart, run.Start, "The runs do not cover the text without gaps");
            FuzzAssert.True(run.End > run.Start, string.Create(CultureInfo.InvariantCulture, $"The run {run.Start}-{run.End} is empty"));
            FuzzAssert.True(previousStyle is null || previousStyle != run.Style, string.Create(CultureInfo.InvariantCulture, $"The run {run.Start}-{run.End} has the style of the previous run"));
            if (run.Style == AnsiTextProcessor.AnsiStyle.None)
            {
                FuzzAssert.True(ReferenceEquals(run.Style, AnsiTextProcessor.AnsiStyle.None), "A run without style does not use AnsiStyle.None");
            }

            expectedStart = run.End;
            previousStyle = run.Style;
        }

        FuzzAssert.Equal(parsed.Text.Length, expectedStart, "The runs do not cover the whole text");
    }

    // The processed text is the input with some characters removed.
    private static void CheckSubsequence(string result, string input, string method)
    {
        var index = 0;
        foreach (var c in result)
        {
            index = input.IndexOf(c, index, StringComparison.Ordinal);
            FuzzAssert.True(index >= 0, $"{method} returned characters that are not in the input, in this order: {FuzzAssert.Format(result)}");
            index++;
        }
    }

    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        var source = FuzzResources.ReadText(typeof(AnsiFormattingFuzzTargets).Assembly, "AnsiTextProcessorTests.cs");
        var texts = StringLiteralRegex().Matches(source)
            .Select(match => Regex.Unescape(match.Groups["value"].Value))
            .Where(text => text.Contains(EscapeCharacter, StringComparison.Ordinal))
            .Concat(RealWorldTexts)
            .Append("Hello World")
            .Distinct(StringComparer.Ordinal);

        var index = 0;
        foreach (var text in texts)
        {
            yield return FuzzSeed.FromText(index.ToString(CultureInfo.InvariantCulture), text);
            index++;
        }
    }

    [GeneratedRegex("""(?<!@)"(?<value>(?:[^"\\\r\n]|\\.)*)"(?!")""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex StringLiteralRegex();
}
