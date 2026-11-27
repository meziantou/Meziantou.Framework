using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Unicode;
using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml;

/// <summary>
/// Writes YAML as UTF-8 to a <see cref="Stream"/> or an <see cref="IBufferWriter{T}"/>, one node at a time, like
/// <c>System.Text.Json.Utf8JsonWriter</c>. Block collections are indented, flow collections are written on one line,
/// and every scalar gets the simplest style that reads back as the same value.
/// </summary>
/// <remarks>
/// <para>
/// The bytes are kept in a buffer until <see cref="Flush"/> or <see cref="Dispose"/>, or until the buffer is large: call
/// one of them when the writing is done.
/// </para>
/// <para>
/// A mapping alternates keys and values. A key is usually written with <see cref="WritePropertyName(ReadOnlySpan{char})"/>,
/// but any node can be a key: a collection or a long scalar is written as an explicit key (<c>? key</c>).
/// </para>
/// <para>
/// <see cref="WriteTagDirective(ReadOnlySpan{char}, ReadOnlySpan{char})"/> declares the <c>%TAG</c> directives of a
/// document before it starts, and <see cref="WriteComment(ReadOnlySpan{char})"/> writes comments between the entries of
/// block collections.
/// </para>
/// </remarks>
public sealed class Utf8YamlWriter : IDisposable
{
    private const int FlushThreshold = 64 * 1024;

    // An implicit key is at most 1024 characters, its properties included; a UTF-8 byte is at most one character
    private const int MaxImplicitKeyLength = 1000;

    // Letters, digits, space, and punctuation that is neither an indicator nor a line break: no rule of the plain style
    // applies to them, wherever they appear in the scalar and whatever the context.
    private static readonly SearchValues<byte> PlainSafeBytes = SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 -_./+=~;$()<^\\"u8);

    // The ASCII characters a quoted scalar contains as is, except its quote and the escape character
    private static readonly SearchValues<byte> QuotedSafeBytes = SearchValues.Create(" !#$%&()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[]^_`abcdefghijklmnopqrstuvwxyz{|}~"u8);

    private readonly IBufferWriter<byte>? _bufferWriter;
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The stream belongs to the caller")]
    private readonly Stream? _stream;
    private readonly int _indentSize;
    private readonly int _maxDepth;
    private byte[] _buffer;
    private int _pending;
    private long _committed;
    private bool _atLineStart = true;
    private bool _disposed;

    private Frame[] _frames = new Frame[8];
    private int _depth;
    private DocumentState _documentState;

    // The properties of the next node, as written: "&anchor", "!tag", separated by a space
    private byte[] _properties = new byte[64];
    private int _propertiesLength;
    private bool _hasAnchor;
    private bool _hasTag;

    // The %TAG directives of the current document, or of the next one before it starts: the handle and the decoded prefix
    private readonly List<(byte[] Handle, byte[] Prefix)> _tagDirectives = [];

    /// <summary>Initializes a new instance of the <see cref="Utf8YamlWriter"/> class that writes to a buffer writer.</summary>
    /// <param name="bufferWriter">The destination of the UTF-8 bytes.</param>
    /// <param name="options">The options of the writer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="bufferWriter"/> is <see langword="null"/>.</exception>
    public Utf8YamlWriter(IBufferWriter<byte> bufferWriter, Utf8YamlWriterOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(bufferWriter);
        _bufferWriter = bufferWriter;
        Options = options;
        _indentSize = options.EffectiveIndentSize;
        _maxDepth = options.EffectiveMaxDepth;
        _buffer = ArrayPool<byte>.Shared.Rent(4096);
    }

    /// <summary>Initializes a new instance of the <see cref="Utf8YamlWriter"/> class that writes to a stream.</summary>
    /// <param name="utf8Stream">The destination of the UTF-8 bytes. The writer does not dispose it.</param>
    /// <param name="options">The options of the writer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="utf8Stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="utf8Stream"/> is not writable.</exception>
    public Utf8YamlWriter(Stream utf8Stream, Utf8YamlWriterOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(utf8Stream);
        if (!utf8Stream.CanWrite)
            throw new ArgumentException("The stream is not writable.", nameof(utf8Stream));

        _stream = utf8Stream;
        Options = options;
        _indentSize = options.EffectiveIndentSize;
        _maxDepth = options.EffectiveMaxDepth;
        _buffer = ArrayPool<byte>.Shared.Rent(4096);
    }

    /// <summary>Gets the options of the writer.</summary>
    public Utf8YamlWriterOptions Options { get; }

    /// <summary>Gets the number of collections that are started and not ended.</summary>
    public int CurrentDepth => _depth;

    /// <summary>Gets the number of bytes written to the destination so far.</summary>
    public long BytesCommitted => _committed;

    /// <summary>Gets the number of bytes written to the buffer of the writer and not yet to the destination.</summary>
    public int BytesPending => _pending;

    private bool IsKeyPosition => _depth > 0 && _frames[_depth - 1].Kind is FrameKind.BlockMapping or FrameKind.FlowMapping && (_frames[_depth - 1].Count & 1) == 0;

    private bool InFlow => _depth > 0 && _frames[_depth - 1].Kind is FrameKind.FlowMapping or FrameKind.FlowSequence;

    /// <summary>Starts a new document with an explicit <c>---</c> marker. The first document does not need one.</summary>
    /// <exception cref="InvalidOperationException">A collection is not ended, or properties are pending.</exception>
    public void WriteStartDocument()
    {
        EnsureNotDisposed();
        if (_depth > 0)
            throw new InvalidOperationException("A document cannot start inside a collection.");

        EnsureNoProperties();
        if (_documentState is not DocumentState.AfterDirectives)
        {
            // The directives declared for the previous document do not apply to this one
            _tagDirectives.Clear();
        }

        EnsureNewLine();
        WriteBytes("---"u8);
        _documentState = DocumentState.AfterStartMarker;
    }

    /// <summary>
    /// Declares a <c>%TAG</c> directive for the next document: the tags that start with <paramref name="prefix"/> are
    /// written with <paramref name="handle"/>, such as <c>!e!point</c> for <c>tag:example.com,2000:point</c>.
    /// </summary>
    /// <param name="handle">The handle: <c>!</c>, <c>!!</c>, or <c>!</c> followed by letters, digits, <c>-</c> or <c>_</c> and <c>!</c>, such as <c>!e!</c>.</param>
    /// <param name="prefix">The prefix of the tags, decoded: <c>%</c> escapes are written where they are needed.</param>
    /// <remarks>
    /// A directive is written before the <c>---</c> of its document, which the writer writes after the directives. Once the
    /// root node of a document is written, a directive ends it with <c>...</c> and applies to the next document. A tag that
    /// is neither a local tag nor a URI can only be written with a directive whose prefix starts it.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="handle"/> is not a valid handle, or <paramref name="prefix"/> is empty or contains an invalid UTF-16 sequence.</exception>
    /// <exception cref="InvalidOperationException">A collection is not ended, properties are pending, the document already started, or the handle is already declared.</exception>
    public void WriteTagDirective(ReadOnlySpan<char> handle, ReadOnlySpan<char> prefix)
    {
        Span<byte> handleBuffer = stackalloc byte[64];
        Span<byte> prefixBuffer = stackalloc byte[256];
        var utf8Handle = ToUtf8(handle, handleBuffer, out var rentedHandle, nameof(handle));
        try
        {
            var utf8Prefix = ToUtf8(prefix, prefixBuffer, out var rentedPrefix, nameof(prefix));
            try
            {
                WriteTagDirectiveCore(utf8Handle, utf8Prefix);
            }
            finally
            {
                Return(rentedPrefix);
            }
        }
        finally
        {
            Return(rentedHandle);
        }
    }

