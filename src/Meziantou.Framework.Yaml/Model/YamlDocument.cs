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
        var documentStart = eventReader.Expect<DocumentStart>();

        var anchors = new Dictionary<string, YamlElement>(StringComparer.Ordinal);
        var contents = ReadElement(eventReader, anchors);

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
