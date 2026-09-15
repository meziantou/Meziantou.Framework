using System.Runtime.InteropServices;

namespace Meziantou.Framework.DependencyScanning.Internals;

/// <summary>
/// Minimal Swift lexer: it only distinguishes what the manifest scanner needs (identifiers, string literals, punctuation and operators)
/// and drops comments and whitespace. Strings containing interpolations are returned as a single token.
/// </summary>
internal static class SwiftLexer
{
    public static List<SwiftToken> Tokenize(string text)
    {
        var tokens = new List<SwiftToken>();

        // Explicit stack instead of recursion, so deeply nested interpolations cannot overflow the call stack
        var frames = new Stack<Frame>();
        frames.Push(Frame.Code(isInterpolation: false));

        var i = 0;
        while (i < text.Length)
        {
            var frame = frames.Peek();
            if (frame.Kind is FrameKind.String)
            {
                i = ScanStringCharacter(text, i, frames, tokens);
                continue;
            }

            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';
            var isTopLevel = frames.Count == 1;

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '/' && next == '/')
            {
                i = SkipLineComment(text, i);
                continue;
            }

            if (c == '/' && next == '*')
            {
                i = SkipBlockComment(text, i);
                continue;
            }

            if (frame.Kind is FrameKind.Interpolation)
            {
                if (c == '(')
                {
                    frames.Pop();
                    frames.Push(frame with { ParenthesisDepth = frame.ParenthesisDepth + 1 });
                    i++;
                    continue;
                }

                if (c == ')')
                {
                    frames.Pop();
                    if (frame.ParenthesisDepth > 0)
                    {
                        frames.Push(frame with { ParenthesisDepth = frame.ParenthesisDepth - 1 });
                    }

                    i++;
                    continue;
                }
            }

            if (c is '#' or '"')
            {
                var hashCount = CountHashes(text, i);
                var quoteIndex = i + hashCount;
                if (quoteIndex < text.Length && text[quoteIndex] == '"')
                {
                    var isMultiline = text.AsSpan(quoteIndex).StartsWith("\"\"\"", StringComparison.Ordinal);
                    var contentStart = quoteIndex + (isMultiline ? 3 : 1);
                    frames.Push(Frame.String(i, contentStart, hashCount, isMultiline));
                    i = contentStart;
                    continue;
                }

                // Compiler directive or attribute such as #if, #endif, #available
                var end = i + 1;
                while (end < text.Length && IsIdentifierCharacter(text[end]))
                {
                    end++;
                }

                AddToken(tokens, isTopLevel, SwiftTokenKind.Other, i, end);
                i = end;
                continue;
            }

            if (IsIdentifierStart(c))
            {
                var end = i + 1;
                while (end < text.Length && IsIdentifierCharacter(text[end]))
                {
                    end++;
                }

                AddToken(tokens, isTopLevel, SwiftTokenKind.Identifier, i, end);
                i = end;
                continue;
            }

            if (c == '`')
            {
                var end = text.IndexOf('`', i + 1, StringComparison.Ordinal);
                var lineEnd = text.AsSpan(i + 1).IndexOfAny('\r', '\n');
                if (end > i + 1 && (lineEnd < 0 || end < i + 1 + lineEnd))
                {
                    AddToken(tokens, isTopLevel, SwiftTokenKind.Identifier, i + 1, end);
                    i = end + 1;
                    continue;
                }

                AddToken(tokens, isTopLevel, SwiftTokenKind.Other, i, i + 1);
                i++;
                continue;
            }

            if (char.IsAsciiDigit(c))
            {
                var end = i + 1;
                while (end < text.Length && (IsIdentifierCharacter(text[end]) || (text[end] == '.' && end + 1 < text.Length && char.IsAsciiDigit(text[end + 1]))))
                {
                    end++;
                }

                AddToken(tokens, isTopLevel, SwiftTokenKind.Other, i, end);
                i = end;
                continue;
            }

            if (c == '.')
            {
                if (next == '.')
                {
                    var end = i + 1;
                    while (end < text.Length && (text[end] == '.' || IsOperatorCharacter(text, end)))
                    {
                        end++;
                    }

                    AddToken(tokens, isTopLevel, SwiftTokenKind.Operator, i, end);
                    i = end;
                    continue;
                }

                AddToken(tokens, isTopLevel, SwiftTokenKind.Punctuation, i, i + 1);
                i++;
                continue;
            }

            if (IsOperatorCharacter(text, i))
            {
                var end = i + 1;
                while (end < text.Length && IsOperatorCharacter(text, end))
                {
                    end++;
                }

                AddToken(tokens, isTopLevel, SwiftTokenKind.Operator, i, end);
                i = end;
                continue;
            }

            var kind = c is '(' or ')' or '[' or ']' or '{' or '}' or ',' or ':' or ';' ? SwiftTokenKind.Punctuation : SwiftTokenKind.Other;
            AddToken(tokens, isTopLevel, kind, i, i + 1);
            i++;
        }

        // Unterminated string: report the outermost one up to the end of the text
        Frame? outermostString = null;
        foreach (var frame in frames)
        {
            if (frame.Kind is FrameKind.String)
            {
                outermostString = frame;
            }
        }

        if (outermostString is { } unterminated)
        {
            tokens.Add(new SwiftToken(SwiftTokenKind.StringLiteral, unterminated.TokenStart, text.Length, unterminated.ContentStart, text.Length, unterminated.IsMultiline, IsTerminated: false));
        }