    /// <summary>Writes a comment on its own line: <c># text</c>, one line per line of <paramref name="text"/>.</summary>
    /// <param name="text">The text of the comment. Its line breaks start new comment lines.</param>
    /// <remarks>
    /// A comment is written before the root node of a document, after it, or between the entries of a block collection, at
    /// their indentation. A comment inside an empty block collection is followed by <c>{}</c> or <c>[]</c> on the next line.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="text"/> contains an invalid UTF-16 sequence or a character that a comment cannot contain, such as a control character.</exception>
    /// <exception cref="InvalidOperationException">The writer is inside a flow collection, between a key and its value, or properties are pending.</exception>
    public void WriteComment(ReadOnlySpan<char> text)
    {
        Span<byte> stackBuffer = stackalloc byte[256];
        var utf8 = ToUtf8(text, stackBuffer, out var rented, nameof(text));
        try
        {
            WriteCommentCore(utf8, nameof(text));
        }
        finally
        {
            Return(rented);
        }
    }

    /// <summary>Ends the current document with an explicit <c>...</c> marker.</summary>
    /// <exception cref="InvalidOperationException">No document is started, a collection is not ended, or properties are pending.</exception>
    public void WriteEndDocument()
    {
        EnsureNotDisposed();
        if (_depth > 0)
            throw new InvalidOperationException("A document cannot end inside a collection.");

        if (_documentState is DocumentState.Start or DocumentState.AfterEndMarker)
            throw new InvalidOperationException("There is no document to end.");

        EnsureNoProperties();
        StartDocumentAfterDirectives();
        EnsureNewLine();
        WriteBytes("..."u8);
        WriteNewLine();
        _documentState = DocumentState.AfterEndMarker;
        _tagDirectives.Clear();
    }

    /// <summary>Starts a mapping.</summary>
    /// <param name="style">The style of the mapping. Inside a flow collection, every collection is written in the flow style.</param>
    /// <exception cref="InvalidOperationException">A node cannot be written here, or the maximum depth is reached.</exception>
    public void WriteStartMapping(YamlStyle style = YamlStyle.Any) => WriteStartCollection(isMapping: true, style);

    /// <summary>Ends the current mapping.</summary>
    /// <exception cref="InvalidOperationException">The current collection is not a mapping, its last key has no value, or properties are pending.</exception>
    public void WriteEndMapping() => WriteEndCollection(isMapping: true);

    /// <summary>Starts a sequence.</summary>
    /// <param name="style">The style of the sequence. Inside a flow collection, every collection is written in the flow style.</param>
    /// <exception cref="InvalidOperationException">A node cannot be written here, or the maximum depth is reached.</exception>
    public void WriteStartSequence(YamlStyle style = YamlStyle.Any) => WriteStartCollection(isMapping: false, style);

    /// <summary>Ends the current sequence.</summary>
    /// <exception cref="InvalidOperationException">The current collection is not a sequence, or properties are pending.</exception>
    public void WriteEndSequence() => WriteEndCollection(isMapping: false);

    /// <summary>Writes the key of a mapping entry, as a string scalar.</summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> contains an invalid UTF-16 sequence.</exception>
    /// <exception cref="InvalidOperationException">The writer is not at the key of a mapping entry.</exception>
    public void WritePropertyName(ReadOnlySpan<char> name)
    {
        EnsureKeyPosition();
        WriteStringValue(name);
    }

    /// <summary>Writes the key of a mapping entry, as a string scalar.</summary>
    /// <exception cref="ArgumentException"><paramref name="utf8Name"/> is not valid UTF-8.</exception>
    /// <exception cref="InvalidOperationException">The writer is not at the key of a mapping entry.</exception>
    public void WritePropertyName(ReadOnlySpan<byte> utf8Name)
    {
        EnsureKeyPosition();
        WriteStringValue(utf8Name);
    }

    /// <summary>Writes a string scalar.</summary>
    /// <param name="value">The value.</param>
    /// <param name="style">
    /// The preferred style. <see cref="ScalarStyle.Any"/> writes a plain scalar when it reads back as the same string,
    /// and a quoted one otherwise. <see cref="ScalarStyle.Plain"/> writes a plain scalar even when it reads back as
    /// another type, such as <c>123</c>. A quoted style that cannot represent the value falls back to double quotes, and
    /// <see cref="ScalarStyle.Literal"/> and <see cref="ScalarStyle.Folded"/> write a literal block scalar where it is
    /// possible.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="value"/> contains an invalid UTF-16 sequence, or cannot be written in the plain style that was asked for.</exception>
    /// <exception cref="InvalidOperationException">A node cannot be written here.</exception>
    public void WriteStringValue(ReadOnlySpan<char> value, ScalarStyle style = ScalarStyle.Any)
    {
        Span<byte> stackBuffer = stackalloc byte[256];
        var utf8 = ToUtf8(value, stackBuffer, out var rented, nameof(value));
        try
        {
            WriteScalar(utf8, style, verbatim: false);
        }
        finally
        {
            Return(rented);
        }
    }

    /// <summary>Writes a string scalar from its UTF-8 bytes.</summary>
    /// <param name="utf8Value">The value, encoded in UTF-8.</param>
    /// <param name="style">The preferred style, see <see cref="WriteStringValue(ReadOnlySpan{char}, ScalarStyle)"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="utf8Value"/> is not valid UTF-8, or cannot be written in the plain style that was asked for.</exception>
    /// <exception cref="InvalidOperationException">A node cannot be written here.</exception>
    public void WriteStringValue(ReadOnlySpan<byte> utf8Value, ScalarStyle style = ScalarStyle.Any)
    {
        if (!Utf8.IsValid(utf8Value))
            throw new ArgumentException("The value is not valid UTF-8.", nameof(utf8Value));

        WriteScalar(utf8Value, style, verbatim: false);
    }

    /// <summary>Writes an integer as a plain scalar.</summary>
    /// <exception cref="InvalidOperationException">A node cannot be written here.</exception>
    public void WriteNumberValue(int value) => WriteNumberValue((long)value);

    /// <summary>Writes an integer as a plain scalar.</summary>
    /// <exception cref="InvalidOperationException">A node cannot be written here.</exception>
    public void WriteNumberValue(long value)
    {
        Span<byte> text = stackalloc byte[24];
        _ = Utf8Formatter.TryFormat(value, text, out var length);
        WriteScalar(text[..length], ScalarStyle.Plain, verbatim: true);
    }

    /// <summary>Writes an integer as a plain scalar.</summary>
    /// <exception cref="InvalidOperationException">A node cannot be written here.</exception>
    public void WriteNumberValue(uint value) => WriteNumberValue((ulong)value);

    /// <summary>Writes an integer as a plain scalar.</summary>
    /// <exception cref="InvalidOperationException">A node cannot be written here.</exception>
    public void WriteNumberValue(ulong value)
    {
        Span<byte> text = stackalloc byte[24];
        _ = Utf8Formatter.TryFormat(value, text, out var length);
        WriteScalar(text[..length], ScalarStyle.Plain, verbatim: true);
    }

    /// <summary>Writes a floating-point number as a plain scalar: <c>.inf</c>, <c>-.inf</c> and <c>.nan</c> for the special values.</summary>
    /// <exception cref="InvalidOperationException">A node cannot be written here.</exception>
    public void WriteNumberValue(float value)
    {
        Span<byte> text = stackalloc byte[48];
        var length = float.IsFinite(value) ? FormatFloatingPoint(value, text) : FormatSpecialFloatingPoint(value, text);
        WriteScalar(text[..length], ScalarStyle.Plain, verbatim: true);
    }

