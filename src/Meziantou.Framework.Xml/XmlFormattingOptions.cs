namespace Meziantou.Framework.Xml;

/// <summary>
/// Controls whitespace and layout decisions used by <see cref="Formatter"/>.
/// </summary>
/// <example>
/// <code>
/// var options = new XmlFormattingOptions { IndentationSize = 4, SpaceAroundEquals = true };
/// var formatted = Formatter.Format(tree, options);
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
