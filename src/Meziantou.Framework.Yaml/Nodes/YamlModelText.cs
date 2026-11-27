using Meziantou.Framework.Yaml.Events;
using DocumentStart = Meziantou.Framework.Yaml.Events.DocumentStart;
using StreamStart = Meziantou.Framework.Yaml.Events.StreamStart;

namespace Meziantou.Framework.Yaml.Nodes;

/// <summary>The text of a stream, a document or a node: what <c>WriteTo</c>, <c>ToString</c> and <c>ToObject</c> share.</summary>
internal static class YamlModelText
{
    public static void WriteTo(IYamlModelNode node, IEmitter emitter, bool suppressDocumentTags)
    {
        ArgumentNullException.ThrowIfNull(emitter);

        // The emitter expects a stream, and a node inside a document. A stream provides both, even when it has no
        // document at all, and a document provides the latter.
        if (node is not YamlStream)
        {
            emitter.Emit(new StreamStart());
            if (node is not YamlStreamDocument)
            {
                emitter.Emit(new DocumentStart());
            }
        }

        foreach (var evnt in new YamlNodeEventEnumerator(node))
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

    /// <summary>Writes the object as a YAML document.</summary>
    /// <remarks>
    /// Unlike <c>ToString</c>, the text is not trimmed: the trailing line breaks of a "keep" block scalar and the
    /// indentation of its content are part of the value.
    /// </remarks>
    public static string ToYaml(IYamlModelNode node)
    {
        var sb = new StringBuilder();
        WriteTo(node, new Emitter(new StringWriter(sb, CultureInfo.InvariantCulture)), suppressDocumentTags: true);
        return sb.ToString();
    }

    public static object? ToObject(IYamlModelNode node, Type type, YamlSerializerOptions? options)
    {
        ArgumentNullException.ThrowIfNull(type);
        return YamlSerializer.Deserialize(ToYaml(node), type, options);
    }
}
