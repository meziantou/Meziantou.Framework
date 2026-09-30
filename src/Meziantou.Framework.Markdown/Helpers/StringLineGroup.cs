// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Helpers;

/// <summary>
/// A group of <see cref="StringLine"/>.
/// </summary>
/// <seealso cref="IEnumerable" />
public struct StringLineGroup : IEnumerable
{
    // Feel free to change these numbers if you see a positive change
    private static readonly CustomArrayPool<StringLine> Pool
        = new CustomArrayPool<StringLine>(512, 386, 128, 64);

    /// <summary>
    /// Initializes a new instance of the <see cref="StringLineGroup"/> class.
    /// </summary>
    /// <param name="capacity"></param>
    public StringLineGroup(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        Lines = Pool.Rent(capacity);
        Count = 0;
    }

    internal StringLineGroup(int capacity, bool willRelease)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        Lines = Pool.Rent(willRelease ? Math.Max(8, capacity) : capacity);
        Count = 0;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StringLineGroup"/> class.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <exception cref="ArgumentNullException"></exception>
    public StringLineGroup(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Lines = new StringLine[1];
        Count = 0;
        Add(new StringSlice(text));
    }

    /// <summary>
    /// Gets the lines.
    /// </summary>
    public StringLine[] Lines { get; private set; }

    /// <summary>
    /// Gets the number of lines.
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// Clears this instance.
    /// </summary>
    public void Clear()
    {
        Array.Clear(Lines, 0, Lines.Length);
        Count = 0;
    }

    /// <summary>
    /// Removes the line at the specified index.
    /// </summary>
    /// <param name="index">The index.</param>
    public void RemoveAt(int index)
    {
        if (index != Count - 1)
        {
            Array.Copy(Lines, index + 1, Lines, index, Count - index - 1);
        }

        Lines[Count - 1] = new StringLine();
        Count--;
    }

    internal void RemoveStartRange(int toRemove)
    {
        int remaining = Count - toRemove;
        Count = remaining;
        Array.Copy(Lines, toRemove, Lines, 0, remaining);
        Array.Clear(Lines, remaining, toRemove);
    }

    /// <summary>
    /// Adds the specified line to this instance.
    /// </summary>
    /// <param name="line">The line.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(ref StringLine line)
    {
        if (Count == Lines.Length) IncreaseCapacity();
        Lines[Count++] = line;
    }

    /// <summary>
    /// Adds the specified slice to this instance.
    /// </summary>
    /// <param name="slice">The slice.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(StringSlice slice)
    {
        if (Count == Lines.Length) IncreaseCapacity();
        Lines[Count++] = new StringLine(ref slice);
    }

    /// <summary>
    /// Performs the to string operation.
    /// </summary>
    public readonly override string ToString()
    {
        return ToSlice().ToString();
    }

    /// <summary>
    /// Converts the lines to a single <see cref="StringSlice"/> by concatenating the lines.
    /// </summary>
    /// <param name="lineOffsets">The position of the `\n` line offsets from the beginning of the returned slice.</param>
    /// <returns>A single slice concatenating the lines of this instance</returns>
    public readonly StringSlice ToSlice(List<LineOffset>? lineOffsets = null)
    {
        // Optimization case for a single line.
        if (Count == 1)
        {
            ref StringLine line = ref Lines[0];
            lineOffsets?.Add(new LineOffset(line.Position, line.Column, line.Slice.Start - line.Position, line.Slice.Start, line.Slice.End + 1));
            return Lines[0];
        }

        // Optimization case when no lines
        if (Count == 0)
        {
            return StringSlice.Empty;
        }

        if (lineOffsets != null && lineOffsets.Capacity < lineOffsets.Count + Count)
        {
            lineOffsets.Capacity = Math.Max(lineOffsets.Count + Count, lineOffsets.Capacity * 2);
        }

        // Else use a builder
        var builder = new ValueStringBuilder(unsafe(stackalloc char[ValueStringBuilder.StackallocThreshold]));
        int previousStartOfLine = 0;
        var newLine = NewLine.None;
        for (int i = 0; i < Count; i++)
        {
            if (i > 0)
            {
                builder.Append(newLine.AsString());
                previousStartOfLine = builder.Length;
            }
            ref StringLine line = ref Lines[i];
            if (!line.Slice.IsEmpty)
            {
                builder.Append(line.Slice.AsSpan());
            }
            newLine = line.NewLine;

            lineOffsets?.Add(new LineOffset(line.Position, line.Column, line.Slice.Start - line.Position, previousStartOfLine, builder.Length));
        }
        return new StringSlice(builder.ToString());
    }

