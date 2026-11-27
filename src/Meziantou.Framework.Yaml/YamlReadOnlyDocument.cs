using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;

namespace Meziantou.Framework.Yaml;

/// <summary>
/// A read-only representation of a UTF-8 YAML stream: one row per node in a pooled array over the input bytes. Scalars
/// are decoded only when asked for, and every node keeps the location of its text in the input.
/// </summary>
/// <remarks>Dispose the document to return its buffers to the pool. Its nodes must not be used after that.</remarks>
public sealed class YamlReadOnlyDocument : IDisposable
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

    private YamlReadOnlyDocument(ReadOnlyMemory<byte> utf8, byte[]? ownedBuffer, Row[] rows, int rowCount, int[] documentRoots, string?[]? tags, Utf8YamlReader.TagDirective[]?[] documentTagDirectives)
    {
        _documentTagDirectives = documentTagDirectives;
        _utf8 = utf8;
        _ownedBuffer = ownedBuffer;
        _rows = rows;
        _rowCount = rowCount;
        _documentRoots = documentRoots;
        _tags = tags;
    }

    /// <summary>Gets the number of documents of the stream.</summary>
    public int DocumentCount => _documentRoots.Length;

    /// <summary>Gets the root node of the first document.</summary>
    /// <exception cref="InvalidOperationException">The stream has no document.</exception>
    public YamlReadOnlyNode Root => DocumentCount > 0 ? GetDocumentRoot(0) : throw new InvalidOperationException("The YAML stream has no document.");

    /// <summary>Gets the UTF-8 input of the document.</summary>
    public ReadOnlyMemory<byte> Utf8Text => _utf8;

    /// <summary>Gets the root node of a document of the stream.</summary>
    public YamlReadOnlyNode GetDocumentRoot(int index) => new(this, _documentRoots[index]);

    /// <summary>Parses a UTF-8 YAML stream. The document keeps a reference to <paramref name="utf8Yaml"/>, which must not change while it is used.</summary>
    /// <exception cref="YamlException">The input is not valid YAML.</exception>
    public static YamlReadOnlyDocument Parse(ReadOnlyMemory<byte> utf8Yaml, Utf8YamlReaderOptions options = default)
    {
        return Parse(utf8Yaml, ownedBuffer: null, options);
    }

    /// <summary>Parses a YAML stream.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="yaml"/> is <see langword="null"/>.</exception>
    /// <exception cref="YamlException">The input is not valid YAML.</exception>
    public static YamlReadOnlyDocument Parse(string yaml, Utf8YamlReaderOptions options = default)
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
    public static YamlReadOnlyDocument Parse(Stream utf8Yaml, Utf8YamlReaderOptions options = default)
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

    private static YamlReadOnlyDocument Parse(ReadOnlyMemory<byte> utf8, byte[]? ownedBuffer, Utf8YamlReaderOptions options)
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

            return new YamlReadOnlyDocument(utf8, ownedBuffer, rows, rowCount, [.. roots], tags?.ToArray(), [.. rootTagDirectives]);
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
    /// <remarks>See <see cref="YamlReadOnlyNode.WriteTo(Utf8YamlWriter)"/>. The tags are written with the handles of the directives.</remarks>
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
        var rows = _rows;
        if (rows is null)
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

        return new Mark((int)Math.Min(index, int.MaxValue), line, Encoding.UTF8.GetCharCount(buffer[columnStart..offset]));
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
    private sealed class LineIndex(int[] starts, long[] charStarts)
    {
        public int[] Starts { get; } = starts;

        public long[] CharStarts { get; } = charStarts;

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
