namespace Meziantou.Framework.Imaging.Internals;

/// <summary>An element of an SVG document: its name, its attributes, and the elements it contains.</summary>
internal sealed class SvgElement(string name, SvgElement? parent, bool isForeign)
{
    public string Name { get; } = name;

    public SvgElement? Parent { get; } = parent;

    /// <summary>Gets a value indicating whether the element is not an SVG element: it is never drawn.</summary>
    public bool IsForeign { get; } = isForeign;

    public Dictionary<string, string> Attributes { get; } = new(StringComparer.Ordinal);

    public List<SvgElement> Children { get; } = [];

    public string? GetAttribute(string attribute) => Attributes.GetValueOrDefault(attribute);

    public void SetAttribute(string attribute, string value) => Attributes[attribute] = value;
}