    /// <summary>
    /// Converts this instance into a <see cref="ICharIterator"/>.
    /// </summary>
    /// <returns></returns>
    public readonly Iterator ToCharIterator()
    {
        return new Iterator(this);
    }

    /// <summary>
    /// Creates an iterator over the lines starting at <paramref name="startLine"/>, whose positions are relative to the start of that line.
    /// </summary>
    /// <param name="startLine">The index of the first line.</param>
    /// <param name="end">The position of the last character, as returned by <see cref="GetCharacterCount"/> minus one.</param>
    internal readonly Iterator ToCharIterator(int startLine, int end)
    {
        return new Iterator(this, startLine, end);
    }

    /// <summary>
    /// Gets the number of characters, new lines included, of the lines starting at <paramref name="startLine"/>.
    /// </summary>
    internal readonly int GetCharacterCount(int startLine)
    {
        int count = 0;
        StringLine[] lines = Lines;
        for (int i = startLine; i < Count && i < lines.Length; i++)
        {
            ref StringSlice slice = ref lines[i].Slice;
            count += slice.Length + slice.NewLine.Length();
        }

        return count;
    }

    /// <summary>
    /// Trims each lines of the specified <see cref="StringLineGroup"/>.
    /// </summary>
    public void Trim()
    {
        for (int i = 0; i < Count; i++)
        {
            Lines[i].Slice.Trim();
        }
    }

    internal SourceSpan ConvertToAbsoluteSpan(SourceSpan span) => ConvertToAbsoluteSpan(span, startLine: 0);

    /// <summary>
    /// Converts a span relative to the start of the line at <paramref name="startLine"/> to absolute positions.
    /// </summary>
    internal SourceSpan ConvertToAbsoluteSpan(SourceSpan span, int startLine)
    {
        if (span.IsEmpty || Count == startLine) return span;

        var startPosition = GetAbsolutePosition(span.Start, startLine);
        var endPosition = GetAbsolutePosition(span.End, startLine);

        return new SourceSpan(startPosition, endPosition);
    }

    /// <summary>
    /// Gets the text of a span relative to the start of the line at <paramref name="startLine"/>. The text of a span that crosses
    /// lines is made of these lines only, without what their containers consumed between them (such as quote markers), which
    /// the roundtrip renderer writes itself at the start of each line.
    /// </summary>
    internal readonly StringSlice GetText(SourceSpan span, int startLine)
    {
        var text = Lines[startLine].Slice.Text;
        if (span.IsEmpty)
        {
            return new StringSlice(text, span.Start, span.End);
        }

        int offset = 0;
        int i = startLine;
        for (; i < Count - 1; i++)
        {
            ref StringSlice slice = ref Lines[i].Slice;
            var lineLength = slice.Length + slice.NewLine.Length();
            if (span.Start < offset + lineLength)
            {
                break;
            }

            offset += lineLength;
        }

        ref StringSlice first = ref Lines[i].Slice;
        var start = first.Start + (span.Start - offset);
        var firstLength = first.Length + first.NewLine.Length();
        if (i == Count - 1 || span.End < offset + firstLength)
        {
            return new StringSlice(text, start, first.Start + (span.End - offset));
        }

        var builder = new ValueStringBuilder(unsafe(stackalloc char[ValueStringBuilder.StackallocThreshold]));
        builder.Append(text.AsSpan(start, first.Start + firstLength - start));
        offset += firstLength;
        for (i++; ; i++)
        {
            ref StringSlice slice = ref Lines[i].Slice;
            var lineLength = slice.Length + slice.NewLine.Length();
            if (i == Count - 1 || span.End < offset + lineLength)
            {
                builder.Append(text.AsSpan(slice.Start, span.End - offset + 1));
                break;
            }

            builder.Append(text.AsSpan(slice.Start, lineLength));
            offset += lineLength;
        }

        return new StringSlice(builder.ToString());
    }

    private int GetAbsolutePosition(int position, int startLine)
    {
        int offset = 0;
        for (int i = startLine; i < Count; i++)
        {
            ref StringSlice slice = ref Lines[i].Slice;
            var lineLength = slice.Length + slice.NewLine.Length();

            if (i == Count - 1 || position < offset + lineLength)
            {
                return slice.Start + (position - offset);
            }
            offset += lineLength;
        }
        return Lines[Count - 1].Slice.End + 1;
    }

