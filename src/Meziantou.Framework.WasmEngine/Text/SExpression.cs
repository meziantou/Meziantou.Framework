using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Meziantou.Framework.WasmEngine.Text;

internal enum TokenKind : byte
{
    Keyword,
    Id,
    String,
    Number,
    Reserved,
}

/// <summary>A node of the text format: a token, or a parenthesized list of nodes (https://webassembly.github.io/spec/core/text/lexical.html).</summary>
internal sealed class SNode
{
    private SNode(int position)
    {
        Position = position;
    }

    public int Position { get; }

    public bool IsList => Items is not null;

    public List<SNode>? Items { get; private init; }

    public TokenKind Kind { get; private init; }

    /// <summary>The text of a token (the content of an id without the '$', the bytes of a string are in <see cref="Bytes"/>).</summary>
    public string Text { get; private init; } = "";

    public byte[] Bytes { get; private init; } = [];

    public static SNode List(int position, List<SNode> items) => new(position) { Items = items };

    public static SNode Token(int position, TokenKind kind, string text, byte[]? bytes = null) => new(position) { Kind = kind, Text = text, Bytes = bytes ?? [] };

    public bool IsKeyword(string keyword) => !IsList && Kind == TokenKind.Keyword && Text == keyword;

    /// <summary>Whether the node is a list whose first item is the keyword.</summary>
    public bool IsListOf(string keyword) => IsList && Items!.Count > 0 && Items[0].IsKeyword(keyword);

    /// <summary>The keyword of a list (its first item), or null.</summary>
    public string? Head => IsList && Items!.Count > 0 && Items[0] is { IsList: false, Kind: TokenKind.Keyword } head ? head.Text : null;

    // The lists nested too deeply for the stack are elided
    public override string ToString() => IsList ? (RuntimeHelpers.TryEnsureSufficientExecutionStack() ? "(" + string.Join(' ', Items!) + ")" : "(...)") : Kind switch
    {
        TokenKind.Id => "$" + Text,
        TokenKind.String => "\"" + Text + "\"",
        _ => Text,
    };
}

/// <summary>A malformed text.</summary>
#pragma warning disable CA1032 // Internal: the message has the position of the error
internal sealed class WatException(string message, int position) : FormatException(message + " (at offset " + position.ToString(CultureInfo.InvariantCulture) + ")")
{
    public int Position { get; } = position;

    /// <summary>Throws when the stack is too small to read or encode a nested node: deeply nested text is malformed, rather than a stack overflow.</summary>
    public static void ThrowIfNestedTooDeeply(int position)
    {
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
            throw new WatException("nesting too deep", position);
    }
}
#pragma warning restore CA1032

/// <summary>
/// Reads the tokens and the lists of the text format. The annotations (<c>(@id ...)</c>) are skipped, except the custom
/// annotations of a module (<c>(@custom ...)</c>), which are lists whose head is the keyword <c>@custom</c>.
/// </summary>
internal static class SExpressionReader
{
    public static List<SNode> Parse(string text)
    {
        var position = 0;
        var result = new List<SNode>();
        while (true)
        {
            SkipWhitespace(text, ref position);
            if (position >= text.Length)
                return result;

            if (ReadNode(text, ref position, inAnnotation: false, keepCustom: true) is { } node)
            {
                result.Add(node);
            }
        }
    }

    private static void SkipWhitespace(string text, ref int position)
    {
        while (position < text.Length)
        {
            var c = text[position];
            if (c is ' ' or '\t' or '\n' or '\r')
            {
                position++;
            }
            else if (c == ';' && position + 1 < text.Length && text[position + 1] == ';')
            {
                // A line comment ends at a newline: \n, \r or \r\n
                while (position < text.Length && text[position] is not '\n' and not '\r')
                {
                    position++;
                }
            }
            else if (c == '(' && position + 1 < text.Length && text[position + 1] == ';')
            {
                SkipBlockComment(text, ref position);
            }
            else
            {
                return;
            }
        }
    }

