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
}
