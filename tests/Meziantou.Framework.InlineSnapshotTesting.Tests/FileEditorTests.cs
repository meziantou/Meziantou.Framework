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

    [Fact]
    public void UpdateFile_SnapshotDiffers_UpdatesTheFile()
    {
        using var directory = TemporaryDirectory.Create();
        var path = directory.CreateTextFile("Sample.cs", "InlineSnapshot.Validate(1, \"\");\n");
        var strategy = new RecordingStrategy();

        FileEditor.UpdateFile(CreateContext(path), CreateSettings(strategy), existingValue: "", newValue: "1");

        Assert.Equal(1, strategy.UpdateCount);
    }

    [Fact]
    public void UpdateFile_SnapshotAlreadyHoldsTheNewValue_DoesNotUpdateTheFile()
    {
        // Another test process, or a previous execution of the same call, already updated the snapshot. This used to throw.
        using var directory = TemporaryDirectory.Create();
        var path = directory.CreateTextFile("Sample.cs", "InlineSnapshot.Validate(1, \"1\");\n");
        var strategy = new RecordingStrategy();

        FileEditor.UpdateFile(CreateContext(path), CreateSettings(strategy), existingValue: "", newValue: "1");

        Assert.Equal(0, strategy.UpdateCount);
    }

    [Fact]
    public void UpdateFile_SnapshotAlreadyWrittenThePreferredWay_DoesNotUpdateTheFile()
    {
        // ForceUpdateSnapshots used to rewrite the file, or open a merge tool, for a snapshot that was already formatted
        using var directory = TemporaryDirectory.Create();
        var path = directory.CreateTextFile("Sample.cs", "InlineSnapshot.Validate(1, \"1\");\n");
        var strategy = new RecordingStrategy();

        FileEditor.UpdateFile(CreateContext(path), CreateSettings(strategy), existingValue: "1", newValue: "1");

        Assert.Equal(0, strategy.UpdateCount);
    }

    [Fact]
    public void UpdateFile_ForceUpdateOfSnapshotNotWrittenThePreferredWay_UpdatesTheFile()
    {
        using var directory = TemporaryDirectory.Create();
        var path = directory.CreateTextFile("Sample.cs", "InlineSnapshot.Validate(1, @\"1\");\n");
        var strategy = new RecordingStrategy();

        FileEditor.UpdateFile(CreateContext(path), CreateSettings(strategy), existingValue: "1", newValue: "1");

        Assert.Equal(1, strategy.UpdateCount);
    }

    [Fact]
    public void DeleteStaleTemporaryFiles_DeletesOnlyTheOldDirectories()
    {
        using var directory = TemporaryDirectory.Create();
        var oldFile = directory.CreateTextFile("old/Sample.cs", "");
        var recentFile = directory.CreateTextFile("recent/Sample.cs", "");
        var oldDate = DateTime.UtcNow.AddDays(-2);
        File.SetLastWriteTimeUtc(oldFile, oldDate);
        new FileInfo(oldFile).IsReadOnly = true;
        Directory.SetLastWriteTimeUtc(oldFile.Parent, oldDate);

        FileEditor.DeleteStaleTemporaryFiles(directory.FullPath);

        Assert.False(Directory.Exists(oldFile.Parent));
        Assert.True(File.Exists(recentFile));
    }

    private static CallerContext CreateContext(FullPath path) => new(
        path,
        LineNumber: 1,
        SequencePointLineNumber: 0,
        SequencePointColumnNumber: 0,
        MethodName: nameof(InlineSnapshot.Validate),
        ParameterName: "expected",
        ParameterIndex: 1,
        ParameterDefaultValue: null,
        IsExtensionMethod: false,
        DeclaringTypeName: nameof(InlineSnapshot),
        CallerMemberName: null,
        AssemblyLocation: null);

    private static InlineSnapshotSettings CreateSettings(SnapshotUpdateStrategy strategy) => new()
    {
        SnapshotUpdateStrategy = strategy,
        AllowedStringFormats = CSharpStringFormats.Quoted,
    };

    private sealed class RecordingStrategy : SnapshotUpdateStrategy
    {
        public int UpdateCount { get; private set; }

        public override bool ReuseTemporaryFile => false;

        public override bool CanUpdateSnapshot(InlineSnapshotSettings settings, string path, string? expectedSnapshot, string? actualSnapshot) => true;

        public override bool MustReportError(InlineSnapshotSettings settings, string path) => true;

        public override void UpdateFile(InlineSnapshotSettings settings, string targetFile, string tempFile) => UpdateCount++;
    }
}
