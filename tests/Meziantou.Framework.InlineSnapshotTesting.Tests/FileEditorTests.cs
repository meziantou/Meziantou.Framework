using System.Reflection;
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

    [Fact]
    public void UpdateFile_StaleLineInAnotherMember_UpdatesTheCallerMember()
    {
        // The line was recorded when the opening brace was on the line of the class. It now points to the call in A,
        // which has the same snapshot. This used to update A.
        using var directory = TemporaryDirectory.Create();
        var path = directory.CreateTextFile("Sample.cs", """
            class Sample
            {
                void A() { InlineSnapshot.Validate(1, ""); }
                void B() { InlineSnapshot.Validate(2, ""); }
            }
            """);
        var strategy = new RecordingStrategy();

        FileEditor.UpdateFile(CreateContext(path) with { LineNumber = 3, CallerMemberName = "B", CallerTypeName = "Sample", CallerParameterCount = 0 }, CreateSettings(strategy), existingValue: "", newValue: "2");

        Assert.Equal("""
            class Sample
            {
                void A() { InlineSnapshot.Validate(1, ""); }
                void B() { InlineSnapshot.Validate(2, "2"); }
            }
            """, strategy.Content);
    }

    [Fact]
    public void UpdateFile_StaleLineInMemberOfAnotherType_UpdatesTheCallerMember()
    {
        using var directory = TemporaryDirectory.Create();
        var path = directory.CreateTextFile("Sample.cs", """
            class A { void Test() { InlineSnapshot.Validate(1, ""); } }
            class B { void Test() { InlineSnapshot.Validate(2, ""); } }
            """);
        var strategy = new RecordingStrategy();

        FileEditor.UpdateFile(CreateContext(path) with { LineNumber = 1, CallerMemberName = "Test", CallerTypeName = "B", CallerParameterCount = 0 }, CreateSettings(strategy), existingValue: "", newValue: "2");

        Assert.Equal("""
            class A { void Test() { InlineSnapshot.Validate(1, ""); } }
            class B { void Test() { InlineSnapshot.Validate(2, "2"); } }
            """, strategy.Content);
    }

    [Fact]
    public void UpdateFile_StaleLineInOverloadWithTheSameIdentity_Throws()
    {
        // Both overloads have one parameter, so the line cannot be checked against the caller
        using var directory = TemporaryDirectory.Create();
        const string Source = """
            class Sample
            {
                void B(int value) { InlineSnapshot.Validate(1, ""); }
                void B(string value) { InlineSnapshot.Validate(2, ""); }
            }
            """;
        var path = directory.CreateTextFile("Sample.cs", Source);
        var strategy = new RecordingStrategy();

        var exception = Assert.Throws<InlineSnapshotException>(() => FileEditor.UpdateFile(CreateContext(path) with { LineNumber = 3, CallerMemberName = "B", CallerTypeName = "Sample", CallerParameterCount = 1 }, CreateSettings(strategy), existingValue: "", newValue: "2"));

        Assert.Contains("several members named 'B'", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, strategy.UpdateCount);
        Assert.Equal(Source, File.ReadAllText(path));
    }

    [Fact]
    public void UpdateFile_OverloadsWithDifferentSnapshots_UpdatesTheCallOnTheLine()
    {
        using var directory = TemporaryDirectory.Create();
        var path = directory.CreateTextFile("Sample.cs", """
            class Sample
            {
                void B(int value) { InlineSnapshot.Validate(1, ""); }
                void B(string value) { InlineSnapshot.Validate(2, "2"); }
            }
            """);
        var strategy = new RecordingStrategy();

        FileEditor.UpdateFile(CreateContext(path) with { LineNumber = 3, CallerMemberName = "B", CallerTypeName = "Sample", CallerParameterCount = 1 }, CreateSettings(strategy), existingValue: "", newValue: "1");

        Assert.Equal("""
            class Sample
            {
                void B(int value) { InlineSnapshot.Validate(1, "1"); }
                void B(string value) { InlineSnapshot.Validate(2, "2"); }
            }
            """, strategy.Content);
    }

    // The receiver argument and the snapshot hold the same value: only the snapshot may change
    [Theory]
    [InlineData("using Alias = HelperExtensions;\nAlias.Check(\"old\", \"old\");", "Check", "using Alias = HelperExtensions;\nAlias.Check(\"old\", \"new\");")]
    [InlineData("using Alias = global::Sample.HelperExtensions;\nAlias.Check(\"old\", \"old\");", "Check", "using Alias = global::Sample.HelperExtensions;\nAlias.Check(\"old\", \"new\");")]
    [InlineData("\nHelperExtensions.Check(\"old\", \"old\");", "Check", "\nHelperExtensions.Check(\"old\", \"new\");")]
    [InlineData("\nSample.HelperExtensions.Check(\"old\", \"old\");", "Check", "\nSample.HelperExtensions.Check(\"old\", \"new\");")]
    [InlineData("\nHelperExtensions.CheckWithMessage(\"old\", \"old\", \"old\");", "CheckWithMessage", "\nHelperExtensions.CheckWithMessage(\"old\", \"new\", \"old\");")]
    [InlineData("\n\"old\".CheckWithMessage(\"old\", \"old\");", "CheckWithMessage", "\n\"old\".CheckWithMessage(\"new\", \"old\");")]
    [InlineData("var subject = \"old\";\nsubject.CheckWithMessage(\"old\", \"old\");", "CheckWithMessage", "var subject = \"old\";\nsubject.CheckWithMessage(\"new\", \"old\");")]
    [InlineData("using Alias = HelperExtensions;\nAlias.CheckWithMessage(\"old\", message: \"old\", expected: \"old\");", "CheckWithMessage", "using Alias = HelperExtensions;\nAlias.CheckWithMessage(\"old\", message: \"old\", expected: \"new\");")]
    public void UpdateFile_ExtensionMethodWithIdenticalArguments_UpdatesTheSnapshot(string source, string methodName, string expected)
    {
        using var directory = TemporaryDirectory.Create();
        var path = directory.CreateTextFile("Sample.cs", source);
        var strategy = new RecordingStrategy();

        FileEditor.UpdateFile(CreateExtensionMethodContext(path, methodName), CreateSettings(strategy), existingValue: "old", newValue: "new");

        Assert.Equal(expected, strategy.Content);
    }

    [Fact]
    public void UpdateFile_ExtensionMethodCalledThroughUnknownName_Throws()
    {
        // Alias may be a value, or an alias declared in another file, such as a global using
        using var directory = TemporaryDirectory.Create();
        const string Source = "\nAlias.Check(\"old\", \"old\");";
        var path = directory.CreateTextFile("Sample.cs", Source);
        var strategy = new RecordingStrategy();

        var exception = Assert.Throws<InlineSnapshotException>(() => FileEditor.UpdateFile(CreateExtensionMethodContext(path, "Check"), CreateSettings(strategy), existingValue: "old", newValue: "new"));

        Assert.Contains("Name the argument (expected: ...)", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, strategy.UpdateCount);
        Assert.Equal(Source, File.ReadAllText(path));
    }

    [Fact]
    public void UpdateFile_ExtensionMethodCalledThroughUnknownName_AddsANamedArgument()
    {
        using var directory = TemporaryDirectory.Create();
        var path = directory.CreateTextFile("Sample.cs", "\nAlias.Check(\"old\");");
        var strategy = new RecordingStrategy();

        FileEditor.UpdateFile(CreateExtensionMethodContext(path, "Check"), CreateSettings(strategy), existingValue: null, newValue: "new");

        Assert.Equal("\nAlias.Check(\"old\", expected: \"new\");", strategy.Content);
    }

    [Fact]
    public void GetSourceTypeName_Lambda_ReturnsTheTypeDeclaringTheMember()
    {
        Func<int> lambda = () => 1;

        Assert.Equal(nameof(FileEditorTests), CallerContext.GetSourceTypeName(lambda.Method));
        Assert.Equal(-1, CallerContext.GetSourceParameterCount(lambda.Method));
    }

    [Fact]
    public void GetSourceTypeName_GenericType_ReturnsTheNameWithoutArity()
    {
        var method = typeof(GenericType<int>).GetMethod(nameof(GenericType<>.Method), BindingFlags.Static | BindingFlags.Public)!;

        Assert.Equal("GenericType", CallerContext.GetSourceTypeName(method));
        Assert.Equal(2, CallerContext.GetSourceParameterCount(method));
    }

    [Fact]
    public void GetSourceMemberName_ExplicitInterfaceImplementation_ReturnsTheMemberName()
    {
        var method = typeof(GenericType<int>).GetMethod("System.IDisposable.Dispose", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.Equal("Dispose", CallerContext.GetSourceMemberName(method));
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
        CallerTypeName: null,
        CallerParameterCount: -1,
        AssemblyLocation: null);

    private static CallerContext CreateExtensionMethodContext(FullPath path, string methodName) => CreateContext(path) with
    {
        LineNumber = 2,
        MethodName = methodName,
        IsExtensionMethod = true,
        DeclaringTypeName = "HelperExtensions",
    };

    private static InlineSnapshotSettings CreateSettings(SnapshotUpdateStrategy strategy) => new()
    {
        SnapshotUpdateStrategy = strategy,
        AllowedStringFormats = CSharpStringFormats.Quoted,
    };

    private sealed class GenericType<T> : IDisposable
    {
        public static void Method(T value, int count)
        {
        }

        void IDisposable.Dispose()
        {
        }
    }

    private sealed class RecordingStrategy : SnapshotUpdateStrategy
    {
        public int UpdateCount { get; private set; }

        public string? Content { get; private set; }

        public override bool ReuseTemporaryFile => false;

        public override bool CanUpdateSnapshot(InlineSnapshotSettings settings, string path, string? expectedSnapshot, string? actualSnapshot) => true;

        public override bool MustReportError(InlineSnapshotSettings settings, string path) => true;

        public override void UpdateFile(InlineSnapshotSettings settings, string targetFile, string tempFile)
        {
            UpdateCount++;
            Content = File.ReadAllText(tempFile);
        }
    }
}
