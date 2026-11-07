using System.Text.Encodings.Web;
using System.Text.Json;

namespace Meziantou.Framework.Language.Tool;

/// <summary>
/// Serializes a syntax tree. The four languages share one tree, so one writer covers them all: it only ever touches
/// <see cref="SyntaxNode"/>, <see cref="SyntaxToken"/> and <see cref="SyntaxTrivia"/>, and asks the language only for
/// the name of a raw kind.
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

    public static void Write(Utf8JsonWriter writer, SyntaxTree tree, SyntaxLanguage language, DumpOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("language", language.Family.ToString().ToLowerInvariant());

        var dialect = language.DialectName;
        if (dialect is not null)
        {
            writer.WriteString("dialect", dialect);
        }

        RegexMetadataWriter.Write(writer, tree, options);
        WriteDiagnostics(writer, tree, options);

        writer.WritePropertyName("root");
        WriteNode(writer, tree.GetRoot(), language, options);
        writer.WriteEndObject();
    }

    private static void WriteDiagnostics(Utf8JsonWriter writer, SyntaxTree tree, DumpOptions options)
    {
        var diagnostics = tree.GetDiagnostics();
        if (diagnostics.Count == 0)
            return;

        writer.WriteStartArray("diagnostics");
        foreach (var diagnostic in diagnostics)
        {
            writer.WriteStartObject();
            writer.WriteString("id", diagnostic.Id);
            writer.WriteString("severity", diagnostic.Severity.ToString());
            writer.WriteString("message", diagnostic.Message);

            var location = diagnostic.Location;
            WriteSpan(writer, "span", location.SourceSpan, options);
            if (options.IncludeSpans && location.SourceText is not null)
            {
                var lineSpan = location.GetLineSpan();
                writer.WriteStartObject("lineSpan");
                WriteLinePosition(writer, "start", lineSpan.Start);
                WriteLinePosition(writer, "end", lineSpan.End);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteLinePosition(Utf8JsonWriter writer, string propertyName, LinePosition position)
    {
        writer.WriteStartObject(propertyName);
        writer.WriteNumber("line", position.Line);
        writer.WriteNumber("character", position.Character);
        writer.WriteEndObject();
    }

    // Walked with an explicit stack: the JSON and XML parsers put no limit on nesting depth, so recursion here would
    // run the stack out on input that parsed perfectly well. A null entry closes the node whose children were just
    // written; SyntaxNodeOrToken is a struct whose default value still answers AsToken, so it cannot be the marker.
    private static void WriteNode(Utf8JsonWriter writer, SyntaxNode root, SyntaxLanguage language, DumpOptions options)
    {
        var stack = new Stack<SyntaxNodeOrToken?>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            if (stack.Pop() is not { } current)
            {
                writer.WriteEndArray();
                writer.WriteEndObject();
                continue;
            }

            if (current.AsNode(out var node))
            {
                WriteNodeStart(writer, node, language, options);

                // --no-tokens keeps only the nodes, which leaves the shape of the tree without the lexical detail.
                var children = node.ChildNodesAndTokens();
                var emitted = 0;
                foreach (var child in children)
                {
                    if (options.IncludeTokens || child.IsNode)
                    {
                        emitted++;
                    }
                }

                if (emitted == 0)
                {
                    writer.WriteEndObject();
                    continue;
                }

                writer.WriteStartArray("children");
                stack.Push(null);
                for (var i = children.Count - 1; i >= 0; i--)
                {
                    var child = children[i];
                    if (options.IncludeTokens || child.IsNode)
                    {
                        stack.Push(child);
                    }
                }
            }
            else
            {
                WriteToken(writer, current.AsToken(), language, options);
            }
        }
    }

    private static void WriteNodeStart(Utf8JsonWriter writer, SyntaxNode node, SyntaxLanguage language, DumpOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", language.GetKindName(node.RawKind));
        writer.WriteString("type", node.GetType().Name);
        if (node.IsMissing)
        {
            writer.WriteBoolean("isMissing", value: true);
        }

        WriteSpan(writer, "span", node.Span, options);
        WriteSpan(writer, "fullSpan", node.FullSpan, options);
        if (options.IncludeText)
        {
            writer.WriteString("text", node.ToFullString());
        }
    }

    private static void WriteToken(Utf8JsonWriter writer, SyntaxToken token, SyntaxLanguage language, DumpOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", language.GetKindName(token.RawKind));
        writer.WriteString("type", nameof(SyntaxToken));
        if (options.IncludeText)
        {
            writer.WriteString("text", token.Text);
            if (!string.Equals(token.ValueText, token.Text, StringComparison.Ordinal))
            {
                writer.WriteString("valueText", token.ValueText);
            }
        }

        if (token.IsMissing)
        {
            writer.WriteBoolean("isMissing", value: true);
        }

        WriteSpan(writer, "span", token.Span, options);
        WriteSpan(writer, "fullSpan", token.FullSpan, options);
        WriteTrivia(writer, "leadingTrivia", token.LeadingTrivia, language, options);
        WriteTrivia(writer, "trailingTrivia", token.TrailingTrivia, language, options);
        writer.WriteEndObject();
    }

    private static void WriteTrivia(Utf8JsonWriter writer, string propertyName, SyntaxTriviaList trivia, SyntaxLanguage language, DumpOptions options)
    {
        if (!options.IncludeTrivia || trivia.Count == 0)
            return;

        writer.WriteStartArray(propertyName);
        foreach (var item in trivia)
        {
            writer.WriteStartObject();
            writer.WriteString("kind", language.GetKindName(item.RawKind));
            if (options.IncludeText)
            {
                writer.WriteString("text", item.ToFullString());
            }

            WriteSpan(writer, "span", item.Span, options);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteSpan(Utf8JsonWriter writer, string propertyName, TextSpan span, DumpOptions options)
    {
        if (!options.IncludeSpans)
            return;

        writer.WriteStartObject(propertyName);
        writer.WriteNumber("start", span.Start);
        writer.WriteNumber("length", span.Length);
        writer.WriteEndObject();
    }
}
