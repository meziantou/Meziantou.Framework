using FluentAssertions;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Meziantou.Framework.InlineSnapshotTesting.Tests;
public sealed class FileEditorTests
{
    [Fact]
    public void DetectEndOfLine_SingleLine_Default() => FileEditor.DetectEndOfLine(SourceText.From("test")).Should().Be(Environment.NewLine);

    [Fact]
    public void DetectEndOfLine_Lf() => FileEditor.DetectEndOfLine(SourceText.From("test\n")).Should().Be("\n");

    [Fact]
    public void DetectEndOfLine_CrLf() => FileEditor.DetectEndOfLine(SourceText.From("test\r\n")).Should().Be("\r\n");

    [Fact]
    public void DetectIndentation_FirstLineIndented() => FileEditor.DetectIndentation(SourceText.From("  dummy")).Should().Be("  ");

    [Fact]
    public void DetectIndentation_SecondLineIndented() => FileEditor.DetectIndentation(SourceText.From("dummy\n  dummy")).Should().Be("  ");
}
