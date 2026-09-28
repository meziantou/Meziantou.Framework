// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Diagnostics;

using Meziantou.Framework.Markdown.Extensions.Tables;
using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Parsers.Inlines;

/// <summary>
/// An inline parser for a <see cref="CodeInline"/>.
/// </summary>
/// <seealso cref="InlineParser" />
public class CodeInlineParser : InlineParser
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CodeInlineParser"/> class.
    /// </summary>
    public CodeInlineParser()
    {
        OpeningCharacters = ['`'];
    }

    /// <summary>
    /// Attempts to match the parser at the current position.
    /// </summary>
    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        char match = slice.CurrentChar;
        if (slice.PeekCharExtra(-1) == match && !IsEscaped(in slice, -1))
        {
            // A backtick string is not preceded by a backtick, unless that backtick is escaped and so is literal text
            return false;
        }

        Debug.Assert(match is not ('\r' or '\n'));

        // Match the opened sticks
        int openingStart = slice.Start;
        int openSticks = slice.CountAndSkipChar(match);

        // A backtick string is a string of one or more backtick characters (`) that is neither preceded nor followed by a backtick.
        // A code span begins with a backtick string and ends with a backtick string of equal length.
        // The contents of the code span are the characters between the two backtick strings, normalized in the following ways:

        // 1. line endings are converted to spaces.

        // 2. If the resulting string both begins AND ends with a space character, but does not consist entirely
        // of space characters, a single space character is removed from the front and back.
        // This allows you to include code that begins or ends with backtick characters, which must be separated by
        // whitespace from the opening or closing backtick strings.

        var unclosedCodeSpans = processor.ParserStates[Index] as UnclosedCodeSpans;
        if (unclosedCodeSpans is not null && unclosedCodeSpans.IsKnownUnclosed(slice, openSticks))
        {
            return false;
        }

        ReadOnlySpan<char> span = slice.AsSpan();
        bool containsNewLines = false;

        while (true)
        {
            int i = span.IndexOfAny('\r', '\n', match);

            if ((uint)i >= (uint)span.Length)
            {
                // We got to the end of the input before seeing the match character. CodeInline can't match here.
                unclosedCodeSpans ??= processor.GetParserState<UnclosedCodeSpans>(this);
                unclosedCodeSpans.SetUnclosed(slice, openSticks);
                return false;
            }

            int closeSticks = 0;

            while ((uint)i < (uint)span.Length && span[i] == match)
            {
                closeSticks++;
                i++;
            }

            span = span.Slice(i);

            if (openSticks == closeSticks)
            {
                break;
            }

            if (closeSticks == 0)
            {
                if (span.TrimStart(['\r', '\n']).StartsWith('|'))
                {
                    // We saw the start of a code inline, but the close sticks are not present on the same line.
                    // If the next line starts with a pipe character, this is likely an incomplete CodeInline within a table.
                    // Treat it as regular text to avoid breaking the overall table shape.
                    // Use ContainsParentOrSiblingOfType to handle both nested and flat pipe table structures.
                    if (processor.Inline != null && processor.ContainsPipeTableDelimiter(processor.Inline))
                    {
                        slice.Start = openingStart;
                        return false;
                    }
                }

                containsNewLines = true;
                span = span.Slice(1);
            }
        }

        ReadOnlySpan<char> rawContent = slice.AsSpan().Slice(0, slice.Length - span.Length - openSticks);

        var content = containsNewLines
            ? new LazySubstring(ReplaceNewLines(rawContent)) // Should be the rare path.
            : new LazySubstring(slice.Text, slice.Start, rawContent.Length);

        // Remove one space from front and back if the string is not all spaces
        if (rawContent.Length > 2 &&
            rawContent[0] is ' ' or '\n' &&
            rawContent[rawContent.Length - 1] is ' ' or '\n' &&
            rawContent.ContainsAnyExcept(' ', '\r', '\n'))
        {
            content.Offset++;
            content.Length -= 2;
        }

        int startPosition = slice.Start;
        slice.Start = startPosition + rawContent.Length + openSticks;

        // We've already skipped the opening sticks. Account for that here.
        startPosition -= openSticks;

        var codeInline = new CodeInline(content)
        {
            Delimiter = slice.Text[startPosition],
            Span = new SourceSpan(processor.GetSourcePosition(startPosition, out int line, out int column), processor.GetSourcePosition(slice.Start - 1)),
            Line = line,
            Column = column,
            DelimiterCount = openSticks,
        };

        if (processor.TrackTrivia)
        {
            // startPosition and slice.Start include the opening/closing sticks.
            codeInline.ContentWithTrivia = new StringSlice(slice.Text, startPosition + openSticks, slice.Start - openSticks - 1);
        }

        processor.Inline = codeInline;
        return true;
    }

    // Remembers, for each length of backtick string, a position after which the inline text has no backtick string of this
    // length. Without it, each opening backtick string without a closing one scans the rest of the text again, and inputs
    // such as \``a repeated n times take O(n²) time.
    private sealed class UnclosedCodeSpans
    {
        private readonly Dictionary<int, int> _noClosingStringAfter = [];
        private string? _text;
        private int _textEnd;

        public bool IsKnownUnclosed(StringSlice slice, int openSticks)
        {
            return ReferenceEquals(_text, slice.Text) &&
                _textEnd == slice.End &&
                _noClosingStringAfter.TryGetValue(openSticks, out var start) &&
                slice.Start >= start;
        }

        public void SetUnclosed(StringSlice slice, int openSticks)
        {
            // The result of a search only depends on the characters between its start and the end of the text
            if (!ReferenceEquals(_text, slice.Text) || _textEnd != slice.End)
            {
                _noClosingStringAfter.Clear();
                _text = slice.Text;
                _textEnd = slice.End;
            }

            _noClosingStringAfter[openSticks] = _noClosingStringAfter.TryGetValue(openSticks, out var start) ? Math.Min(start, slice.Start) : slice.Start;
        }
    }

    // A character is escaped when it follows an odd number of backslashes
    private static bool IsEscaped(in StringSlice slice, int offset)
    {
        var backslashCount = 0;
        while (slice.PeekCharExtra(offset - 1 - backslashCount) == '\\')
        {
            backslashCount++;
        }

        return (backslashCount & 1) != 0;
    }

    private static string ReplaceNewLines(ReadOnlySpan<char> content)
    {
        var builder = new ValueStringBuilder(unsafe(stackalloc char[ValueStringBuilder.StackallocThreshold]));

        while (true)
        {
            int i = content.IndexOfAny('\r', '\n');

            if ((uint)i >= (uint)content.Length)
            {
                builder.Append(content);
                break;
            }

            builder.Append(content.Slice(0, i));

            if (content[i] == '\n')
            {
                // Transform '\n' into a single space
                builder.Append(' ');
            }

            content = content.Slice(i + 1);
        }

        return builder.ToString();
    }
}
