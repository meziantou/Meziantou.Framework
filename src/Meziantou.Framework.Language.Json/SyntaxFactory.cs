namespace Meziantou.Framework.Language.Json;

#pragma warning disable CA1720 // Keep factory method names aligned with JSON syntax concepts.

/// <summary>Creates JSON syntax nodes, tokens, and trivia programmatically.</summary>
public static class SyntaxFactory
{
    public static JsonSyntaxTree ParseText(string text) => JsonSyntaxTree.ParseText(text);

    public static JsonSyntaxToken Token(
        JsonSyntaxKind kind,
        string text,
        string? valueText = null,
        bool isMissing = false,
        IReadOnlyList<JsonSyntaxTrivia>? leadingTrivia = null,
        IReadOnlyList<JsonSyntaxTrivia>? trailingTrivia = null)
    {
        return new JsonSyntaxToken(kind, text, valueText, isMissing, leadingTrivia, trailingTrivia);
    }

    public static JsonSyntaxTrivia Trivia(JsonSyntaxKind kind, string text) => new(kind, text);
    public static JsonSyntaxToken StringToken(string value) => new(JsonSyntaxKind.StringToken, "\"" + EscapeString(value) + "\"", value);
    public static JsonSyntaxToken NumberToken(string text) => new(JsonSyntaxKind.NumberToken, text, text);

    public static JsonDocumentSyntax Document(JsonValueSyntax value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new JsonDocumentSyntax([value], new JsonSyntaxToken(JsonSyntaxKind.EndOfFileToken, string.Empty), value.ToFullString());
    }

    public static JsonObjectSyntax Object(params JsonMemberSyntax[] members)
    {
        ArgumentNullException.ThrowIfNull(members);

        return Object((IEnumerable<JsonMemberSyntax>)members);
    }

    public static JsonObjectSyntax Object(IEnumerable<JsonMemberSyntax> members)
    {
        ArgumentNullException.ThrowIfNull(members);

        var memberList = members.ToArray();
        var nodes = new JsonSyntaxNode[memberList.Length];
        for (var index = 0; index < memberList.Length; index++)
        {
            var member = memberList[index];
            if (index < memberList.Length - 1 && member.CommaToken is null)
            {
                member = member.WithCommaToken(new JsonSyntaxToken(JsonSyntaxKind.CommaToken, ","));
            }

            nodes[index] = member;
        }

        return new JsonObjectSyntax(
            new JsonSyntaxToken(JsonSyntaxKind.OpenBraceToken, "{"),
            nodes,
            new JsonSyntaxToken(JsonSyntaxKind.CloseBraceToken, "}"));
    }

    public static JsonMemberSyntax Member(string name, JsonValueSyntax value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);

        return new JsonMemberSyntax(StringToken(name), new JsonSyntaxToken(JsonSyntaxKind.ColonToken, ":"), value);
    }

    public static JsonArraySyntax Array(params JsonValueSyntax[] values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var elements = new JsonArrayElementSyntax[values.Length];
        for (var index = 0; index < values.Length; index++)
        {
            var comma = index < values.Length - 1 ? new JsonSyntaxToken(JsonSyntaxKind.CommaToken, ",") : null;
            elements[index] = new JsonArrayElementSyntax(values[index], comma);
        }

        return Array(elements);
    }

    public static JsonArraySyntax Array(IEnumerable<JsonArrayElementSyntax> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);

        return new JsonArraySyntax(
            new JsonSyntaxToken(JsonSyntaxKind.OpenBracketToken, "["),
            elements.Cast<JsonSyntaxNode>().ToArray(),
            new JsonSyntaxToken(JsonSyntaxKind.CloseBracketToken, "]"));
    }

    public static JsonStringSyntax String(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new JsonStringSyntax(StringToken(value));
    }

    public static JsonNumberSyntax Number(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new JsonNumberSyntax(NumberToken(text));
    }

    public static JsonLiteralSyntax TrueLiteral()
    {
        return new JsonLiteralSyntax(new JsonSyntaxToken(JsonSyntaxKind.TrueKeyword, "true", "true"));
    }

    public static JsonLiteralSyntax FalseLiteral()
    {
        return new JsonLiteralSyntax(new JsonSyntaxToken(JsonSyntaxKind.FalseKeyword, "false", "false"));
    }

    public static JsonLiteralSyntax NullLiteral()
    {
        return new JsonLiteralSyntax(new JsonSyntaxToken(JsonSyntaxKind.NullKeyword, "null", "null"));
    }

    public static JsonSkippedTextSyntax SkippedText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new JsonSkippedTextSyntax([new JsonSyntaxToken(JsonSyntaxKind.BadToken, text, text)], fullStart: 0);
    }

    private static string EscapeString(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (char.IsControl(character))
                    {
                        builder.Append("\\u");
                        builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        return builder.ToString();
    }
}
