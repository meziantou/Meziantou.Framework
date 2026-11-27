using System.Text;
using Meziantou.Framework.Yaml.Events;
using Meziantou.Framework.Yaml.Serialization.References;

namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>
/// Reads YAML tokens for use by <see cref="YamlConverter"/> implementations.
/// </summary>
/// <remarks>
/// This API is intentionally similar in spirit to <c>System.Text.Json</c>'s reader,
/// but it models YAML constructs (mappings, sequences, scalars).
/// </remarks>
public sealed class YamlReader : YamlReaderWriterBase
{
    // A set that grew this large is not kept: clearing it costs as much as its capacity, and most mappings are small.
    private const int MaxPooledKeySetCount = 64;

    private readonly YamlReaderState _state;
    private Stack<HashSet<string>>? _keySets;

    internal YamlReader(YamlReaderState state, YamlSerializerOptions options)
        : base(options)
    {
        _state = state;
    }

    /// <summary>Creates a YAML reader over a string payload.</summary>
    /// <param name="yaml">The YAML payload.</param>
    /// <param name="options">Options used for parsing behaviors such as reference handling.</param>
    /// <exception cref="ArgumentNullException"><paramref name="yaml"/> is <see langword="null"/>.</exception>
    public static YamlReader Create(string yaml, YamlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        var effectiveOptions = options ?? YamlSerializerOptions.Default;
        return Create(new Utf8YamlTokenSource(yaml, effectiveOptions.EffectiveMaxDepth, effectiveOptions.SourceName), effectiveOptions);
    }

