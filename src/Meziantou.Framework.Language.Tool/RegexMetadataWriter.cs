using System.Text.Json;
using Meziantou.Framework.Language.Regex;

namespace Meziantou.Framework.Language.Tool;

/// <summary>
/// The header fields only a regular expression has. The capture table and the pattern options are not recoverable
/// from the nodes alone, so they are worth carrying even though nothing else in the dump is language-specific.
/// </summary>
internal static class RegexMetadataWriter
{
    public static void Write(Utf8JsonWriter writer, SyntaxTree tree, DumpOptions options)
    {
        if (tree is not RegexSyntaxTree regexTree)
            return;

        WritePatternOptions(writer, regexTree.PatternOptions);
        WriteCaptures(writer, regexTree.Captures, options);
    }

    /// <summary>The options in effect at the start of the pattern, as the names of the flags that are set.</summary>
    private static void WritePatternOptions(Utf8JsonWriter writer, RegexPatternOptions patternOptions)
    {
        if (patternOptions == RegexPatternOptions.None)
            return;

        writer.WriteStartArray("patternOptions");
        foreach (var value in Enum.GetValues<RegexPatternOptions>())
        {
            if (value != RegexPatternOptions.None && patternOptions.HasFlag(value))
            {
                writer.WriteStringValue(value.ToString());
            }
        }

        writer.WriteEndArray();
    }

    private static void WriteCaptures(Utf8JsonWriter writer, IReadOnlyList<RegexCaptureInfo> captures, DumpOptions options)
    {
        if (captures.Count == 0)
            return;

        writer.WriteStartArray("captures");
        foreach (var capture in captures)
        {
            writer.WriteStartObject();
            writer.WriteNumber("number", capture.Number);
            writer.WriteString("name", capture.Name);
            if (options.IncludeSpans)
            {
                writer.WriteStartObject("span");
                writer.WriteNumber("start", capture.Span.Start);
                writer.WriteNumber("length", capture.Span.Length);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
}
