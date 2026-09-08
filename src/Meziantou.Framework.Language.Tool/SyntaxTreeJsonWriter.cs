using System.Text.Encodings.Web;
using System.Text.Json;

namespace Meziantou.Framework.LanguageTool;

/// <summary>
/// Holds the shape of the dump. The four languages share no types, so each has its own writer, but every field name
/// and every field's shape is decided here so the four dumps stay the same document format.
/// </summary>
internal static class SyntaxTreeJsonWriter
{
    /// <summary>A node costs about three JSON levels, so the writer's default limit of 1000 is soon reached.</summary>
    private const int MaxDepth = 1_000_000;

    public static JsonWriterOptions CreateWriterOptions() => new()
    {
        Indented = false,
        // The default encoder escapes '<', '>', '&' and every non-ASCII character, which makes an XML dump unreadable.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        MaxDepth = MaxDepth,
    };

    public static void WriteSpan(Utf8JsonWriter writer, string propertyName, int start, int length, DumpOptions options)
    {
        if (!options.IncludeSpans)
            return;

        writer.WriteStartObject(propertyName);
        writer.WriteNumber("start", start);
        writer.WriteNumber("length", length);
        writer.WriteEndObject();
    }

    /// <summary>Opens a node object and writes everything that comes before its tokens and children.</summary>
    public static void WriteNodeStart(Utf8JsonWriter writer, object node, string kind, string? text, int spanStart, int spanLength, int fullSpanStart, int fullSpanLength, DumpOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", kind);
        writer.WriteString("type", node.GetType().Name);
        WriteSpan(writer, "span", spanStart, spanLength, options);
        WriteSpan(writer, "fullSpan", fullSpanStart, fullSpanLength, options);
        if (text is not null)
        {
            writer.WriteString("text", text);
        }
    }

    /// <summary>Opens a token object and writes everything that comes before its trivia.</summary>
    public static void WriteTokenStart(Utf8JsonWriter writer, string kind, string text, string valueText, bool isMissing, int spanStart, int spanLength, int fullSpanStart, int fullSpanLength, DumpOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", kind);
        if (options.IncludeText)
        {
            writer.WriteString("text", text);
            if (!string.Equals(valueText, text, StringComparison.Ordinal))
            {
                writer.WriteString("valueText", valueText);
            }
        }

        if (isMissing)
        {
            writer.WriteBoolean("isMissing", value: true);
        }

        WriteSpan(writer, "span", spanStart, spanLength, options);
        WriteSpan(writer, "fullSpan", fullSpanStart, fullSpanLength, options);
    }

    public static void WriteTrivia(Utf8JsonWriter writer, string kind, string text, int spanStart, int spanLength, DumpOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", kind);
        if (options.IncludeText)
        {
            writer.WriteString("text", text);
        }

        WriteSpan(writer, "span", spanStart, spanLength, options);
        writer.WriteEndObject();
    }

    public static void WriteDiagnostic(Utf8JsonWriter writer, string id, string message, string severity, int spanStart, int spanLength, DumpOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("id", id);
        writer.WriteString("severity", severity);
        writer.WriteString("message", message);
        WriteSpan(writer, "span", spanStart, spanLength, options);
        writer.WriteEndObject();
    }
}
