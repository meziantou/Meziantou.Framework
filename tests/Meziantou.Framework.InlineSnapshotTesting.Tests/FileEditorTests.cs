using Microsoft.CodeAnalysis.Text;

namespace Meziantou.Framework.InlineSnapshotTesting.Tests;
public sealed class FileEditorTests
{
    [Fact]
    public void DetectEndOfLine_SingleLine_Default() => Assert.Equal(Environment.NewLine, FileEditor.DetectEndOfLine(SourceText.From("test")));

    [Fact]
    public void DetectEndOfLine_Lf() => Assert.Equal("\n", FileEditor.DetectEndOfLine(SourceText.From("test\n")));

    [Fact]
    public void DetectEndOfLine_CrLf() => Assert.Equal("\r\n", FileEditor.DetectEndOfLine(SourceText.From("test\r\n")));

    [Fact]
    public void DetectIndentation_FirstLineIndented() => Assert.Equal("  ", FileEditor.DetectIndentation(SourceText.From("  dummy")));

    [Fact]
    public void DetectIndentation_SecondLineIndented() => Assert.Equal("  ", FileEditor.DetectIndentation(SourceText.From("dummy\n  dummy")));

    // A text ending with a line break has a trailing empty line. Indexing it used to throw IndexOutOfRangeException
    // whenever no earlier line was indented, which is the shape of a top-level-statements file.
    [Fact]
    public void DetectIndentation_NoLineIndented_TrailingLineBreak() => Assert.Equal("    ", FileEditor.DetectIndentation(SourceText.From("dummy\ndummy\n")));

    [Fact]
    public void DetectIndentation_Empty() => Assert.Equal("    ", FileEditor.DetectIndentation(SourceText.From("")));
}
