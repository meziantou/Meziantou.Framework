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
        var events = EnumerateEvents().ToList();

        // Emitter will throw an exception if we attempt to use it without
        // starting StremStart and DocumentStart events.
        if (events[0] is not StreamStart)
            events.Insert(0, new StreamStart());

        if (events[1] is not DocumentStart)
            events.Insert(1, new DocumentStart());

        foreach (var evnt in events)
        {
            if (suppressDocumentTags)
            {
                if (evnt is DocumentStart document && document.Tags != null)
                {
                    document.Tags.Clear();
                }
            }

            emitter.Emit(evnt);
        }
    }

    /// <summary>Returns a string representation of the current instance.</summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        WriteTo(new StringWriter(sb), true);
        return sb.ToString().Trim();
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
        return YamlSerializer.Deserialize(ToString(), type, options);
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
