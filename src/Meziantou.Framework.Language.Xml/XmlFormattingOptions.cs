namespace Meziantou.Framework.Language.Xml;

/// <summary>
/// Controls whitespace and layout decisions used by <see cref="Formatter"/>.
/// </summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Language.Xml.Tests/XmlSyntaxTreeTests.cs" ref:region="XmlFormattingOptions_Format" ref:dedent="true">
/// var tree = XmlSyntaxTree.ParseText("&lt;root&gt;&lt;item id=\"1\"/&gt;&lt;/root&gt;");
/// var options = new XmlFormattingOptions { IndentationSize = 4, NewLine = "\r\n", SpaceAroundEquals = true };
/// var formatted = Formatter.Format(tree.GetRoot(), options);
/// Assert.Equal("&lt;root&gt;\r\n    &lt;item id = \"1\" /&gt;\r\n&lt;/root&gt;", formatted.ToFullString());
/// </code>
/// </example>
public sealed class XmlFormattingOptions
{
    private readonly int _maxDepth = 64;

    public static XmlFormattingOptions Default { get; } = new();

    /// <summary>Gets the number of spaces per indentation level. It is ignored when <see cref="UseTabs"/> is <see langword="true"/>. The default is 2.</summary>
    public int IndentationSize { get; init; } = 2;

    /// <summary>Gets a value indicating whether each indentation level is a tab instead of <see cref="IndentationSize"/> spaces.</summary>
    public bool UseTabs { get; init; }

    /// <summary>Gets the line break written between the nodes the formatter puts on their own line. The default is <c>"\n"</c>.</summary>
    public string NewLine { get; init; } = "\n";

    /// <summary>Gets a value indicating whether an element written on a single line is kept as it is, instead of putting each of its children on its own line.</summary>
    /// <remarks>The element still starts on its own line. Its whitespace is kept, and its attributes stay on its first line even when <see cref="AttributeWrapping"/> is set.</remarks>
    public bool PreserveSingleLineElements { get; init; }

    /// <summary>Gets a value indicating whether each attribute of an element that has more than one goes on its own line, indented one level deeper than the element.</summary>
    /// <remarks>It applies to the elements the formatter puts on their own line, not to those inside text.</remarks>
    public bool AttributeWrapping { get; init; }

    /// <summary>Gets a value indicating whether the equals sign of each attribute is written with a space on both sides, as in <c>name = "value"</c>.</summary>
    public bool SpaceAroundEquals { get; init; }

    /// <summary>Gets a value indicating whether a self-closing tag ends with <c> /&gt;</c> instead of <c>/&gt;</c>. The default is <see langword="true"/>.</summary>
    public bool SpaceBeforeSelfClosingSlash { get; init; } = true;

    /// <summary>Gets the number of nested element levels the formatter lays out. The default is 64.</summary>
    /// <remarks>
    /// The root element is at depth 1. An element deeper than this value still starts on its own line, but the whitespace
    /// of its content is kept as it is, like the content of an element that contains text, and its attributes stay on its
    /// first line even when <see cref="AttributeWrapping"/> is set. This bounds the indentation, so the size of the output
    /// stays proportional to the size of the input.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int MaxDepth
    {
        get => _maxDepth;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _maxDepth = value;
        }
    }
}
