namespace Meziantou.Framework.Language;

/// <summary>Describes a single logical line in a <see cref="SourceText"/> instance.</summary>
/// <example>
/// <code>
/// var line = SourceText.From("first\nsecond").Lines[1];
/// var text = line.Text;
/// </code>
/// </example>
public readonly record struct TextLine(int LineNumber, int Start, int End, string Text);