        return tokens;
    }

    public static string GetStringValue(string text, SwiftToken token)
    {
        var content = text.AsSpan(token.ContentStart, token.ContentEnd - token.ContentStart);
        if (!token.IsMultiline)
            return content.ToString();

        // Multiline string: the content starts on the line after the opening delimiter,
        // and the indentation of the closing delimiter is removed from every line
        var firstNewLine = content.IndexOfAny('\r', '\n');
        if (firstNewLine < 0)
            return content.ToString();

        var bodyStart = firstNewLine + 1;
        if (content[firstNewLine] == '\r' && bodyStart < content.Length && content[bodyStart] == '\n')
        {
            bodyStart++;
        }

        var body = content[bodyStart..];
        var lastNewLine = body.LastIndexOfAny('\r', '\n');
        var indentation = lastNewLine < 0 ? body : body[(lastNewLine + 1)..];
        if (!indentation.IsWhiteSpace())
            return body.ToString();

        body = lastNewLine < 0 ? [] : body[..lastNewLine];
        if (body.Length > 0 && body[^1] == '\r')
        {
            body = body[..^1];
        }

        var result = new StringBuilder(body.Length);
        var first = true;
        foreach (var line in body.EnumerateLines())
        {
            if (!first)
            {
                result.Append('\n');
            }

            first = false;
            result.Append(line.StartsWith(indentation, StringComparison.Ordinal) ? line[indentation.Length..] : line.TrimStart());
        }

        return result.ToString();
    }

    private static int ScanStringCharacter(string text, int i, Stack<Frame> frames, List<SwiftToken> tokens)
    {
        var frame = frames.Peek();
        var c = text[i];

        if (c == '\\' && HasHashes(text, i + 1, frame.HashCount))
        {
            var escapedIndex = i + 1 + frame.HashCount;
            if (escapedIndex < text.Length && text[escapedIndex] == '(')
            {
                frames.Push(Frame.Code(isInterpolation: true));
                return escapedIndex + 1;
            }

            // The escaped character must not terminate a single-line string
            if (escapedIndex < text.Length && text[escapedIndex] is '\r' or '\n' && !frame.IsMultiline)
                return escapedIndex;

            return Math.Min(escapedIndex + 1, text.Length);
        }

        if (!frame.IsMultiline && c is '\r' or '\n')
        {
            // Unterminated single-line string
            frames.Pop();
            AddToken(tokens, frames.Count == 1, new SwiftToken(SwiftTokenKind.StringLiteral, frame.TokenStart, i, frame.ContentStart, i, IsMultiline: false, IsTerminated: false));
            return i;
        }

        if (c == '"')
        {
            var quoteLength = frame.IsMultiline ? 3 : 1;
            if (text.AsSpan(i).StartsWith(frame.IsMultiline ? "\"\"\"" : "\"", StringComparison.Ordinal) && HasHashes(text, i + quoteLength, frame.HashCount))
            {
                var end = i + quoteLength + frame.HashCount;
                frames.Pop();
                AddToken(tokens, frames.Count == 1, new SwiftToken(SwiftTokenKind.StringLiteral, frame.TokenStart, end, frame.ContentStart, i, frame.IsMultiline, IsTerminated: true));
                return end;
            }
        }

        return i + 1;
    }

    private static int SkipLineComment(string text, int i)
    {
        var end = text.AsSpan(i).IndexOfAny('\r', '\n');
        return end < 0 ? text.Length : i + end;
    }

    private static int SkipBlockComment(string text, int i)
    {
        // Swift block comments nest
        var depth = 0;
        while (i < text.Length)
        {
            if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                depth++;
                i += 2;
            }
            else if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == '/')
            {
                depth--;
                i += 2;
                if (depth == 0)
                    return i;
            }
            else
            {
                i++;
            }
        }

        return text.Length;
    }

    private static int CountHashes(string text, int i)
    {
        var count = 0;
        while (i + count < text.Length && text[i + count] == '#')
        {
            count++;
        }

        return count;
    }

    private static bool HasHashes(string text, int i, int count)
    {
        if (i + count > text.Length)
            return false;

        for (var j = 0; j < count; j++)
        {
            if (text[i + j] != '#')
                return false;
        }

        return true;
    }

    private static void AddToken(List<SwiftToken> tokens, bool isTopLevel, SwiftTokenKind kind, int start, int end)
    {
        AddToken(tokens, isTopLevel, new SwiftToken(kind, start, end, start, end, IsMultiline: false, IsTerminated: true));
    }

    private static void AddToken(List<SwiftToken> tokens, bool isTopLevel, SwiftToken token)
    {
        if (isTopLevel)
        {
            tokens.Add(token);
        }
    }

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c is '_' or '$';

    private static bool IsIdentifierCharacter(char c) => char.IsLetterOrDigit(c) || c is '_' or '$';

    private static bool IsOperatorCharacter(string text, int i)
    {
        var c = text[i];
        if (c == '/' && i + 1 < text.Length && text[i + 1] is '/' or '*')
            return false;

        return c is '/' or '=' or '-' or '+' or '!' or '*' or '%' or '<' or '>' or '&' or '|' or '^' or '~' or '?';
    }

    private enum FrameKind
    {
        Code,
        Interpolation,
        String,
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Frame(FrameKind Kind, int TokenStart, int ContentStart, int HashCount, bool IsMultiline, int ParenthesisDepth)
    {
        public static Frame Code(bool isInterpolation) => new(isInterpolation ? FrameKind.Interpolation : FrameKind.Code, 0, 0, 0, IsMultiline: false, 0);

        public static Frame String(int tokenStart, int contentStart, int hashCount, bool isMultiline) => new(FrameKind.String, tokenStart, contentStart, hashCount, isMultiline, 0);
    }
}
