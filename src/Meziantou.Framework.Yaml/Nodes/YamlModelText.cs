using Meziantou.Framework.Yaml.Events;
using Meziantou.Framework.Yaml.Tokens;
using DocumentStart = Meziantou.Framework.Yaml.Events.DocumentStart;
using StreamStart = Meziantou.Framework.Yaml.Events.StreamStart;

namespace Meziantou.Framework.Yaml.Nodes;

/// <summary>The text of a stream, a document or a node: what <c>WriteTo</c>, <c>ToString</c> and <c>ToObject</c> share.</summary>
internal static class YamlModelText
{
    public static void WriteTo(IYamlModelNode node, IEmitter emitter, bool suppressDocumentTags) => WriteTo(node, emitter, suppressDocumentTags, declaredTags: null);

    /// <param name="node">The stream, the document or the node to write.</param>
    /// <param name="emitter">The emitter that writes the text.</param>
    /// <param name="suppressDocumentTags">Whether the directives of a document are written only when a tag needs them.</param>
    /// <param name="declaredTags">
    /// For a node, the directives of the text that it is written in: the emitter writes the tags with them, but does not
    /// write them. <see langword="null"/> writes the directives that the tags of the node need.
    /// </param>
    internal static void WriteTo(IYamlModelNode node, IEmitter emitter, bool suppressDocumentTags, TagDirectiveCollection? declaredTags)
    {
        ArgumentNullException.ThrowIfNull(emitter);

        // The emitter expects a stream, and a node inside a document. A stream provides both, even when it has no
        // document at all, and a document provides the latter.
        if (node is not YamlStream)
        {
            emitter.Emit(new StreamStart());
            if (node is YamlNode contents)
            {
                emitter.Emit(declaredTags is null
                    ? new DocumentStart(version: null, GetTagDirectives(contents, declared: null, keepDeclared: false), isImplicit: true)
                    : new DocumentStart(version: null, declaredTags, isImplicit: true) { WritesTagDirectives = false });
            }
        }

        var documentIndex = 0;
        foreach (var evnt in new YamlNodeEventEnumerator(node))
        {
            if (evnt is DocumentStart documentStart)
            {
                // The event belongs to the document, so changing its directives would change the model: the emitter
                // gets a copy
                var document = node as YamlStreamDocument ?? ((YamlStream)node)[documentIndex++];
                var tags = GetTagDirectives(document.Contents, documentStart.Tags, keepDeclared: !suppressDocumentTags);
                if (!ReferenceEquals(tags, documentStart.Tags))
                {
                    emitter.Emit(new DocumentStart(documentStart.Version, tags, documentStart.IsImplicit, documentStart.Start, documentStart.End));
                    continue;
                }
            }

            emitter.Emit(evnt);
        }
    }

    /// <summary>
    /// Gets the directives to write before a document. A tag that is neither a local tag nor a URI, such as the
    /// <c>tag.com,2000:app/foo</c> of <c>%TAG !e! tag.com,2000:app/</c>, has no verbatim form: it is written with the
    /// handle of a directive whose prefix starts it. The directive of the document that does is kept, or a new one is added.
    /// </summary>
    /// <param name="contents">The root node of the document.</param>
    /// <param name="declared">The directives of the document.</param>
    /// <param name="keepDeclared">Whether every directive of the document is written, rather than only the ones a tag needs.</param>
    /// <returns><paramref name="declared"/> when <paramref name="keepDeclared"/> is <see langword="true"/> and no directive is added.</returns>
    public static TagDirectiveCollection? GetTagDirectives(YamlNode? contents, TagDirectiveCollection? declared, bool keepDeclared)
    {
        List<TagDirective>? kept = null;
        List<TagDirective>? added = null;
        if (contents is not null)
        {
            var pending = new Stack<YamlNode>();
            pending.Push(contents);
            while (pending.TryPop(out var node))
            {
                if (node.Tag is { } tag && !Emitter.HasVerbatimForm(tag) && FindTagDirective(added, tag) is null)
                {
                    if (FindTagDirective(declared, tag) is { } directive)
                    {
                        if (!keepDeclared && (kept is null || !kept.Contains(directive)))
                        {
                            (kept ??= []).Add(directive);
                        }
                    }
                    else if (CreateTagDirective(tag, declared, added) is { } created)
                    {
                        (added ??= []).Add(created);
                    }
                }

                // The children in reverse order, so that they are visited in the order of the text
                switch (node)
                {
                    case YamlMapping mapping:
                        for (var i = mapping.Count - 1; i >= 0; i--)
                        {
                            var entry = mapping[i];
                            if (entry.Value is not null)
                            {
                                pending.Push(entry.Value);
                            }

                            pending.Push(entry.Key);
                        }

                        break;

                    case YamlSequence sequence:
                        for (var i = sequence.Count - 1; i >= 0; i--)
                        {
                            pending.Push(sequence[i]);
                        }

                        break;
                }
            }
        }

        if (keepDeclared)
            return added is null ? declared : new TagDirectiveCollection([.. declared ?? [], .. added]);

        if (kept is null && added is null)
            return null;

        return new TagDirectiveCollection([.. kept ?? [], .. added ?? []]);
    }

    // The emitter writes a tag with the handle of a directive whose prefix starts it, followed by a non-empty suffix
    private static TagDirective? FindTagDirective(IEnumerable<TagDirective>? directives, string tag)
    {
        if (directives is not null)
        {
            foreach (var directive in directives)
            {
                if (directive.Prefix is not ['!', ..] && tag.Length > directive.Prefix.Length && tag.StartsWith(directive.Prefix, StringComparison.Ordinal))
                    return directive;
            }
        }

        return null;
    }

    // A new directive whose prefix ends at the last ':', '/' or '#' before the end of the tag, so that the tags that
    // share it share the directive. A tag of one character has no prefix and suffix: the emitter throws.
    private static TagDirective? CreateTagDirective(string tag, TagDirectiveCollection? declared, List<TagDirective>? added)
    {
        if (tag.Length < 2)
            return null;

        var separator = tag.AsSpan(0, tag.Length - 1).LastIndexOfAny(":/#");
        var prefix = tag[..(separator >= 0 ? separator + 1 : 1)];
        for (var i = 0; ; i++)
        {
            var handle = i == 0 ? "!t!" : string.Create(CultureInfo.InvariantCulture, $"!t{i}!");
            if (declared?.Contains(handle) is not true && added?.Exists(directive => directive.Handle == handle) is not true)
                return new TagDirective(handle, prefix);
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
