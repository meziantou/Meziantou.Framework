using System.Buffers;
using System.Buffers.Text;
using System.Collections;
using System.Runtime.InteropServices;
using System.Text;

namespace Meziantou.Framework.Yaml;

/// <summary>A node of a <see cref="YamlReadOnlyDocument"/>: a scalar, a mapping, a sequence or an alias.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct YamlReadOnlyNode : IEquatable<YamlReadOnlyNode>
{
    private readonly YamlReadOnlyDocument _document;
    private readonly int _index;

    internal YamlReadOnlyNode(YamlReadOnlyDocument document, int index)
    {
        _document = document;
        _index = index;
    }

    private ref readonly YamlReadOnlyDocument.Row Row => ref _document.GetRow(_index);

    /// <summary>Gets the kind of the node.</summary>
    public YamlReadOnlyNodeKind Kind => Row.Token.Kind switch
    {
        Utf8YamlTokenKind.MappingStart => YamlReadOnlyNodeKind.Mapping,
        Utf8YamlTokenKind.SequenceStart => YamlReadOnlyNodeKind.Sequence,
        Utf8YamlTokenKind.Alias => YamlReadOnlyNodeKind.Alias,
        _ => YamlReadOnlyNodeKind.Scalar,
    };

    /// <summary>Gets the style of the scalar.</summary>
    public ScalarStyle ScalarStyle => (ScalarStyle)(Row.Token.Flags & Utf8YamlTokenFlags.ScalarStyleMask);

    /// <summary>Gets a value indicating whether the collection is written in the flow style.</summary>
    public bool IsFlow => (Row.Token.Flags & Utf8YamlTokenFlags.Flow) != Utf8YamlTokenFlags.None;

    /// <summary>Gets the anchor of the node, or the name an alias refers to.</summary>
    public string? Anchor
    {
        get
        {
            ref readonly var row = ref Row;
            if (row.Token.Kind is Utf8YamlTokenKind.Alias)
                return Encoding.UTF8.GetString(_document.Utf8Span.Slice(row.Token.Start + 1, row.Token.Length - 1));

            return row.Token.Properties.AnchorStart < 0 ? null : Encoding.UTF8.GetString(_document.Utf8Span.Slice(row.Token.Properties.AnchorStart + 1, row.Token.Properties.AnchorLength - 1));
        }
    }

    /// <summary>Gets the tag of the node resolved with the %TAG directives, such as <c>tag:yaml.org,2002:str</c>.</summary>
    public string? Tag => _document.GetTag(Row.TagIndex);

    /// <summary>Gets the offset of the node in <see cref="YamlReadOnlyDocument.Utf8Text"/>, in bytes, at its anchor or tag if any.</summary>
    /// <remarks>
    /// The offset counts UTF-8 bytes, even when the document was parsed from a string. Use <see cref="Start"/> for a
    /// position in the string.
    /// </remarks>
    public int Utf8Offset => Row.NodeStart;

    /// <summary>Gets the length of the text of the node in <see cref="YamlReadOnlyDocument.Utf8Text"/>, in bytes, with its properties and its content.</summary>
    public int Utf8Length => Row.NodeEnd - Row.NodeStart;

    /// <summary>Gets the location of the start of the node, at its anchor or tag if any.</summary>
    /// <remarks>
    /// <see cref="Mark.Index"/> counts UTF-16 characters: it is the index in the string the document was parsed from,
    /// or in the decoded text of UTF-8 input. Line and column are 0-based.
    /// </remarks>
    public Mark Start => _document.GetMark(Row.NodeStart);

    /// <summary>Gets the location of the end of the node, after its content.</summary>
    /// <remarks>See <see cref="Start"/>.</remarks>
    public Mark End => _document.GetMark(Row.NodeEnd);

    /// <summary>Gets the text of the node in the input, with its properties and its content.</summary>
    public string GetRawText() => Encoding.UTF8.GetString(GetRawUtf8Text());

    /// <summary>Gets the text of the node in the input, byte for byte, without allocating.</summary>
    public ReadOnlySpan<byte> GetRawUtf8Text() => _document.Utf8Span.Slice(Utf8Offset, Utf8Length);

    /// <summary>Writes the node and its descendants: their values, styles, anchors and tags.</summary>
    /// <param name="writer">The destination.</param>
    /// <remarks>
    /// A plain scalar stays plain, so it keeps its type. A quoted or block scalar may be written in another quoted style,
    /// and a folded scalar is written as a literal one. Comments and document markers are not written.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// A tag is neither <c>!</c>, a local tag nor a URI, and the writer has no <c>%TAG</c> directive whose prefix starts
    /// it: see <see cref="Utf8YamlWriter.WriteTagDirective(ReadOnlySpan{char}, ReadOnlySpan{char})"/>.
    /// <see cref="YamlReadOnlyDocument.WriteTo(Utf8YamlWriter)"/> writes the directives of the documents.
    /// </exception>
    public void WriteTo(Utf8YamlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _document.WriteNode(_index, writer);
    }

    /// <summary>Gets the number of items of a sequence, or of entries of a mapping.</summary>
    /// <exception cref="InvalidOperationException">The node is not a collection.</exception>
    public int GetLength()
    {
        ref readonly var row = ref Row;
        return row.Token.Kind switch
        {
            Utf8YamlTokenKind.SequenceStart => row.ChildCount,
            Utf8YamlTokenKind.MappingStart => row.ChildCount / 2,
            _ => throw new InvalidOperationException("The node is not a collection."),
        };
    }

    /// <summary>Decodes the value of the scalar.</summary>
    /// <exception cref="InvalidOperationException">The node is not a scalar.</exception>
    public string GetString()
    {
        ref readonly var row = ref EnsureScalar();
        return Utf8YamlReader.DecodeString(_document.Utf8Span, in row.Token);
    }

    /// <summary>Compares the value of the scalar with a UTF-8 text without allocating.</summary>
    public bool ValueEquals(ReadOnlySpan<byte> utf8Text)
    {
        ref readonly var row = ref Row;
        if (row.Token.Kind is not Utf8YamlTokenKind.Scalar)
            return false;

        return Utf8YamlReader.ValueEquals(_document.Utf8Span, in row.Token, utf8Text);
    }

    /// <summary>Compares the value of the scalar with a text without allocating.</summary>
    /// <remarks>A text with an invalid UTF-16 sequence, such as a lone surrogate, is equal to no value.</remarks>
    public bool ValueEquals(ReadOnlySpan<char> text)
    {
        ref readonly var row = ref Row;
        if (row.Token.Kind is not Utf8YamlTokenKind.Scalar)
            return false;

        return Utf8YamlReader.ValueEquals(_document.Utf8Span, in row.Token, text);
    }

    /// <summary>Compares the value of the scalar with a text without allocating.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public bool ValueEquals(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return ValueEquals(text.AsSpan());
    }

    /// <summary>Parses the plain scalar as a 64-bit integer, in the decimal notation.</summary>
    public bool TryGetInt64(out long value)
    {
        ref readonly var row = ref Row;
        value = 0;
        return IsPlainRaw(in row, out var raw) && Utf8Parser.TryParse(raw, out value, out var consumed) && consumed == raw.Length;
    }

    /// <summary>Parses the plain scalar as a floating-point number, including <c>.inf</c>, <c>-.inf</c> and <c>.nan</c>.</summary>
    public bool TryGetDouble(out double value)
    {
        ref readonly var row = ref Row;
        value = 0;
        if (!IsPlainRaw(in row, out var raw))
            return false;

        if (raw.SequenceEqual(".inf"u8) || raw.SequenceEqual("+.inf"u8) || raw.SequenceEqual(".Inf"u8) || raw.SequenceEqual(".INF"u8))
        {
            value = double.PositiveInfinity;
            return true;
        }

        if (raw.SequenceEqual("-.inf"u8) || raw.SequenceEqual("-.Inf"u8) || raw.SequenceEqual("-.INF"u8))
        {
            value = double.NegativeInfinity;
            return true;
        }

        if (raw.SequenceEqual(".nan"u8) || raw.SequenceEqual(".NaN"u8) || raw.SequenceEqual(".NAN"u8))
        {
            value = double.NaN;
            return true;
        }

        return Utf8Parser.TryParse(raw, out value, out var consumed) && consumed == raw.Length;
    }

    /// <summary>Parses the plain scalar as a boolean of the core schema: <c>true</c>, <c>True</c>, <c>TRUE</c>, <c>false</c>, <c>False</c>, <c>FALSE</c>.</summary>
    public bool TryGetBoolean(out bool value)
    {
        ref readonly var row = ref Row;
        value = false;
        if (!IsPlainRaw(in row, out var raw))
            return false;

        if (raw.SequenceEqual("true"u8) || raw.SequenceEqual("True"u8) || raw.SequenceEqual("TRUE"u8))
        {
            value = true;
            return true;
        }

        return raw.SequenceEqual("false"u8) || raw.SequenceEqual("False"u8) || raw.SequenceEqual("FALSE"u8);
    }

    /// <summary>Gets a value indicating whether the node is a null of the core schema: an empty plain scalar, <c>~</c> or <c>null</c>, without tag.</summary>
    public bool IsNull
    {
        get
        {
            ref readonly var row = ref Row;
            if (!IsPlainRaw(in row, out var raw) || row.TagIndex >= 0)
                return false;

            return raw.IsEmpty || raw.SequenceEqual("~"u8) || raw.SequenceEqual("null"u8) || raw.SequenceEqual("Null"u8) || raw.SequenceEqual("NULL"u8);
        }
    }

    /// <summary>Gets the item of a sequence.</summary>
    /// <exception cref="InvalidOperationException">The node is not a sequence.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the sequence.</exception>
    public YamlReadOnlyNode this[int index]
    {
        get
        {
            ref readonly var row = ref Row;
            if (row.Token.Kind is not Utf8YamlTokenKind.SequenceStart)
                throw new InvalidOperationException("The node is not a sequence.");

            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, row.ChildCount);
            var child = _index + 1;
            for (var i = 0; i < index; i++)
            {
                child = _document.GetRow(child).EndRow;
            }

            return new YamlReadOnlyNode(_document, child);
        }
    }

    /// <summary>Gets the value of a mapping entry whose key is the scalar <paramref name="key"/>.</summary>
    /// <exception cref="KeyNotFoundException">The mapping has no such key.</exception>
    public YamlReadOnlyNode this[string key] => TryGetProperty(key, out var value) ? value : throw new KeyNotFoundException($"The mapping has no key '{key}'.");

    /// <summary>Finds the value of a mapping entry whose key is a scalar equal to a UTF-8 text, without allocating.</summary>
    public bool TryGetProperty(ReadOnlySpan<byte> utf8Key, out YamlReadOnlyNode value)
    {
        foreach (var entry in EnumerateMapping())
        {
            if (entry.Key.ValueEquals(utf8Key))
            {
                value = entry.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>Finds the value of a mapping entry whose key is a scalar equal to <paramref name="key"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public bool TryGetProperty(string key, out YamlReadOnlyNode value)
    {
        ArgumentNullException.ThrowIfNull(key);
        return TryGetProperty(key.AsSpan(), out value);
    }

    /// <summary>Finds the value of a mapping entry whose key is a scalar equal to <paramref name="key"/>, without allocating.</summary>
    public bool TryGetProperty(ReadOnlySpan<char> key, out YamlReadOnlyNode value)
    {
        foreach (var entry in EnumerateMapping())
        {
            if (entry.Key.ValueEquals(key))
            {
                value = entry.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>Gets the node an alias refers to: the last node before it with that anchor.</summary>
    /// <exception cref="InvalidOperationException">The node is not an alias, or its anchor is not defined.</exception>
    public YamlReadOnlyNode ResolveAlias()
    {
        if (Row.Token.Kind is not Utf8YamlTokenKind.Alias)
            throw new InvalidOperationException("The node is not an alias.");

        var anchored = _document.FindAnchor(_index);
        return anchored >= 0 ? new YamlReadOnlyNode(_document, anchored) : throw new InvalidOperationException($"The anchor '{Anchor}' is not defined.");
    }

    /// <summary>Enumerates the items of a sequence.</summary>
    /// <exception cref="InvalidOperationException">The node is not a sequence.</exception>
    public SequenceEnumerator EnumerateSequence()
    {
        if (Row.Token.Kind is not Utf8YamlTokenKind.SequenceStart)
            throw new InvalidOperationException("The node is not a sequence.");

        return new SequenceEnumerator(_document, _index);
    }

    /// <summary>Enumerates the entries of a mapping.</summary>
    /// <exception cref="InvalidOperationException">The node is not a mapping.</exception>
    public MappingEnumerator EnumerateMapping()
    {
        if (Row.Token.Kind is not Utf8YamlTokenKind.MappingStart)
            throw new InvalidOperationException("The node is not a mapping.");

        return new MappingEnumerator(_document, _index);
    }

    /// <inheritdoc/>
    public bool Equals(YamlReadOnlyNode other) => ReferenceEquals(_document, other._document) && _index == other._index;

    /// <inheritdoc/>
    public override bool Equals([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] object? obj) => obj is YamlReadOnlyNode other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_document, _index);

    /// <summary>Returns the value of a scalar, or the text of a collection.</summary>
    public override string ToString() => Kind is YamlReadOnlyNodeKind.Scalar ? GetString() : GetRawText();

    public static bool operator ==(YamlReadOnlyNode left, YamlReadOnlyNode right) => left.Equals(right);

    public static bool operator !=(YamlReadOnlyNode left, YamlReadOnlyNode right) => !left.Equals(right);

    private ref readonly YamlReadOnlyDocument.Row EnsureScalar()
    {
        ref readonly var row = ref Row;
        if (row.Token.Kind is not Utf8YamlTokenKind.Scalar)
            throw new InvalidOperationException("The node is not a scalar.");

        return ref row;
    }

    private bool IsPlainRaw(in YamlReadOnlyDocument.Row row, out ReadOnlySpan<byte> raw)
    {
        if (row.Token.Kind is not Utf8YamlTokenKind.Scalar || (ScalarStyle)(row.Token.Flags & Utf8YamlTokenFlags.ScalarStyleMask) is not ScalarStyle.Plain || (row.Token.Flags & Utf8YamlTokenFlags.RawValue) == Utf8YamlTokenFlags.None)
        {
            raw = default;
            return false;
        }

        raw = row.Token.ValueLength == 0 ? default : _document.Utf8Span.Slice(row.Token.ValueStart, row.Token.ValueLength);
        return true;
    }

    /// <summary>Enumerates the items of a sequence.</summary>
    [StructLayout(LayoutKind.Auto)]
    public struct SequenceEnumerator : IEnumerable<YamlReadOnlyNode>, IEnumerator<YamlReadOnlyNode>
    {
        private readonly YamlReadOnlyDocument _document;
        private readonly int _parent;
        private int _current;

        internal SequenceEnumerator(YamlReadOnlyDocument document, int parent)
        {
            _document = document;
            _parent = parent;
            _current = -1;
        }

        public readonly YamlReadOnlyNode Current => new(_document, _current);

        readonly object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            var next = _current < 0 ? _parent + 1 : _document.GetRow(_current).EndRow;
            if (next >= _document.GetRow(_parent).EndRow)
                return false;

            _current = next;
            return true;
        }

        public void Reset() => _current = -1;

        public readonly SequenceEnumerator GetEnumerator() => new(_document, _parent);

        readonly IEnumerator<YamlReadOnlyNode> IEnumerable<YamlReadOnlyNode>.GetEnumerator() => GetEnumerator();

        readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public readonly void Dispose()
        {
        }
    }

    /// <summary>Enumerates the entries of a mapping.</summary>
    [StructLayout(LayoutKind.Auto)]
    public struct MappingEnumerator : IEnumerable<KeyValuePair<YamlReadOnlyNode, YamlReadOnlyNode>>, IEnumerator<KeyValuePair<YamlReadOnlyNode, YamlReadOnlyNode>>
    {
        private readonly YamlReadOnlyDocument _document;
        private readonly int _parent;
        private int _key;
        private int _value;

        internal MappingEnumerator(YamlReadOnlyDocument document, int parent)
        {
            _document = document;
            _parent = parent;
            _key = -1;
            _value = -1;
        }

        public readonly KeyValuePair<YamlReadOnlyNode, YamlReadOnlyNode> Current => new(new YamlReadOnlyNode(_document, _key), new YamlReadOnlyNode(_document, _value));

        readonly object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            var next = _value < 0 ? _parent + 1 : _document.GetRow(_value).EndRow;
            if (next >= _document.GetRow(_parent).EndRow)
                return false;

            _key = next;
            _value = _document.GetRow(next).EndRow;
            return true;
        }

        public void Reset()
        {
            _key = -1;
            _value = -1;
        }

        public readonly MappingEnumerator GetEnumerator() => new(_document, _parent);

        readonly IEnumerator<KeyValuePair<YamlReadOnlyNode, YamlReadOnlyNode>> IEnumerable<KeyValuePair<YamlReadOnlyNode, YamlReadOnlyNode>>.GetEnumerator() => GetEnumerator();

        readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public readonly void Dispose()
        {
        }
    }
}
