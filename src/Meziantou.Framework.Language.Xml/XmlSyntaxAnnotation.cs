namespace Meziantou.Framework.Language.Xml;

/// <summary>Represents user-defined metadata attached to syntax.</summary>
/// <example>
/// <code>
/// var annotation = new XmlSyntaxAnnotation("source", "generated");
/// </code>
/// </example>
public sealed class XmlSyntaxAnnotation
{
    public XmlSyntaxAnnotation(string kind, string? data = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        Kind = kind;
        Data = data;
    }

    public string Kind { get; }
    public string? Data { get; }
}