    private static void SkipBlockComment(string text, ref int position)
    {
        var start = position;
        var depth = 0;
        while (position < text.Length)
        {
            if (text[position] == '(' && position + 1 < text.Length && text[position + 1] == ';')
            {
                depth++;
                position += 2;
            }
            else if (text[position] == ';' && position + 1 < text.Length && text[position + 1] == ')')
            {
                depth--;
                position += 2;
                if (depth == 0)
                    return;
            }
            else
            {
                position++;
            }
        }

        throw new WatException("unclosed comment", start);
    }

    /// <summary>Reads a node, or returns null for an annotation. The tokens of an annotation can be any reserved token.</summary>
    private static SNode? ReadNode(string text, ref int position, bool inAnnotation, bool keepCustom)
    {
        var start = position;
        var c = text[position];
        if (c == '(')
        {
            WatException.ThrowIfNestedTooDeeply(start);
            position++;
            var annotation = false;
            var custom = false;
            // The lists of an annotation are not annotations: (@a (@) (@ x)) is valid
            if (!inAnnotation && position < text.Length && text[position] == '@')
            {
                // The id of the annotation: (@id ...) or (@"name" ...)
                annotation = true;
                position++;
                var idStart = position;
                if (position < text.Length && text[position] == '"')
                {
                    var name = ReadString(text, ref position);
                    if (name.Length == 0)
                        throw new WatException("empty annotation id", start);

                    custom = keepCustom && DecodeName(name, idStart) == "custom";
                }
                else
                {
                    while (position < text.Length && IsIdChar(text[position]))
                    {
                        position++;
                    }

                    custom = keepCustom && text.AsSpan(idStart, position - idStart).SequenceEqual("custom");
                    if (position == idStart)
                        throw new WatException("empty annotation id", start);
                }
            }

            var items = new List<SNode>();
            if (custom)
            {
                items.Add(SNode.Token(start, TokenKind.Keyword, "@custom"));
            }

            while (true)
            {
                SkipWhitespace(text, ref position);
                if (position >= text.Length)
                    throw new WatException(annotation || inAnnotation ? "unclosed annotation" : "unclosed parenthesis", start);

                if (text[position] == ')')
                {
                    position++;
                    return annotation && !custom ? null : SNode.List(start, items);
                }

                // The custom annotations are kept in a module, and in a core module of a component: (core module ...)
                var inModule = items.Count > 0 && (items[0].IsKeyword("module") || (items.Count > 1 && items[0].IsKeyword("core") && items[1].IsKeyword("module")));
                if (ReadNode(text, ref position, inAnnotation || annotation, keepCustom: !annotation && !inAnnotation && inModule) is { } item)
                {
                    items.Add(item);
                }
            }
        }

        if (c == ')')
            throw new WatException("unexpected token", start);

        if (inAnnotation)
            return ReadReservedToken(text, ref position);

        if (c == '"')
        {
            var bytes = ReadString(text, ref position);
            CheckSeparated(text, position, start);
            return SNode.Token(start, TokenKind.String, Encoding.UTF8.GetString(bytes), bytes);
        }

        while (position < text.Length && IsIdChar(text[position]))
        {
            position++;
        }

        // An id can be a string: $"name"
        if (position < text.Length && text[position] == '"' && position == start + 1 && c == '$')
        {
            var bytes = ReadString(text, ref position);
            if (bytes.Length == 0)
                throw new WatException("empty identifier", start);

            CheckSeparated(text, position, start);
            return SNode.Token(start, TokenKind.Id, DecodeName(bytes, start));
        }

        if (position == start)
            throw new WatException(text[start] is < ' ' or > '~' ? "illegal character" : "unknown operator", start);

        CheckSeparated(text, position, start);

        var token = text[start..position];
        if (c == '$')
        {
            if (token.Length == 1)
                throw new WatException("empty identifier", start);

            return SNode.Token(start, TokenKind.Id, token[1..]);
        }

        if (c is >= 'a' and <= 'z')
            return SNode.Token(start, TokenKind.Keyword, token);

        if (c is >= '0' and <= '9' or '+' or '-')
            return SNode.Token(start, TokenKind.Number, token);

        return SNode.Token(start, TokenKind.Reserved, token);
    }

