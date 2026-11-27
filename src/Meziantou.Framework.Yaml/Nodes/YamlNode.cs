using Meziantou.Framework.Yaml.Events;
using Scalar = Meziantou.Framework.Yaml.Events.Scalar;

namespace Meziantou.Framework.Yaml.Nodes;

/// <summary>
/// A node of the document object model: a <see cref="YamlMapping"/>, a <see cref="YamlSequence"/> or a
/// <see cref="YamlValue"/>, like <c>JsonNode</c> in System.Text.Json.
/// </summary>
/// <remarks>
/// A <see cref="YamlStreamDocument"/> holds the root node of a document, and a <see cref="YamlStream"/> holds documents:
/// they are not nodes. For a read-only document that does not create an object per node, see <see cref="YamlDocument"/>.
/// </remarks>
public abstract class YamlNode : IYamlModelNode
{
    /// <summary>Gets or sets anchor.</summary>
    public abstract string? Anchor { get; set; }

    /// <summary>Gets or sets tag.</summary>
    public abstract string? Tag { get; set; }

    /// <summary>Gets is Canonical.</summary>
    public abstract bool IsCanonical { get; }

    /// <summary>Gets or sets a value indicating whether the node was loaded from a text that keeps its changes, see <see cref="YamlStream.ToStringPreservingFormatting"/>.</summary>
    internal bool IsTracked { get; set; }

    /// <summary>Gets or sets the state of the node before its first change, or <see langword="null"/> while it is unchanged.</summary>
    internal YamlNodeSnapshot? Snapshot { get; set; }

    bool IYamlModelNode.IsTracked { get => IsTracked; set => IsTracked = value; }

    YamlNodeSnapshot? IYamlModelNode.Snapshot { get => Snapshot; set => Snapshot = value; }

    /// <summary>Gets or sets the location of the alias this node was copied from when it was loaded.</summary>
    internal Mark? AliasStart { get; set; }

    internal Mark? AliasEnd { get; set; }

    /// <summary>Gets or sets the anchored node this node was copied from when it was loaded.</summary>
    internal YamlNode? AliasTarget { get; set; }

    /// <summary>Records the state of the node before its first change.</summary>
    internal void OnChanging()
    {
        if (IsTracked && Snapshot is null)
        {
            Snapshot = YamlSourceEditor.CaptureCurrent(this);
        }
    }

    /// <summary>Reads the next YAML node from the event stream.</summary>
    internal static YamlNode? ReadElement(EventReader eventReader)
    {
        return ReadElement(eventReader, context: null);
    }

    internal static YamlNode? ReadElement(EventReader eventReader, YamlModelLoadContext? context)
    {
        if (eventReader.Peek<MappingStart>() is { } mappingStart)
        {
            var mapping = YamlMapping.Load(eventReader, context);
            RegisterAnchor(mapping, context, mappingStart.Start, mappingStart.End);
            return mapping;
        }

        if (eventReader.Peek<SequenceStart>() is { } sequenceStart)
        {
            var sequence = YamlSequence.Load(eventReader, context);
            RegisterAnchor(sequence, context, sequenceStart.Start, sequenceStart.End);
            return sequence;
        }

        if (eventReader.Peek<Scalar>() is { } scalarStart)
        {
            var value = YamlValue.Load(eventReader);
            RegisterAnchor(value, context, scalarStart.Start, scalarStart.End);
            return value;
        }

        if (eventReader.Accept<AnchorAlias>())
        {
            var alias = eventReader.Expect<AnchorAlias>();

            if (context is not null && !context.AllowAliases)
            {
                throw new YamlException(alias.Start, alias.End, "YAML aliases are not allowed.");
            }

            if (context is null || !context.Anchors.TryGetValue(alias.Value, out var anchored))
            {
                throw new YamlException(alias.Start, alias.End, FormattableString.Invariant($"Found an alias '*{alias.Value}' referencing an unknown anchor."));
            }

            // The model API does not currently preserve aliases as a distinct node type, so the anchored subtree is
            // copied. That copy is what makes alias amplification possible, so it is charged against a budget first.
            context.ChargeAliasExpansion(anchored, alias.Value, alias.Start, alias.End);

            var clone = anchored.DeepClone();
            clone.Anchor = null;
            clone.AliasStart = alias.Start;
            clone.AliasEnd = alias.End;
            clone.AliasTarget = anchored;
            if (context.TrackSource)
            {
                YamlSourceEditor.Track(clone);
            }

            return clone;
        }

        return null;
    }

    private static void RegisterAnchor(YamlNode node, YamlModelLoadContext? context, Mark start, Mark end)
    {
        if (context is null)
        {
            return;
        }

        node.IsTracked = context.TrackSource;

        var anchor = node.Anchor;
        if (!string.IsNullOrEmpty(anchor))
        {
            if (!context.AllowAnchors)
            {
                throw new YamlException(start, end, "YAML anchors are not allowed.");
            }

            context.Anchors[anchor] = node;
            context.RegisterNodeCount(node);
        }
    }

    /// <summary>Enumerates parsing events for this YAML node.</summary>
    public IEnumerable<ParsingEvent> EnumerateEvents()
    {
        return new YamlNodeEventEnumerator(this);
    }

    /// <summary>Writes the node as a YAML document.</summary>
    public void WriteTo(TextWriter writer, bool suppressDocumentTags = false)
    {
        WriteTo(new Emitter(writer), suppressDocumentTags);
    }

    /// <summary>Writes the node as a YAML document.</summary>
    public void WriteTo(IEmitter emitter, bool suppressDocumentTags = false)
    {
        YamlModelText.WriteTo(this, emitter, suppressDocumentTags);
    }

    /// <summary>Returns a string representation of the current instance.</summary>
    public override string ToString()
    {
        return YamlModelText.ToYaml(this).Trim();
    }

    /// <summary>Converts this node to an instance of <typeparamref name="T"/>.</summary>
    public T? ToObject<T>(YamlSerializerOptions? options = null)
    {
        return (T?)ToObject(typeof(T), options);
    }

    /// <summary>Converts this YAML node to an object.</summary>
    public object? ToObject(Type type, YamlSerializerOptions? options = null)
    {
        return YamlModelText.ToObject(this, type, options);
    }

    /// <summary>Creates a YAML node from an object.</summary>
    public static YamlNode FromObject(object value, YamlSerializerOptions? options = null, Type? expectedType = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        var effectiveOptions = options ?? YamlSerializerOptions.Default;
        var yaml = expectedType is null
            ? YamlSerializer.Serialize(value, effectiveOptions)
            : YamlSerializer.Serialize(value, expectedType, effectiveOptions);
        var stream = YamlStream.Load(new EventReader(Parser.CreateParser(new StringReader(yaml), effectiveOptions.EffectiveMaxDepth)));
        var contents = stream.Count == 0 ? null : stream[0].Contents;
        if (contents is null)
        {
            throw new YamlException("Unable to materialize a YAML node from the serialized object graph.");
        }

        return contents;
    }

    /// <summary>Creates a deep clone of the current node.</summary>
    public abstract YamlNode DeepClone();
}
