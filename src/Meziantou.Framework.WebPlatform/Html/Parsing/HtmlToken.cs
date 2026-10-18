namespace Meziantou.Framework.WebPlatform.Html.Parsing;

internal enum HtmlTokenType : byte
{
    None,
    Doctype,
    StartTag,
    EndTag,
    Comment,
    ProcessingInstruction,
    Characters,
    EndOfFile,
}

/// <summary>An attribute of a tag token.</summary>
internal struct HtmlTokenAttribute
{
    public string Name;
    public string Value;

    // Set by the tree builder when adjusting foreign attributes
    public string? Prefix;
    public string? Namespace;
    public string LocalName;

    public HtmlTokenAttribute(string name, string value)
    {
        Name = name;
        Value = value;
        LocalName = name;
    }
}

/// <summary>
/// A start or end tag token. Instances are created by the tokenizer and kept by the tree builder when needed
/// (e.g. the list of active formatting elements keeps the token of each element). The tokens that are not kept are reused
/// by the tokenizer.
/// </summary>
internal sealed class HtmlTagToken
{
    private HtmlTokenAttribute[] _attributes;
    private int _attributeCount;

    public HtmlTagToken(string name, TagId tagId, HtmlTokenAttribute[] attributes, bool selfClosing)
    {
        Name = name;
        TagId = tagId;
        _attributes = attributes;
        _attributeCount = attributes.Length;
        SelfClosing = selfClosing;
    }

    public string Name { get; set; }

    public TagId TagId { get; set; }

    public Span<HtmlTokenAttribute> Attributes => _attributes.AsSpan(0, _attributeCount);

    /// <summary>Whether the tree builder keeps the token after it is processed (e.g. in the list of active formatting elements): it is not reused.</summary>
    public bool Retained { get; set; }

    /// <summary>Replaces the attributes of the token with a copy of the attributes (the array of the token is reused when it is large enough).</summary>
    public void SetAttributes(ReadOnlySpan<HtmlTokenAttribute> attributes)
    {
        if (_attributes.Length < attributes.Length)
        {
            _attributes = new HtmlTokenAttribute[attributes.Length];
        }
        else
        {
            // The values of the previous attributes are not retained
            _attributes.AsSpan(attributes.Length, _attributeCount - Math.Min(_attributeCount, attributes.Length)).Clear();
        }

        attributes.CopyTo(_attributes);
        _attributeCount = attributes.Length;
    }

    public bool SelfClosing { get; set; }

    /// <summary>Whether the tokenizer reported a duplicate-attribute parse error for the token (the attribute was dropped).</summary>
    public bool HadDuplicateAttribute { get; set; }

    public bool SelfClosingAcknowledged { get; set; }

    public string? GetAttribute(string name)
    {
        foreach (ref var attribute in Attributes)
        {
            if (attribute.Name == name)
                return attribute.Value;
        }

        return null;
    }

    public bool HasAttribute(string name) => GetAttribute(name) is not null;
}