    private static string DecodeName(byte[] bytes, int position)
    {
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new WatException("malformed UTF-8 encoding", position);
        }
    }

    /// <summary>The tokens must be separated by whitespace, a comment or a parenthesis.</summary>
    private static void CheckSeparated(string text, int position, int start)
    {
        if (position < text.Length && text[position] is not (' ' or '\t' or '\n' or '\r' or '(' or ')' or ';'))
            throw new WatException("unknown operator", start);
    }

    /// <summary>A token of an annotation: a sequence of id characters, strings and the characters , ; [ ] { } (https://webassembly.github.io/spec/core/text/lexical.html#tokens).</summary>
    private static SNode ReadReservedToken(string text, ref int position)
    {
        var start = position;
        byte[]? singleString = null;
        var parts = 0;
        while (position < text.Length)
        {
            var c = text[position];
            if (IsIdChar(c) || c is ',' or '[' or ']' or '{' or '}' || (c == ';' && (position + 1 >= text.Length || text[position + 1] != ';')))
            {
                position++;
                parts++;
            }
            else if (c == '"')
            {
                singleString = ReadString(text, ref position);
                parts++;
            }
            else
            {
                break;
            }
        }

        if (position == start)
            throw new WatException(text[start] is < ' ' or > '~' ? "illegal character" : "unknown operator", start);

        // A string alone is a string token (the data of a custom annotation)
        if (parts == 1 && singleString is not null)
            return SNode.Token(start, TokenKind.String, Encoding.UTF8.GetString(singleString), singleString);

        return SNode.Token(start, TokenKind.Reserved, text[start..position]);
    }

    public static bool IsIdChar(char c) => c is >= '0' and <= '9' or >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '/' or ':' or '<' or '=' or '>' or '?' or '@' or '\\' or '^' or '_' or '`' or '|' or '~';

    private static byte[] ReadString(string text, ref int position)
    {
        var start = position;
        position++;
        var result = new List<byte>();
        Span<byte> buffer = stackalloc byte[4];
        while (true)
        {
            if (position >= text.Length)
                throw new WatException("unclosed string", start);

            var c = text[position++];
            if (c == '"')
                return [.. result];

            if (c < 0x20 || c == 0x7F)
                throw new WatException("malformed string: control character", position - 1);

            if (c != '\\')
            {
                if (char.IsHighSurrogate(c) && position < text.Length && char.IsLowSurrogate(text[position]))
                {
                    var length = Encoding.UTF8.GetBytes([c, text[position++]], buffer);
                    result.AddRange(buffer[..length].ToArray());
                }
                else if (c < 0x80)
                {
                    result.Add((byte)c);
                }
                else
                {
                    var length = Encoding.UTF8.GetBytes([c], buffer);
                    result.AddRange(buffer[..length].ToArray());
                }

                continue;
            }

            if (position >= text.Length)
                throw new WatException("unclosed string", start);

            var escape = text[position++];
            switch (escape)
            {
                case 't': result.Add((byte)'\t'); break;
                case 'n': result.Add((byte)'\n'); break;
                case 'r': result.Add((byte)'\r'); break;
                case '"': result.Add((byte)'"'); break;
                case '\'': result.Add((byte)'\''); break;
                case '\\': result.Add((byte)'\\'); break;
                case 'u':
                {
                    if (position >= text.Length || text[position] != '{')
                        throw new WatException("malformed escape", position);

                    var end = text.IndexOf("}", position, StringComparison.Ordinal);
                    if (end < 0)
                        throw new WatException("malformed escape", position);

                    var digits = text[(position + 1)..end].Replace("_", "", StringComparison.Ordinal);
                    if (digits.Length == 0 || !int.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var codePoint) || codePoint > 0x10FFFF || codePoint is >= 0xD800 and <= 0xDFFF)
                        throw new WatException("malformed escape: invalid code point", position);

                    var length = Encoding.UTF8.GetBytes(char.ConvertFromUtf32(codePoint), buffer);
                    result.AddRange(buffer[..length].ToArray());
                    position = end + 1;
                    break;
                }

                default:
                    if (position >= text.Length || !Uri.IsHexDigit(escape) || !Uri.IsHexDigit(text[position]))
                        throw new WatException("malformed escape", position - 1);

                    result.Add((byte)((Uri.FromHex(escape) << 4) | Uri.FromHex(text[position++])));
                    break;
            }
        }
    }
}
