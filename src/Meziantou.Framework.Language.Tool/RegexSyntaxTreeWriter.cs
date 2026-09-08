using System.Text.Json;
using Meziantou.Framework.Language.Regex;

namespace Meziantou.Framework.LanguageTool;

internal static class RegexSyntaxTreeWriter
{
    public static void Write(Utf8JsonWriter writer, RegexSyntaxTree tree, DumpOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("language", "regex");
        writer.WriteString("dialect", tree.Dialect.Name);
        WritePatternOptions(writer, tree.PatternOptions);
        WriteCaptures(writer, tree.Captures, options);

        if (tree.Diagnostics.Count > 0)
        {
            writer.WriteStartArray("diagnostics");
            foreach (var diagnostic in tree.Diagnostics)
            {
                SyntaxTreeJsonWriter.WriteDiagnostic(writer, diagnostic.Id, diagnostic.Message, diagnostic.Severity.ToString(), diagnostic.Span.Start, diagnostic.Span.Length, options);
            }

            writer.WriteEndArray();
        }

        writer.WritePropertyName("root");
        WriteNode(writer, tree.Root, options);
        writer.WriteEndObject();
    }

    // Walked with an explicit stack rather than by recursion, the way the library walks its own tree.
    // A null entry closes the node whose children were just written.
    private static void WriteNode(Utf8JsonWriter writer, RegexSyntaxNode root, DumpOptions options)
    {
        var stack = new Stack<RegexSyntaxNode?>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is null)
            {
                writer.WriteEndArray();
                writer.WriteEndObject();
                continue;
            }

            var span = node.Span;
            var fullSpan = node.FullSpan;
            SyntaxTreeJsonWriter.WriteNodeStart(writer, node, node.Kind.ToString(), options.IncludeText ? node.ToFullString() : null, span.Start, span.Length, fullSpan.Start, fullSpan.Length, options);
            WriteTokens(writer, node.Tokens, options);

            var childNodes = node.ChildNodes;
            if (childNodes.Count == 0)
            {
                writer.WriteEndObject();
                continue;
            }

            writer.WriteStartArray("childNodes");
            stack.Push(null);
            for (var i = childNodes.Count - 1; i >= 0; i--)
            {
                stack.Push(childNodes[i]);
            }
        }
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

    /// <summary>The capture groups the pattern declares. They are not recoverable from the nodes alone.</summary>
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
            SyntaxTreeJsonWriter.WriteSpan(writer, "span", capture.Span.Start, capture.Span.Length, options);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteTokens(Utf8JsonWriter writer, IReadOnlyList<RegexSyntaxToken> tokens, DumpOptions options)
    {
        if (!options.IncludeTokens || tokens.Count == 0)
            return;

        writer.WriteStartArray("tokens");
        foreach (var token in tokens)
        {
            var span = token.Span;
            var fullSpan = token.FullSpan;
            SyntaxTreeJsonWriter.WriteTokenStart(writer, token.Kind.ToString(), token.Text, token.ValueText, token.IsMissing, span.Start, span.Length, fullSpan.Start, fullSpan.Length, options);
            WriteTrivia(writer, "leadingTrivia", token.LeadingTrivia, options);
            WriteTrivia(writer, "trailingTrivia", token.TrailingTrivia, options);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteTrivia(Utf8JsonWriter writer, string propertyName, IReadOnlyList<RegexSyntaxTrivia> trivia, DumpOptions options)
    {
        if (!options.IncludeTrivia || trivia.Count == 0)
            return;

        writer.WriteStartArray(propertyName);
        foreach (var item in trivia)
        {
            SyntaxTreeJsonWriter.WriteTrivia(writer, item.Kind.ToString(), item.Text, item.Span.Start, item.Span.Length, options);
        }

        writer.WriteEndArray();
    }
}
