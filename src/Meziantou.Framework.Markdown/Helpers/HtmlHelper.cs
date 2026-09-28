// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Markdown.Helpers;

/// <summary>
/// Helper to parse several HTML tags.
/// </summary>
public static class HtmlHelper
{
    private static readonly char[] SearchBackAndAmp = ['\\', '&'];
    private static readonly char[] SearchAmp = ['&'];
    private static readonly string[] EscapeUrlsForAscii = new string[128];

    static HtmlHelper()
    {
        for (int i = 0; i < EscapeUrlsForAscii.Length; i++)
        {
            if (i <= 32 || @"""'<>[\]^`{|}~".Contains((char)i, StringComparison.Ordinal) || i == 127)
            {
                EscapeUrlsForAscii[i] = $"%{i:X2}";
            }
            else if ((char) i == '&')
            {
                EscapeUrlsForAscii[i] = "&amp;";
            }
        }
    }

    /// <summary>
    /// Performs the escape url character operation.
    /// </summary>
    public static string? EscapeUrlCharacter(char c)
    {
        return c < 128 ? EscapeUrlsForAscii[c] : null;
    }

    /// <summary>
    /// Attempts to parse html tag.
    /// </summary>
    public static bool TryParseHtmlTag(ref StringSlice text, [NotNullWhen(true)] out string? htmlTag)
    {
        return TryParseHtmlTag(ref text, out htmlTag, scanCache: null);
    }

    /// <summary>
    /// Attempts to parse html tag, reusing the searches of the previous calls on the same text.
    /// </summary>
    internal static bool TryParseHtmlTag(ref StringSlice text, [NotNullWhen(true)] out string? htmlTag, InlineHtmlScanCache? scanCache)
    {
        scanCache?.SetText(text);
        var builder = new ValueStringBuilder(unsafe(stackalloc char[ValueStringBuilder.StackallocThreshold]));
        if (TryParseHtmlTag(ref text, ref builder, scanCache))
        {
            htmlTag = builder.ToString();
            return true;
        }
        else
        {
            builder.Dispose();
            htmlTag = null;
            return false;
        }
    }

    private static bool TryParseHtmlTag(ref StringSlice text, ref ValueStringBuilder builder, InlineHtmlScanCache? scanCache)
    {
        var c = text.CurrentChar;
        if (c != '<')
        {
            return false;
        }
        c = text.NextChar();

        builder.Append('<');

        switch (c)
        {
            case '/':
                return TryParseHtmlCloseTag(ref text, ref builder);
            case '?':
                return TryParseHtmlTagProcessingInstruction(ref text, ref builder, scanCache);
            case '!':
                builder.Append(c);
                c = text.NextChar();
                if (c == '-')
                {
                    return TryParseHtmlTagHtmlComment(ref text, ref builder, scanCache);
                }

                if (c == '[')
                {
                    return TryParseHtmlTagCData(ref text, ref builder, scanCache);
                }

                return TryParseHtmlTagDeclaration(ref text, ref builder, scanCache);
        }

        return TryParseHtmlTagOpenTag(ref text, ref builder);
    }

    internal static bool TryParseHtmlTagOpenTag(ref StringSlice text, ref ValueStringBuilder builder)
    {
        var c = text.CurrentChar;

        // Parse the tagname
        if (!c.IsAlpha())
        {
            return false;
        }
        builder.Append(c);

        while (true)
        {
            c = text.NextChar();
            if (c.IsAlphaNumeric() || c == '-')
            {
                builder.Append(c);
            }
            else
            {
                break;
            }
        }

        bool hasAttribute = false;
        while (true)
        {
            var hasWhitespaces = false;
            // Skip any whitespaces
            while (c.IsWhitespace())
            {
                builder.Append(c);
                c = text.NextChar();
                hasWhitespaces = true;
            }

            switch (c)
            {
                case '\0':
                    return false;
                case '>':
                    text.SkipChar();
                    builder.Append(c);
                    return true;
                case '/':
                    builder.Append('/');
                    c = text.NextChar();
                    if (c != '>')
                    {
                        return false;
                    }
                    text.SkipChar();
                    builder.Append('>');
                    return true;
                case '=':

                    if (!hasAttribute)
                    {
                        return false;
                    }

                    builder.Append('=');

                    // Skip any spaces after
                    c = text.NextChar();
                    while (c.IsWhitespace())
                    {
                        builder.Append(c);
                        c = text.NextChar();
                    }

                    // Parse a quoted string
                    if (c == '\'' || c == '\"')
                    {
                        builder.Append(c);
                        char openingStringChar = c;
                        while (true)
                        {
                            c = text.NextChar();
                            if (c == '\0')
                            {
                                return false;
                            }
                            if (c != openingStringChar)
                            {
                                builder.Append(c);
                            }
                            else
                            {
                                break;
                            }
                        }
                        builder.Append(c);
                        c = text.NextChar();
                    }
                    else
                    {
                        // Parse until we match a space or a special html character
                        int matchCount = 0;
                        while (true)
                        {
                            if (c == '\0')
                            {
                                return false;
                            }
                            if (IsSpaceOrSpecialHtmlChar(c))
                            {
                                break;
                            }
                            matchCount++;
                            builder.Append(c);
                            c = text.NextChar();
                        }

                        [MethodImpl(MethodImplOptions.AggressiveInlining)]
                        static bool IsSpaceOrSpecialHtmlChar(char c)
                        {
                            if (c > '>')
                            {
                                return c == '`';
                            }

                            const long BitMask =
                                  (1L << ' ')
                                | (1L << '\n')
                                | (1L << '"')
                                | (1L << '\'')
                                | (1L << '=')
                                | (1L << '<')
                                | (1L << '>');

                            return (BitMask & (1L << c)) != 0;
                        }

                        // We need at least one char after '='
                        if (matchCount == 0)
                        {
                            return false;
                        }
                    }

                    hasAttribute = false;
                    continue;
                default:
                    if (!hasWhitespaces)
                    {
                        return false;
                    }

                    // Parse the attribute name
                    if (!(c.IsAlpha() || c == '_' || c == ':'))
                    {
                        return false;
                    }
                    builder.Append(c);

                    while (true)
                    {
                        c = text.NextChar();
                        if (c.IsAlphaNumeric() || IsCharToAppend(c))
                        {
                            builder.Append(c);
                        }
                        else
                        {
                            break;
                        }

                        [MethodImpl(MethodImplOptions.AggressiveInlining)]
                        static bool IsCharToAppend(char c)
                        {
                            if ((uint)(c - '-') > '_' - '-')
                            {
                                return false;
                            }

                            const long BitMask =
                                  (1L << '_')
                                | (1L << ':')
                                | (1L << '.')
                                | (1L << '-');

                            return (BitMask & (1L << c)) != 0;
                        }
                    }

                    hasAttribute = true;
                    break;
            }
        }
    }

    private static bool TryParseHtmlTagDeclaration(ref StringSlice text, ref ValueStringBuilder builder, InlineHtmlScanCache? scanCache)
    {
        var c = text.CurrentChar;
        bool hasAlpha = false;
        while (c.IsAlphaUpper())
        {
            builder.Append(c);
            c = text.NextChar();
            hasAlpha = true;
        }

        if (!hasAlpha || !c.IsWhitespace())
        {
            return false;
        }

        // Regexp: "\\![A-Z]+\\s+[^>\\x00]*>"
        if (!TryFindEnd(ref text, text.Start + 1, HtmlScanTarget.DeclarationEnd, scanCache, out var end))
        {
            return false;
        }

        AppendAndSkip(ref text, ref builder, end + 1);
        return true;
    }

    private static bool TryParseHtmlTagCData(ref StringSlice text, ref ValueStringBuilder builder, InlineHtmlScanCache? scanCache)
    {
        if (!text.Match("[CDATA["))
        {
            return false;
        }

        // The "]]>" that ends the section starts after "[CDATA["
        if (!TryFindEnd(ref text, text.Start + "[CDATA[".Length, HtmlScanTarget.CDataEnd, scanCache, out var end))
        {
            return false;
        }

        AppendAndSkip(ref text, ref builder, end + "]]>".Length);
        return true;
    }

    internal static bool TryParseHtmlCloseTag(ref StringSlice text, ref ValueStringBuilder builder)
    {
        // </[A-Za-z][A-Za-z0-9]+\s*>
        builder.Append('/');

        var c = text.NextChar();
        if (!c.IsAlpha())
        {
            return false;
        }
        builder.Append(c);

        bool skipSpaces = false;
        while (true)
        {
            c = text.NextChar();
            if (c == '>')
            {
                text.SkipChar();
                builder.Append('>');
                return true;
            }

            if (skipSpaces)
            {
                if (c != ' ')
                {
                    break;
                }
            }
            else if (c == ' ')
            {
                skipSpaces = true;
            }
            else if (!(c.IsAlphaNumeric() || c == '-'))
            {
                break;
            }

            builder.Append(c);
        }
        return false;
    }


    private static bool TryParseHtmlTagHtmlComment(ref StringSlice text, ref ValueStringBuilder builder, InlineHtmlScanCache? scanCache)
    {
        // https://spec.commonmark.org/0.31.2/#raw-html
        // An HTML comment consists of <!-->, <!--->, or
        // <!--, a string of characters not including the string -->, and -->.

        // The caller already checked <!-
        Debug.Assert(text.CurrentChar == '-' && text.PeekCharExtra(-1) == '!' && text.PeekCharExtra(-2) == '<');

        var c = text.NextChar();
        if (c != '-')
        {
            return false;
        }

        c = text.NextChar();

        if (c == '>')
        {
            // <!--> is considered valid.
            builder.Append("-->");
            text.SkipChar();
            return true;
        }

        if (c == '-' && text.PeekChar() == '>')
        {
            // <!---> is also considered valid.
            builder.Append("--->");
            text.SkipChar();
            text.SkipChar();
            return true;
        }

        // Unlike the other constructs, the search does not stop at a null character
        var end = InlineHtmlScanCache.IndexOf(text, text.Start, HtmlScanTarget.CommentEnd, scanCache);
        if (end < 0)
        {
            return false;
        }

        builder.Append("--");
        AppendAndSkip(ref text, ref builder, end + "-->".Length);
        return true;
    }

    private static bool TryParseHtmlTagProcessingInstruction(ref StringSlice text, ref ValueStringBuilder builder, InlineHtmlScanCache? scanCache)
    {
        // The '?' that opens the instruction cannot start the "?>" that ends it
        if (!TryFindEnd(ref text, text.Start + 1, HtmlScanTarget.ProcessingInstructionEnd, scanCache, out var end))
        {
            return false;
        }

        AppendAndSkip(ref text, ref builder, end + "?>".Length);
        return true;
    }

    /// <summary>
    /// Finds the first occurrence of <paramref name="target"/> at or after <paramref name="start"/>, as a scan reading one
    /// character at a time and stopping at the first null character or at the end of <paramref name="text"/> would.
    /// </summary>
    private static bool TryFindEnd(ref StringSlice text, int start, HtmlScanTarget target, InlineHtmlScanCache? scanCache, out int end)
    {
        end = InlineHtmlScanCache.IndexOf(text, start, target, scanCache);

        int nullCharacter;
        if (scanCache is null && end >= 0)
        {
            // Without a cache, only search up to the end of the construct so that parsing it stays proportional to its length
            nullCharacter = text.Text.AsSpan(start, end - start).IndexOf('\0');
            if (nullCharacter >= 0)
            {
                nullCharacter += start;
            }
        }
        else
        {
            nullCharacter = InlineHtmlScanCache.IndexOf(text, start, HtmlScanTarget.NullCharacter, scanCache);
        }

        // On failure, leave the slice where the character-by-character scan stopped
        if (nullCharacter >= 0 && (end < 0 || nullCharacter < end))
        {
            text.Start = nullCharacter;
            return false;
        }

        if (end < 0)
        {
            text.Start = text.End + 1;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Appends the characters from the current position of <paramref name="text"/> up to <paramref name="end"/> (excluded), and moves the slice after them.
    /// </summary>
    private static void AppendAndSkip(ref StringSlice text, ref ValueStringBuilder builder, int end)
    {
        builder.Append(text.Text.AsSpan(text.Start, end - text.Start));
        text.Start = end;
    }

    /// <summary>
    /// Destructively unescape a string: remove backslashes before punctuation or symbol characters.
    /// </summary>
    /// <param name="text">The string data that will be changed by unescaping any punctuation or symbol characters.</param>
    /// <param name="removeBackSlash">if set to <c>true</c> [remove back slash].</param>
    /// <returns></returns>
    public static string Unescape(string? text, bool removeBackSlash = true)
    {
        // Credits: code from CommonMark.NET
        // Copyright (c) 2014, Kārlis Gaņģis All rights reserved.
        // See license for details:  https://github.com/Knagis/CommonMark.NET/blob/master/LICENSE.md
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // remove backslashes before punctuation chars:
        int searchPos = 0;
        int lastPos = 0;
        char c = '\0';
        char[] search = removeBackSlash ? SearchBackAndAmp : SearchAmp;
        var sb = new ValueStringBuilder(unsafe(stackalloc char[ValueStringBuilder.StackallocThreshold]));

        while ((searchPos = text.IndexOfAny(search, searchPos)) != -1)
        {
            c = text[searchPos];
            if (removeBackSlash && c == '\\')
            {
                searchPos++;

                if (text.Length == searchPos)
                    break;

                c = text[searchPos];
                if (c.IsEscapableSymbol())
                {
                    sb.Append(text.AsSpan(lastPos, searchPos - lastPos - 1));
                    lastPos = searchPos;
                }
            }
            else if (c == '&')
            {
                var match = ScanEntity(new StringSlice(text, searchPos, text.Length - 1), out int numericEntity, out int entityNameStart, out int entityNameLength);
                if (match == 0)
                {
                    searchPos++;
                }
                else
                {
                    searchPos += match;

                    if (entityNameLength > 0)
                    {
                        var decoded = EntityHelper.DecodeEntity(text.AsSpan(entityNameStart, entityNameLength));
                        if (decoded != null)
                        {
                            sb.Append(text.AsSpan(lastPos, searchPos - match - lastPos));
                            sb.Append(decoded);
                            lastPos = searchPos;
                        }
                    }
                    else if (numericEntity >= 0)
                    {
                        sb.Append(text.AsSpan(lastPos, searchPos - match - lastPos));
                        EntityHelper.DecodeEntity(numericEntity, ref sb);
                        lastPos = searchPos;
                    }
                }
            }
        }

        if (c == 0)
        {
            sb.Dispose();
            return text;
        }

        sb.Append(text.AsSpan(lastPos, text.Length - lastPos));
        return sb.ToString();
    }

    /// <summary>
    /// Scans an entity.
    /// Returns number of chars matched.
    /// </summary>
    public static int ScanEntity<T>(T slice, out int numericEntity, out int namedEntityStart,  out int namedEntityLength) where T : ICharIterator
    {
        // Credits: code from CommonMark.NET
        // Copyright (c) 2014, Kārlis Gaņģis All rights reserved.
        // See license for details:  https://github.com/Knagis/CommonMark.NET/blob/master/LICENSE.md

        numericEntity = 0;
        namedEntityStart = 0;
        namedEntityLength = 0;

        if (slice.CurrentChar != '&' || slice.PeekChar(3) == '\0')
        {
            return 0;
        }

        var start = slice.Start;
        char c = slice.NextChar();
        int counter = 0;

        if (c == '#')
        {
            c = slice.PeekChar();
            if ((c | 0x20) == 'x')
            {
                c = slice.NextChar(); // skip #
                // expect 1-6 hex digits starting from pos+3
                while (c != '\0')
                {
                    c = slice.NextChar();

                    if (c.IsDigit())
                    {
                        if (++counter == 7) return 0;
                        numericEntity = numericEntity * 16 + (c - '0');
                        continue;
                    }
                    else if ((uint)((c - 'A') & ~0x20) <= ('F' - 'A'))
                    {
                        if (++counter == 7) return 0;
                        numericEntity = numericEntity * 16 + ((c | 0x20) - 'a' + 10);
                        continue;
                    }

                    if (c == ';')
                        return counter == 0 ? 0 : slice.Start - start + 1;

                    return 0;
                }
            }
            else
            {
                // expect 1-7 digits starting from pos+2
                while (c != '\0')
                {
                    c = slice.NextChar();

                    if (c.IsDigit())
                    {
                        if (++counter == 8) return 0;
                        numericEntity = numericEntity * 10 + (c - '0');
                        continue;
                    }

                    if (c == ';')
                        return counter == 0 ? 0 : slice.Start - start + 1;

                    return 0;
                }
            }
        }
        else
        {
            // expect a letter and 1-31 letters or digits
            if (!c.IsAlpha())
                return 0;

            namedEntityStart = slice.Start;
            namedEntityLength++;

            while (c != '\0')
            {
                c = slice.NextChar();

                if (c.IsAlphaNumeric())
                {
                    if (++counter == 32)
                        return 0;
                    namedEntityLength++;
                    continue;
                }

                if (c == ';')
                {
                    return counter == 0 ? 0 : slice.Start - start + 1;
                }

                return 0;
            }
        }

        return 0;
    }
}
