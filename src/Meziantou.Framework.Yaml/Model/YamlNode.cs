using Meziantou.Framework.Yaml.Events;
using DocumentStart = Meziantou.Framework.Yaml.Events.DocumentStart;
using Scalar = Meziantou.Framework.Yaml.Events.Scalar;
using StreamStart = Meziantou.Framework.Yaml.Events.StreamStart;

namespace Meziantou.Framework.Yaml.Model;

/// <summary>Represents the Yaml Node.</summary>
public abstract class YamlNode
{
    /// <summary>Reads the next YAML element from the event stream.</summary>
    protected static YamlElement? ReadElement(EventReader eventReader)
    {
        return ReadElement(eventReader, context: null);
    }

    internal static YamlElement? ReadElement(EventReader eventReader, YamlModelLoadContext? context)
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

            var clone = (YamlElement)anchored.DeepClone();
            clone.Anchor = null;
            return clone;
        }

        return null;
    }

    private static void RegisterAnchor(YamlElement element, YamlModelLoadContext? context, Mark start, Mark end)
    {
        if (context is null)
        {
            return;
        }

        var anchor = element.Anchor;
        if (!string.IsNullOrEmpty(anchor))
        {
            if (!context.AllowAnchors)
            {
                throw new YamlException(start, end, "YAML anchors are not allowed.");
            }

            context.Anchors[anchor] = element;
            context.RegisterNodeCount(element);
        }
    }

    /// <summary>Enumerates parsing events for this YAML node.</summary>
    public IEnumerable<ParsingEvent> EnumerateEvents()
    {
        return new YamlNodeEventEnumerator(this);
    }

    /// <summary>Writes to.</summary>
    public void WriteTo(TextWriter writer, bool suppressDocumentTags = false)
    {
        WriteTo(new Emitter(writer), suppressDocumentTags);
    }

    /// <summary>Writes to.</summary>
    public void WriteTo(IEmitter emitter, bool suppressDocumentTags = false)
    {
        ArgumentNullException.ThrowIfNull(emitter);

        // The emitter expects a stream, and a node inside a document. A stream provides both, even when it has no
        // document at all, and a document provides the latter.
        if (this is not YamlStream)
        {
            emitter.Emit(new StreamStart());
            if (this is not YamlDocument)
            {
                emitter.Emit(new DocumentStart());
            }
        }

        foreach (var evnt in EnumerateEvents())
        {
            if (suppressDocumentTags && evnt is DocumentStart { Tags.Count: > 0 } document)
            {
                // The event belongs to the document, so clearing its directives would remove them from the model.
                emitter.Emit(new DocumentStart(document.Version, tags: null, document.IsImplicit, document.Start, document.End));
                continue;
            }

            emitter.Emit(evnt);
        }
    }

    /// <summary>Returns a string representation of the current instance.</summary>
    public override string ToString()
    {
        return ToYaml().Trim();
    }

    /// <summary>Writes this node as a YAML document.</summary>
    /// <remarks>
    /// Unlike <see cref="ToString"/>, the text is not trimmed: the trailing line breaks of a "keep" block scalar and
    /// the indentation of its content are part of the value.
    /// </remarks>
    private string ToYaml()
    {
        var sb = new StringBuilder();
        WriteTo(new StringWriter(sb, CultureInfo.InvariantCulture), suppressDocumentTags: true);
        return sb.ToString();
    }

    /// <summary>Converts this node to an instance of <typeparamref name="T"/>.</summary>
    public T? ToObject<T>(YamlSerializerOptions? options = null)
    {
        return (T?)ToObject(typeof(T), options);
    }

    /// <summary>Converts this YAML node to an object.</summary>
    public object? ToObject(Type type, YamlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        return YamlSerializer.Deserialize(ToYaml(), type, options);
    }

    /// <summary>Creates a YAML element from an object.</summary>
    public static YamlElement FromObject(object value, YamlSerializerOptions? options = null, Type? expectedType = null)
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
            throw new YamlException("Unable to materialize a YAML element from the serialized object graph.");
        }

        return contents;
    }

    /// <summary>Creates a deep clone of the current value.</summary>
    public abstract YamlNode DeepClone();
}
