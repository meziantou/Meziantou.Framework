namespace Meziantou.Framework.Xml;

/// <summary>
/// Describes a single logical line in a <see cref="SourceText"/> instance.
/// </summary>
/// <example>
/// <code>
/// var line = SourceText.From("&lt;root/&gt;").Lines[0];
/// var text = line.Text;
/// </code>
/// </example>
public readonly record struct TextLine(int LineNumber, int Start, int End, string Text);
