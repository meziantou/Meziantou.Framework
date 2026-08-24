using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Meziantou.Framework.SnapshotTesting.Roslyn.Tests;

public sealed class RoslynSnapshotSerializerTests
{
    private static readonly DiagnosticDescriptor SampleDescriptor = new("SG0001", "Sample", "Sample message '{0}'", "Usage", DiagnosticSeverity.Warning, isEnabledByDefault: true);

    [Fact]
    public void Serialize_OneFilePerGeneratedSourceOrderedByHintName()
    {
        var data = Serialize(new TestGenerator
        {
            Sources = [("b.g.cs", "class B;\r\nclass B2;\r\n"), ("a.g.cs", "class A;\n")],
        });

        Assert.HasCount(2, data);
        Assert.All(data, item => Assert.Equal("cs", item.Extension));
        Assert.Equal("// HintName: a.g.cs\nclass A;\n", GetText(data[0]));
        Assert.Equal("// HintName: b.g.cs\nclass B;\nclass B2;\n", GetText(data[1]));
    }

    [Fact]
    public void Serialize_DiagnosticsAreReportedInATextFile()
    {
        var data = Serialize(new TestGenerator
        {
            Sources = [("a.g.cs", "class A;")],
            Diagnostics = ["first", "second"],
        });

        Assert.HasCount(2, data);
        Assert.Equal("cs", data[0].Extension);
        Assert.Equal("txt", data[1].Extension);
        Assert.Equal("SG0001: Sample message 'first'\nSG0001: Sample message 'second'\n", GetText(data[1]));
    }

    [Fact]
    public void Serialize_NoTextFileWhenThereIsNothingToReport()
    {
        var data = Serialize(new TestGenerator { Sources = [("a.g.cs", "class A;")] });

        var source = Assert.Single(data);
        Assert.Equal("cs", source.Extension);
    }

    [Fact]
    public void Serialize_GeneratorExceptionIsReportedInATextFile()
    {
        var data = Serialize(new TestGenerator { Exception = new InvalidOperationException("boom") });

        var report = Assert.Single(data);
        Assert.Equal("txt", report.Extension);
        Assert.Contains($"{typeof(TestGenerator).FullName}: System.InvalidOperationException: boom\n", GetText(report));
    }

    [Fact]
    public void Serialize_EmptyRunProducesNoSnapshotAtAll()
    {
        Assert.Empty(Serialize(new TestGenerator()));
    }

    [Fact]
    public void Validate_ThrowsWhenTheRunProducedNothing()
    {
        var settings = CreateSettings();
        settings.AutoDetectContinuousEnvironment = false;

        Assert.Throws<SnapshotException>(() => Snapshot.Validate(Run(new TestGenerator()), settings));
    }

    [Fact]
    public void Serialize_IgnoresValuesThatAreNotRunResults()
    {
        var settings = CreateSettings();
        var data = settings.Serializers.Serialize(SnapshotType.Default, "sample");

        var snapshot = Assert.Single(data.Data);
        Assert.Equal(SnapshotType.Default.FileExtension, snapshot.Extension);
    }

    [Fact]
    public void AddRoslyn_IsIdempotent()
    {
        var settings = new SnapshotSettings();
        var count = settings.Serializers.Count;
        settings.AddRoslyn();
        settings.AddRoslyn();

        Assert.Equal(count + 2, settings.Serializers.Count);
    }

    [Fact]
    public void Serialize_SyntaxTreeKeepsTheOriginalText()
    {
        var tree = CSharpSyntaxTree.ParseText("class Sample\r\n{\r    // comment\r\n}\r\n");
        var data = CreateSettings().Serializers.Serialize(SnapshotType.Default, tree).Data;

        var snapshot = Assert.Single(data);
        Assert.Equal("cs", snapshot.Extension);
        Assert.Equal("class Sample\n{\n    // comment\n}\n", GetText(snapshot));
    }

    [Fact]
    public void Serialize_SyntaxNodeKeepsItsTrivia()
    {
        var tree = CSharpSyntaxTree.ParseText("class Sample\n{\n    // comment\n    void Method() { }\n}\n");
        var node = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var data = CreateSettings().Serializers.Serialize(SnapshotType.Default, node).Data;

        var snapshot = Assert.Single(data);
        Assert.Equal("cs", snapshot.Extension);
        Assert.Equal("    // comment\n    void Method() { }\n", GetText(snapshot));
    }

    [Fact]
    public void Validate_WritesSyntaxTreeAsACSharpFile()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateSettings();
        settings.AutoDetectContinuousEnvironment = false;
        settings.SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure;
        settings.SnapshotPathStrategy = context => directory / ("snapshot_" + context.Index.ToString(CultureInfo.InvariantCulture) + ".verified." + context.Extension);

        Snapshot.Validate(CSharpSyntaxTree.ParseText("class Sample;"), settings);

        var file = Assert.Single(Directory.GetFiles(directory.FullPath));
        Assert.EndsWith(".verified.cs", file);
        Assert.Equal("class Sample;", File.ReadAllText(file));
    }

    [Fact]
    public void Validate_WritesGeneratedSourcesAsCSharpFiles()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateSettings();
        settings.AutoDetectContinuousEnvironment = false;
        settings.SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure;
        settings.SnapshotPathStrategy = context => directory / ("snapshot_" + context.Index.ToString(CultureInfo.InvariantCulture) + ".verified." + context.Extension);

        var runResult = Run(new TestGenerator
        {
            Sources = [("a.g.cs", "class A;"), ("b.g.cs", "class B;")],
            Diagnostics = ["sample"],
        });

        Snapshot.Validate(runResult, settings);

        Assert.True(File.Exists(directory / "snapshot_0.verified.cs"));
        Assert.True(File.Exists(directory / "snapshot_1.verified.cs"));
        Assert.True(File.Exists(directory / "snapshot_2.verified.txt"));
        Assert.HasCount(3, Directory.GetFiles(directory.FullPath));
    }

    private static SnapshotSettings CreateSettings()
    {
        var settings = new SnapshotSettings();
        settings.AddRoslyn();
        return settings;
    }

    private static IReadOnlyList<SnapshotData> Serialize(TestGenerator generator)
        => CreateSettings().Serializers.Serialize(SnapshotType.Default, Run(generator)).Data;

    private static GeneratorDriverRunResult Run(TestGenerator generator)
    {
        var compilation = CSharpCompilation.Create("compilation", [CSharpSyntaxTree.ParseText("class Sample;")]);
        return CSharpGeneratorDriver.Create(generator).RunGenerators(compilation).GetRunResult();
    }

    private static string GetText(SnapshotData data) => Encoding.UTF8.GetString(data.Data);

    private sealed class TestGenerator : IIncrementalGenerator
    {
        public ImmutableArray<(string HintName, string Source)> Sources { get; init; } = [];
        public ImmutableArray<string> Diagnostics { get; init; } = [];
        public Exception? Exception { get; init; }

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterSourceOutput(context.CompilationProvider, (productionContext, _) =>
            {
                if (Exception is not null)
                    throw Exception;

                foreach (var (hintName, source) in Sources)
                {
                    productionContext.AddSource(hintName, SourceText.From(source, Encoding.UTF8));
                }

                foreach (var message in Diagnostics)
                {
                    productionContext.ReportDiagnostic(Diagnostic.Create(SampleDescriptor, Location.None, message));
                }
            });
        }
    }
}
