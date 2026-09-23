using System.Buffers;
using Meziantou.Framework.Yaml.Serialization.References;

namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>
/// Writes YAML tokens for use by <see cref="YamlConverter"/> implementations.
/// </summary>
public sealed class YamlWriter : YamlReaderWriterBase
{
    private readonly TextWriter? _writer;
    private readonly StringBuilder? _stringBuilder;
    private readonly YamlReferenceWriter? _referenceWriter;
    private readonly StringBuilder _indentBuilder = new();
    private ContainerFrame[] _frames = new ContainerFrame[8];
    private int _depth;
    private string? _pendingAnchor;
    private string? _pendingTag;
    private bool _hasWrittenChar;
    private char _lastWrittenChar;
    private YamlSequenceItemStyle _blockSequenceMappingStyle;
    private YamlSequenceItemStyle _blockSequenceSequenceStyle;
    private ScalarStyle _stringStyle;
    private bool _suppressNextNewLine;
    private readonly bool _forceBlockStyle;

    /// <summary>
    /// Initializes a new instance of the <see cref="YamlWriter"/> class.
    /// </summary>
    /// <param name="writer">The destination writer.</param>
    /// <param name="options">The serializer options used for formatting.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/>.</exception>
    public YamlWriter(TextWriter writer, YamlSerializerOptions? options = null)
        : base(options ?? YamlSerializerOptions.Default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
        _referenceWriter = Options.ReferenceHandling != YamlReferenceHandling.None ? new YamlReferenceWriter() : null;
        InitializeFormattingState();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="YamlWriter"/> class that writes directly to a <see cref="StringBuilder"/>.
    /// </summary>
    /// <param name="stringBuilder">The destination string builder.</param>
    /// <param name="options">The serializer options used for formatting.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stringBuilder"/> is <see langword="null"/>.</exception>
    public YamlWriter(StringBuilder stringBuilder, YamlSerializerOptions? options = null)
        : base(options ?? YamlSerializerOptions.Default)
    {
        ArgumentNullException.ThrowIfNull(stringBuilder);
        _stringBuilder = stringBuilder;
        _referenceWriter = Options.ReferenceHandling != YamlReferenceHandling.None ? new YamlReferenceWriter() : null;
        InitializeFormattingState();
    }

    internal YamlWriter(TextWriter writer, YamlSerializerOptions options, YamlReferenceWriter referenceWriter)
        : base(options)
    {
        _writer = writer;
        _referenceWriter = referenceWriter;
        InitializeFormattingState();
    }

    /// <summary>Initializes a new instance of the <see cref="YamlWriter"/> class that writes block collections regardless of <see cref="YamlSerializerOptions.WriteIndented"/>.</summary>
    /// <remarks>
    /// The block style lets more scalars be written plain than the flow style does, which is what keeps a copy of a
    /// node resolving the same way as the source it was read from.
    /// </remarks>
    internal YamlWriter(StringBuilder stringBuilder, YamlSerializerOptions options, bool forceBlockStyle)
        : this(stringBuilder, options)
    {
        _forceBlockStyle = forceBlockStyle;
    }

    internal YamlReferenceWriter? ReferenceWriter => _referenceWriter;

    /// <summary>
    /// Gets a value indicating whether this writer only collects the object references of the value being serialized,
    /// and discards what it writes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With <see cref="YamlReferenceHandling.PreserveMinimal"/>, a value is written twice: a first pass finds the
    /// references that are shared or cyclic, and only those get an anchor when the value is actually written by the
    /// second pass. Converters are called in both passes, so a converter with side effects can check this property to
    /// run them once.
    /// </para>
    /// <para>
    /// The <see cref="IYamlOnSerialized.OnSerialized"/> callback is not invoked by the first pass. See
    /// <see cref="ShouldInvokeOnSerializing(object)"/> for the <see cref="IYamlOnSerializing.OnSerializing"/> callback.
    /// </para>
    /// </remarks>
    public bool IsCollectingReferences => _referenceWriter?.IsCollecting is true;

    /// <summary>
    /// Determines whether the <see cref="IYamlOnSerializing.OnSerializing"/> callback of <paramref name="value"/> is
    /// invoked by this writer, so it runs once when the value is written by the two passes of
    /// <see cref="YamlReferenceHandling.PreserveMinimal"/>.
    /// </summary>
    /// <param name="value">The value about to be written.</param>
    /// <returns><see langword="true"/> when the callback must be invoked before writing <paramref name="value"/>; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// <para>
    /// The callback of an object runs in the first pass, before its references are collected, so the second pass
    /// writes the same references. The callback of a value type runs in the second pass: each pass writes its own copy
    /// of the value, so a change made to the copy of the first pass would be lost.
    /// </para>
    /// <para>
    /// This method always returns <see langword="true"/> for other reference handling modes. It is intended for
    /// generated serializers and custom converters that invoke the serialization callbacks themselves. See
    /// <see cref="IsCollectingReferences"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public bool ShouldInvokeOnSerializing(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (_referenceWriter is null)
        {
            return true;
        }

        var isValueType = value.GetType().IsValueType;
        if (_referenceWriter.IsCollecting)
        {
            return !isValueType;
        }

        // An object written by the first pass already had its callback invoked there.
        return !_referenceWriter.IsCollectionComplete || isValueType || !_referenceWriter.WasCollected(value);
    }

    internal bool EndsWithNewLine => _hasWrittenChar && _lastWrittenChar == '\n';

    /// <summary>Gets a value indicating whether the next character is written at the first column of a line.</summary>
    /// <remarks>A document marker (<c>---</c> or <c>...</c>) only ends the current document there.</remarks>
    private bool IsAtLineStart => !_hasWrittenChar || _lastWrittenChar == '\n';

    /// <summary>
    /// Gets a value indicating whether collections are written using the flow style, which keeps the document on a single line.
    /// </summary>
    private bool IsFlow => !_forceBlockStyle && !Options.WriteIndented;

    /// <summary>Temporarily overrides how nested block collections are emitted when they appear as items in block sequences.</summary>
    /// <param name="mappingStyle">The mapping style override, or <see cref="YamlSequenceItemStyle.Default"/> to keep the current mapping style.</param>
    /// <param name="sequenceStyle">The sequence style override, or <see cref="YamlSequenceItemStyle.Default"/> to keep the current sequence style.</param>
    /// <returns>A scope that restores the previous styles when disposed.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mappingStyle"/> or <paramref name="sequenceStyle"/> is not a defined <see cref="YamlSequenceItemStyle"/>.</exception>
    public BlockSequenceItemStyleScope PushBlockSequenceItemStyle(YamlSequenceItemStyle mappingStyle, YamlSequenceItemStyle sequenceStyle)
    {
        YamlSerializerOptions.ValidateSequenceItemStyle(mappingStyle, nameof(mappingStyle));
        YamlSerializerOptions.ValidateSequenceItemStyle(sequenceStyle, nameof(sequenceStyle));

        var scope = new BlockSequenceItemStyleScope(this, _blockSequenceMappingStyle, _blockSequenceSequenceStyle);
        if (mappingStyle != YamlSequenceItemStyle.Default)
        {
            _blockSequenceMappingStyle = mappingStyle;
        }

        if (sequenceStyle != YamlSequenceItemStyle.Default)
        {
            _blockSequenceSequenceStyle = sequenceStyle;
        }

        return scope;
    }

    /// <summary>Temporarily overrides the style used to write string values.</summary>
    /// <param name="style">The style override, or <see cref="ScalarStyle.Any"/> to keep the current style.</param>
    /// <returns>A scope that restores the previous style when disposed.</returns>
    /// <remarks>
    /// The style applies to string values only. It is ignored for a value that cannot be represented in it, and for
    /// mapping keys.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="style"/> is not a defined <see cref="ScalarStyle"/>.</exception>
    public StringStyleScope PushStringStyle(ScalarStyle style)
    {
        YamlSerializerOptions.ValidateScalarStyle(style, nameof(style));

        var scope = new StringStyleScope(this, _stringStyle);
        if (style != ScalarStyle.Any)
        {
            _stringStyle = style;
        }

        return scope;
    }

