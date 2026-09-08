using System.Text.Json;
using Meziantou.Framework.Language.Json;

namespace Meziantou.Framework.Language.Tool;

internal static class JsonSyntaxTreeWriter
{
    public static void Write(Utf8JsonWriter writer, JsonSyntaxTree tree, DumpOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("language", "json");

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

    // Walked with an explicit stack: the JSON parser puts no limit on nesting depth, so recursion here would run the
    // stack out on input that parsed perfectly well. A null entry closes the node whose children were just written.
    private static void WriteNode(Utf8JsonWriter writer, JsonSyntaxNode root, DumpOptions options)
    {
        var stack = new Stack<JsonSyntaxNode?>();
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

    private static void WriteTokens(Utf8JsonWriter writer, IReadOnlyList<JsonSyntaxToken> tokens, DumpOptions options)
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

    private static void WriteTrivia(Utf8JsonWriter writer, string propertyName, IReadOnlyList<JsonSyntaxTrivia> trivia, DumpOptions options)
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
