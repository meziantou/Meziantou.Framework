using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;

namespace Meziantou.Framework.Yaml;

/// <summary>
/// A read-only representation of a UTF-8 YAML stream: one row per node in a pooled array over the input bytes. Scalars
/// are decoded only when asked for, and every node keeps the location of its text in the input.
/// </summary>
/// <remarks>Dispose the document to return its buffers to the pool. Its nodes must not be used after that.</remarks>
public sealed class YamlDocument : IDisposable
{
    // A lone surrogate of the text is an error, like for the other inputs, instead of a replacement character
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private ReadOnlyMemory<byte> _utf8;
    private byte[]? _ownedBuffer;
    private Row[]? _rows;
    private int _rowCount;
    private readonly int[] _documentRoots;
    private readonly string?[]? _tags;

    // The %TAG directives of each document, with their prefixes as written
    private readonly Utf8YamlReader.TagDirective[]?[] _documentTagDirectives;
    private LineIndex? _lineIndex;

    // A clone owns its arrays, which are not pooled. Its text is made of segments of the input: the offset of each one in
    // the text of the clone and in the input give the positions of its nodes in the input.
    private readonly bool _isClone;
    private readonly int[]? _segmentStarts;
    private readonly int[]? _segmentSourceStarts;

    private YamlDocument(ReadOnlyMemory<byte> utf8, byte[]? ownedBuffer, Row[] rows, int rowCount, int[] documentRoots, string?[]? tags, Utf8YamlReader.TagDirective[]?[] documentTagDirectives)
    {
        _documentTagDirectives = documentTagDirectives;
        _utf8 = utf8;
        _ownedBuffer = ownedBuffer;
        _rows = rows;
        _rowCount = rowCount;
        _documentRoots = documentRoots;
        _tags = tags;
    }

    private YamlDocument(byte[] utf8, Row[] rows, string?[]? tags, LineIndex lineIndex, int[] segmentStarts, int[] segmentSourceStarts)
        : this(utf8, ownedBuffer: null, rows, rows.Length, documentRoots: [0], tags, documentTagDirectives: [null])
    {
        _isClone = true;
        _lineIndex = lineIndex;
        _segmentStarts = segmentStarts;
        _segmentSourceStarts = segmentSourceStarts;
    }

    /// <summary>Gets the number of documents of the stream.</summary>
    public int DocumentCount => _documentRoots.Length;

    /// <summary>Gets the root node of the first document.</summary>
    /// <exception cref="InvalidOperationException">The stream has no document.</exception>
    public YamlElement Root => DocumentCount > 0 ? GetDocumentRoot(0) : throw new InvalidOperationException("The YAML stream has no document.");

    /// <summary>Gets the UTF-8 input of the document.</summary>
    public ReadOnlyMemory<byte> Utf8Text => _utf8;

    /// <summary>Gets the root node of a document of the stream.</summary>
    public YamlElement GetDocumentRoot(int index) => new(this, _documentRoots[index]);

    /// <summary>Parses a UTF-8 YAML stream. The document keeps a reference to <paramref name="utf8Yaml"/>, which must not change while it is used.</summary>
    /// <exception cref="YamlException">The input is not valid YAML.</exception>
    public static YamlDocument Parse(ReadOnlyMemory<byte> utf8Yaml, Utf8YamlReaderOptions options = default)
    {
        return Parse(utf8Yaml, ownedBuffer: null, options);
    }