    /// <summary>
    /// Attempts to preserve object references by writing an alias when <paramref name="value"/> was previously
    /// anchored, or by writing an anchor for the next value when it is seen for the first time.
    /// </summary>
    /// <param name="value">The value to track.</param>
    /// <returns><see langword="true"/> when an alias was written and no further output for this value is required.</returns>
    /// <remarks>
    /// This method is intended for use by generated serializers and custom converters. It is a no-op unless
    /// <see cref="YamlSerializerOptions.ReferenceHandling"/> is <see cref="YamlReferenceHandling.Preserve"/> or
    /// <see cref="YamlReferenceHandling.PreserveMinimal"/>.
    /// </remarks>
    public bool TryWriteReference(object? value)
    {
        if (_referenceWriter is null || value is null)
        {
            return false;
        }

        // Match the reflection pipeline behavior: do not anchor scalar strings and do not track value types.
        if (value is string || value.GetType().IsValueType)
        {
            return false;
        }

        if (_referenceWriter.TryGetAnchor(value, out var existing))
        {
            WriteAlias(existing);
            return true;
        }

        var anchor = _referenceWriter.GetOrAddAnchor(value);
        if (anchor is not null)
        {
            WriteAnchor(anchor);
        }
        return false;
    }

    /// <summary>Writes a YAML tag for the next node, which is a mapping key when <see cref="WritePropertyName(string)"/> is called next.</summary>
    /// <param name="tag">The YAML tag, such as <c>!dog</c>.</param>
    public void WriteTag(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        if (tag.Length == 0)
        {
            throw new ArgumentException("Tag cannot be empty.", nameof(tag));
        }

        _pendingTag = tag;
    }

    /// <summary>Writes a tag, as the parser resolves it, for the next value.</summary>
    /// <param name="tag">The resolved tag, such as <c>tag:yaml.org,2002:str</c> or <c>!dog</c>.</param>
    /// <remarks>
    /// The parser expands a tag handle into its prefix, so <c>!!str</c> is read as <c>tag:yaml.org,2002:str</c>,
    /// which is not valid YAML syntax on its own. The tag is written back in a form that reads as the same resolved
    /// tag: the <c>!!</c> shorthand for a core tag, the <c>!</c> shorthand for a local tag, and the verbatim
    /// <c>!&lt;...&gt;</c> form for any other tag. Characters a tag cannot contain are written as URI escapes.
    /// </remarks>
    internal void WriteResolvedTag(string tag)
    {
        _pendingTag = FormatResolvedTag(tag);
    }

    private static string FormatResolvedTag(string tag)
    {
        const string CoreTagPrefix = "tag:yaml.org,2002:";

        // The non-specific tag has no suffix to escape.
        if (tag == "!")
        {
            return tag;
        }

        var builder = new StringBuilder(tag.Length + 4);
        if (tag.Length > CoreTagPrefix.Length && tag.StartsWith(CoreTagPrefix, StringComparison.Ordinal))
        {
            builder.Append("!!");
            AppendTagUri(builder, tag.AsSpan(CoreTagPrefix.Length), allowFlowIndicators: false);
        }
        else if (tag.Length > 1 && tag[0] == '!')
        {
            // An escaped '!' cannot end a tag handle, so a local tag such as "!a!b" is not read as the handle "!a!".
            builder.Append('!');
            AppendTagUri(builder, tag.AsSpan(1), allowFlowIndicators: false);
        }
        else
        {
            builder.Append("!<");
            AppendTagUri(builder, tag, allowFlowIndicators: true);
            builder.Append('>');
        }

        return builder.ToString();
    }

    /// <summary>Appends <paramref name="value"/> to <paramref name="builder"/>, escaping each character a tag cannot contain as its UTF-8 octets.</summary>
    /// <param name="builder">The destination builder.</param>
    /// <param name="value">The tag text.</param>
    /// <param name="allowFlowIndicators">
    /// Whether <c>,</c>, <c>!</c>, <c>[</c>, and <c>]</c> are written as is, which a verbatim tag allows and a tag
    /// shorthand does not.
    /// </param>
    private static void AppendTagUri(StringBuilder builder, ReadOnlySpan<char> value, bool allowFlowIndicators)
    {
        Span<byte> utf8 = stackalloc byte[4];
        var index = 0;
        while (index < value.Length)
        {
            var c = value[index];
            if (IsTagUriChar(c, allowFlowIndicators))
            {
                builder.Append(c);
                index++;
                continue;
            }

            if (Rune.DecodeFromUtf16(value[index..], out var rune, out var consumed) != OperationStatus.Done)
            {
                throw new YamlException("A tag contains an unpaired UTF-16 surrogate.");
            }

            var written = rune.EncodeToUtf8(utf8);
            foreach (var octet in utf8[..written])
            {
                builder.Append(CultureInfo.InvariantCulture, $"%{octet:X2}");
            }

            index += consumed;
        }
    }

    private static bool IsTagUriChar(char c, bool allowFlowIndicators)
        => char.IsAsciiLetterOrDigit(c) ||
           c is '_' or '-' or ';' or '/' or '?' or ':' or '@' or '&' or '=' or '+' or '$' or '.' or '~' or '*' or '\'' or '(' or ')' ||
           (allowFlowIndicators && c is ',' or '!' or '[' or ']');

    /// <summary>Writes a YAML anchor for the next node, which is a mapping key when <see cref="WritePropertyName(string)"/> is called next.</summary>
    public void WriteAnchor(string anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        if (anchor.Length == 0)
        {
            throw new ArgumentException("Anchor cannot be empty.", nameof(anchor));
        }

        _pendingAnchor = anchor;
    }

    /// <summary>Writes a YAML alias value (a reference to an anchor).</summary>
    public void WriteAlias(string alias)
    {
        ArgumentNullException.ThrowIfNull(alias);
        if (alias.Length == 0)
        {
            throw new ArgumentException("Alias cannot be empty.", nameof(alias));
        }

        WriteValuePrefixForAlias();
        Write('*');
        Write(alias);
        CompleteValueAfterScalar();
    }

    /// <summary>Writes the start of a mapping.</summary>
    /// <exception cref="YamlException">The configured maximum nesting depth was exceeded.</exception>
    public void WriteStartMapping()
    {
        PushContainer(ContainerKind.Mapping);
    }

    /// <summary>Writes the end of a mapping.</summary>
    public void WriteEndMapping()
    {
        var frame = PopFrame(ContainerKind.Mapping);
        if (IsFlow)
        {
            Write('}');
        }
        else if (!frame.HasContent)
        {
            WriteEmptyContainerInline(ContainerKind.Mapping, frame.PendingStart);
        }

        CompleteValueAfterContainer();
    }

    /// <summary>Writes the start of a sequence.</summary>
    /// <exception cref="YamlException">The configured maximum nesting depth was exceeded.</exception>
    public void WriteStartSequence()
    {
        PushContainer(ContainerKind.Sequence);
    }

    /// <summary>Writes the end of a sequence.</summary>
    public void WriteEndSequence()
    {
        var frame = PopFrame(ContainerKind.Sequence);
        if (IsFlow)
        {
            Write(']');
        }
        else if (!frame.HasContent)
        {
            WriteEmptyContainerInline(ContainerKind.Sequence, frame.PendingStart);
        }

        CompleteValueAfterContainer();
    }