    /// <summary>Creates a YAML reader over a text reader.</summary>
    /// <param name="reader">The YAML payload reader.</param>
    /// <param name="options">Options used for parsing behaviors such as reference handling.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    public static YamlReader Create(TextReader reader, YamlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var effectiveOptions = options ?? YamlSerializerOptions.Default;
        return Create(new Utf8YamlTokenSource(reader, effectiveOptions.EffectiveMaxDepth, effectiveOptions.SourceName), effectiveOptions);
    }

    /// <summary>Creates a YAML reader over a token source, with its own reference anchor state.</summary>
    internal static YamlReader Create(Utf8YamlTokenSource source, YamlSerializerOptions options)
    {
        var referenceReader = options.ReferenceHandling != YamlReferenceHandling.None ? new YamlReferenceReader(options.EffectiveMaxAliasExpansionNodeCount) : null;
        return new YamlReader(new YamlReaderState(source, referenceReader, options.SourceName, options.AllowAnchors, options.AllowAliases, IsJsonSchemaStrict(options)), options);
    }

    /// <summary>Creates a YAML reader over a string payload that shares the current reader's reference anchor state.</summary>
    /// <param name="yaml">The YAML payload.</param>
    /// <returns>A new reader for <paramref name="yaml"/>.</returns>
    /// <remarks>
    /// This is intended for advanced scenarios such as polymorphic deserialization that buffers a node and needs to
    /// re-parse it while preserving anchors and aliases.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="yaml"/> is <see langword="null"/>.</exception>
    public YamlReader CreateReader(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        return Create(yaml, _state.ReferenceReader, _state.SourceName, Options);
    }

    /// <remarks>
    /// The JSON schema only resolves null, booleans, and numbers from plain scalars; any other plain scalar is an
    /// error (YAML 1.2 §10.2.2), so a string must be quoted.
    /// </remarks>
    private static bool IsJsonSchemaStrict(YamlSerializerOptions options) => options.UseSchema && options.Schema is YamlSchemaKind.Json;

    internal static YamlReader Create(string yaml, YamlReferenceReader? referenceReader, string? sourceName, YamlSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(options);
        var parser = new Utf8YamlTokenSource(yaml, options.EffectiveMaxDepth, sourceName);
        return new YamlReader(new YamlReaderState(parser, referenceReader, sourceName, options.AllowAnchors, options.AllowAliases, IsJsonSchemaStrict(options)), options);
    }

    /// <summary>
    /// Gets an empty set of mapping keys that compares them with <see cref="YamlReaderWriterBase.PropertyNameComparer"/>.
    /// Give it back with <see cref="ReturnPropertyNameKeySet"/> once the mapping is read, so the next mapping reuses it.
    /// </summary>
    internal HashSet<string> RentPropertyNameKeySet()
    {
        return _keySets is not null && _keySets.TryPop(out var set) ? set : new HashSet<string>(PropertyNameComparer);
    }

    /// <summary>Gets a set from <see cref="RentPropertyNameKeySet"/> that contains the keys of <paramref name="keys"/>.</summary>
    internal HashSet<string> CopyPropertyNameKeySet(HashSet<string> keys)
    {
        var set = RentPropertyNameKeySet();
        set.UnionWith(keys);
        return set;
    }

    /// <summary>
    /// Gets an empty set of mapping keys that compares them with <paramref name="comparer"/>, to find the duplicate keys
    /// of a mapping. Give it back with <see cref="ReturnKeySet"/> once the mapping is read, so the next mapping reuses it.
    /// </summary>
    /// <param name="comparer">The comparer of the keys.</param>
    /// <returns>
    /// An empty set. It comes from a pool of the reader when <paramref name="comparer"/> is the comparer of the property
    /// names: <see cref="StringComparer.OrdinalIgnoreCase"/> when <see cref="YamlSerializerOptions.PropertyNameCaseInsensitive"/>
    /// is set, and <see cref="StringComparer.Ordinal"/> otherwise.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="comparer"/> is <see langword="null"/>.</exception>
    public HashSet<string> RentKeySet(IEqualityComparer<string> comparer)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        return Equals(comparer, PropertyNameComparer) ? RentPropertyNameKeySet() : new HashSet<string>(comparer);
    }

    /// <summary>Gives back a set obtained from <see cref="RentKeySet(IEqualityComparer{string})"/>. The set must not be used afterward.</summary>
    /// <param name="set">The set, or <see langword="null"/> to do nothing.</param>
    public void ReturnKeySet(HashSet<string>? set)
    {
        if (set is not null && Equals(set.Comparer, PropertyNameComparer))
        {
            ReturnPropertyNameKeySet(set);
        }
    }

    /// <summary>Gives back a set obtained from <see cref="RentPropertyNameKeySet"/>. The set must not be used afterward.</summary>
    internal void ReturnPropertyNameKeySet(HashSet<string>? set)
    {
        if (set is null || set.Count > MaxPooledKeySetCount)
            return;

        set.Clear();
        _keySets ??= new Stack<HashSet<string>>();
        _keySets.Push(set);
    }

    /// <summary>Gets the current token type.</summary>
    public YamlTokenType TokenType => _state.TokenType;

    /// <summary>
    /// Gets the current scalar value when <see cref="TokenType"/> is <see cref="YamlTokenType.Scalar"/>.
    /// </summary>
    public string? ScalarValue => _state.ScalarValue;

    /// <summary>
    /// Gets the current scalar style when <see cref="TokenType"/> is <see cref="YamlTokenType.Scalar"/>.
    /// </summary>
    public ScalarStyle ScalarStyle => _state.ScalarStyle;

    /// <summary>Gets the current YAML tag (when present) for the current token.</summary>
    public string? Tag => _state.Tag;

    /// <summary>Gets the current YAML anchor (when present) for the current token.</summary>
    public string? Anchor => _state.Anchor;

    /// <summary>
    /// Gets the current YAML alias when <see cref="TokenType"/> is <see cref="YamlTokenType.Alias"/>.
    /// </summary>
    public string? Alias => _state.Alias;

    /// <summary>Gets the optional source name associated with the YAML payload (for example, a file path).</summary>
    public string? SourceName => _state.SourceName;

    /// <summary>Gets the start location of the current token.</summary>
    public Mark Start => _state.Start;

    /// <summary>Gets the end location of the current token.</summary>
    public Mark End => _state.End;

    /// <summary>
    /// Gets the location of the current token. Unlike <see cref="Start"/> and <see cref="End"/>, its line and column are
    /// computed only when they are asked for, which makes it cheap to keep for an error that may never be reported.
    /// </summary>
    public YamlLocation CurrentLocation => _state.CurrentLocation;

    /// <summary>Gets the UTF-8 bytes of the current scalar, when they are its value, without decoding it.</summary>
    internal bool TryGetRawUtf8Scalar(out ReadOnlySpan<byte> value) => _state.TryGetRawUtf8Scalar(out value);

    internal ParsingEvent? CurrentEvent => _state.CurrentEvent;

    internal YamlReferenceReader? ReferenceReader => _state.ReferenceReader;

    /// <summary>
    /// Gets a value indicating whether the derived type of the current node was already selected by its type
    /// discriminator or tag.
    /// </summary>
    /// <remarks>
    /// A polymorphic type reading a node flagged by <see cref="MarkCurrentNodeDerivedTypeResolved"/> is the derived type
    /// the node explicitly named. When the node has no type discriminator or tag of its own, the polymorphic type reads
    /// the node as itself instead of selecting its default derived type or running a type classifier.
    /// </remarks>
    public bool IsCurrentNodeDerivedTypeResolved => _state.CurrentEvent is not null && ReferenceEquals(_state.CurrentEvent, _state.DerivedTypeResolvedEvent);

    /// <summary>
    /// Records that the derived type of the current node was selected by its type discriminator or tag, which
    /// polymorphic deserialization consumed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Polymorphic deserialization removes the discriminator from the node before reading it as the selected derived
    /// type. When that type is polymorphic itself, the node no longer tells it which type was selected, so without
    /// this flag it would select its own default derived type instead of itself.
    /// </para>
    /// <para>
    /// The flag only applies to the current node, not to the nodes it contains. It is intended for generated
    /// serializers and custom polymorphic converters. See <see cref="IsCurrentNodeDerivedTypeResolved"/>.
    /// </para>
    /// </remarks>
    public void MarkCurrentNodeDerivedTypeResolved()
    {
        _state.DerivedTypeResolvedEvent = _state.CurrentEvent;
    }

    /// <summary>Attempts to resolve the current alias token into an anchored object value.</summary>
    /// <param name="value">When successful, receives the resolved anchored object.</param>
    /// <returns><see langword="true"/> when the current token is an alias and reference handling is enabled; otherwise <see langword="false"/>.</returns>
    public bool TryReadAlias(out object? value)
    {
        if (TokenType == YamlTokenType.Alias && _state.ReferenceReader is not null)
        {
            var alias = Alias;
            if (alias is null)
            {
                throw new YamlException(SourceName, Start, End, "Alias token did not provide an alias value.");
            }

            try
            {
                value = _state.ReferenceReader.Resolve(alias);
            }
            catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException)
            {
                throw new YamlException(SourceName, Start, End, exception.Message, exception);
            }

            Read();
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>Replays the mapping node the current alias refers to, so that it can be read again as a new node.</summary>
    /// <returns>
    /// <see langword="true"/> when the current token is an alias and reference handling is enabled: the reader is then
    /// positioned on the start of a copy of the anchored mapping node. <see langword="false"/> otherwise, and the reader
    /// does not move.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is how a merge key (<c>&lt;&lt;: *defaults</c>) reads its alias: the merge copies the entries of the anchored
    /// mapping node, as they appear in the document, and not the members of the value that node was deserialized into.
    /// </para>
    /// <para>
    /// The copy is read like the original node, except that the anchors it contains are not defined again. An alias it
    /// contains still refers to the value its anchor was deserialized into. The replayed nodes count towards
    /// <see cref="YamlSerializerOptions.MaxAliasExpansionNodeCount"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="YamlException">
    /// The alias is unknown, does not refer to a mapping, refers to a mapping that contains it, or exceeds the alias
    /// expansion limit.
    /// </exception>
    public bool TryReplayMappingAlias()
    {
        if (TokenType != YamlTokenType.Alias || _state.ReferenceReader is null)
        {
            return false;
        }

        var alias = Alias ?? throw new YamlException(SourceName, Start, End, "Alias token did not provide an alias value.");
        IReadOnlyList<ParsingEvent> events;
        try
        {
            events = _state.ReferenceReader.GetMappingEvents(alias);
        }
        catch (InvalidOperationException exception)
        {
            throw new YamlException(SourceName, Start, End, exception.Message, exception);
        }

        _state.Replay(events);
        return true;
    }

    /// <summary>Registers an anchored value for later alias resolution.</summary>
    /// <param name="anchor">The anchor name, without the <c>&amp;</c> prefix.</param>
    /// <param name="value">The anchored value instance.</param>
    public void RegisterAnchor(string anchor, object value)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(value);

        _state.ReferenceReader?.Register(anchor, value);
    }

    /// <summary>Buffers the current YAML node to a string while optionally extracting a discriminator value from the root mapping.</summary>
    /// <param name="reader">The reader positioned at the start of the node.</param>
    /// <param name="discriminatorPropertyName">The mapping key name to treat as a discriminator.</param>
    /// <param name="discriminatorValue">Receives the discriminator scalar value when present on the root mapping.</param>
    /// <returns>The buffered YAML for the node.</returns>
    /// <remarks>
    /// This method consumes the buffered node from <paramref name="reader"/> and advances it past the node.
    /// It is primarily used by polymorphic deserialization implementations.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> or <paramref name="discriminatorPropertyName"/> is <see langword="null"/>.</exception>
    public static string BufferCurrentNodeToStringAndFindDiscriminator(
        YamlReader reader,
        string discriminatorPropertyName,
        out string? discriminatorValue)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(discriminatorPropertyName);

        var comparer = reader.Options.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        discriminatorValue = null;

        var builder = new StringBuilder();
        var yamlWriter = CreateBufferWriter(builder, reader.Options);

        WriteBufferedNode(reader, yamlWriter, comparer, discriminatorPropertyName, isRootMapping: true, removeDiscriminator: false, removeRootTag: false, ref discriminatorValue);
        return builder.ToString();
    }

    /// <summary>
    /// Buffers the current YAML node to a string, extracting the type discriminator of the root mapping and removing it,
    /// along with the tag of the root mapping when requested, from the buffered YAML.
    /// </summary>
    /// <param name="reader">The reader positioned at the start of the node.</param>
    /// <param name="discriminatorPropertyName">
    /// The mapping key holding the discriminator, or <see langword="null"/> to keep every entry of the root mapping.
    /// </param>
    /// <param name="removeTag">Whether the tag of the root mapping is removed from the buffered YAML.</param>
    /// <param name="discriminatorValue">Receives the discriminator scalar value when present on the root mapping.</param>
    /// <returns>The buffered YAML for the node, without its type discriminator.</returns>
    /// <remarks>
    /// Polymorphic deserialization consumes the type discriminator to select the type to deserialize, so the selected
    /// type must not see it again: it is not a member of that type, and a type that is itself polymorphic would try to
    /// resolve it against its own derived types. Only the first matching entry of the root mapping is removed.
    /// This method consumes the buffered node from <paramref name="reader"/> and advances it past the node.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    public static string BufferCurrentNodeToStringAndRemoveDiscriminator(
        YamlReader reader,
        string? discriminatorPropertyName,
        bool removeTag,
        out string? discriminatorValue)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var comparer = reader.Options.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        discriminatorValue = null;

        var builder = new StringBuilder();
        var yamlWriter = CreateBufferWriter(builder, reader.Options);

        WriteBufferedNode(
            reader,
            yamlWriter,
            comparer,
            discriminatorPropertyName ?? string.Empty,
            isRootMapping: discriminatorPropertyName is not null,
            removeDiscriminator: true,
            removeRootTag: removeTag,
            ref discriminatorValue);
        return builder.ToString();
    }

    /// <summary>Buffers the current YAML node to a string.</summary>
    /// <param name="reader">The reader positioned at the start of the node.</param>
    /// <returns>The buffered YAML for the node.</returns>
    /// <remarks>
    /// This method consumes the buffered node from <paramref name="reader"/> and advances it past the node.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    public static string BufferCurrentNodeToString(YamlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var builder = new StringBuilder();
        var yamlWriter = CreateBufferWriter(builder, reader.Options);

        string? unused = null;
        WriteBufferedNode(reader, yamlWriter, StringComparer.Ordinal, discriminatorPropertyName: string.Empty, isRootMapping: false, removeDiscriminator: false, removeRootTag: false, ref unused);
        return builder.ToString();
    }

    /// <summary>Advances to the next token.</summary>
    public bool Read() => _state.Read();

    /// <summary>Skips the current node and any nested content.</summary>
    public void Skip() => _state.Skip();

    /// <summary>
    /// Ensures the current token is <see cref="YamlTokenType.Scalar"/> and returns its value.
    /// </summary>
    /// <exception cref="InvalidOperationException">The current token is not a scalar.</exception>
    public string GetScalarValue()
    {
        if (TokenType != YamlTokenType.Scalar)
        {
            throw new YamlException(SourceName, Start, End, $"Expected a scalar token but found '{TokenType}'.");
        }

        return ScalarValue ?? string.Empty;
    }

    /// <summary>
    /// Ensures the current token is <see cref="YamlTokenType.Scalar"/> and returns its value, as the string instance of
    /// <paramref name="knownValues"/> that is equal to it when there is one, so that a mapping key that names a member does
    /// not allocate a string.
    /// </summary>
    /// <param name="knownValues">The values to reuse, such as the names of the members of a type.</param>
    /// <exception cref="YamlException">The current token is not a scalar.</exception>
    public string GetScalarValue(ReadOnlySpan<string> knownValues)
    {
        if (_state.TryGetRawUtf8Scalar(out var raw))
        {
            foreach (var knownValue in knownValues)
            {
                if (knownValue.Length == raw.Length && Ascii.Equals(raw, knownValue))
                    return knownValue;
            }
        }

        return GetScalarValue();
    }

    /// <summary>Creates the writer that copies a node, so that every scalar of the copy resolves as it did in the source.</summary>
    /// <remarks>
    /// Scalars keep their style, which is what tells a quoted <c>"42"</c> from the number <c>42</c>. The copy is always
    /// written in the block style, which can keep more scalars plain than the flow style.
    /// </remarks>
    private static YamlWriter CreateBufferWriter(StringBuilder builder, YamlSerializerOptions options)
        => new(builder, options, forceBlockStyle: true);

    private static void WriteBufferedNode(
        YamlReader reader,
        YamlWriter writer,
        StringComparer keyComparer,
        string discriminatorPropertyName,
        bool isRootMapping,
        bool removeDiscriminator,
        bool removeRootTag,
        ref string? discriminatorValue)
    {
        switch (reader.TokenType)
        {
            case YamlTokenType.Scalar:
                if (reader.Anchor is not null)
                {
                    writer.WriteAnchor(reader.Anchor);
                }

                if (reader.Tag is not null)
                {
                    writer.WriteResolvedTag(reader.Tag);
                }

                writer.WriteScalar(reader.ScalarValue ?? string.Empty, reader.ScalarStyle);
                reader.Read();
                return;

            case YamlTokenType.Alias:
                writer.WriteAlias(reader.Alias ?? throw new InvalidOperationException("Alias token did not provide an alias value."));
                reader.Read();
                return;

            case YamlTokenType.StartSequence:
                if (reader.Anchor is not null)
                {
                    writer.WriteAnchor(reader.Anchor);
                }

                if (reader.Tag is not null)
                {
                    writer.WriteResolvedTag(reader.Tag);
                }

                writer.WriteStartSequence();
                reader.Read();
                while (reader.TokenType != YamlTokenType.EndSequence)
                {
                    WriteBufferedNode(reader, writer, keyComparer, discriminatorPropertyName, isRootMapping: false, removeDiscriminator: false, removeRootTag: false, ref discriminatorValue);
                }
                writer.WriteEndSequence();
                reader.Read();
                return;

            case YamlTokenType.StartMapping:
                if (reader.Anchor is not null)
                {
                    writer.WriteAnchor(reader.Anchor);
                }

                if (reader.Tag is not null && !removeRootTag)
                {
                    writer.WriteResolvedTag(reader.Tag);
                }

                writer.WriteStartMapping();
                reader.Read();
                while (reader.TokenType != YamlTokenType.EndMapping)
                {
                    if (reader.TokenType != YamlTokenType.Scalar)
                    {
                        throw YamlThrowHelper.ThrowExpectedScalarKey(reader);
                    }

                    if (reader.Anchor is not null)
                    {
                        writer.WriteAnchor(reader.Anchor);
                    }

                    if (reader.Tag is not null)
                    {
                        writer.WriteResolvedTag(reader.Tag);
                    }

                    var key = reader.ScalarValue ?? string.Empty;
                    var keyStyle = reader.ScalarStyle;
                    reader.Read();

                    if (isRootMapping && discriminatorValue is null && keyComparer.Equals(key, discriminatorPropertyName))
                    {
                        if (reader.TokenType != YamlTokenType.Scalar)
                        {
                            throw YamlThrowHelper.ThrowExpectedDiscriminatorScalar(reader, discriminatorPropertyName);
                        }

                        discriminatorValue = reader.ScalarValue ?? string.Empty;
                        if (removeDiscriminator)
                        {
                            reader.Read();
                            continue;
                        }
                    }

                    writer.WritePropertyName(key, keyStyle);

                    WriteBufferedNode(reader, writer, keyComparer, discriminatorPropertyName, isRootMapping: false, removeDiscriminator: false, removeRootTag: false, ref discriminatorValue);
                }
                writer.WriteEndMapping();
                reader.Read();
                return;

            default:
                throw YamlThrowHelper.ThrowUnexpectedToken(reader);
        }
    }

    internal sealed class YamlReaderState
    {
        private readonly Utf8YamlTokenSource _source;
        private readonly bool _allowAnchors;
        private readonly bool _allowAliases;
        private readonly bool _jsonSchemaStrict;
        private readonly bool _recordMappings;
        private Stack<ReplayFrame>? _replays;

        // The current token comes from the source; its value, its location and its event are created when asked for
        private bool _fromSource;
        private string? _scalarValue;
        private Mark _start = Mark.Empty;
        private Mark _end = Mark.Empty;
        private ParsingEvent? _currentEvent;

        public YamlReaderState(Utf8YamlTokenSource source, YamlReferenceReader? referenceReader, string? sourceName, bool allowAnchors = true, bool allowAliases = true, bool jsonSchemaStrict = false)
        {
            _jsonSchemaStrict = jsonSchemaStrict;
            _source = source;
            TokenType = YamlTokenType.None;
            ReferenceReader = referenceReader;
            SourceName = sourceName;
            _allowAnchors = allowAnchors;
            _allowAliases = allowAliases;
            _recordMappings = referenceReader is not null && allowAliases;
        }

        /// <summary>Reads <paramref name="events"/> before the rest of the input, starting with the next read.</summary>
        public void Replay(IReadOnlyList<ParsingEvent> events)
        {
            _replays ??= new Stack<ReplayFrame>();
            _replays.Push(new ReplayFrame(events));
            Read();
        }

        private bool TryGetReplayedEvent([NotNullWhen(true)] out ParsingEvent? current)
        {
            while (_replays is { Count: > 0 })
            {
                var frame = _replays.Peek();
                if (frame.Index < frame.Events.Count)
                {
                    current = frame.Events[frame.Index++];
                    return true;
                }

                _replays.Pop();
            }

            current = null;
            return false;
        }

        public YamlTokenType TokenType { get; private set; }

        public string? ScalarValue => _fromSource && _scalarValue is null && TokenType is YamlTokenType.Scalar ? _scalarValue = _source.ScalarValue : _scalarValue;

        public ScalarStyle ScalarStyle { get; private set; } = ScalarStyle.Any;

        public string? Tag { get; private set; }

        public string? Anchor { get; private set; }

        public string? Alias { get; private set; }

        public Mark Start => _fromSource ? _source.Start : _start;

        public Mark End => _fromSource ? _source.End : _end;

        public bool TryGetRawUtf8Scalar(out ReadOnlySpan<byte> value)
        {
            if (_fromSource && TokenType is YamlTokenType.Scalar)
                return _source.TryGetRawScalar(out value);

            value = default;
            return false;
        }

        // The line and column of a key are only needed for an error: when the source keeps the whole input, they are
        // computed then, from the offsets of the token
        public YamlLocation CurrentLocation => _fromSource && _source.KeepsWholeInput
            ? new YamlLocation(_source, _source.TokenStartOffset, _source.TokenEndOffset)
            : new YamlLocation(Start, End);

        public ParsingEvent? CurrentEvent => _fromSource && _currentEvent is null && TokenType is not YamlTokenType.None ? _currentEvent = _source.CreateEvent() : _currentEvent;

        public ParsingEvent? DerivedTypeResolvedEvent { get; set; }

        public YamlReferenceReader? ReferenceReader { get; }

        public string? SourceName { get; }

        public bool Read()
        {
            while (true)
            {
                if (TryGetReplayedEvent(out var replayed))
                {
                    if (ReadEvent(replayed))
                        return true;

                    continue;
                }

                if (!_source.Read())
                    break;

                if (_recordMappings)
                {
                    // The recorded events are the ones that a replay reads
                    var current = _source.CreateEvent();
                    ReferenceReader!.Record(current);
                    if (ReadEvent(current, isReplayed: false))
                        return true;

                    continue;
                }

                if (ReadSourceToken())
                    return true;
            }

            _fromSource = false;
            TokenType = YamlTokenType.None;
            _scalarValue = null;
            ScalarStyle = ScalarStyle.Any;
            Tag = null;
            Anchor = null;
            Alias = null;
            _start = Mark.Empty;
            _end = Mark.Empty;
            _currentEvent = null;
            return false;
        }

        // Reads the current token of the source without creating its event
        private bool ReadSourceToken()
        {
            var kind = _source.Kind;
            if (kind is Utf8YamlTokenKind.DocumentStart or Utf8YamlTokenKind.DocumentEnd)
                return false;

            _fromSource = true;
            _currentEvent = null;
            _scalarValue = null;
            ScalarStyle = ScalarStyle.Any;
            Tag = null;
            Anchor = null;
            Alias = null;
            switch (kind)
            {
                case Utf8YamlTokenKind.MappingStart:
                    TokenType = YamlTokenType.StartMapping;
                    Tag = _source.Tag;
                    Anchor = _source.Anchor;
                    ThrowIfAnchorNotAllowed();
                    return true;

                case Utf8YamlTokenKind.MappingEnd:
                    TokenType = YamlTokenType.EndMapping;
                    return true;

                case Utf8YamlTokenKind.SequenceStart:
                    TokenType = YamlTokenType.StartSequence;
                    Tag = _source.Tag;
                    Anchor = _source.Anchor;
                    ThrowIfAnchorNotAllowed();
                    return true;

                case Utf8YamlTokenKind.SequenceEnd:
                    TokenType = YamlTokenType.EndSequence;
                    return true;

                case Utf8YamlTokenKind.Scalar:
                    TokenType = YamlTokenType.Scalar;
                    ScalarStyle = _source.ScalarStyle;
                    Tag = _source.Tag;
                    Anchor = _source.Anchor;
                    ThrowIfAnchorNotAllowed();
                    ThrowIfInvalidJsonSchemaScalar();
                    if (Anchor is not null)
                    {
                        ReferenceReader?.RegisterScalar(Anchor, (Scalar)CurrentEvent!);
                    }

                    return true;

                default:
                    TokenType = YamlTokenType.Alias;
                    return ReadAlias(_source.Anchor!);
            }
        }

        private bool ReadEvent(ParsingEvent current, bool isReplayed = true)
        {
            // These are stream/document framing tokens that most converters should not see.
            if (current is StreamStart or StreamEnd or DocumentStart or DocumentEnd)
                return false;

            // The anchors of a replayed node are not defined again: they keep naming the original node.
            _fromSource = false;
            _start = current.Start;
            _end = current.End;
            _currentEvent = current;
            _scalarValue = null;
            ScalarStyle = ScalarStyle.Any;
            Tag = null;
            Anchor = null;
            Alias = null;

            switch (current)
            {
                case MappingStart mappingStart:
                    TokenType = YamlTokenType.StartMapping;
                    Tag = mappingStart.Tag;
                    Anchor = isReplayed ? null : mappingStart.Anchor;
                    ThrowIfAnchorNotAllowed();
                    return true;

                case MappingEnd:
                    TokenType = YamlTokenType.EndMapping;
                    return true;

                case SequenceStart sequenceStart:
                    TokenType = YamlTokenType.StartSequence;
                    Tag = sequenceStart.Tag;
                    Anchor = isReplayed ? null : sequenceStart.Anchor;
                    ThrowIfAnchorNotAllowed();
                    return true;

                case SequenceEnd:
                    TokenType = YamlTokenType.EndSequence;
                    return true;

                case Scalar scalar:
                    TokenType = YamlTokenType.Scalar;
                    _scalarValue = scalar.Value;
                    ScalarStyle = scalar.Style;
                    Tag = scalar.Tag;
                    Anchor = isReplayed ? null : scalar.Anchor;
                    ThrowIfAnchorNotAllowed();
                    ThrowIfInvalidJsonSchemaScalar();
                    if (Anchor is not null)
                    {
                        ReferenceReader?.RegisterScalar(Anchor, scalar);
                    }

                    return true;

                case AnchorAlias alias:
                    return ReadAlias(alias.Value);
            }

            // Ignore any other event types (directives, etc.) for now.
            return false;
        }

        private bool ReadAlias(string alias)
        {
            if (!_allowAliases)
                throw new YamlException(SourceName, Start, End, "YAML aliases are not allowed.");

            // An alias to a scalar is presented as that scalar, so every converter reads it as the type
            // it expects instead of the type the anchored occurrence produced.
            if (ReferenceReader is not null && ReferenceReader.TryResolveScalar(alias, out var anchoredScalar))
            {
                if (_fromSource)
                {
                    _start = _source.Start;
                    _end = _source.End;
                    _currentEvent = _source.CreateEvent();
                    _fromSource = false;
                }

                TokenType = YamlTokenType.Scalar;
                _scalarValue = anchoredScalar.Value;
                ScalarStyle = anchoredScalar.Style;
                Tag = anchoredScalar.Tag;
                return true;
            }

            TokenType = YamlTokenType.Alias;
            Alias = alias;
            return true;
        }

        private void ThrowIfAnchorNotAllowed()
        {
            if (Anchor is not null && !_allowAnchors)
            {
                throw new YamlException(SourceName, Start, End, "YAML anchors are not allowed.");
            }
        }

        private void ThrowIfInvalidJsonSchemaScalar()
        {
            if (_jsonSchemaStrict &&
                Tag is null &&
                ScalarStyle is ScalarStyle.Any or ScalarStyle.Plain &&
                !YamlScalar.ResolvesToNonString(ScalarValue.AsSpan(), YamlSchemaKind.Json))
            {
                throw new YamlException(SourceName, Start, End, $"The plain scalar '{ScalarValue}' is not valid under the JSON schema, which only resolves null, booleans, and numbers. Quote it to read it as a string.");
            }
        }

        public void Skip()
        {
            switch (TokenType)
            {
                case YamlTokenType.StartMapping:
                case YamlTokenType.StartSequence:
                    SkipContainer();
                    return;

                case YamlTokenType.Scalar:
                case YamlTokenType.Alias:
                    Read();
                    return;

                default:
                    Read();
                    return;
            }
        }

        private void SkipContainer()
        {
            var depth = 1;

            while (Read())
            {
                if (TokenType is YamlTokenType.StartMapping or YamlTokenType.StartSequence)
                {
                    depth++;
                }
                else if (TokenType is YamlTokenType.EndMapping or YamlTokenType.EndSequence)
                {
                    depth--;
                    if (depth == 0)
                    {
                        break;
                    }
                }
            }

            // Move past the end token.
            if (TokenType is YamlTokenType.EndMapping or YamlTokenType.EndSequence)
            {
                Read();
            }
        }

        private sealed class ReplayFrame(IReadOnlyList<ParsingEvent> events)
        {
            public IReadOnlyList<ParsingEvent> Events { get; } = events;

            public int Index { get; set; }
        }
    }
}