    /// <summary>Parses a YAML stream.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="yaml"/> is <see langword="null"/>.</exception>
    /// <exception cref="YamlException">The input is not valid YAML.</exception>
    public static YamlDocument Parse(string yaml, Utf8YamlReaderOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        var buffer = ArrayPool<byte>.Shared.Rent(StrictUtf8.GetMaxByteCount(yaml.Length));
        try
        {
            int length;
            try
            {
                length = StrictUtf8.GetBytes(yaml, buffer);
            }
            catch (EncoderFallbackException exception)
            {
                throw CreateInvalidUtf16Exception(yaml, exception.Index);
            }

            return Parse(buffer.AsMemory(0, length), buffer, options);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    // The location of the invalid character: a line ends at a line feed, or at a carriage return that no line feed follows
    private static SyntaxErrorException CreateInvalidUtf16Exception(string yaml, int index)
    {
        var line = 0;
        var lineStart = 0;
        for (var i = 0; i < index; i++)
        {
            if (yaml[i] == '\n' || (yaml[i] == '\r' && (i + 1 >= yaml.Length || yaml[i + 1] != '\n')))
            {
                line++;
                lineStart = i + 1;
            }
        }

        var mark = new Mark(index, line, index - lineStart);
        return new SyntaxErrorException(mark, mark, "The input contains an invalid UTF-16 sequence.");
    }

    /// <summary>Reads a UTF-8 YAML stream to its end and parses it.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="utf8Yaml"/> is <see langword="null"/>.</exception>
    /// <exception cref="YamlException">The input is not valid YAML.</exception>
    public static YamlDocument Parse(Stream utf8Yaml, Utf8YamlReaderOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(utf8Yaml);
        var buffer = ArrayPool<byte>.Shared.Rent(utf8Yaml.CanSeek ? (int)Math.Clamp(utf8Yaml.Length - utf8Yaml.Position + 1, 256, Array.MaxLength) : 4096);
        var length = 0;
        try
        {
            while (true)
            {
                if (length == buffer.Length)
                {
                    var larger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                    buffer.AsSpan(0, length).CopyTo(larger);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = larger;
                }

                var read = utf8Yaml.Read(buffer, length, buffer.Length - length);
                if (read == 0)
                    break;

                length += read;
            }

            return Parse(buffer.AsMemory(0, length), buffer, options);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    private static YamlDocument Parse(ReadOnlyMemory<byte> utf8, byte[]? ownedBuffer, Utf8YamlReaderOptions options)
    {
        var rows = ArrayPool<Row>.Shared.Rent(Math.Max(16, utf8.Length / 8));
        var rowCount = 0;
        var roots = new List<int>();
        var rootTagDirectives = new List<Utf8YamlReader.TagDirective[]?>();
        Utf8YamlReader.TagDirective[]? tagDirectives = null;
        List<string?>? tags = null;
        Span<int> inlineStack = stackalloc int[64];
        var stack = inlineStack;
        int[]? rentedStack = null;
        var depth = 0;
        var atDocumentRoot = false;
        try
        {
            var reader = new Utf8YamlReader(utf8.Span, options);
            while (reader.Read())
            {
                var kind = reader.TokenKind;
                switch (kind)
                {
                    case Utf8YamlTokenKind.DocumentStart:
                        atDocumentRoot = true;
                        tagDirectives = reader.DocumentTagDirectives;
                        continue;

                    case Utf8YamlTokenKind.MappingEnd or Utf8YamlTokenKind.SequenceEnd:
                    {
                        var start = stack[--depth];
                        ref var row = ref rows[start];
                        row.EndRow = rowCount;
                        row.NodeEnd = reader.TokenStart + reader.TokenLength;
                        continue;
                    }

                    case Utf8YamlTokenKind.MappingStart or Utf8YamlTokenKind.SequenceStart or Utf8YamlTokenKind.Scalar or Utf8YamlTokenKind.Alias:
                        break;

                    default:
                        continue;
                }

                if (rowCount == rows.Length)
                {
                    var larger = ArrayPool<Row>.Shared.Rent(rows.Length * 2);
                    rows.AsSpan(0, rowCount).CopyTo(larger);
                    ArrayPool<Row>.Shared.Return(rows);
                    rows = larger;
                }

                if (depth > 0)
                {
                    rows[stack[depth - 1]].ChildCount++;
                }

                if (atDocumentRoot)
                {
                    roots.Add(rowCount);
                    rootTagDirectives.Add(tagDirectives);
                    atDocumentRoot = false;
                }

                var token = reader.CurrentToken;
                var tagIndex = -1;
                if (token.Properties.TagStart >= 0)
                {
                    tags ??= [];
                    tagIndex = tags.Count;
                    tags.Add(reader.GetTag());
                }

                var nodeStart = token.Start;
                if (token.Properties.AnchorStart >= 0)
                {
                    nodeStart = Math.Min(nodeStart, token.Properties.AnchorStart);
                }

                if (token.Properties.TagStart >= 0)
                {
                    nodeStart = Math.Min(nodeStart, token.Properties.TagStart);
                }

                rows[rowCount] = new Row
                {
                    Token = token,
                    TagIndex = tagIndex,
                    NodeStart = nodeStart,
                    NodeEnd = token.Start + token.Length,
                    EndRow = rowCount + 1,
                };

                if (kind is Utf8YamlTokenKind.MappingStart or Utf8YamlTokenKind.SequenceStart)
                {
                    if (depth == stack.Length)
                    {
                        var larger = ArrayPool<int>.Shared.Rent(stack.Length * 2);
                        stack.CopyTo(larger);
                        if (rentedStack is not null)
                        {
                            ArrayPool<int>.Shared.Return(rentedStack);
                        }

                        rentedStack = larger;
                        stack = larger;
                    }

                    stack[depth++] = rowCount;
                }

                rowCount++;
            }

            return new YamlDocument(utf8, ownedBuffer, rows, rowCount, [.. roots], tags?.ToArray(), [.. rootTagDirectives]);
        }
        catch
        {
            ArrayPool<Row>.Shared.Return(rows);
            throw;
        }
        finally
        {
            if (rentedStack is not null)
            {
                ArrayPool<int>.Shared.Return(rentedStack);
            }
        }
    }

    /// <summary>Writes every document of the stream, with its <c>%TAG</c> directives, each one after the first with a <c>---</c> marker.</summary>
    /// <param name="writer">The destination.</param>
    /// <remarks>See <see cref="YamlElement.WriteTo(Utf8YamlWriter)"/>. The tags are written with the handles of the directives.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/>.</exception>
    public void WriteTo(Utf8YamlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        for (var i = 0; i < _documentRoots.Length; i++)
        {
            if (_documentTagDirectives[i] is { Length: > 0 } directives)
            {
                // The directives start the document
                foreach (var directive in directives)
                {
                    writer.WriteTagDirectiveCore(directive.Handle, Utf8YamlReader.DecodeTagPrefix(directive.Prefix));
                }
            }
            else if (i > 0)
            {
                writer.WriteStartDocument();
            }

            WriteNode(_documentRoots[i], writer);
        }
    }

    // The rows are in the order of the text: a collection is followed by its descendants, up to its end row. The nodes
    // are written in that order, without recursion, so a deep document does not exhaust the stack.
    internal void WriteNode(int index, Utf8YamlWriter writer)
    {
        var buffer = Utf8Span;
        var open = new Stack<(int EndRow, bool IsMapping)>();
        var end = GetRow(index).EndRow;
        byte[]? rented = null;
        try
        {
            for (var i = index; i < end; i++)
            {
                CloseCollections(open, i, writer);
                ref readonly var row = ref GetRow(i);
                ref readonly var token = ref row.Token;
                if (token.Kind is Utf8YamlTokenKind.Alias)
                {
                    writer.WriteAliasValueCore(buffer.Slice(token.Start + 1, token.Length - 1), nameof(writer));
                    continue;
                }

                if (token.Properties.AnchorStart >= 0)
                {
                    writer.WriteAnchorCore(buffer.Slice(token.Properties.AnchorStart + 1, token.Properties.AnchorLength - 1), nameof(writer));
                }

                if (GetTag(row.TagIndex) is { } tag)
                {
                    writer.WriteTag(tag);
                }

                var style = (token.Flags & Utf8YamlTokenFlags.Flow) != Utf8YamlTokenFlags.None ? YamlStyle.Flow : YamlStyle.Block;
                switch (token.Kind)
                {
                    case Utf8YamlTokenKind.MappingStart:
                        writer.WriteStartMapping(style);
                        open.Push((row.EndRow, true));
                        break;

                    case Utf8YamlTokenKind.SequenceStart:
                        writer.WriteStartSequence(style);
                        open.Push((row.EndRow, false));
                        break;

                    default:
                        var scalarStyle = (ScalarStyle)(token.Flags & Utf8YamlTokenFlags.ScalarStyleMask);
                        if (scalarStyle is ScalarStyle.Plain && (token.Flags & Utf8YamlTokenFlags.RawValue) != Utf8YamlTokenFlags.None)
                        {
                            writer.WriteSourcePlainScalarCore(buffer.Slice(token.ValueStart, token.ValueLength));
                            break;
                        }

                        var maxLength = token.ValueLength + (token.ValueLength / 2) + 1;
                        if (rented is null || rented.Length < maxLength)
                        {
                            if (rented is not null)
                            {
                                ArrayPool<byte>.Shared.Return(rented);
                            }

                            rented = ArrayPool<byte>.Shared.Rent(Math.Max(maxLength, 256));
                        }

                        var length = Utf8YamlReader.CopyScalarValue(buffer, in token, rented);
                        writer.WriteStringValue(rented.AsSpan(0, length), scalarStyle);
                        break;
                }
            }

            CloseCollections(open, end, writer);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        static void CloseCollections(Stack<(int EndRow, bool IsMapping)> open, int row, Utf8YamlWriter writer)
        {
            while (open.TryPeek(out var collection) && collection.EndRow <= row)
            {
                open.Pop();
                if (collection.IsMapping)
                {
                    writer.WriteEndMapping();
                }
                else
                {
                    writer.WriteEndSequence();
                }
            }
        }
    }

    /// <summary>Returns the buffers of the document to the pool.</summary>
    public void Dispose()
    {
        // The arrays of a clone are not pooled: its nodes stay valid
        var rows = _rows;
        if (rows is null || _isClone)
            return;

        _rows = null;
        _rowCount = 0;
        _utf8 = default;
        _lineIndex = null;
        ArrayPool<Row>.Shared.Return(rows);
        if (_ownedBuffer is not null)
        {
            ArrayPool<byte>.Shared.Return(_ownedBuffer);
            _ownedBuffer = null;
        }
    }

    internal ref readonly Row GetRow(int index)
    {
        ObjectDisposedException.ThrowIf(_rows is null, this);
        return ref _rows[index];
    }

    internal ReadOnlySpan<byte> Utf8Span
    {
        get
        {
            ObjectDisposedException.ThrowIf(_rows is null, this);
            return _utf8.Span;
        }
    }

    internal string? GetTag(int tagIndex) => tagIndex < 0 ? null : _tags![tagIndex];

    // The location of a position of the input, like the marks of the parser: UTF-16 index, 0-based line and column.
    // The lines are indexed on first use, so a location costs a binary search and the length of its line.
    internal Mark GetMark(int offset)
    {
        var buffer = Utf8Span;
        var lines = _lineIndex ??= LineIndex.Create(buffer);
        var line = Array.BinarySearch(lines.Starts, offset);
        if (line < 0)
        {
            line = ~line - 1;
        }

        var lineStart = lines.Starts[line];
        var index = lines.CharStarts[line] + Encoding.UTF8.GetCharCount(buffer[lineStart..offset]);

        // A byte order mark at the start of a line, before a document, is not a column
        var columnStart = lineStart;
        if (offset >= lineStart + 3 && buffer[lineStart..].StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            columnStart += 3;
        }

        var lineNumber = lines.Lines is null ? line : lines.Lines[line];
        return new Mark((int)Math.Min(index, int.MaxValue), lineNumber, Encoding.UTF8.GetCharCount(buffer[columnStart..offset]));
    }

    // The offset in the input of an offset of the text: they differ in a clone, whose text is made of parts of the input
    internal int GetSourceOffset(int offset)
    {
        if (_segmentStarts is null)
            return offset;

        var segment = Array.BinarySearch(_segmentStarts, offset);
        if (segment < 0)
        {
            segment = ~segment - 1;
        }

        return _segmentSourceStarts![segment] + (offset - _segmentStarts[segment]);
    }

    // A copy of the node in a document that owns its arrays: the rows of the node, and the rows of the anchors that its
    // aliases refer to before it, so that an alias resolves to the same node. The bytes are copied by lines, so that the
    // positions are the ones of the input and a block scalar is followed by the same byte.
    internal YamlElement CloneNode(int index)
    {
        if (_isClone)
            return new YamlElement(this, index);

        var rows = _rows;
        ObjectDisposedException.ThrowIf(rows is null, this);
        var buffer = Utf8Span;
        var ranges = new List<(int Start, int End)>();
        var pending = new Stack<int>();
        AddRange(index);
        while (pending.TryPop(out var start))
        {
            for (var i = start; i < rows[start].EndRow; i++)
            {
                if (rows[i].Token.Kind is Utf8YamlTokenKind.Alias && FindAnchor(i) is >= 0 and var anchored)
                {
                    AddRange(anchored);
                }
            }
        }

        // The lines of the rows, merged when they overlap
        var sourceLines = _lineIndex ??= LineIndex.Create(buffer);
        var spans = new List<(int Start, int End)>();
        var rangeSpans = new int[ranges.Count];
        for (var i = 0; i < ranges.Count; i++)
        {
            ref readonly var first = ref rows[ranges[i].Start];
            var start = LineStart(buffer, first.NodeStart);
            var end = LineEnd(buffer, first.NodeEnd);
            if (spans.Count > 0 && start <= spans[^1].End)
            {
                spans[^1] = (spans[^1].Start, Math.Max(end, spans[^1].End));
            }
            else
            {
                spans.Add((start, end));
            }

            rangeSpans[i] = spans.Count - 1;
        }

        var segmentStarts = new int[spans.Count];
        var segmentSourceStarts = new int[spans.Count];
        var length = 0;
        foreach (var span in spans)
        {
            length += span.End - span.Start;
        }

        var text = new byte[length];
        var written = 0;
        for (var i = 0; i < spans.Count; i++)
        {
            segmentStarts[i] = written;
            segmentSourceStarts[i] = spans[i].Start;
            buffer[spans[i].Start..spans[i].End].CopyTo(text.AsSpan(written));
            written += spans[i].End - spans[i].Start;
        }

        var rowCount = 0;
        foreach (var range in ranges)
        {
            rowCount += range.End - range.Start;
        }

        var cloneRows = new Row[rowCount];
        var cloneIndex = -1;
        var rowStart = 0;
        for (var i = 0; i < ranges.Count; i++)
        {
            var range = ranges[i];
            var delta = segmentStarts[rangeSpans[i]] - segmentSourceStarts[rangeSpans[i]];
            for (var row = range.Start; row < range.End; row++)
            {
                cloneRows[rowStart + row - range.Start] = Rebase(rows[row], delta, rowStart - range.Start);
            }

            if (index >= range.Start && index < range.End)
            {
                cloneIndex = rowStart + index - range.Start;
            }

            rowStart += range.End - range.Start;
        }

        var clone = new YamlDocument(text, cloneRows, _tags, CreateLineIndex(text, segmentStarts, segmentSourceStarts, sourceLines), segmentStarts, segmentSourceStarts);
        return new YamlElement(clone, cloneIndex);

        // Adds the node and its descendants, unless a range has them; a range they contain is replaced
        void AddRange(int row)
        {
            var end = rows[row].EndRow;
            foreach (var range in ranges)
            {
                if (row >= range.Start && end <= range.End)
                    return;
            }

            ranges.RemoveAll(range => range.Start >= row && range.End <= end);
            var position = 0;
            while (position < ranges.Count && ranges[position].Start < row)
            {
                position++;
            }

            ranges.Insert(position, (row, end));
            pending.Push(row);
        }

        static Row Rebase(Row row, int delta, int rowDelta)
        {
            row.Token.Start += delta;
            row.Token.ValueStart += delta;
            row.Token.Indicator = row.Token.Indicator < 0 ? row.Token.Indicator : row.Token.Indicator + delta;
            row.Token.ExplicitKeyIndicator = row.Token.ExplicitKeyIndicator < 0 ? row.Token.ExplicitKeyIndicator : row.Token.ExplicitKeyIndicator + delta;
            row.Token.Properties.AnchorStart = row.Token.Properties.AnchorStart < 0 ? row.Token.Properties.AnchorStart : row.Token.Properties.AnchorStart + delta;
            row.Token.Properties.TagStart = row.Token.Properties.TagStart < 0 ? row.Token.Properties.TagStart : row.Token.Properties.TagStart + delta;
            row.NodeStart += delta;
            row.NodeEnd += delta;
            row.EndRow += rowDelta;
            return row;
        }

        static int LineStart(ReadOnlySpan<byte> buffer, int offset) => buffer[..offset].LastIndexOfAny((byte)'\r', (byte)'\n') + 1;

        // After the line break of the line, so that a block scalar at the end of the line is followed by it
        static int LineEnd(ReadOnlySpan<byte> buffer, int offset)
        {
            var lineBreak = buffer[offset..].IndexOfAny((byte)'\r', (byte)'\n');
            if (lineBreak < 0)
                return buffer.Length;

            lineBreak += offset;
            return buffer[lineBreak] == '\r' && lineBreak + 1 < buffer.Length && buffer[lineBreak + 1] == '\n' ? lineBreak + 2 : lineBreak + 1;
        }
    }

    // The lines of the text of a clone, numbered like the lines of the input they come from: each segment starts a line
    private static LineIndex CreateLineIndex(ReadOnlySpan<byte> text, int[] segmentStarts, int[] segmentSourceStarts, LineIndex sourceLines)
    {
        var starts = new List<int>();
        var charStarts = new List<long>();
        var lineNumbers = new List<int>();
        for (var i = 0; i < segmentStarts.Length; i++)
        {
            var line = Array.BinarySearch(sourceLines.Starts, segmentSourceStarts[i]);
            var position = segmentStarts[i];
            var end = i + 1 < segmentStarts.Length ? segmentStarts[i + 1] : text.Length;
            var charIndex = sourceLines.CharStarts[line];
            starts.Add(position);
            charStarts.Add(charIndex);
            lineNumbers.Add(line);
            while (true)
            {
                var next = text[position..end].IndexOfAny((byte)'\r', (byte)'\n');
                if (next < 0)
                    break;

                var lineBreak = position + next;
                if (text[lineBreak] == '\r' && lineBreak + 1 < end && text[lineBreak + 1] == '\n')
                {
                    lineBreak++;
                }

                charIndex += Encoding.UTF8.GetCharCount(text[position..(lineBreak + 1)]);
                position = lineBreak + 1;
                line++;

                // The next segment starts the next line; after the last one, the line is empty like in the input
                if (position == end && i + 1 < segmentStarts.Length)
                    break;

                starts.Add(position);
                charStarts.Add(charIndex);
                lineNumbers.Add(line);
            }
        }

        return new LineIndex([.. starts], [.. charStarts], [.. lineNumbers]);
    }

    // The last node before the alias that has its anchor, in the same document: an anchor is not defined in the others
    internal int FindAnchor(int aliasIndex)
    {
        var rows = _rows!;
        var buffer = Utf8Span;
        ref readonly var alias = ref rows[aliasIndex];
        var name = buffer.Slice(alias.Token.Start + 1, alias.Token.Length - 1);
        var documentRoot = 0;
        foreach (var root in _documentRoots)
        {
            if (root > aliasIndex)
                break;

            documentRoot = root;
        }

        for (var i = aliasIndex - 1; i >= documentRoot; i--)
        {
            ref readonly var row = ref rows[i];
            if (row.Token.Properties.AnchorStart >= 0 && buffer.Slice(row.Token.Properties.AnchorStart + 1, row.Token.Properties.AnchorLength - 1).SequenceEqual(name))
                return i;
        }

        return -1;
    }

    // The byte offset and the UTF-16 index of the start of every line. A line ends at a line feed, or at a carriage
    // return that is not followed by a line feed.
    private sealed class LineIndex(int[] starts, long[] charStarts, int[]? lines = null)
    {
        public int[] Starts { get; } = starts;

        public long[] CharStarts { get; } = charStarts;

        // The number of each line in the input, when it is not its index: the lines of a clone come from parts of the input
        public int[]? Lines { get; } = lines;

        public static LineIndex Create(ReadOnlySpan<byte> buffer)
        {
            var starts = new List<int> { 0 };
            var charStarts = new List<long> { 0 };
            var position = 0;
            long charIndex = 0;
            while (true)
            {
                var next = buffer[position..].IndexOfAny((byte)'\r', (byte)'\n');
                if (next < 0)
                    break;

                var lineBreak = position + next;
                if (buffer[lineBreak] == '\r' && lineBreak + 1 < buffer.Length && buffer[lineBreak + 1] == '\n')
                {
                    lineBreak++;
                }

                charIndex += Encoding.UTF8.GetCharCount(buffer[position..(lineBreak + 1)]);
                position = lineBreak + 1;
                starts.Add(position);
                charStarts.Add(charIndex);
            }

            return new LineIndex([.. starts], [.. charStarts]);
        }
    }

    [StructLayout(LayoutKind.Auto)]
    internal struct Row
    {
        public Utf8YamlReader.TokenData Token;
        public int TagIndex;
        public int NodeStart;
        public int NodeEnd;

        // Index of the row after the node and its descendants
        public int EndRow;

        // Number of child nodes: items of a sequence, keys and values of a mapping
        public int ChildCount;
    }
}