    /// <summary>Writes a mapping key.</summary>
    /// <param name="name">The key name.</param>
    /// <exception cref="InvalidOperationException">The writer is not positioned within a mapping key.</exception>
    public void WritePropertyName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        WritePropertyNameCore(name, ScalarStyle.Any);
    }

    /// <summary>Writes a dictionary key the way the built-in dictionary converters write it.</summary>
    /// <param name="key">The key.</param>
    /// <remarks>
    /// <para>
    /// A string key is converted using <see cref="YamlSerializerOptions.DictionaryKeyPolicy"/> and is quoted when it
    /// would resolve to a null, a boolean, or a number, so it is read back as a string by an untyped reader. An enum
    /// key is written with the names of the enum converter, and the other keys use their invariant representation
    /// (<c>true</c>, <c>1.5</c>, <c>.inf</c>, the round-trip format of dates, ...).
    /// </para>
    /// <para>
    /// This is how the keys of a dictionary whose key type is <see cref="object"/> are written.
    /// </para>
    /// </remarks>
    /// <exception cref="YamlException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The writer is not positioned within a mapping key.</exception>
    public void WriteDictionaryKey(object key)
    {
        Converters.YamlDictionaryConverterHelper.WriteKey(this, key);
    }

    /// <summary>Writes a mapping key that keeps the style it was read with.</summary>
    /// <param name="name">The key name.</param>
    /// <param name="style">The style of the key in the source document.</param>
    /// <remarks>
    /// A plain key is written plain, including the <c>&lt;&lt;</c> merge key, whenever it can be, so that it resolves
    /// the same way when it is read back. Any other key is written quoted: single-quoted when the source used that
    /// style and the key can be represented in it, double-quoted otherwise.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The writer is not positioned within a mapping key.</exception>
    internal void WritePropertyName(string name, ScalarStyle style)
    {
        WritePropertyNameCore(name, style);
    }

    private void WritePropertyNameCore(string name, ScalarStyle style)
    {
        if (_depth == 0 || _frames[_depth - 1].Kind != ContainerKind.Mapping)
        {
            throw new InvalidOperationException("Property names can only be written inside a mapping.");
        }

        ref var frame = ref _frames[_depth - 1];
        if (!frame.ExpectingKey)
        {
            throw new InvalidOperationException("A property name cannot be written when a value is expected.");
        }

        if (IsFlow)
        {
            if (frame.HasContent)
            {
                Write(", ");
            }

            if (RequiresExplicitKey(name, style))
            {
                Write("? ");
            }

            WriteNodeProperties(writeLeadingSpace: false, writeTrailingSpace: true);
            WriteKeyScalar(name, style);
            Write(':');

            frame.HasContent = true;
            frame.ExpectingKey = false;
            return;
        }

        var startedCompact = EnsureContainerStarted(ref frame);

        if (frame.HasContent)
        {
            WriteNewLine();
        }

        if (!startedCompact)
        {
            WriteIndent(frame.Indent);
        }

        var explicitKey = RequiresExplicitKey(name, style);
        if (explicitKey)
        {
            Write("? ");
        }

        // An anchor or a tag written before the name belongs to the key, which is the next node.
        WriteNodeProperties(writeLeadingSpace: false, writeTrailingSpace: true);
        WriteKeyScalar(name, style);
        if (explicitKey)
        {
            WriteNewLine();
            WriteIndent(frame.Indent);
        }

        Write(':');

        frame.HasContent = true;
        frame.ExpectingKey = false;
    }

    /// <summary>Writes a scalar value.</summary>
    /// <remarks>
    /// A non-null value is never written as a null scalar: text such as <c>null</c> or <c>~</c>, which a plain scalar
    /// would resolve to null, is quoted. Use <see cref="WriteNullValue"/> to write a null.
    /// </remarks>
    public void WriteScalar(string? value)
    {
        WriteValuePrefixForScalar();
        WriteNodeProperties(writeLeadingSpace: false, writeTrailingSpace: true);

        if (value is null)
        {
            Write("null");
            CompleteValueAfterScalar();
            return;
        }

        WriteNonNullScalarCore(value);
        CompleteValueAfterScalar();
    }

    /// <summary>Writes a scalar value that keeps the style it was read with.</summary>
    /// <param name="value">The scalar text.</param>
    /// <param name="style">The style of the scalar in the source document.</param>
    /// <remarks>
    /// The value resolves the same way when it is read back: a plain scalar stays plain, so it can still resolve to
    /// a null, a boolean, or a number, and any other scalar stays non-plain, so it is still read as a string. The
    /// requested style is used when it can represent the value in the current context, and the double-quoted style
    /// otherwise.
    /// </remarks>
    internal void WriteScalar(string value, ScalarStyle style)
    {
        WriteValuePrefixForScalar();

        if (value.Length == 0 && style is ScalarStyle.Any or ScalarStyle.Plain)
        {
            WriteEmptyPlainScalar();
            CompleteValueAfterScalar();
            return;
        }

        WriteNodeProperties(writeLeadingSpace: false, writeTrailingSpace: true);
        switch (style)
        {
            case ScalarStyle.Any or ScalarStyle.Plain when IsPlainSafe(value, isKey: false):
                Write(value);
                break;

            case ScalarStyle.SingleQuoted when TryWriteSingleQuotedScalar(value):
                break;

            case ScalarStyle.Literal or ScalarStyle.Folded when TryWriteBlockScalar(value, style):
                break;

            default:
                Write('"');
                WriteEscaped(value);
                Write('"');
                break;
        }

        CompleteValueAfterScalar();
    }

    /// <summary>Writes an empty plain scalar, which is how an omitted value such as <c>key:</c> is read.</summary>
    /// <remarks>
    /// Quoting it would turn a null into an empty string. The node properties alone denote the empty scalar; a
    /// document without them is marked by its start marker, because an empty stream has no document at all.
    /// </remarks>
    private void WriteEmptyPlainScalar()
    {
        if (_pendingAnchor is not null || _pendingTag is not null)
        {
            WriteNodeProperties(writeLeadingSpace: false, writeTrailingSpace: false);
        }
        else if (_depth == 0)
        {
            Write("---");
        }
    }

    /// <summary>Writes a CLR string value, quoting ambiguous YAML scalars when configured.</summary>
    /// <param name="value">The string value to write.</param>
    public void WriteString(string? value)
    {
        WriteValuePrefixForScalar();
        WriteNodeProperties(writeLeadingSpace: false, writeTrailingSpace: true);

        if (value is null)
        {
            Write("null");
            CompleteValueAfterScalar();
            return;
        }

        WriteStringCore(value.AsSpan(), isKey: false);
        CompleteValueAfterScalar();
    }

    /// <summary>Writes a scalar value from a character span.</summary>
    /// <param name="value">The scalar text.</param>
    /// <remarks>Text that a plain scalar would resolve to null, such as <c>null</c> or <c>~</c>, is quoted.</remarks>
    public void WriteScalar(ReadOnlySpan<char> value)
    {
        WriteValuePrefixForScalar();
        WriteNodeProperties(writeLeadingSpace: false, writeTrailingSpace: true);
        WriteNonNullScalarCore(value);
        CompleteValueAfterScalar();
    }

    /// <summary>Writes a boolean scalar value.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteScalar(bool value)
    {
        WritePlainScalar(value ? "true" : "false");
    }

    /// <summary>Writes an 8-bit unsigned integer scalar value.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteScalar(byte value)
    {
        WriteFormattableScalar(value, format: default, plainSafe: true);
    }

    /// <summary>Writes an 8-bit signed integer scalar value.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteScalar(sbyte value)
    {
        WriteFormattableScalar(value, format: default, plainSafe: true);
    }

    /// <summary>Writes a 16-bit signed integer scalar value.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteScalar(short value)
    {
        WriteFormattableScalar(value, format: default, plainSafe: true);
    }

    /// <summary>Writes a 16-bit unsigned integer scalar value.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteScalar(ushort value)
    {
        WriteFormattableScalar(value, format: default, plainSafe: true);
    }

    /// <summary>Writes a 32-bit signed integer scalar value.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteScalar(int value)
    {
        WriteFormattableScalar(value, format: default, plainSafe: true);
    }

    /// <summary>Writes a 32-bit unsigned integer scalar value.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteScalar(uint value)
    {
        WriteFormattableScalar(value, format: default, plainSafe: true);
    }

    /// <summary>Writes a 64-bit signed integer scalar value.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteScalar(long value)
    {
        WriteFormattableScalar(value, format: default, plainSafe: true);
    }

    /// <summary>Writes a 64-bit unsigned integer scalar value.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteScalar(ulong value)
    {
        WriteFormattableScalar(value, format: default, plainSafe: true);
    }

    /// <summary>Writes a decimal scalar value.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteScalar(decimal value)
    {
        WriteFormattableScalar(value, format: default, plainSafe: true);
    }

    /// <summary>Writes a platform-sized signed integer scalar value.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteScalar(nint value)
    {
        WritePlainScalar(((long)value).ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Writes a platform-sized unsigned integer scalar value.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteScalar(nuint value)
    {
        WritePlainScalar(((ulong)value).ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Writes a character scalar value.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteScalar(char value)
    {
        // A plain '~' is read back as null, which a character cannot be, so WriteScalar quotes it.
        Span<char> span = stackalloc char[1];
        span[0] = value;
        WriteScalar(span);
    }

    /// <summary>Writes a double-precision floating-point scalar value.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteScalar(double value)
    {
        if (double.IsPositiveInfinity(value))
        {
            WriteNonFiniteScalar(".inf");
            return;
        }

        if (double.IsNegativeInfinity(value))
        {
            WriteNonFiniteScalar("-.inf");
            return;
        }

        if (double.IsNaN(value))
        {
            WriteNonFiniteScalar(".nan");
            return;
        }

        if (value == 0 && double.IsNegative(value))
        {
            WritePlainScalar(NegativeZero);
            return;
        }

        Span<char> buffer = stackalloc char[32];
        value.TryFormat(buffer, out var written, "R", CultureInfo.InvariantCulture);
        WriteFloatingPointScalar(buffer, written);
    }

    /// <summary>Writes a single-precision floating-point scalar value.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteScalar(float value)
    {
        if (float.IsPositiveInfinity(value))
        {
            WriteNonFiniteScalar(".inf");
            return;
        }

        if (float.IsNegativeInfinity(value))
        {
            WriteNonFiniteScalar("-.inf");
            return;
        }

        if (float.IsNaN(value))
        {
            WriteNonFiniteScalar(".nan");
            return;
        }

        if (value == 0 && float.IsNegative(value))
        {
            WritePlainScalar(NegativeZero);
            return;
        }

        Span<char> buffer = stackalloc char[32];
        value.TryFormat(buffer, out var written, "R", CultureInfo.InvariantCulture);
        WriteFloatingPointScalar(buffer, written);
    }

    /// <remarks>
    /// The round-trip format writes negative zero as "-0", which the core schema resolves to the integer 0, so the
    /// sign would be lost.
    /// </remarks>
    private const string NegativeZero = "-0.0";

    /// <summary>Writes a formatted binary floating-point value so that it resolves to a float.</summary>
    /// <param name="buffer">A buffer holding the formatted value, with room for two more characters.</param>
    /// <param name="length">The length of the formatted value.</param>
    /// <remarks>
    /// The round-trip format writes a whole number without a fraction, such as "1", which the core schema resolves to
    /// an integer, so ".0" is appended to keep the value a float.
    /// </remarks>
    private void WriteFloatingPointScalar(Span<char> buffer, int length)
    {
        if (buffer[..length].IndexOfAny('.', 'E', 'e') < 0)
        {
            buffer[length++] = '.';
            buffer[length++] = '0';
        }

        WritePlainScalar(buffer[..length]);
    }

    private void WriteNonFiniteScalar(string value)
    {
        if (Options.Schema is YamlSchemaKind.Json && _pendingTag is null)
        {
            WriteTag("!!float");
        }

        WritePlainScalar(value);
    }

    /// <summary>Writes a date and time scalar value using the round-trip format.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteScalar(DateTime value)
    {
        WritePlainScalar(value.ToString("O", CultureInfo.InvariantCulture));
    }

    /// <summary>Writes a date and time with offset scalar value using the round-trip format.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteScalar(DateTimeOffset value)
    {
        if (value.Offset == TimeSpan.Zero)
        {
            WritePlainScalar(value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture));
            return;
        }

        WritePlainScalar(value.ToString("O", CultureInfo.InvariantCulture));
    }

    /// <summary>Writes a GUID scalar value.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteScalar(Guid value)
    {
        WritePlainScalar(value.ToString("D"));
    }

    /// <summary>Writes a time interval scalar value using the constant format.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteScalar(TimeSpan value)
    {
        WritePlainScalar(value.ToString("c", CultureInfo.InvariantCulture));
    }

    /// <summary>Writes a date-only scalar value using the round-trip format.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteScalar(DateOnly value)
    {
        WritePlainScalar(value.ToString("O", CultureInfo.InvariantCulture));
    }

    /// <summary>Writes a time-only scalar value using the round-trip format.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteScalar(TimeOnly value)
    {
        WritePlainScalar(value.ToString("O", CultureInfo.InvariantCulture));
    }

    /// <summary>Writes a half-precision floating-point scalar value.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteScalar(Half value)
    {
        if (!Half.IsFinite(value) || (value == Half.Zero && Half.IsNegative(value)))
        {
            WriteScalar((double)value);
            return;
        }

        Span<char> buffer = stackalloc char[32];
        value.TryFormat(buffer, out var written, format: default, CultureInfo.InvariantCulture);
        WriteFloatingPointScalar(buffer, written);
    }

    /// <summary>Writes a 128-bit signed integer scalar value.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteScalar(Int128 value)
    {
        WritePlainScalar(value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Writes a 128-bit unsigned integer scalar value.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteScalar(UInt128 value)
    {
        WritePlainScalar(value.ToString(CultureInfo.InvariantCulture));
    }

#if NET11_0_OR_GREATER
    /// <summary>Writes a bfloat16 floating-point scalar value.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteScalar(BFloat16 value)
    {
        WriteIeee754Scalar(value);
    }

    /// <summary>Writes a 32-bit IEEE 754 decimal floating-point scalar value.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteScalar(Decimal32 value)
    {
        WriteIeee754Scalar(value);
    }

    /// <summary>Writes a 64-bit IEEE 754 decimal floating-point scalar value.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteScalar(Decimal64 value)
    {
        WriteIeee754Scalar(value);
    }

    /// <summary>Writes a 128-bit IEEE 754 decimal floating-point scalar value.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteScalar(Decimal128 value)
    {
        WriteIeee754Scalar(value);
    }

    // The decimal types can format to thousands of characters, so the fixed-size buffer used by
    // WriteFormattableScalar is not usable here.
    private void WriteIeee754Scalar<T>(T value)
        where T : struct, IFloatingPointIeee754<T>
    {
        if (T.IsPositiveInfinity(value))
        {
            WriteNonFiniteScalar(".inf");
            return;
        }

        if (T.IsNegativeInfinity(value))
        {
            WriteNonFiniteScalar("-.inf");
            return;
        }

        if (T.IsNaN(value))
        {
            WriteNonFiniteScalar(".nan");
            return;
        }

        if (T.IsZero(value) && T.IsNegative(value))
        {
            WritePlainScalar(NegativeZero);
            return;
        }

        var text = value.ToString(format: null, CultureInfo.InvariantCulture);

        // A whole binary floating-point number would resolve to an integer. The decimal types keep the exact text
        // of their value, whose scale is significant.
        if (typeof(T) == typeof(BFloat16) && text.AsSpan().IndexOfAny('.', 'E', 'e') < 0)
        {
            text += ".0";
        }

        WritePlainScalar(text);
    }
#endif

    /// <summary>Writes a scalar value for a span-formattable type using invariant culture formatting.</summary>
    /// <typeparam name="T">The value type to write.</typeparam>
    /// <param name="value">The value to write.</param>
    public void WriteScalar<T>(T value)
        where T : IFormattable
    {
        WriteFormattableScalar(value, format: default, plainSafe: false);
    }

    /// <summary>Writes a null scalar.</summary>
    public void WriteNullValue() => WriteScalar(null);

    private void WriteValuePrefixForScalar()
    {
        if (_depth == 0)
        {
            return;
        }

        ref var frame = ref _frames[_depth - 1];
        if (IsFlow)
        {
            WriteFlowValuePrefix(ref frame, "A scalar value cannot be written when a key is expected.");
            return;
        }

        var startedCompact = EnsureContainerStarted(ref frame);

        if (frame.Kind == ContainerKind.Mapping)
        {
            if (frame.ExpectingKey)
            {
                throw new InvalidOperationException("A scalar value cannot be written when a key is expected.");
            }

            Write(' ');
            return;
        }

        if (frame.HasContent)
        {
            WriteNewLine();
        }

        if (!startedCompact)
        {
            WriteIndent(frame.Indent);
        }
        Write("- ");
        frame.HasContent = true;
    }

    private void WriteValuePrefixForAlias()
    {
        if (_depth == 0)
        {
            return;
        }

        ref var frame = ref _frames[_depth - 1];
        if (IsFlow)
        {
            WriteFlowValuePrefix(ref frame, "An alias value cannot be written when a key is expected.");
            return;
        }

        var startedCompact = EnsureContainerStarted(ref frame);

        if (frame.Kind == ContainerKind.Mapping)
        {
            if (frame.ExpectingKey)
            {
                throw new InvalidOperationException("An alias value cannot be written when a key is expected.");
            }

            Write(' ');
            return;
        }

        if (frame.HasContent)
        {
            WriteNewLine();
        }

        if (!startedCompact)
        {
            WriteIndent(frame.Indent);
        }
        Write("- ");
        frame.HasContent = true;
    }

    /// <summary>Writes the separator that precedes a value in a flow collection.</summary>
    /// <param name="frame">The collection the value belongs to.</param>
    /// <param name="keyExpectedError">The error message used when the collection is a mapping that expects a key.</param>
    /// <exception cref="InvalidOperationException">The collection is a mapping and a key is expected.</exception>
    private void WriteFlowValuePrefix(ref ContainerFrame frame, string keyExpectedError)
    {
        if (frame.Kind == ContainerKind.Mapping)
        {
            if (frame.ExpectingKey)
            {
                throw new InvalidOperationException(keyExpectedError);
            }

            Write(' ');
            return;
        }

        if (frame.HasContent)
        {
            Write(", ");
        }

        frame.HasContent = true;
    }

    private void WriteNodeProperties(bool writeLeadingSpace, bool writeTrailingSpace)
    {
        if (_pendingAnchor is null && _pendingTag is null)
        {
            return;
        }

        if (writeLeadingSpace)
        {
            Write(' ');
        }

        var wroteAny = false;
        if (_pendingAnchor is not null)
        {
            Write('&');
            Write(_pendingAnchor);
            wroteAny = true;
        }

        if (_pendingTag is not null)
        {
            if (wroteAny)
            {
                Write(' ');
            }

            Write(_pendingTag);
            wroteAny = true;
        }

        _pendingAnchor = null;
        _pendingTag = null;

        if (writeTrailingSpace && wroteAny)
        {
            Write(' ');
        }
    }

    private bool EnsureContainerStarted(ref ContainerFrame frame)
    {
        if (frame.PendingStart == PendingStartKind.None)
        {
            return false;
        }

        var pendingStart = frame.PendingStart;
        frame.PendingStart = PendingStartKind.None;
        if (pendingStart == PendingStartKind.SequenceItemCompact)
        {
            Write(' ');
            return true;
        }

        WriteNewLine();
        return false;
    }

    private void CompleteValueAfterScalar()
    {
        if (_depth == 0)
        {
            return;
        }

        ref var frame = ref _frames[_depth - 1];
        if (frame.Kind == ContainerKind.Mapping)
        {
            frame.ExpectingKey = true;
        }
    }

    private void CompleteValueAfterContainer()
    {
        if (_depth == 0)
        {
            return;
        }

        ref var frame = ref _frames[_depth - 1];
        if (frame.Kind == ContainerKind.Mapping)
        {
            frame.ExpectingKey = true;
        }
    }

    private void PushContainer(ContainerKind kind)
    {
        if (_depth >= Options.EffectiveMaxDepth)
        {
            throw YamlDepthHelper.CreateMaxDepthExceededException(Options.EffectiveMaxDepth);
        }

        if (IsFlow)
        {
            PushFlowContainer(kind);
            return;
        }

        PendingStartKind pendingStart;
        int indent;

        if (_depth == 0)
        {
            indent = 0;
            if (_pendingAnchor is not null || _pendingTag is not null)
            {
                // Root node properties must be followed by a newline before content.
                WriteNodeProperties(writeLeadingSpace: false, writeTrailingSpace: false);
                pendingStart = PendingStartKind.Root;
            }
            else
            {
                pendingStart = PendingStartKind.None;
            }
        }
        else
        {
            ref var parent = ref _frames[_depth - 1];
            var parentStartedCompact = EnsureContainerStarted(ref parent);

            if (parent.Kind == ContainerKind.Mapping)
            {
                if (parent.ExpectingKey)
                {
                    throw new InvalidOperationException("A container value cannot be written when a key is expected.");
                }

                pendingStart = PendingStartKind.MappingValue;
                indent = parent.Indent + GetMappingValueIndentStep(kind);
                if (_pendingAnchor is not null || _pendingTag is not null)
                {
                    WriteNodeProperties(writeLeadingSpace: true, writeTrailingSpace: false);
                }
            }
            else
            {
                if (parent.HasContent)
                {
                    WriteNewLine();
                }

                if (!parentStartedCompact)
                {
                    WriteIndent(parent.Indent);
                }
                Write('-');
                var hasNodeProperties = _pendingAnchor is not null || _pendingTag is not null;
                if (hasNodeProperties)
                {
                    WriteNodeProperties(writeLeadingSpace: true, writeTrailingSpace: false);
                }
                parent.HasContent = true;

                // A node property between the "-" indicator and the collection rules out the compact form.
                if (!hasNodeProperties && ShouldCompactSequenceItem(kind))
                {
                    pendingStart = PendingStartKind.SequenceItemCompact;

                    // The content starts right after the "- " indicator, so the following lines must align with it.
                    indent = parent.Indent + 2;
                }
                else
                {
                    pendingStart = PendingStartKind.SequenceItem;

                    // The content starts on the next line and must be indented past the "-" indicator.
                    indent = parent.Indent + Math.Max(1, GetIndentStep(kind));
                }
            }
        }

        if (_depth == _frames.Length)
        {
            Array.Resize(ref _frames, _frames.Length * 2);
        }

        _frames[_depth++] = new ContainerFrame(kind, pendingStart, indent);
    }

    /// <summary>Opens a collection written using the flow style.</summary>
    /// <param name="kind">The kind of collection to open.</param>
    private void PushFlowContainer(ContainerKind kind)
    {
        if (_depth > 0)
        {
            ref var parent = ref _frames[_depth - 1];
            WriteFlowValuePrefix(ref parent, "A container value cannot be written when a key is expected.");
        }

        WriteNodeProperties(writeLeadingSpace: false, writeTrailingSpace: true);
        Write(kind == ContainerKind.Mapping ? '{' : '[');

        if (_depth == _frames.Length)
        {
            Array.Resize(ref _frames, _frames.Length * 2);
        }

        _frames[_depth++] = new ContainerFrame(kind, PendingStartKind.None, indent: 0);
    }

    private ContainerFrame PopFrame(ContainerKind expectedKind)
    {
        if (_depth == 0)
        {
            throw new InvalidOperationException("No container is open.");
        }

        var frame = _frames[--_depth];
        if (frame.Kind != expectedKind)
        {
            throw new InvalidOperationException($"Mismatched container end. Expected '{expectedKind}' but was '{frame.Kind}'.");
        }

        return frame;
    }

    private void WriteEmptyContainerInline(ContainerKind kind, PendingStartKind pendingStart)
    {
        // A root collection without node properties starts the document. With properties (PendingStartKind.Root),
        // the "{}" or "[]" must be separated from them, or "&a{}" is read as a malformed anchor.
        if (pendingStart == PendingStartKind.None && _depth == 0)
        {
            Write(kind == ContainerKind.Mapping ? "{}" : "[]");
            return;
        }

        Write(' ');
        Write(kind == ContainerKind.Mapping ? "{}" : "[]");
    }

    private void WriteIndent(int spaces)
    {
        if (spaces == 0)
        {
            return;
        }

        if (_indentBuilder.Length != spaces)
        {
            _indentBuilder.Clear();
            _indentBuilder.Append(' ', spaces);
        }

        Write(_indentBuilder);
    }

    /// <summary>
    /// Gets the number of columns a nested block collection is indented by, relative to the collection that contains it.
    /// </summary>
    /// <remarks>
    /// Block YAML expresses nesting through indentation, so a nested block mapping always needs at least one column.
    /// A block sequence that is the value of a mapping key can be written at the indentation of its parent mapping,
    /// which is the compact form used when <see cref="YamlSerializerOptions.WriteIndented"/> is disabled.
    /// </remarks>
    private int GetIndentStep(ContainerKind kind)
    {
        if (Options.WriteIndented)
        {
            return Options.IndentSize;
        }

        return kind == ContainerKind.Sequence ? 0 : 1;
    }

    /// <summary>
    /// Gets the number of columns a block collection written as a mapping value is indented by, relative to the mapping
    /// that contains it.
    /// </summary>
    /// <remarks>
    /// A block sequence that is the value of a mapping key does not need its own indentation level, so
    /// <see cref="YamlSerializerOptions.IndentBlockSequences"/> can keep its dashes aligned with the parent mapping.
    /// </remarks>
    private int GetMappingValueIndentStep(ContainerKind kind)
    {
        if (kind == ContainerKind.Sequence && !Options.IndentBlockSequences)
        {
            return 0;
        }

        return GetIndentStep(kind);
    }

    private void WriteNewLine()
    {
        // A "keep" block scalar already wrote the line break that separates it from the next node.
        if (_suppressNextNewLine)
        {
            _suppressNextNewLine = false;
            return;
        }

        Write('\n');
    }

    private void InitializeFormattingState()
    {
        _blockSequenceMappingStyle = ResolveOptionStyle(Options.BlockSequenceMappingStyle, YamlSequenceItemStyle.Compact);
        _blockSequenceSequenceStyle = ResolveOptionStyle(Options.BlockSequenceSequenceStyle, YamlSequenceItemStyle.Expanded);
        _stringStyle = Options.ScalarStylePreferences.StringStyle;
    }

    private bool ShouldCompactSequenceItem(ContainerKind kind)
    {
        return kind switch
        {
            ContainerKind.Mapping => _blockSequenceMappingStyle == YamlSequenceItemStyle.Compact,
            ContainerKind.Sequence => _blockSequenceSequenceStyle == YamlSequenceItemStyle.Compact,
            _ => false,
        };
    }

    private void RestoreBlockSequenceItemStyle(YamlSequenceItemStyle mappingStyle, YamlSequenceItemStyle sequenceStyle)
    {
        _blockSequenceMappingStyle = mappingStyle;
        _blockSequenceSequenceStyle = sequenceStyle;
    }

    private void RestoreStringStyle(ScalarStyle style)
    {
        _stringStyle = style;
    }

    private static YamlSequenceItemStyle ResolveOptionStyle(YamlSequenceItemStyle style, YamlSequenceItemStyle fallback)
        => style == YamlSequenceItemStyle.Default ? fallback : style;

    private void WriteFormattableScalar<T>(T value, ReadOnlySpan<char> format, bool plainSafe)
        where T : IFormattable
    {
        if (value is ISpanFormattable spanFormattable)
        {
            // A value that does not fit in the buffer, such as a large BigInteger, is formatted as a string below.
            Span<char> buffer = stackalloc char[64];
            if (spanFormattable.TryFormat(buffer, out var written, format, CultureInfo.InvariantCulture))
            {
                if (plainSafe)
                {
                    WritePlainScalar(buffer[..written]);
                    return;
                }

                WriteScalar(buffer[..written]);
                return;
            }
        }

        var formatString = format.Length == 0
            ? null
            : new string(format);
        var text = value.ToString(formatString, CultureInfo.InvariantCulture);
        if (text is null)
        {
            throw new InvalidOperationException($"Unable to format scalar value of type '{typeof(T)}'.");
        }

        if (plainSafe)
        {
            WritePlainScalar(text);
            return;
        }

        WriteScalar(text);
    }

    private void WriteScalarCore(string value, bool isKey)
    {
        WriteScalarCore(value.AsSpan(), isKey);
    }

    /// <summary>Writes the text of a non-null scalar so that it is not read back as null.</summary>
    /// <remarks>
    /// The empty scalar is already quoted by <see cref="WriteScalarCore(ReadOnlySpan{char}, bool)"/>. The other
    /// spellings of null (<c>null</c>, <c>Null</c>, <c>NULL</c>, <c>~</c>) are quoted here, whatever the schema, so
    /// the text of an enum name, a URI, or a custom converter never turns into a null.
    /// </remarks>
    private void WriteNonNullScalarCore(ReadOnlySpan<char> value)
    {
        if (value.Length != 0 && YamlScalar.IsNull(value))
        {
            Write('"');
            WriteEscaped(value);
            Write('"');
            return;
        }

        WriteScalarCore(value, isKey: false);
    }

    private void WriteScalarCore(ReadOnlySpan<char> value, bool isKey)
    {
        if (value.Length == 0)
        {
            Write("''");
            return;
        }

        if (IsPlainSafe(value, isKey) && IsPlainAllowedBySchema(value))
        {
            Write(value);
            return;
        }

        Write('"');
        WriteEscaped(value);
        Write('"');
    }

    /// <summary>Selects the style a mapping key is written with.</summary>
    /// <param name="value">The key name.</param>
    /// <param name="style">The requested style, or <see cref="ScalarStyle.Any"/> to let the writer choose.</param>
    /// <returns><see cref="ScalarStyle.Plain"/>, <see cref="ScalarStyle.SingleQuoted"/>, or <see cref="ScalarStyle.DoubleQuoted"/>.</returns>
    private ScalarStyle GetKeyStyle(ReadOnlySpan<char> value, ScalarStyle style)
    {
        // An empty key has no plain form that reads back as a key, so it is always written as ''.
        if (value.Length == 0)
        {
            return ScalarStyle.SingleQuoted;
        }

        return style switch
        {
            // A plain "<<" key was read as the merge key, so it is only quoted when the writer picks the style.
            ScalarStyle.Plain when IsPlainSafe(value, isKey: false) && IsPlainAllowedBySchema(value) => ScalarStyle.Plain,
            ScalarStyle.SingleQuoted when CanWriteSingleQuotedScalar(value) => ScalarStyle.SingleQuoted,
            // A plain "null" or "~" key is read back as a null key, which no dictionary accepts.
            ScalarStyle.Any when Options.ScalarStylePreferences.PreferPlainStyle && IsPlainSafe(value, isKey: true) && !YamlScalar.IsNull(value) && IsPlainAllowedBySchema(value) => ScalarStyle.Plain,
            _ => ScalarStyle.DoubleQuoted,
        };
    }

    private void WriteKeyScalar(ReadOnlySpan<char> value, ScalarStyle style)
    {
        switch (GetKeyStyle(value, style))
        {
            case ScalarStyle.Plain:
                Write(value);
                break;

            case ScalarStyle.SingleQuoted:
                WriteSingleQuotedScalar(value);
                break;

            default:
                Write('"');
                WriteEscaped(value);
                Write('"');
                break;
        }
    }

    private void WriteStringCore(ReadOnlySpan<char> value, bool isKey)
    {
        if (value.Length == 0)
        {
            Write("''");
            return;
        }

        if (!isKey && TryWriteStyledString(value))
        {
            return;
        }

        if (!Options.ScalarStylePreferences.PreferPlainStyle || ShouldQuoteAmbiguousScalar(value))
        {
            Write('"');
            WriteEscaped(value);
            Write('"');
            return;
        }

        WriteScalarCore(value, isKey);
    }

    /// <summary>Writes <paramref name="value"/> using the requested string style.</summary>
    /// <param name="value">The string value to write.</param>
    /// <returns>
    /// <see langword="true"/> when the value was written; <see langword="false"/> when the requested style cannot
    /// represent it and the automatic style must be used instead.
    /// </returns>
    private bool TryWriteStyledString(ReadOnlySpan<char> value)
    {
        switch (_stringStyle)
        {
            case ScalarStyle.Literal or ScalarStyle.Folded:
                return TryWriteBlockScalar(value, _stringStyle);

            case ScalarStyle.SingleQuoted:
                return TryWriteSingleQuotedScalar(value);

            case ScalarStyle.DoubleQuoted:
                Write('"');
                WriteEscaped(value);
                Write('"');
                return true;

            // A plain string that reads as a null, a boolean, or a number would not be read back as that string.
            case ScalarStyle.Plain when IsPlainSafe(value, isKey: false) && !ShouldQuoteAmbiguousScalar(value) && IsPlainAllowedBySchema(value):
                Write(value);
                return true;

            default:
                return false;
        }
    }

    private bool TryWriteSingleQuotedScalar(ReadOnlySpan<char> value)
    {
        if (!CanWriteSingleQuotedScalar(value))
        {
            return false;
        }

        WriteSingleQuotedScalar(value);
        return true;
    }

    private static bool CanWriteSingleQuotedScalar(ReadOnlySpan<char> value)
    {
        foreach (var c in value)
        {
            // A line break inside a single-quoted scalar is folded when it is read back, and a control character
            // can only be represented by a double-quoted escape.
            if (IsLineBreak(c) || c == '\uFEFF' || (c != '\t' && !Emitter.IsPrintable(c)))
            {
                return false;
            }
        }

        return true;
    }

    private void WriteSingleQuotedScalar(ReadOnlySpan<char> value)
    {
        Write('\'');
        foreach (var c in value)
        {
            if (c == '\'')
            {
                Write("''");
            }
            else
            {
                Write(c);
            }
        }

        Write('\'');
    }

    /// <summary>Writes <paramref name="value"/> using the literal (<c>|</c>) or folded (<c>&gt;</c>) block style.</summary>
    /// <returns><see langword="true"/> when the value was written as a block scalar.</returns>
    private bool TryWriteBlockScalar(ReadOnlySpan<char> value, ScalarStyle style)
    {
        // A block scalar spans several lines, so it has no meaning inside a flow collection.
        if (IsFlow || !TryAnalyzeBlockScalar(value, style, out var body, out var trailingBreaks, out var needsIndentIndicator))
        {
            return false;
        }

        Write(style == ScalarStyle.Literal ? '|' : '>');

        if (needsIndentIndicator)
        {
            Write((char)('0' + Options.IndentSize));
        }

        if (trailingBreaks == 0)
        {
            Write('-');
        }
        else if (trailingBreaks > 1)
        {
            Write('+');
        }

        var parentIndent = _depth == 0 ? 0 : _frames[_depth - 1].Indent;
        WriteBlockScalarBody(body, style, parentIndent + Options.IndentSize);

        if (trailingBreaks > 1)
        {
            // A "keep" block scalar owns every trailing line break, including the one that separates it from the
            // node that follows, so that separator is written here instead.
            for (var i = 0; i < trailingBreaks; i++)
            {
                Write('\n');
            }

            _suppressNextNewLine = true;
        }

        return true;
    }

    /// <summary>Determines whether <paramref name="value"/> round-trips through a block scalar.</summary>
    /// <param name="value">The string value to write.</param>
    /// <param name="style">The block style to use.</param>
    /// <param name="body">The value without its trailing line breaks.</param>
    /// <param name="trailingBreaks">The number of line breaks at the end of the value, which selects the chomping indicator.</param>
    /// <param name="needsIndentIndicator">Whether the content indentation must be stated explicitly.</param>
    private bool TryAnalyzeBlockScalar(ReadOnlySpan<char> value, ScalarStyle style, out ReadOnlySpan<char> body, out int trailingBreaks, out bool needsIndentIndicator)
    {
        needsIndentIndicator = false;
        trailingBreaks = 0;
        body = value;

        while (body.Length > 0 && body[^1] == '\n')
        {
            body = body[..^1];
            trailingBreaks++;
        }

        // A block scalar needs at least one content line, and a blank at the very end would be read back as
        // trailing whitespace that a reader is free to drop.
        if (body.Length == 0 || body[^1] is ' ' or '\t')
        {
            return false;
        }

        var isLineStart = true;
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (c == '\n')
            {
                if (i > 0 && body[i - 1] is ' ' or '\t')
                {
                    return false;
                }

                isLineStart = true;
                continue;
            }

            // Any other line break is normalized to a line feed when the block scalar is read back, and a
            // control character can only be represented by a double-quoted escape.
            if (IsLineBreak(c) || c == '\uFEFF' || (c != '\t' && !Emitter.IsPrintable(c)))
            {
                return false;
            }

            if (isLineStart)
            {
                // Indentation is written with spaces, and a reader stops at a tab where it expects one.
                if (c == '\t')
                {
                    return false;
                }

                // A folded scalar reads a more-indented line literally and stops folding around it, so a line
                // that starts with a blank would not round-trip.
                if (c == ' ' && style == ScalarStyle.Folded)
                {
                    return false;
                }
            }

            isLineStart = false;
        }

        // The content indentation is detected from the first non-empty line, so a value that starts with a blank
        // or with an empty line has to state it explicitly. YAML writes that indicator as a single digit.
        if (body[0] is ' ' or '\n')
        {
            if (style == ScalarStyle.Folded || Options.IndentSize > 9)
            {
                return false;
            }

            needsIndentIndicator = true;
        }

        return true;
    }

    private void WriteBlockScalarBody(ReadOnlySpan<char> body, ScalarStyle style, int contentIndent)
    {
        var isFirstLine = true;
        var previousLineWasEmpty = false;

        for (var start = 0; ;)
        {
            var index = body[start..].IndexOf('\n');
            var end = index < 0 ? body.Length : start + index;
            var line = body[start..end];

            // Folding turns a single line break into a space, so a line break in the value is written as a blank
            // line unless the previous line was already blank.
            var breaks = !isFirstLine && style == ScalarStyle.Folded && !previousLineWasEmpty ? 2 : 1;
            for (var i = 0; i < breaks; i++)
            {
                Write('\n');
            }

            // An empty line is left empty so the document carries no trailing whitespace.
            if (!line.IsEmpty)
            {
                WriteIndent(contentIndent);
                Write(line);
            }

            if (index < 0)
            {
                return;
            }

            isFirstLine = false;
            previousLineWasEmpty = line.IsEmpty;
            start = end + 1;
        }
    }

    internal bool ShouldQuoteAmbiguousScalar(ReadOnlySpan<char> value)
    {
        if (!Options.ScalarStylePreferences.PreferQuotedForAmbiguousScalars)
        {
            return false;
        }

        if (value.Equals("<<", StringComparison.Ordinal))
        {
            return true;
        }

        return YamlScalar.IsNull(value) ||
               YamlScalar.TryParseBool(value, out _) ||
               YamlScalar.TryParseInt64(value, out _) ||
               YamlScalar.TryParseDouble(value, out _) ||
               YamlScalar.ResolvesToNonString(value, Options.Schema) ||
               IsNamedFloatingPointLiteral(value);
    }

    /// <remarks>
    /// "NaN" and "Infinity" are strings in YAML, but they are quoted anyway, as a reader that parses numbers with .NET
    /// would read them as floating-point values.
    /// </remarks>
    private static bool IsNamedFloatingPointLiteral(ReadOnlySpan<char> value)
    {
        return value.Length > 0 && char.IsAsciiLetter(value[^1]) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    private bool RequiresExplicitKey(ReadOnlySpan<char> value, ScalarStyle style)
    {
        // An implicit key may span at most 1024 characters of YAML, including quotes and escapes.
        switch (GetKeyStyle(value, style))
        {
            case ScalarStyle.Plain:
                return value.Length > 1024;

            case ScalarStyle.SingleQuoted:
                return value.Length + value.Count('\'') + 2 > 1024;
        }

        var length = 2;
        foreach (var c in value)
        {
            length += c switch
            {
                '\\' or '"' or '\n' or '\r' or '\t' => 2,
                _ when (!Emitter.IsPrintable(c) && !char.IsSurrogate(c)) || IsLineBreak(c) || c == '\uFEFF' => 6,
                _ => 1,
            };

            if (length > 1024)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsPlainSafe(ReadOnlySpan<char> value, bool isKey)
    {
        // Keep it conservative: if in doubt, quote.
        if (value.Length == 0)
        {
            return false;
        }

        // A plain "<<" key is the merge key of the YAML merge extension, so an ordinary key with that name is quoted.
        if (isKey && value.Equals("<<", StringComparison.Ordinal))
        {
            return false;
        }

        if (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]))
        {
            return false;
        }

        var isFlow = IsFlow;

        // Disallow YAML special characters and common ambiguities.
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\t' || IsLineBreak(c))
            {
                return false;
            }

            // Non-printable characters and content byte order marks must be quoted and escaped.
            if (!Emitter.IsPrintable(c) || c == '\uFEFF')
            {
                return false;
            }

            // A flow indicator closes the enclosing collection, but it is an ordinary character in a block context.
            if (isFlow && c is '{' or '}' or '[' or ']' or ',')
            {
                return false;
            }

            // '#' only starts a comment when a space comes before it, so "C# 14" needs no quotes.
            if (c is '#' && (i == 0 || char.IsWhiteSpace(value[i - 1])))
            {
                return false;
            }

            // ':' only separates a key from its value when a space (or the end of the scalar) comes after it,
            // so "12:30" needs no quotes. Inside a flow collection the character closing the collection also
            // ends the scalar, which puts the ':' at its end again.
            if (c is ':' && (i == value.Length - 1 || char.IsWhiteSpace(value[i + 1]) || (isFlow && value[i + 1] is '{' or '}' or '[' or ']' or ',')))
            {
                return false;
            }

            // The remaining indicators only carry a meaning as the first character of a scalar.
            if (i == 0 && c is '{' or '}' or '[' or ']' or ',' or '&' or '*' or '!' or '|' or '>' or '\'' or '"' or '%' or '@' or '`')
            {
                return false;
            }

            // Inside a flow collection a '?' ends the plain scalar, wherever it appears: a leading one is the
            // key indicator, and a later one is a scalar terminator.
            if (isFlow && c == '?')
            {
                return false;
            }
        }

        // A leading '-' or '?' followed by separation/end is a collection/key indicator, not a plain scalar.
        if ((value[0] is '-' or '?') && (value.Length == 1 || char.IsWhiteSpace(value[1])))
        {
            return false;
        }

        // At the first column of a line, "---" and "..." end the current document, so a value starting with one
        // of them has to be quoted. Elsewhere they are ordinary characters.
        if (IsAtLineStart && StartsWithDocumentMarker(value))
        {
            return false;
        }

        return true;
    }

    /// <summary>Gets a value indicating whether <paramref name="value"/> starts with a YAML document marker.</summary>
    /// <remarks>
    /// A marker ends at the end of the line or at a separation character, so text such as <c>---hello</c> is an
    /// ordinary plain scalar.
    /// </remarks>
    private static bool StartsWithDocumentMarker(ReadOnlySpan<char> value)
    {
        if (value.Length < 3)
        {
            return false;
        }

        if (!value.StartsWith("---", StringComparison.Ordinal) && !value.StartsWith("...", StringComparison.Ordinal))
        {
            return false;
        }

        return value.Length == 3 || value[3] is ' ' or '\t';
    }

    /// <summary>Gets a value indicating whether <paramref name="c"/> is written as an escape rather than verbatim.</summary>
    /// <remarks>
    /// U+0085, U+2028, and U+2029 are ordinary content in YAML 1.2, but they were line breaks in YAML 1.1 and are
    /// not control characters, so <see cref="Emitter.IsPrintable"/> accepts them. They are escaped rather than
    /// written verbatim so a reader cannot fold them, which also keeps the output free of invisible line breaks.
    /// </remarks>
    private static bool IsLineBreak(char c) => c is '\n' or '\r' or '\u0085' or '\u2028' or '\u2029';

    private void WriteEscaped(ReadOnlySpan<char> value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsSurrogate(c))
            {
                if (!char.IsHighSurrogate(c) || i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                {
                    throw new YamlException("A scalar contains an unpaired UTF-16 surrogate.");
                }

                Write(value.Slice(i, 2));
                i++;
                continue;
            }

            switch (c)
            {
                case '\\':
                    Write("\\\\");
                    break;
                case '"':
                    Write("\\\"");
                    break;
                case '\n':
                    Write("\\n");
                    break;
                case '\r':
                    Write("\\r");
                    break;
                case '\t':
                    Write("\\t");
                    break;
                default:
                    if (!Emitter.IsPrintable(c) || IsLineBreak(c) || c == '\uFEFF')
                    {
                        Write("\\u");
                        Write(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        Write(c);
                    }
                    break;
            }
        }
    }

    private void WritePlainScalar(string value)
    {
        WritePlainScalar(value.AsSpan());
    }

    private void WritePlainScalar(ReadOnlySpan<char> value)
    {
        // An explicit tag decides how the scalar resolves, so only an untagged scalar is checked against the schema.
        var quote = _pendingTag is null && !IsPlainAllowedBySchema(value);
        WriteValuePrefixForScalar();
        WriteNodeProperties(writeLeadingSpace: false, writeTrailingSpace: true);
        if (quote)
        {
            Write('"');
            WriteEscaped(value);
            Write('"');
        }
        else
        {
            Write(value);
        }

        CompleteValueAfterScalar();
    }

    /// <summary>Determines whether the schema in use accepts a plain scalar with this text.</summary>
    /// <remarks>
    /// The JSON schema only resolves null, booleans, and numbers from plain scalars, and any other plain scalar is an
    /// error (YAML 1.2 §10.2.2), so dates, identifiers, and strings are quoted.
    /// </remarks>
    private bool IsPlainAllowedBySchema(ReadOnlySpan<char> value)
        => Options.Schema is not YamlSchemaKind.Json || YamlScalar.ResolvesToNonString(value, YamlSchemaKind.Json);

    private void Write(string value)
    {
        if (_stringBuilder is not null)
        {
            _stringBuilder.Append(value);
            TrackLastChar(value);
            return;
        }

        _writer!.Write(value);
        TrackLastChar(value);
    }

    private void Write(char value)
    {
        if (_stringBuilder is not null)
        {
            _stringBuilder.Append(value);
            TrackLastChar(value);
            return;
        }

        _writer!.Write(value);
        TrackLastChar(value);
    }

    private void Write(ReadOnlySpan<char> value)
    {
        if (_stringBuilder is not null)
        {
            _stringBuilder.Append(value);
            TrackLastChar(value);
            return;
        }

        _writer!.Write(value);
        TrackLastChar(value);
    }

    private void Write(StringBuilder value)
    {
        if (_stringBuilder is not null)
        {
            _stringBuilder.Append(value);
            TrackLastChar(value);
            return;
        }

        _writer!.Write(value);
        TrackLastChar(value);
    }

    private void TrackLastChar(string value)
    {
        if (value.Length == 0)
        {
            return;
        }

        _hasWrittenChar = true;
        _lastWrittenChar = value[value.Length - 1];
    }

    private void TrackLastChar(char value)
    {
        _hasWrittenChar = true;
        _lastWrittenChar = value;
    }

    private void TrackLastChar(ReadOnlySpan<char> value)
    {
        if (value.Length == 0)
        {
            return;
        }

        _hasWrittenChar = true;
        _lastWrittenChar = value[value.Length - 1];
    }

    private void TrackLastChar(StringBuilder value)
    {
        if (value.Length == 0)
        {
            return;
        }

        _hasWrittenChar = true;
        _lastWrittenChar = value[value.Length - 1];
    }

    private enum ContainerKind
    {
        Mapping,
        Sequence,
    }

    private enum PendingStartKind
    {
        None,
        MappingValue,
        SequenceItem,
        SequenceItemCompact,
        Root,
    }

    /// <summary>
    /// Restores the block sequence item styles that were active before a <see cref="PushBlockSequenceItemStyle"/> call.
    /// </summary>
    public readonly struct BlockSequenceItemStyleScope : IDisposable
    {
        private readonly YamlWriter? _writer;
        private readonly YamlSequenceItemStyle _mappingStyle;
        private readonly YamlSequenceItemStyle _sequenceStyle;

        internal BlockSequenceItemStyleScope(YamlWriter writer, YamlSequenceItemStyle mappingStyle, YamlSequenceItemStyle sequenceStyle)
        {
            _writer = writer;
            _mappingStyle = mappingStyle;
            _sequenceStyle = sequenceStyle;
        }

        /// <summary>Restores the previously active block sequence item styles.</summary>
        public void Dispose()
        {
            _writer?.RestoreBlockSequenceItemStyle(_mappingStyle, _sequenceStyle);
        }
    }

    /// <summary>Restores the string style that was active before a <see cref="PushStringStyle"/> call.</summary>
    public readonly struct StringStyleScope : IDisposable
    {
        private readonly YamlWriter? _writer;
        private readonly ScalarStyle _style;

        internal StringStyleScope(YamlWriter writer, ScalarStyle style)
        {
            _writer = writer;
            _style = style;
        }

        /// <summary>Restores the previously active string style.</summary>
        public void Dispose()
        {
            _writer?.RestoreStringStyle(_style);
        }
    }

    private struct ContainerFrame
    {
        public ContainerFrame(ContainerKind kind, PendingStartKind pendingStart, int indent)
        {
            Kind = kind;
            HasContent = false;
            ExpectingKey = kind == ContainerKind.Mapping;
            PendingStart = pendingStart;
            Indent = indent;
        }

        public ContainerKind Kind;
        public bool HasContent;
        public bool ExpectingKey;
        public PendingStartKind PendingStart;

        /// <summary>The number of columns each line of this collection's content is indented by.</summary>
        public int Indent;
    }
}
