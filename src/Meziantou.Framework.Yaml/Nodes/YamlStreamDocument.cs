using Meziantou.Framework.Yaml.Events;
using DocumentEnd = Meziantou.Framework.Yaml.Events.DocumentEnd;
using DocumentStart = Meziantou.Framework.Yaml.Events.DocumentStart;

namespace Meziantou.Framework.Yaml.Nodes;

/// <summary>A document of a <see cref="YamlStream"/>: its directives and markers, and its root node in <see cref="Contents"/>.</summary>
/// <remarks>For a read-only document that does not create an object per node, see <see cref="YamlDocument"/>.</remarks>
public class YamlStreamDocument : IYamlModelNode
{
    private DocumentStart _documentStart;
    private DocumentEnd _documentEnd;
    private YamlNode? _contents;

    /// <summary>Initializes a new instance of this type.</summary>
    public YamlStreamDocument()
    {
        _documentStart = new DocumentStart(null, new TagDirectiveCollection(), true);
        _documentEnd = new DocumentEnd(true);
    }

    private YamlStreamDocument(DocumentStart documentStart, DocumentEnd documentEnd, YamlNode? contents)
    {
        DocumentStart = documentStart;
        DocumentEnd = documentEnd;
        Contents = contents;
    }

    /// <summary>Loads data.</summary>
    public static YamlStreamDocument Load(EventReader eventReader)
    {
        return Load(eventReader, options: null);
    }

    /// <summary>Loads data, honoring the anchor, alias and alias-expansion limits of <paramref name="options"/>.</summary>
    /// <param name="eventReader">The event reader.</param>
    /// <param name="options">
    /// The options used to bound alias expansion. If <see langword="null"/>, <see cref="YamlSerializerOptions.Default"/> is used.
    /// </param>
    public static YamlStreamDocument Load(EventReader eventReader, YamlSerializerOptions? options)
    {
        ArgumentNullException.ThrowIfNull(eventReader);
        return Load(eventReader, CreateContext(options));
    }

    internal static YamlModelLoadContext CreateContext(YamlSerializerOptions? options)
    {
        var effectiveOptions = options ?? YamlSerializerOptions.Default;
        return new YamlModelLoadContext(effectiveOptions.EffectiveMaxAliasExpansionNodeCount, effectiveOptions.AllowAnchors, effectiveOptions.AllowAliases);
    }

    internal static YamlStreamDocument Load(EventReader eventReader, YamlModelLoadContext context)
    {
        var documentStart = eventReader.Expect<DocumentStart>();

        var contents = YamlNode.ReadElement(eventReader, context);

        var documentEnd = eventReader.Expect<DocumentEnd>();

        return new YamlStreamDocument(documentStart, documentEnd, contents) { IsTracked = context.TrackSource };
    }

    /// <summary>Gets document Start.</summary>
    public DocumentStart DocumentStart
    {
        get => _documentStart;
        [MemberNotNull(nameof(_documentStart))]
        set
        {
            OnChanging();
            _documentStart = value;
        }
    }

    /// <summary>Gets document End.</summary>
    public DocumentEnd DocumentEnd
    {
        get => _documentEnd;
        [MemberNotNull(nameof(_documentEnd))]
        set
        {
            _documentEnd = value;
        }
    }

    /// <summary>Gets contents.</summary>
    public YamlNode? Contents
    {
        get { return _contents; }
        set
        {
            OnChanging();
            _contents = value;
        }
    }

    /// <summary>Gets or sets a value indicating whether the document was loaded from a text that keeps its changes, see <see cref="YamlStream.ToStringPreservingFormatting"/>.</summary>
    internal bool IsTracked { get; set; }

    /// <summary>Gets or sets the state of the document before its first change, or <see langword="null"/> while it is unchanged.</summary>
    internal YamlNodeSnapshot? Snapshot { get; set; }

    bool IYamlModelNode.IsTracked { get => IsTracked; set => IsTracked = value; }

    YamlNodeSnapshot? IYamlModelNode.Snapshot { get => Snapshot; set => Snapshot = value; }

    /// <summary>Records the state of the document before its first change.</summary>
    internal void OnChanging()
    {
        if (IsTracked && Snapshot is null)
        {
            Snapshot = YamlSourceEditor.CaptureCurrent(this);
        }
    }

    /// <summary>Enumerates the parsing events of the document.</summary>
    public IEnumerable<ParsingEvent> EnumerateEvents()
    {
        return new YamlNodeEventEnumerator(this);
    }

    /// <summary>Writes the document as YAML.</summary>
    public void WriteTo(TextWriter writer, bool suppressDocumentTags = false)
    {
        WriteTo(new Emitter(writer), suppressDocumentTags);
    }

    /// <summary>Writes the document as YAML.</summary>
    public void WriteTo(IEmitter emitter, bool suppressDocumentTags = false)
    {
        YamlModelText.WriteTo(this, emitter, suppressDocumentTags);
    }

    /// <summary>Returns a string representation of the current instance.</summary>
    public override string ToString()
    {
        return YamlModelText.ToYaml(this).Trim();
    }

    /// <summary>Converts the document to an instance of <typeparamref name="T"/>.</summary>
    public T? ToObject<T>(YamlSerializerOptions? options = null)
    {
        return (T?)ToObject(typeof(T), options);
    }

    /// <summary>Converts the document to an object.</summary>
    public object? ToObject(Type type, YamlSerializerOptions? options = null)
    {
        return YamlModelText.ToObject(this, type, options);
    }

    /// <summary>Creates a deep clone of the current value.</summary>
    public YamlStreamDocument DeepClone()
    {
        return new YamlStreamDocument(_documentStart, _documentEnd, Contents?.DeepClone());
    }
}