    /// <summary>Writes a floating-point number as a plain scalar: <c>.inf</c>, <c>-.inf</c> and <c>.nan</c> for the special values.</summary>
    /// <exception cref="InvalidOperationException">A node cannot be written here.</exception>
    public void WriteNumberValue(double value)
    {
        Span<byte> text = stackalloc byte[48];
        var length = double.IsFinite(value) ? FormatFloatingPoint(value, text) : FormatSpecialFloatingPoint(value, text);
        WriteScalar(text[..length], ScalarStyle.Plain, verbatim: true);
    }

    /// <summary>Writes a decimal number as a plain scalar.</summary>
    /// <exception cref="InvalidOperationException">A node cannot be written here.</exception>
    public void WriteNumberValue(decimal value)
    {
        Span<byte> text = stackalloc byte[48];
        _ = value.TryFormat(text, out var length, provider: CultureInfo.InvariantCulture);
        WriteScalar(text[..length], ScalarStyle.Plain, verbatim: true);
    }

    /// <summary>Writes <c>true</c> or <c>false</c> as a plain scalar.</summary>
    /// <exception cref="InvalidOperationException">A node cannot be written here.</exception>
    public void WriteBooleanValue(bool value) => WriteScalar(value ? "true"u8 : "false"u8, ScalarStyle.Plain, verbatim: true);

    /// <summary>Writes <c>null</c> as a plain scalar.</summary>
    /// <exception cref="InvalidOperationException">A node cannot be written here.</exception>
    public void WriteNullValue() => WriteScalar("null"u8, ScalarStyle.Plain, verbatim: true);

    /// <summary>Writes an alias to the node with the anchor <paramref name="anchorName"/>: <c>*name</c>.</summary>
    /// <exception cref="ArgumentException"><paramref name="anchorName"/> is not a valid anchor name.</exception>
    /// <exception cref="InvalidOperationException">A node cannot be written here, or properties are pending: an alias has none.</exception>
    public void WriteAliasValue(ReadOnlySpan<char> anchorName)
    {
        Span<byte> stackBuffer = stackalloc byte[128];
        var utf8 = ToUtf8(anchorName, stackBuffer, out var rented, nameof(anchorName));
        try
        {
            WriteAliasValueCore(utf8, nameof(anchorName));
        }
        finally
        {
            Return(rented);
        }
    }

    /// <summary>Sets the anchor of the next node: <c>&amp;name</c>.</summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not a valid anchor name.</exception>
    /// <exception cref="InvalidOperationException">The next node already has an anchor.</exception>
    public void WriteAnchor(ReadOnlySpan<char> name)
    {
        Span<byte> stackBuffer = stackalloc byte[128];
        var utf8 = ToUtf8(name, stackBuffer, out var rented, nameof(name));
        try
        {
            WriteAnchorCore(utf8, nameof(name));
        }
        finally
        {
            Return(rented);
        }
    }

    /// <summary>Sets the tag of the next node.</summary>
    /// <param name="tag">
    /// The resolved tag: <c>!</c>, a local tag such as <c>!point</c>, or a URI such as <c>tag:yaml.org,2002:str</c>,
    /// written as <c>!!str</c>. A tag that starts with the prefix of a <c>%TAG</c> directive of the document is written
    /// with its handle (see <see cref="WriteTagDirective(ReadOnlySpan{char}, ReadOnlySpan{char})"/>), another URI verbatim
    /// (<c>!&lt;uri&gt;</c>), and the characters a tag cannot contain are escaped (<c>%21</c>).
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="tag"/> contains an invalid UTF-16 sequence, or is neither <c>!</c>, a local tag, a URI, nor starts
    /// with the prefix of a <c>%TAG</c> directive of the document.
    /// </exception>
    /// <exception cref="InvalidOperationException">The next node already has a tag.</exception>
    public void WriteTag(ReadOnlySpan<char> tag)
    {
        Span<byte> stackBuffer = stackalloc byte[128];
        var utf8 = ToUtf8(tag, stackBuffer, out var rented, nameof(tag));
        try
        {
            WriteTagCore(utf8, nameof(tag));
        }
        finally
        {
            Return(rented);
        }
    }

    /// <summary>Writes the pending bytes to the destination, and flushes the stream.</summary>
    /// <exception cref="ObjectDisposedException">The writer is disposed.</exception>
    public void Flush()
    {
        EnsureNotDisposed();
        FlushCore();
        _stream?.Flush();
    }

    /// <summary>Ends the last line of a complete document, writes the pending bytes and releases the buffer of the writer.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        if (_depth == 0)
        {
            // Directives without a document start an empty one
            StartDocumentAfterDirectives();
        }

        if (_depth == 0 && !_atLineStart)
        {
            WriteNewLine();
        }

