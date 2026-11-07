using DocumentEnd = Meziantou.Framework.Yaml.Events.DocumentEnd;
using DocumentStart = Meziantou.Framework.Yaml.Events.DocumentStart;

namespace Meziantou.Framework.Yaml.Model;

/// <summary>Represents the Yaml Document.</summary>
public class YamlDocument : YamlNode
{
    private DocumentStart _documentStart;
    private DocumentEnd _documentEnd;
    private YamlElement? _contents;

    /// <summary>Initializes a new instance of this type.</summary>
    public YamlDocument()
    {
        _documentStart = new DocumentStart(null, new TagDirectiveCollection(), true);
        _documentEnd = new DocumentEnd(true);
    }

    private YamlDocument(DocumentStart documentStart, DocumentEnd documentEnd, YamlElement? contents)
    {
        DocumentStart = documentStart;
        DocumentEnd = documentEnd;
        Contents = contents;
    }

    /// <summary>Loads data.</summary>
    public static YamlDocument Load(EventReader eventReader)
    {
        return Load(eventReader, options: null);
    }

    /// <summary>Loads data, honoring the anchor, alias and alias-expansion limits of <paramref name="options"/>.</summary>
    /// <param name="eventReader">The event reader.</param>
    /// <param name="options">
    /// The options used to bound alias expansion. If <see langword="null"/>, <see cref="YamlSerializerOptions.Default"/> is used.
    /// </param>
    public static YamlDocument Load(EventReader eventReader, YamlSerializerOptions? options)
    {
        ArgumentNullException.ThrowIfNull(eventReader);
        return Load(eventReader, CreateContext(options));
    }

    internal static YamlModelLoadContext CreateContext(YamlSerializerOptions? options)
    {
        var effectiveOptions = options ?? YamlSerializerOptions.Default;
        return new YamlModelLoadContext(effectiveOptions.EffectiveMaxAliasExpansionNodeCount, effectiveOptions.AllowAnchors, effectiveOptions.AllowAliases);
    }

    internal static YamlDocument Load(EventReader eventReader, YamlModelLoadContext context)
    {
        var documentStart = eventReader.Expect<DocumentStart>();

        var contents = ReadElement(eventReader, context);

        var documentEnd = eventReader.Expect<DocumentEnd>();

        return new YamlDocument(documentStart, documentEnd, contents);
    }

    /// <summary>Gets document Start.</summary>
    public DocumentStart DocumentStart
    {
        get => _documentStart;
        [MemberNotNull(nameof(_documentStart))]
        set
        {
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
    public YamlElement? Contents
    {
        get { return _contents; }
        set
        {
            _contents = value;
        }
    }

    /// <summary>Creates a deep clone of the current value.</summary>
    public override YamlNode DeepClone()
    {
        return new YamlDocument(_documentStart, _documentEnd, (YamlElement?)Contents?.DeepClone());
    }
}
