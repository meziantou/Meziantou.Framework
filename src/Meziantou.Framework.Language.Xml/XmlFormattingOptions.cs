namespace Meziantou.Framework.Language.Xml;

/// <summary>
/// Controls whitespace and layout decisions used by <see cref="Formatter"/>.
/// </summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Language.Xml.Tests/XmlSyntaxTreeTests.cs" ref:region="XmlFormattingOptions_Format" ref:dedent="true">
/// var tree = XmlSyntaxTree.ParseText("&lt;root&gt;&lt;item id=\"1\"/&gt;&lt;/root&gt;");
/// var options = new XmlFormattingOptions { IndentationSize = 4, NewLine = "\r\n" };
/// var formatted = Formatter.Format(tree.GetRoot(), options);
/// Assert.Equal("&lt;root&gt;\r\n    &lt;item id=\"1\" /&gt;\r\n&lt;/root&gt;", formatted.ToFullString());
/// </code>
/// </example>
public sealed class XmlFormattingOptions
{
    public static XmlFormattingOptions Default { get; } = new();

    public int IndentationSize { get; init; } = 2;
    public bool UseTabs { get; init; }
    public string NewLine { get; init; } = "\n";
    public bool PreserveSingleLineElements { get; init; }
    public bool AttributeWrapping { get; init; }
    public bool SpaceAroundEquals { get; init; }
    public bool SpaceBeforeSelfClosingSlash { get; init; }
}