        FlushCore();
        _stream?.Flush();
        _disposed = true;
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = [];
    }

    // A plain scalar on one line of a YAML input, unchanged: in a block context it is written as is, without checking it again
    internal void WriteSourcePlainScalarCore(ReadOnlySpan<byte> value)
    {
        // A block context allows characters that a flow collection does not. A final ':' was followed by an indicator in
        // the input, but it is followed by a line break when the key is written explicitly, after '?', or when the scalar
        // was in a flow collection.
        if (InFlow || value is [.., (byte)':'])
        {
            WriteScalar(value, ScalarStyle.Plain, verbatim: false);
            return;
        }

        WriteScalar(value, ScalarStyle.Plain, verbatim: true);
    }

    internal void WriteAliasValueCore(ReadOnlySpan<byte> name, string parameterName)
    {
        EnsureNotDisposed();
        ValidateAnchorName(name, parameterName);
        if (_propertiesLength > 0)
            throw new InvalidOperationException("An alias cannot have an anchor or a tag.");

        if (_depth == 0)
        {
            EnsureRootAllowed();
            StartDocumentAfterDirectives();
        }

        var isKey = IsKeyPosition;
        var placement = BeginNode(implicitKey: true);
        WriteSeparator(placement);
        WriteByte((byte)'*');
        WriteBytes(name);
        if (isKey)
        {
            // An anchor name can contain ':', so a space separates the alias from the value indicator
            _frames[_depth - 1].KeyNeedsSpace = true;
        }

        CompleteNode();
    }

    internal void WriteAnchorCore(ReadOnlySpan<byte> name, string parameterName)
    {
        EnsureNotDisposed();
        if (_hasAnchor)
            throw new InvalidOperationException("The next node already has an anchor.");

        ValidateAnchorName(name, parameterName);
        AppendPropertySeparator(name.Length + 1);
        _properties[_propertiesLength++] = (byte)'&';
        name.CopyTo(_properties.AsSpan(_propertiesLength));
        _propertiesLength += name.Length;
        _hasAnchor = true;
    }

    internal void WriteTagCore(ReadOnlySpan<byte> tag, string parameterName)
    {
        EnsureNotDisposed();
        if (_hasTag)
            throw new InvalidOperationException("The next node already has a tag.");

        if (tag.IsEmpty)
            throw new ArgumentException("The tag is empty.", parameterName);

        // The handle whose prefix is the longest start of the tag: a declared one, or a default one that is not redeclared
        ReadOnlySpan<byte> handle = default;
        var prefixLength = 0;
        if (!tag.SequenceEqual("!"u8))
        {
            FindHandle(tag, "!"u8, "!"u8, ref handle, ref prefixLength);
            FindHandle(tag, "!!"u8, "tag:yaml.org,2002:"u8, ref handle, ref prefixLength);
            foreach (var directive in _tagDirectives)
            {
                FindHandle(tag, directive.Handle, directive.Prefix, ref handle, ref prefixLength);
            }
        }

        if (handle.IsEmpty && !tag.SequenceEqual("!"u8) && tag[0] != '!' && !HasUriScheme(tag))
            throw new ArgumentException("The tag must be '!', a local tag that starts with '!', a URI such as 'tag:example.com,2000:type', or start with the prefix of a %TAG directive of the document.", parameterName);

        // A character is at most 3 escaped bytes, plus the handle or "!<" and ">"
        var maxHandleLength = 2;
        foreach (var directive in _tagDirectives)
        {
            maxHandleLength = Math.Max(maxHandleLength, directive.Handle.Length);
        }

        AppendPropertySeparator((tag.Length * 3) + maxHandleLength + 1);
        var destination = _properties.AsSpan(_propertiesLength);
        int length;
        if (tag.SequenceEqual("!"u8))
        {
            destination[0] = (byte)'!';
            length = 1;
        }
        else if (!handle.IsEmpty)
        {
            handle.CopyTo(destination);
            length = handle.Length + EncodeTag(tag[prefixLength..], destination[handle.Length..], shorthand: true);
        }
        else
        {
            // A local tag whose handle is redeclared, or a URI that no handle starts
            "!<"u8.CopyTo(destination);
            length = 2 + EncodeTag(tag, destination[2..], shorthand: false);
            destination[length++] = (byte)'>';
        }

        _propertiesLength += length;
        _hasTag = true;
    }

    // A handle applies when its prefix starts the tag, with a suffix: a default handle only when it is not redeclared
    private void FindHandle(ReadOnlySpan<byte> tag, ReadOnlySpan<byte> candidate, ReadOnlySpan<byte> prefix, scoped ref ReadOnlySpan<byte> handle, ref int prefixLength)
    {
        if (prefix.Length <= prefixLength || tag.Length <= prefix.Length || !tag.StartsWith(prefix))
            return;

        if (candidate.Length <= 2 && !ReferenceEqualsDirective(candidate, prefix))
        {
            foreach (var directive in _tagDirectives)
            {
                if (directive.Handle.AsSpan().SequenceEqual(candidate))
                    return;
            }
        }

        handle = candidate;
        prefixLength = prefix.Length;
    }

    // Whether the handle and the prefix are a declared directive, rather than a default one
    private bool ReferenceEqualsDirective(ReadOnlySpan<byte> handle, ReadOnlySpan<byte> prefix)
    {
        foreach (var directive in _tagDirectives)
        {
            if (directive.Handle.AsSpan().SequenceEqual(handle) && directive.Prefix.AsSpan().SequenceEqual(prefix))
                return true;
        }

        return false;
    }

    internal void WriteTagDirectiveCore(ReadOnlySpan<byte> handle, ReadOnlySpan<byte> prefix)
    {
        EnsureNotDisposed();
        if (_depth > 0)
            throw new InvalidOperationException("A directive cannot be written inside a collection.");

        EnsureNoProperties();
        if (handle.Length < 1 || handle[0] != '!' || handle[^1] != '!' || (handle.Length > 2 && !IsWord(handle[1..^1])))
            throw new ArgumentException("The handle must be '!', '!!', or '!' followed by letters, digits, '-' or '_' and '!'.", nameof(handle));

        if (prefix.IsEmpty)
            throw new ArgumentException("The prefix is empty.", nameof(prefix));

        switch (_documentState)
        {
            case DocumentState.AfterStartMarker:
                throw new InvalidOperationException("A directive must be written before the start of its document.");

            case DocumentState.AfterRoot:
                // A directive follows the end of the previous document
                EnsureNewLine();
                WriteBytes("..."u8);
                WriteNewLine();
                _tagDirectives.Clear();
                _documentState = DocumentState.AfterEndMarker;
                break;
        }

        foreach (var directive in _tagDirectives)
        {
            if (directive.Handle.AsSpan().SequenceEqual(handle))
                throw new InvalidOperationException("The handle is already declared for this document.");
        }

        EnsureNewLine();
        WriteBytes("%TAG "u8);
        WriteBytes(handle);
        WriteByte((byte)' ');

        // A global prefix cannot start with a flow indicator; a local prefix starts with '!'
        var encoded = GetSpan((prefix.Length * 3) + 1);
        var length = 0;
        var rest = prefix;
        if (prefix[0] == '!')
        {
            encoded[length++] = (byte)'!';
            rest = prefix[1..];
        }
        else if (IsFlowIndicator(prefix[0]))
        {
            length += EncodeTag(prefix[..1], encoded, shorthand: true);
            rest = prefix[1..];
        }

        length += EncodeTag(rest, encoded[length..], shorthand: false);
        Advance(length);
        WriteNewLine();
        _tagDirectives.Add((handle.ToArray(), prefix.ToArray()));
        _documentState = DocumentState.AfterDirectives;

        static bool IsWord(ReadOnlySpan<byte> value)
        {
            foreach (var c in value)
            {
                // Like the reader, which also accepts '_'
                if (!char.IsAsciiLetterOrDigit((char)c) && c is not ((byte)'-' or (byte)'_'))
                    return false;
            }

            return true;
        }
    }

    private void WriteCommentCore(ReadOnlySpan<byte> text, string parameterName)
    {
        EnsureNotDisposed();
        EnsureNoProperties();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c < 0x80)
            {
                if ((c < 0x20 && c is not ((byte)'\t' or (byte)'\r' or (byte)'\n')) || c == 0x7F)
                    throw new ArgumentException("A comment cannot contain a control character.", parameterName);

                continue;
            }

            // NEL, LS and PS are line breaks in YAML 1.1: the rest of the line would be read as YAML
            if (Rune.DecodeFromUtf8(text[i..], out var rune, out var length) is not OperationStatus.Done || !IsPrintableNonBreak(rune))
                throw new ArgumentException("A comment cannot contain a character that is not printable, or NEL, LS or PS.", parameterName);

            i += length - 1;
        }

        var indent = 0;
        if (_depth > 0)
        {
            ref var frame = ref _frames[_depth - 1];
            if (frame.Kind is FrameKind.FlowMapping or FrameKind.FlowSequence)
                throw new InvalidOperationException("A comment cannot be written inside a flow collection.");

            if (frame.Kind is FrameKind.BlockMapping && (frame.Count & 1) == 1)
                throw new InvalidOperationException("A comment cannot be written between a key and its value.");

            // The first entry is no longer on the line of the indicator of the collection
            indent = frame.Indent;
            frame.FirstEntryInline = false;
            frame.HasComment = true;
        }

        while (true)
        {
            var lineBreak = text.IndexOfAny((byte)'\r', (byte)'\n');
            var line = lineBreak < 0 ? text : text[..lineBreak];
            EnsureNewLine();
            WriteIndent(indent);
            WriteByte((byte)'#');
            if (!line.IsEmpty)
            {
                WriteByte((byte)' ');
                WriteBytes(line);
            }

            WriteNewLine();
            if (lineBreak < 0)
                break;

            text = text[(lineBreak + (text[lineBreak] == '\r' && lineBreak + 1 < text.Length && text[lineBreak + 1] == '\n' ? 2 : 1))..];
        }
    }

    // A root node is the only one of its document
    private void EnsureRootAllowed()
    {
        if (_documentState is DocumentState.AfterRoot)
            throw new InvalidOperationException("The document already has a root node: call WriteStartDocument to write another document.");
    }

    // The directives of a document are followed by its "---"
    private void StartDocumentAfterDirectives()
    {
        if (_documentState is DocumentState.AfterDirectives)
        {
            EnsureNewLine();
            WriteBytes("---"u8);
            _documentState = DocumentState.AfterStartMarker;
        }
    }

    private void WriteStartCollection(bool isMapping, YamlStyle style)
    {
        EnsureNotDisposed();
        if (_depth == 0)
        {
            EnsureRootAllowed();
            StartDocumentAfterDirectives();
        }

        if (_depth >= _maxDepth)
            throw new InvalidOperationException(FormattableString.Invariant($"The maximum depth of {_maxDepth} collections is reached."));

        var isFlow = style is YamlStyle.Flow || InFlow;

        // A collection as a key is an explicit key ("? key"), which every reader understands
        var placement = BeginNode(implicitKey: false);
        var hasProperties = WriteProperties(ref placement);
        if (_depth == _frames.Length)
        {
            Array.Resize(ref _frames, _frames.Length * 2);
        }

        if (isFlow)
        {
            WriteSeparator(placement);
            WriteByte(isMapping ? (byte)'{' : (byte)'[');
            _frames[_depth++] = new Frame { Kind = isMapping ? FrameKind.FlowMapping : FrameKind.FlowSequence };
            return;
        }

        // The start of a block collection is written with its first entry: an empty one is written "{}" or "[]"
        _frames[_depth++] = new Frame
        {
            Kind = isMapping ? FrameKind.BlockMapping : FrameKind.BlockSequence,
            Indent = placement.BlockIndent,
            FirstEntryInline = placement.Compact && !hasProperties,
            EmptyNeedsSpace = placement.NeedsSpace,
        };
    }

    private void WriteEndCollection(bool isMapping)
    {
        EnsureNotDisposed();
        if (_depth == 0)
            throw new InvalidOperationException("There is no collection to end.");

        ref var frame = ref _frames[_depth - 1];
        var frameIsMapping = frame.Kind is FrameKind.BlockMapping or FrameKind.FlowMapping;
        if (frameIsMapping != isMapping)
            throw new InvalidOperationException(isMapping ? "The current collection is a sequence." : "The current collection is a mapping.");

        if (isMapping && (frame.Count & 1) == 1)
            throw new InvalidOperationException("The last key of the mapping has no value.");

        EnsureNoProperties();
        switch (frame.Kind)
        {
            case FrameKind.FlowMapping:
                WriteByte((byte)'}');
                break;

            case FrameKind.FlowSequence:
                WriteByte((byte)']');
                break;

            default:
                if (frame.Count == 0)
                {
                    if (frame.HasComment)
                    {
                        // After the comment, on its own line, more indented than the parent
                        EnsureNewLine();
                        WriteIndent(frame.Indent);
                    }
                    else if (frame.EmptyNeedsSpace)
                    {
                        WriteByte((byte)' ');
                    }

                    WriteBytes(isMapping ? "{}"u8 : "[]"u8);
                }

                break;
        }

        _depth--;
        CompleteNode();
    }

    private void WriteScalar(ReadOnlySpan<byte> value, ScalarStyle requested, bool verbatim)
    {
        EnsureNotDisposed();
        var isKey = IsKeyPosition;
        var inFlow = InFlow;
        var isRoot = _depth == 0;
        if (isRoot)
        {
            EnsureRootAllowed();
            StartDocumentAfterDirectives();
        }

        var frame = isRoot ? default : _frames[_depth - 1];
        var hasProperties = _propertiesLength > 0;
        var isEmptyPlain = value.IsEmpty && requested is ScalarStyle.Plain;
        if (isEmptyPlain && !hasProperties && frame.Kind is FrameKind.FlowSequence)
            throw new ArgumentException("An empty plain scalar cannot be an item of a flow sequence.", nameof(value));

        var style = verbatim || isEmptyPlain ? ScalarStyle.Plain : ResolveStyle(value, requested, inFlow, isKey, _hasTag);

        // At the first column of a line, "---" and "..." are document markers. A root node follows the "---" of its
        // document on the same line, unless a comment came between them.
        var afterStartMarker = isRoot && _documentState is DocumentState.AfterStartMarker;
        var atColumnZero = isRoot
            ? !afterStartMarker || _atLineStart
            : isKey && frame.Kind is FrameKind.BlockMapping && frame.Indent == 0 && !(frame.Count == 0 && frame.FirstEntryInline);
        var isMarker = style is ScalarStyle.Plain && atColumnZero && StartsWithDocumentMarker(value);
        if (isMarker && requested is ScalarStyle.Any)
        {
            style = ResolveQuotedStyle(value);
            isMarker = false;
        }

        // An empty root needs a document marker to be a document
        var indentRoot = false;
        if (isRoot && !afterStartMarker && (isMarker || (value.IsEmpty && style is ScalarStyle.Plain && !hasProperties)))
        {
            EnsureNewLine();
            WriteBytes("---"u8);
            _documentState = DocumentState.AfterStartMarker;
        }
        else if (isRoot && isMarker)
        {
            // After a comment: a space moves the scalar off the first column
            indentRoot = true;
        }

        var implicitKey = true;
        if (isKey)
        {
            if (frame.Kind is FrameKind.BlockMapping && (isMarker || (value.IsEmpty && style is ScalarStyle.Plain)))
            {
                implicitKey = false;
            }
            else
            {
                implicitKey = _propertiesLength + MeasureScalar(value, style) <= MaxImplicitKeyLength;
            }
        }

        var placement = BeginNode(implicitKey);
        placement.NeedsSpace |= indentRoot;
        WriteProperties(ref placement);
        switch (style)
        {
            case ScalarStyle.Plain:
                if (value.IsEmpty)
                {
                    // Only the properties: a space separates them from a flow indicator, which a tag can contain
                    if (hasProperties && inFlow)
                    {
                        WriteByte((byte)' ');
                    }
                }
                else
                {
                    WriteSeparator(placement);
                    WriteBytes(value);
                }

                break;

            case ScalarStyle.SingleQuoted:
                WriteSeparator(placement);
                WriteSingleQuoted(value);
                break;

            case ScalarStyle.Literal:
                WriteSeparator(placement);
                WriteLiteral(value, placement.LiteralIndent);
                break;

            default:
                WriteSeparator(placement);
                WriteDoubleQuoted(value);
                break;
        }

        CompleteNode();
    }

    // Writes what comes before a node: the line break and the indentation of an entry, and its indicator
    private Placement BeginNode(bool implicitKey)
    {
        if (_depth == 0)
        {
            EnsureRootAllowed();

            // After "---" on the same line; a comment may have ended that line
            var afterMarker = _documentState is DocumentState.AfterStartMarker && !_atLineStart;
            return new Placement { NeedsSpace = afterMarker, BlockIndent = 0, LiteralIndent = _indentSize };
        }

        ref var frame = ref _frames[_depth - 1];
        switch (frame.Kind)
        {
            case FrameKind.BlockSequence:
                StartBlockEntry(ref frame);
                WriteByte((byte)'-');
                return new Placement { NeedsSpace = true, Compact = true, BlockIndent = frame.Indent + 2, LiteralIndent = frame.Indent + 2 };

            case FrameKind.BlockMapping when (frame.Count & 1) == 0:
                StartBlockEntry(ref frame);
                frame.ExplicitKey = !implicitKey;
                frame.KeyNeedsSpace = false;
                if (implicitKey)
                    return default;

                WriteByte((byte)'?');
                return new Placement { NeedsSpace = true, Compact = true, BlockIndent = frame.Indent + 2, LiteralIndent = frame.Indent + 2 };

            case FrameKind.BlockMapping:
                if (frame.ExplicitKey)
                {
                    EnsureNewLine();
                    WriteIndent(frame.Indent);
                    WriteByte((byte)':');
                    return new Placement { NeedsSpace = true, Compact = true, BlockIndent = frame.Indent + 2, LiteralIndent = frame.Indent + 2 };
                }

                if (frame.KeyNeedsSpace)
                {
                    WriteByte((byte)' ');
                }

                WriteByte((byte)':');
                return new Placement { NeedsSpace = true, BlockIndent = frame.Indent + _indentSize, LiteralIndent = frame.Indent + _indentSize };

            case FrameKind.FlowSequence:
                if (frame.Count > 0)
                {
                    WriteBytes(", "u8);
                }

                return default;

            case FrameKind.FlowMapping when (frame.Count & 1) == 0:
                if (frame.Count > 0)
                {
                    WriteBytes(", "u8);
                }

                frame.ExplicitKey = !implicitKey;
                frame.KeyNeedsSpace = false;
                if (implicitKey)
                    return default;

                WriteByte((byte)'?');
                return new Placement { NeedsSpace = true };

            default:
                if (frame.ExplicitKey || frame.KeyNeedsSpace)
                {
                    WriteByte((byte)' ');
                }

                WriteByte((byte)':');
                return new Placement { NeedsSpace = true };
        }
    }

    // A block entry starts on its own line, at the indentation of the collection, except the first entry of a
    // collection that is an item or an explicit key or value: it follows the indicator ("- key: value")
    private void StartBlockEntry(ref Frame frame)
    {
        if (frame.Count == 0 && frame.FirstEntryInline)
        {
            WriteByte((byte)' ');
            return;
        }

        EnsureNewLine();
        WriteIndent(frame.Indent);
    }

    private void CompleteNode()
    {
        if (_depth == 0)
        {
            _documentState = DocumentState.AfterRoot;
            EnsureNewLine();
            return;
        }

        _frames[_depth - 1].Count++;
    }

    private bool WriteProperties(ref Placement placement)
    {
        if (_propertiesLength == 0)
            return false;

        WriteSeparator(placement);
        WriteBytes(_properties.AsSpan(0, _propertiesLength));
        _propertiesLength = 0;
        _hasAnchor = false;
        _hasTag = false;
        placement.NeedsSpace = true;
        return true;
    }

    private void WriteSeparator(in Placement placement)
    {
        if (placement.NeedsSpace)
        {
            WriteByte((byte)' ');
        }
    }

    private void AppendPropertySeparator(int length)
    {
        var required = _propertiesLength + length + 1;
        if (required > _properties.Length)
        {
            Array.Resize(ref _properties, Math.Max(_properties.Length * 2, required));
        }

        if (_propertiesLength > 0)
        {
            _properties[_propertiesLength++] = (byte)' ';
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // Styles
    // ------------------------------------------------------------------------------------------------------------

    private static ScalarStyle ResolveStyle(ReadOnlySpan<byte> value, ScalarStyle requested, bool inFlow, bool isKey, bool hasTag)
    {
        switch (requested)
        {
            case ScalarStyle.Plain:
                if (IsPlainSafe(value, inFlow))
                    return ScalarStyle.Plain;

                // Quoting a value that reads back as a string keeps its type
                if (hasTag || !ResolvesToNonString(value))
                    return ResolveQuotedStyle(value);

                throw new ArgumentException("The value cannot be written as a plain scalar here, and quoting it would change its type.", nameof(value));

            case ScalarStyle.SingleQuoted:
                return CanWriteSingleQuoted(value) ? ScalarStyle.SingleQuoted : ScalarStyle.DoubleQuoted;

            case ScalarStyle.DoubleQuoted:
                return ScalarStyle.DoubleQuoted;

            case ScalarStyle.Literal or ScalarStyle.Folded:
                return !inFlow && !isKey && CanWriteLiteral(value) ? ScalarStyle.Literal : ScalarStyle.DoubleQuoted;

            default:
                if (IsPlainSafe(value, inFlow) && IsUnambiguousPlain(value, inFlow, isKey) && (hasTag || !ResolvesToNonString(value)))
                    return ScalarStyle.Plain;

                return ResolveQuotedStyle(value);
        }
    }

    private static ScalarStyle ResolveQuotedStyle(ReadOnlySpan<byte> value) => CanWriteSingleQuoted(value) ? ScalarStyle.SingleQuoted : ScalarStyle.DoubleQuoted;

    // The rules of the plain style (YAML 1.2 §7.3.3) for a scalar on one line
    private static bool IsPlainSafe(ReadOnlySpan<byte> value, bool inFlow)
    {
        if (value.IsEmpty)
            return false;

        var first = value[0];
        var last = value[^1];
        if (first is (byte)' ' or (byte)'\t' || last is (byte)' ' or (byte)'\t')
            return false;

        // Indicators only carry a meaning as the first character
        if (first is (byte)'{' or (byte)'}' or (byte)'[' or (byte)']' or (byte)',' or (byte)'#' or (byte)'&' or (byte)'*' or (byte)'!' or (byte)'|' or (byte)'>' or (byte)'\'' or (byte)'"' or (byte)'%' or (byte)'@' or (byte)'`')
            return false;

        // '-', '?' and ':' start a plain scalar only when a safe character follows
        if (first is (byte)'-' or (byte)'?' or (byte)':' && (value.Length == 1 || value[1] is (byte)' ' or (byte)'\t' || (inFlow && IsFlowIndicator(value[1]))))
            return false;

        var start = value.IndexOfAnyExcept(PlainSafeBytes);
        if (start < 0)
            return true;

        for (var i = start; i < value.Length; i++)
        {
            var c = value[i];
            if (c < 0x80)
            {
                if (c is (byte)'\n' or (byte)'\r' || (c < 0x20 && c != '\t') || c == 0x7F)
                    return false;

                // A flow indicator closes the enclosing collection, but it is an ordinary character in a block context
                if (inFlow && IsFlowIndicator(c))
                    return false;

                // '#' only starts a comment after a space, so "C# 14" needs no quotes
                if (c == '#' && value[i - 1] is (byte)' ' or (byte)'\t')
                    return false;

                // ':' only separates a key from its value when a space (or the end of the scalar) follows it
                if (c == ':' && (i == value.Length - 1 || value[i + 1] is (byte)' ' or (byte)'\t' || (inFlow && IsFlowIndicator(value[i + 1]))))
                    return false;

                continue;
            }

            _ = Rune.DecodeFromUtf8(value[i..], out var rune, out var length);
            if (!IsPrintableNonBreak(rune))
                return false;

            i += length - 1;
        }

        return true;
    }

    // Rules on top of the plain style for the values the writer chooses to write plain: they avoid the constructs some
    // readers get wrong, such as a tab, a '?' in a flow collection, or the "<<" merge key
    private static bool IsUnambiguousPlain(ReadOnlySpan<byte> value, bool inFlow, bool isKey)
    {
        if (value.Contains((byte)'\t'))
            return false;

        if (inFlow && value.Contains((byte)'?'))
            return false;

        return !isKey || !value.SequenceEqual("<<"u8);
    }

    private static bool ResolvesToNonString(ReadOnlySpan<byte> value)
    {
        // The null, boolean and number rules of the schemas only match ASCII
        if (value.IsEmpty)
            return true;

        if (!Ascii.IsValid(value))
            return false;

        char[]? rented = null;
        Span<char> chars = value.Length <= 128 ? stackalloc char[128] : (rented = ArrayPool<char>.Shared.Rent(value.Length));
        try
        {
            _ = Ascii.ToUtf16(value, chars, out var length);
            var text = chars[..length];
            return YamlScalar.IsSchemaKeyword(text) || YamlScalar.ResolvesToNonString(text, YamlSchemaKind.Core);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }

    private static bool CanWriteSingleQuoted(ReadOnlySpan<byte> value)
    {
        // A line break inside a single-quoted scalar is folded when it is read back, and a control character can only be
        // written with a double-quoted escape
        var start = value.IndexOfAnyExcept(QuotedSafeBytes);
        if (start < 0)
            return true;

        for (var i = start; i < value.Length; i++)
        {
            var c = value[i];
            if (c < 0x80)
            {
                if (c is (< 0x20 and not (byte)'\t') or 0x7F)
                    return false;

                continue;
            }

            _ = Rune.DecodeFromUtf8(value[i..], out var rune, out var length);
            if (!IsPrintableNonBreak(rune))
                return false;

            i += length - 1;
        }

        return true;
    }

    private static bool CanWriteLiteral(ReadOnlySpan<byte> value)
    {
        var body = value.TrimEnd((byte)'\n');
        if (body.IsEmpty)
            return false;

        var seenContent = false;
        foreach (var range in body.Split((byte)'\n'))
        {
            var line = body[range];
            if (line.IsEmpty)
                continue;

            // The indentation of the content is the one of its first line, and a line of spaces only would be read as
            // an empty line
            if ((!seenContent && line[0] == ' ') || line.IndexOfAnyExcept((byte)' ', (byte)'\t') < 0)
                return false;

            seenContent = true;
            foreach (var c in line)
            {
                if (c is (byte)'\r' || (c < 0x20 && c != '\t') || c == 0x7F)
                    return false;
            }

            for (var i = 0; i < line.Length; i++)
            {
                if (line[i] < 0x80)
                    continue;

                _ = Rune.DecodeFromUtf8(line[i..], out var rune, out var length);
                if (!IsPrintableNonBreak(rune))
                    return false;

                i += length - 1;
            }
        }

        return true;
    }

    // A printable character that no YAML version reads as a line break: NEL, LS and PS are line breaks in YAML 1.1
    private static bool IsPrintableNonBreak(Rune rune)
    {
        var value = rune.Value;
        return value switch
        {
            < 0x20 => value == '\t',
            < 0x7F => true,
            < 0xA0 => false,
            0x2028 or 0x2029 or 0xFEFF or 0xFFFE or 0xFFFF => false,
            >= 0xD800 and <= 0xDFFF => false,
            _ => true,
        };
    }

    private static bool IsFlowIndicator(byte c) => c is (byte)',' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}';

    private static bool StartsWithDocumentMarker(ReadOnlySpan<byte> value)
    {
        return (value.StartsWith("---"u8) || value.StartsWith("..."u8)) && (value.Length == 3 || value[3] is (byte)' ' or (byte)'\t');
    }

    private static int MeasureScalar(ReadOnlySpan<byte> value, ScalarStyle style)
    {
        return style switch
        {
            ScalarStyle.Plain => value.Length,
            ScalarStyle.SingleQuoted => value.Length + value.Count((byte)'\'') + 2,
            _ => MeasureDoubleQuoted(value),
        };
    }

    private static int MeasureDoubleQuoted(ReadOnlySpan<byte> value)
    {
        var length = 2;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c < 0x80)
            {
                length += c is (byte)'"' or (byte)'\\' or (byte)'\0' or 0x07 or (byte)'\b' or (byte)'\t' or (byte)'\n' or 0x0B or (byte)'\f' or (byte)'\r' or 0x1B ? 2 : c is < 0x20 or 0x7F ? 4 : 1;
                continue;
            }

            _ = Rune.DecodeFromUtf8(value[i..], out var rune, out var runeLength);
            length += rune.Value switch
            {
                0x85 or 0x2028 or 0x2029 => 2,
                < 0xA0 => 4,
                0xFEFF or 0xFFFE or 0xFFFF => 6,
                _ => runeLength,
            };
            i += runeLength - 1;
        }

        return length;
    }

    // ------------------------------------------------------------------------------------------------------------
    // Scalars
    // ------------------------------------------------------------------------------------------------------------

    private void WriteSingleQuoted(ReadOnlySpan<byte> value)
    {
        WriteByte((byte)'\'');
        while (true)
        {
            var quote = value.IndexOf((byte)'\'');
            if (quote < 0)
            {
                WriteBytes(value);
                break;
            }

            WriteBytes(value[..(quote + 1)]);
            WriteByte((byte)'\'');
            value = value[(quote + 1)..];
        }

        WriteByte((byte)'\'');
    }

    private void WriteDoubleQuoted(ReadOnlySpan<byte> value)
    {
        WriteByte((byte)'"');
        while (!value.IsEmpty)
        {
            var special = value.IndexOfAnyExcept(QuotedSafeBytes);
            if (special < 0)
            {
                WriteBytes(value);
                break;
            }

            // '\'' is in the safe set of the single quotes only
            WriteBytes(value[..special]);
            value = value[special..];
            var c = value[0];
            if (c < 0x80)
            {
                switch (c)
                {
                    case (byte)'\'':
                        WriteByte(c);
                        break;
                    case (byte)'"':
                        WriteBytes("\\\""u8);
                        break;
                    case (byte)'\\':
                        WriteBytes("\\\\"u8);
                        break;
                    case (byte)'\0':
                        WriteBytes("\\0"u8);
                        break;
                    case 0x07:
                        WriteBytes("\\a"u8);
                        break;
                    case (byte)'\b':
                        WriteBytes("\\b"u8);
                        break;
                    case (byte)'\t':
                        WriteBytes("\\t"u8);
                        break;
                    case (byte)'\n':
                        WriteBytes("\\n"u8);
                        break;
                    case 0x0B:
                        WriteBytes("\\v"u8);
                        break;
                    case (byte)'\f':
                        WriteBytes("\\f"u8);
                        break;
                    case (byte)'\r':
                        WriteBytes("\\r"u8);
                        break;
                    case 0x1B:
                        WriteBytes("\\e"u8);
                        break;
                    default:
                        WriteHexEscape('x', c, 2);
                        break;
                }

                value = value[1..];
                continue;
            }

            _ = Rune.DecodeFromUtf8(value, out var rune, out var length);
            switch (rune.Value)
            {
                case 0x85:
                    WriteBytes("\\N"u8);
                    break;
                case 0x2028:
                    WriteBytes("\\L"u8);
                    break;
                case 0x2029:
                    WriteBytes("\\P"u8);
                    break;
                case < 0xA0:
                    WriteHexEscape('x', rune.Value, 2);
                    break;
                case 0xFEFF or 0xFFFE or 0xFFFF:
                    WriteHexEscape('u', rune.Value, 4);
                    break;
                default:
                    WriteBytes(value[..length]);
                    break;
            }

            value = value[length..];
        }

        WriteByte((byte)'"');
    }

    private void WriteHexEscape(char kind, int value, int digits)
    {
        var span = GetSpan(2 + digits);
        span[0] = (byte)'\\';
        span[1] = (byte)kind;
        for (var i = digits - 1; i >= 0; i--)
        {
            span[2 + i] = (byte)"0123456789ABCDEF"[value & 0xF];
            value >>= 4;
        }

        Advance(2 + digits);
    }

    private void WriteLiteral(ReadOnlySpan<byte> value, int indent)
    {
        var body = value.TrimEnd((byte)'\n');
        var trailingLineBreaks = value.Length - body.Length;
        WriteByte((byte)'|');
        switch (trailingLineBreaks)
        {
            case 0:
                WriteByte((byte)'-');
                break;
            case > 1:
                WriteByte((byte)'+');
                break;
        }

        WriteNewLine();
        foreach (var range in body.Split((byte)'\n'))
        {
            var line = body[range];
            if (!line.IsEmpty)
            {
                WriteIndent(indent);
                WriteBytes(line);
            }

            WriteNewLine();
        }

        for (var i = 1; i < trailingLineBreaks; i++)
        {
            WriteNewLine();
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // Tags and anchors
    // ------------------------------------------------------------------------------------------------------------

    // Escapes the bytes that a tag cannot contain as is (YAML 1.2 §6.8.1): every byte outside ns-uri-char, and in a
    // shorthand tag '!' and the flow indicators. A reader decodes the escapes, so '%' itself is escaped.
    private static int EncodeTag(ReadOnlySpan<byte> tag, Span<byte> destination, bool shorthand)
    {
        var length = 0;
        foreach (var c in tag)
        {
            var isUriCharacter = char.IsAsciiLetterOrDigit((char)c) || c is (byte)'-' or (byte)'#' or (byte)';' or (byte)'/' or (byte)'?' or (byte)':' or (byte)'@' or (byte)'&' or (byte)'=' or (byte)'+' or (byte)'$' or (byte)',' or (byte)'_' or (byte)'.' or (byte)'!' or (byte)'~' or (byte)'*' or (byte)'\'' or (byte)'(' or (byte)')' or (byte)'[' or (byte)']';
            if (isUriCharacter && !(shorthand && (c == '!' || IsFlowIndicator(c))))
            {
                destination[length++] = c;
                continue;
            }

            destination[length++] = (byte)'%';
            destination[length++] = (byte)"0123456789ABCDEF"[c >> 4];
            destination[length++] = (byte)"0123456789ABCDEF"[c & 0xF];
        }

        return length;
    }

    // A verbatim tag that is not a local tag must be a URI: a scheme, ':' and the rest
    private static bool HasUriScheme(ReadOnlySpan<byte> value)
    {
        if (!char.IsAsciiLetter((char)value[0]))
            return false;

        for (var i = 1; i < value.Length; i++)
        {
            var c = value[i];
            if (c == ':')
                return true;

            if (!char.IsAsciiLetterOrDigit((char)c) && c is not ((byte)'+' or (byte)'-' or (byte)'.'))
                return false;
        }

        return false;
    }

    // An anchor name is a sequence of printable characters other than spaces and flow indicators (YAML 1.2 §6.9.2)
    private static void ValidateAnchorName(ReadOnlySpan<byte> name, string parameterName)
    {
        if (name.IsEmpty)
            throw new ArgumentException("The anchor name is empty.", parameterName);

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c < 0x80)
            {
                if (c <= 0x20 || c == 0x7F || IsFlowIndicator(c))
                    throw new ArgumentException("The anchor name contains a character that is not allowed: a space, a flow indicator or a control character.", parameterName);

                continue;
            }

            // NEL, LS and PS are characters of YAML 1.2, like the reader reads them
            if (Rune.DecodeFromUtf8(name[i..], out var rune, out var length) is not OperationStatus.Done || (!IsPrintableNonBreak(rune) && rune.Value is not (0x85 or 0x2028 or 0x2029)))
                throw new ArgumentException("The anchor name contains a character that is not allowed: a space, a flow indicator or a control character.", parameterName);

            i += length - 1;
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // Output
    // ------------------------------------------------------------------------------------------------------------

    private static Span<byte> ToUtf8(ReadOnlySpan<char> text, Span<byte> stackBuffer, out byte[]? rented, string parameterName)
    {
        rented = null;
        var maxLength = checked(text.Length * 3);
        var destination = maxLength <= stackBuffer.Length ? stackBuffer : (Span<byte>)(rented = ArrayPool<byte>.Shared.Rent(maxLength));
        if (Utf8.FromUtf16(text, destination, out _, out var written, replaceInvalidSequences: false) is not OperationStatus.Done)
        {
            Return(rented);
            rented = null;
            throw new ArgumentException("The text contains an invalid UTF-16 sequence.", parameterName);
        }

        return destination[..written];
    }

    private static void Return(byte[]? rented)
    {
        if (rented is not null)
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static int FormatFloatingPoint<T>(T value, Span<byte> destination)
        where T : IUtf8SpanFormattable
    {
        _ = value.TryFormat(destination, out var length, format: default, CultureInfo.InvariantCulture);

        // "5" reads back as an integer: the core schema needs a '.' or an exponent for a float
        if (destination[..length].IndexOfAny((byte)'.', (byte)'E', (byte)'e') < 0)
        {
            ".0"u8.CopyTo(destination[length..]);
            length += 2;
        }

        return length;
    }

    private static int FormatSpecialFloatingPoint(double value, Span<byte> destination)
    {
        var text = double.IsNaN(value) ? ".nan"u8 : value > 0 ? ".inf"u8 : "-.inf"u8;
        text.CopyTo(destination);
        return text.Length;
    }

    private void EnsureNotDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private void EnsureKeyPosition()
    {
        EnsureNotDisposed();
        if (!IsKeyPosition)
            throw new InvalidOperationException("A property name can only be written as the key of a mapping entry.");
    }

    private void EnsureNoProperties()
    {
        if (_propertiesLength > 0)
            throw new InvalidOperationException("An anchor or a tag is pending: it must be followed by a node.");
    }

    private void EnsureNewLine()
    {
        if (!_atLineStart)
        {
            WriteNewLine();
        }
    }

    private void WriteNewLine()
    {
        GetSpan(1)[0] = (byte)'\n';
        _pending++;
        _atLineStart = true;
    }

    private void WriteIndent(int count)
    {
        if (count == 0)
            return;

        GetSpan(count)[..count].Fill((byte)' ');
        Advance(count);
    }

    private void WriteByte(byte value)
    {
        GetSpan(1)[0] = value;
        Advance(1);
    }

    private void WriteBytes(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty)
            return;

        value.CopyTo(GetSpan(value.Length));
        Advance(value.Length);
    }

    private void Advance(int count)
    {
        _pending += count;
        _atLineStart = false;
    }

    private Span<byte> GetSpan(int sizeHint)
    {
        if (_buffer.Length - _pending < sizeHint)
        {
            if (_pending >= FlushThreshold)
            {
                FlushCore();
            }

            if (_buffer.Length - _pending < sizeHint)
            {
                var larger = ArrayPool<byte>.Shared.Rent(Math.Max(_buffer.Length * 2, _pending + sizeHint));
                _buffer.AsSpan(0, _pending).CopyTo(larger);
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = larger;
            }
        }

        return _buffer.AsSpan(_pending);
    }

    private void FlushCore()
    {
        if (_pending == 0)
            return;

        if (_stream is not null)
        {
            _stream.Write(_buffer, 0, _pending);
        }
        else
        {
            _bufferWriter!.Write(_buffer.AsSpan(0, _pending));
        }

        _committed += _pending;
        _pending = 0;
    }

    private enum DocumentState
    {
        Start,
        AfterDirectives,
        AfterStartMarker,
        AfterRoot,
        AfterEndMarker,
    }

    private enum FrameKind
    {
        BlockMapping,
        BlockSequence,
        FlowMapping,
        FlowSequence,
    }

    [StructLayout(LayoutKind.Auto)]
    private struct Frame
    {
        public FrameKind Kind;

        // The column of the entries of a block collection
        public int Indent;

        // The number of nodes written: the items of a sequence, the keys and values of a mapping
        public int Count;

        // The first entry of a block collection follows the indicator of its parent on the same line
        public bool FirstEntryInline;

        // An empty block collection is written "{}" or "[]" after a space
        public bool EmptyNeedsSpace;

        // The current key of a mapping is an explicit key ("? key"), or an alias that needs a space before ':'
        public bool ExplicitKey;

        public bool KeyNeedsSpace;

        // A comment is written inside the collection: an empty one is written on the next line
        public bool HasComment;
    }

    [StructLayout(LayoutKind.Auto)]
    private struct Placement
    {
        // The content follows an indicator or properties on the same line
        public bool NeedsSpace;

        // A block collection starts its first entry on the current line
        public bool Compact;

        // The column of the entries of a block collection
        public int BlockIndent;

        // The column of the lines of a literal scalar
        public int LiteralIndent;
    }
}
