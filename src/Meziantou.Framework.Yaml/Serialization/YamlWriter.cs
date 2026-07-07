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
        _referenceWriter = Options.ReferenceHandling == YamlReferenceHandling.Preserve ? new YamlReferenceWriter() : null;
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
        _referenceWriter = Options.ReferenceHandling == YamlReferenceHandling.Preserve ? new YamlReferenceWriter() : null;
        InitializeFormattingState();
    }

    internal YamlReferenceWriter? ReferenceWriter => _referenceWriter;

    internal bool EndsWithNewLine => _hasWrittenChar && _lastWrittenChar == '\n';

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

    /// <summary>
    /// Attempts to preserve object references by writing an alias when <paramref name="value"/> was previously
    /// anchored, or by writing an anchor for the next value when it is seen for the first time.
    /// </summary>
    /// <param name="value">The value to track.</param>
    /// <returns><see langword="true"/> when an alias was written and no further output for this value is required.</returns>
    /// <remarks>
    /// This method is intended for use by generated serializers and custom converters. It is a no-op unless
    /// <see cref="YamlSerializerOptions.ReferenceHandling"/> is <see cref="YamlReferenceHandling.Preserve"/>.
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
        WriteAnchor(anchor);
        return false;
    }

    /// <summary>Writes a YAML tag for the next value.</summary>
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

    /// <summary>Writes a YAML anchor for the next value.</summary>
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
        if (!frame.HasContent)
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
        if (!frame.HasContent)
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
        if (_depth == 0 || _frames[_depth - 1].Kind != ContainerKind.Mapping)
        {
            throw new InvalidOperationException("Property names can only be written inside a mapping.");
        }

        ref var frame = ref _frames[_depth - 1];
        if (!frame.ExpectingKey)
        {
            throw new InvalidOperationException("A property name cannot be written when a value is expected.");
        }

        var startedCompact = EnsureContainerStarted(ref frame);

        if (frame.HasContent)
        {
            WriteNewLine();
        }

        if (!startedCompact)
        {
            WriteIndent();
        }
        WriteScalarCore(name, isKey: true);
        Write(':');

        frame.HasContent = true;
        frame.ExpectingKey = false;
    }

    /// <summary>Writes a scalar value.</summary>
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

        WriteScalarCore(value, isKey: false);
        CompleteValueAfterScalar();
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
    public void WriteScalar(ReadOnlySpan<char> value)
    {
        WriteValuePrefixForScalar();
        WriteNodeProperties(writeLeadingSpace: false, writeTrailingSpace: true);
        WriteScalarCore(value, isKey: false);
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
            WritePlainScalar(".inf");
            return;
        }

        if (double.IsNegativeInfinity(value))
        {
            WritePlainScalar("-.inf");
            return;
        }

        if (double.IsNaN(value))
        {
            WritePlainScalar(".nan");
            return;
        }

        WriteFormattableScalar(value, format: "R", plainSafe: true);
    }

    /// <summary>Writes a single-precision floating-point scalar value.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteScalar(float value)
    {
        if (float.IsPositiveInfinity(value))
        {
            WritePlainScalar(".inf");
            return;
        }

        if (float.IsNegativeInfinity(value))
        {
            WritePlainScalar("-.inf");
            return;
        }

        if (float.IsNaN(value))
        {
            WritePlainScalar(".nan");
            return;
        }

        WriteFormattableScalar(value, format: "R", plainSafe: true);
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
        WritePlainScalar(value.ToString(CultureInfo.InvariantCulture));
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
            WriteIndent();
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
            WriteIndent();
        }
        Write("- ");
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

        PendingStartKind pendingStart;

        if (_depth == 0)
        {
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
                    WriteIndent();
                }
                Write('-');
                if (_pendingAnchor is not null || _pendingTag is not null)
                {
                    WriteNodeProperties(writeLeadingSpace: true, writeTrailingSpace: false);
                }
                parent.HasContent = true;
                pendingStart = ShouldCompactSequenceItem(kind)
                    ? PendingStartKind.SequenceItemCompact
                    : PendingStartKind.SequenceItem;
            }
        }

        if (_depth == _frames.Length)
        {
            Array.Resize(ref _frames, _frames.Length * 2);
        }

        _frames[_depth++] = new ContainerFrame(kind, pendingStart);
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
        if ((pendingStart == PendingStartKind.None || pendingStart == PendingStartKind.Root) && _depth == 0)
        {
            Write(kind == ContainerKind.Mapping ? "{}" : "[]");
            return;
        }

        Write(' ');
        Write(kind == ContainerKind.Mapping ? "{}" : "[]");
    }

    private void WriteIndent()
    {
        if (!Options.WriteIndented)
        {
            return;
        }

        var indentLevel = Math.Max(0, _depth - 1);
        if (indentLevel == 0)
        {
            return;
        }

        var spaces = Options.IndentSize * indentLevel;
        if (_indentBuilder.Length != spaces)
        {
            _indentBuilder.Clear();
            _indentBuilder.Append(' ', spaces);
        }

        Write(_indentBuilder);
    }

    private void WriteNewLine()
    {
        Write('\n');
    }

    private void InitializeFormattingState()
    {
        _blockSequenceMappingStyle = ResolveOptionStyle(Options.BlockSequenceMappingStyle, YamlSequenceItemStyle.Compact);
        _blockSequenceSequenceStyle = ResolveOptionStyle(Options.BlockSequenceSequenceStyle, YamlSequenceItemStyle.Expanded);
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

    private static YamlSequenceItemStyle ResolveOptionStyle(YamlSequenceItemStyle style, YamlSequenceItemStyle fallback)
        => style == YamlSequenceItemStyle.Default ? fallback : style;

    private void WriteFormattableScalar<T>(T value, ReadOnlySpan<char> format, bool plainSafe)
        where T : IFormattable
    {
        if (value is ISpanFormattable spanFormattable)
        {
            Span<char> buffer = stackalloc char[64];
            if (!spanFormattable.TryFormat(buffer, out var written, format, CultureInfo.InvariantCulture))
            {
                throw new InvalidOperationException($"Unable to format scalar value of type '{typeof(T)}'.");
            }

            if (plainSafe)
            {
                WritePlainScalar(buffer[..written]);
                return;
            }

            WriteScalar(buffer[..written]);
            return;
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

    private void WriteScalarCore(ReadOnlySpan<char> value, bool isKey)
    {
        if (value.Length == 0)
        {
            Write("''");
            return;
        }

        if (IsPlainSafe(value, isKey))
        {
            Write(value);
            return;
        }

        Write('"');
        WriteEscaped(value);
        Write('"');
    }

    private void WriteStringCore(ReadOnlySpan<char> value, bool isKey)
    {
        if (value.Length == 0)
        {
            Write("''");
            return;
        }

        if (ShouldQuoteAmbiguousScalar(value))
        {
            Write('"');
            WriteEscaped(value);
            Write('"');
            return;
        }

        WriteScalarCore(value, isKey);
    }

    private bool ShouldQuoteAmbiguousScalar(ReadOnlySpan<char> value)
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
               YamlScalar.TryParseDouble(value, out _);
    }

    private static bool IsPlainSafe(ReadOnlySpan<char> value, bool isKey)
    {
        // Keep it conservative: if in doubt, quote.
        if (value.Length == 0)
        {
            return false;
        }

        if (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]))
        {
            return false;
        }

        // Disallow YAML special characters and common ambiguities.
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c is '\n' or '\r' or '\t')
            {
                return false;
            }

            // Control characters (including NUL) must be quoted and escaped.
            if (char.IsControl(c))
            {
                return false;
            }
            if (c is ':' or '#' or '{' or '}' or '[' or ']' or ',' or '&' or '*' or '!' or '|' or '>' or '\'' or '"' or '%' or '@' or '`')
            {
                return false;
            }
        }

        if (!isKey && value.Length >= 2 &&
            ((value[0] == '-' && value[1] == ' ') || (value[0] == '?' && value[1] == ' ')))
        {
            return false;
        }

        if (isKey && value.Length == 1 && value[0] == '-')
        {
            return false;
        }

        return true;
    }

    private void WriteEscaped(ReadOnlySpan<char> value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
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
                    if (char.IsControl(c))
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
        WriteValuePrefixForScalar();
        WriteNodeProperties(writeLeadingSpace: false, writeTrailingSpace: true);
        Write(value);
        CompleteValueAfterScalar();
    }

    private void WritePlainScalar(ReadOnlySpan<char> value)
    {
        WriteValuePrefixForScalar();
        WriteNodeProperties(writeLeadingSpace: false, writeTrailingSpace: true);
        Write(value);
        CompleteValueAfterScalar();
    }

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

    private struct ContainerFrame
    {
        public ContainerFrame(ContainerKind kind, PendingStartKind pendingStart)
        {
            Kind = kind;
            HasContent = false;
            ExpectingKey = kind == ContainerKind.Mapping;
            PendingStart = pendingStart;
        }

        public ContainerKind Kind;
        public bool HasContent;
        public bool ExpectingKey;
        public PendingStartKind PendingStart;
    }
}
