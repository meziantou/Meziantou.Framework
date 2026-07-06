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
        return ReadElement(eventReader, anchors: null);
    }

    internal static YamlElement? ReadElement(EventReader eventReader, Dictionary<string, YamlElement>? anchors)
    {
        if (eventReader.Accept<MappingStart>())
        {
            var mapping = YamlMapping.Load(eventReader, anchors);
            RegisterAnchor(mapping, anchors);
            return mapping;
        }

        if (eventReader.Accept<SequenceStart>())
        {
            var sequence = YamlSequence.Load(eventReader, anchors);
            RegisterAnchor(sequence, anchors);
            return sequence;
        }

        if (eventReader.Accept<Scalar>())
        {
            var value = YamlValue.Load(eventReader);
            RegisterAnchor(value, anchors);
            return value;
        }

        if (eventReader.Accept<AnchorAlias>())
        {
            var alias = eventReader.Expect<AnchorAlias>();

            if (anchors == null || !anchors.TryGetValue(alias.Value, out var anchored))
            {
                throw new YamlException(alias.Start, alias.End, FormattableString.Invariant($"Found an alias '*{alias.Value}' referencing an unknown anchor."));
            }

            // The model API does not currently preserve aliases as a distinct node type.
            // We materialize a copy so that writing the model back out does not emit duplicate anchors.
            var clone = (YamlElement)anchored.DeepClone();
            clone.Anchor = null;
            return clone;
        }

        return null;
    }

    private static void RegisterAnchor(YamlElement element, Dictionary<string, YamlElement>? anchors)
    {
        if (anchors == null)
        {
            return;
        }

        var anchor = element.Anchor;
        if (!string.IsNullOrEmpty(anchor))
        {
            anchors[anchor] = element;
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
