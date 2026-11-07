namespace Meziantou.Framework.Language;

/// <summary>Describes a single logical line in a <see cref="SourceText"/> instance.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Language.Tests/SourceTextTests.cs" ref:region="TextLine_FromSourceText" ref:dedent="true">
/// var line = SourceText.From("first\nsecond").Lines[1];
/// var text = line.Text;
/// Assert.Equal("second", text);
/// Assert.Equal(1, line.LineNumber);
/// Assert.Equal(6, line.Start);
/// </code>
/// </example>
public readonly record struct TextLine(int LineNumber, int Start, int End, string Text);