    /// <summary>
    /// Gets or sets the enumerator.
    /// </summary>
    /// <summary>
    /// Represents the Enumerator type.
    /// </summary>
    public struct Enumerator(StringLineGroup parent) : IEnumerator
    {
        private readonly StringLineGroup _parent = parent;
        private int _index = -1;

        /// <summary>
        /// Gets or sets the current.
        /// </summary>
        public object Current => _parent.Lines[_index];

        /// <summary>
        /// Performs the move next operation.
        /// </summary>
        public bool MoveNext()
        {
            return ++_index < _parent.Count;
        }

        /// <summary>
        /// Performs the reset operation.
        /// </summary>
        public void Reset()
        {
            _index = -1;
        }
    }

    /// <summary>
    /// Gets enumerator.
    /// </summary>
    public Enumerator GetEnumerator()
    {
        return new Enumerator(this);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    private void IncreaseCapacity()
    {
        var newItems = Pool.Rent(Lines.Length * 2);
        if (Count > 0)
        {
            Array.Copy(Lines, 0, newItems, 0, Count);
            Array.Clear(Lines, 0, Count);
        }
        Pool.Return(Lines);
        Lines = newItems;
    }

    internal void Release()
    {
        Array.Clear(Lines, 0, Count);
        Pool.Return(Lines);
        Lines = null!;
        Count = -1;
    }

    /// <summary>
    /// The iterator used to iterate other the lines.
    /// </summary>
    /// <seealso cref="ICharIterator" />
    public struct Iterator : ICharIterator
    {
        private readonly StringLineGroup _lines;
        private readonly int _startLine;
        private StringSlice _currentSlice;
        private int _offset;

        /// <summary>
        /// Initializes a new instance of the Iterator class.
        /// </summary>
        public Iterator(StringLineGroup stringLineGroup)
            : this(stringLineGroup, startLine: 0, stringLineGroup.GetCharacterCount(0) - 1)
        {
        }

        internal Iterator(StringLineGroup stringLineGroup, int startLine, int end)
        {
            _lines = stringLineGroup;
            _startLine = startLine;
            Start = -1;
            _offset = -1;
            SliceIndex = startLine;
            CurrentChar = '\0';
            End = end;
            _currentSlice = _lines.Lines[startLine].Slice;
            SkipChar();
        }

        /// <summary>
        /// Gets or sets the start.
        /// </summary>
        public int Start { get; private set; }

        /// <summary>
        /// Gets or sets the current char.
        /// </summary>
        public char CurrentChar { get; private set; }

        /// <summary>
        /// Gets or sets the end.
        /// </summary>
        public int End { get; private set; }

        /// <summary>
        /// Gets or sets the is empty.
        /// </summary>
        public readonly bool IsEmpty => Start > End;

        /// <summary>
        /// Gets or sets the slice index.
        /// </summary>
        public int SliceIndex { get; private set; }

        /// <summary>
        /// Performs the remaining operation.
        /// </summary>
        public StringLineGroup Remaining()
        {
            StringLineGroup lines = _lines;
            if (IsEmpty)
            {
                lines.Clear();
            }
            else
            {
                lines.RemoveStartRange(SliceIndex);

                if (lines.Count > 0 && _offset > 0)
                {
                    ref StringLine line = ref lines.Lines[0];
                    line.Column += _offset;
                    line.Slice.Start += _offset;
                }
            }

            return lines;
        }

        /// <summary>
        /// Does what <see cref="Remaining"/> does to the first line left, without removing the lines before it.
        /// </summary>
        /// <param name="consumedCharacters">The number of characters before the first line left, or of all the lines when none is left.</param>
        /// <returns>The index of the first line left, or -1 when the iterator reached its end.</returns>
        internal readonly int SkipConsumedLines(out int consumedCharacters)
        {
            if (IsEmpty)
            {
                consumedCharacters = End + 1;
                return -1;
            }

            consumedCharacters = 0;
            StringLine[] lines = _lines.Lines;
            for (int i = _startLine; i < SliceIndex && i < _lines.Count; i++)
            {
                ref StringSlice slice = ref lines[i].Slice;
                consumedCharacters += slice.Length + slice.NewLine.Length();
            }

            if (SliceIndex < _lines.Count && _offset > 0)
            {
                ref StringLine line = ref lines[SliceIndex];
                line.Column += _offset;
                line.Slice.Start += _offset;
                consumedCharacters += _offset;
            }

            return SliceIndex;
        }

        /// <summary>
        /// Performs the next char operation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public char NextChar()
        {
            Start++;
            if (Start <= End)
            {
                ref StringSlice slice = ref _currentSlice;
                _offset++;

                int index = slice.Start + _offset;
                string text = slice.Text;
                if (index <= slice.End && (uint)index < (uint)text.Length)
                {
                    char c = text[index];
                    CurrentChar = c;
                    return c;
                }
                else
                {
                    return NextCharNewLine();
                }
            }
            else
            {
                return NextCharEndOfEnumerator();
            }
        }

        private char NextCharNewLine()
        {
            int sliceLength = _currentSlice.Length;
            NewLine newLine = _currentSlice.NewLine;

            if (_offset == sliceLength)
            {
                if (newLine == NewLine.LineFeed)
                {
                    CurrentChar = '\n';
                    goto MoveToNewLine;
                }
                else if (newLine == NewLine.CarriageReturn)
                {
                    CurrentChar = '\r';
                    goto MoveToNewLine;
                }
                else if (newLine == NewLine.CarriageReturnLineFeed)
                {
                    CurrentChar = '\r';
                }
            }
            else if (_offset - 1 == sliceLength)
            {
                if (newLine == NewLine.CarriageReturnLineFeed)
                {
                    CurrentChar = '\n';
                    goto MoveToNewLine;
                }
            }

            goto Return;

        MoveToNewLine:
            if (SliceIndex - _startLine < _lines.Lines.Length - 1)
            {
                SliceIndex++;
                _offset = -1;
                _currentSlice = SliceIndex < _lines.Lines.Length ? _lines.Lines[SliceIndex].Slice : default;
            }

        Return:
            return CurrentChar;
        }

        private char NextCharEndOfEnumerator()
        {
            CurrentChar = '\0';
            Start = End + 1;
            SliceIndex = _lines.Count;
            return '\0';
        }

        /// <summary>
        /// Performs the skip char operation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SkipChar() => NextChar();

        /// <summary>
        /// Performs the peek char operation.
        /// </summary>
        public readonly char PeekChar() => PeekChar(1);

        /// <summary>
        /// Performs the peek char operation.
        /// </summary>
        public readonly char PeekChar(int offset)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(offset);

            if (Start + offset > End)
            {
                return '\0';
            }

            offset += _offset;

            int sliceIndex = SliceIndex;
            ref StringSlice slice = ref _lines.Lines[sliceIndex].Slice;
            NewLine newLine = slice.NewLine;

            if (!(newLine == NewLine.CarriageReturnLineFeed && offset == slice.Length + 1))
            {
                while (offset > slice.Length)
                {
                    // We are not peeking at the same line
                    offset -= slice.Length + 1; // + 1 for new line

                    Debug.Assert(sliceIndex + 1 < _lines.Count, "'Start + offset > End' check above should prevent us from indexing out of range");
                    slice = ref _lines.Lines[++sliceIndex].Slice;
                }
            }
            else
            {
                if (slice.NewLine == NewLine.CarriageReturnLineFeed)
                {
                    return '\n'; // /n of /r/n (second character)
                }
            }

            if (offset == slice.Length)
            {
                if (newLine == NewLine.LineFeed)
                {
                    return '\n';
                }
                if (newLine == NewLine.CarriageReturn)
                {
                    return '\r';
                }
                if (newLine == NewLine.CarriageReturnLineFeed)
                {
                    return '\r'; // /r of /r/n (first character)
                }
            }

            Debug.Assert(offset < slice.Length);
            return slice[slice.Start + offset];
        }

        /// <summary>
        /// Performs the trim start operation.
        /// </summary>
        public bool TrimStart()
        {
            var c = CurrentChar;
            while (c.IsWhitespace())
            {
                c = NextChar();
            }
            return IsEmpty;
        }
    }

    /// <summary>
    /// Gets or sets the line offset.
    /// </summary>
    /// <summary>
    /// Represents the LineOffset type.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly struct LineOffset(
        int linePosition,
        int column,
        int offset,
        int start,
        int end)
    {
        /// <summary>
        /// Gets or sets the line position.
        /// </summary>
        public readonly int LinePosition = linePosition;

        /// <summary>
        /// Gets or sets the column.
        /// </summary>
        public readonly int Column = column;

        /// <summary>
        /// Gets or sets the offset.
        /// </summary>
        public readonly int Offset = offset;

        /// <summary>
        /// Gets or sets the start.
        /// </summary>
        public readonly int Start = start;

        /// <summary>
        /// Gets or sets the end.
        /// </summary>
        public readonly int End = end;
    }
}
