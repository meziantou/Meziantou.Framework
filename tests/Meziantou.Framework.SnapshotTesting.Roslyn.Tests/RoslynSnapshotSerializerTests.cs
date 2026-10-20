using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Meziantou.Framework.SnapshotTesting.Roslyn.Tests;

public sealed class RoslynSnapshotSerializerTests
{
    private static readonly DiagnosticDescriptor SampleDescriptor = new("SG0001", "Sample", "Sample message '{0}'", "Usage", DiagnosticSeverity.Warning, isEnabledByDefault: true);

    // The settings, generator, rewriter and analyzer used by the readme examples
    #region SnapshotSettings_AddRoslyn
    internal static class SnapshotConfiguration
    {
        [ModuleInitializer]
        internal static void Initialize()
        {
            SnapshotSettings.Default.AddRoslyn();
        }
    }
    #endregion

    internal sealed class MyGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var classNames = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax,
                static (context, _) => ((ClassDeclarationSyntax)context.Node).Identifier.ValueText);

            context.RegisterSourceOutput(classNames, (context, className) =>
                context.AddSource(className + ".g.cs", "partial class " + className + "\n{\n    public override string ToString() => \"" + className + "\";\n}\n"));
        }
    }

    internal sealed class MyRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var visited = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;
            return visited.AddModifiers(SyntaxFactory.Token(SyntaxKind.SealedKeyword).WithTrailingTrivia(SyntaxFactory.Space));
        }
    }

    [SuppressMessage("MicrosoftCodeAnalysisCorrectness", "RS1001:Missing diagnostic analyzer attribute", Justification = "The analyzer is only run by CompilationWithAnalyzers; the attribute would make the test project an analyzer project (RS1036, RS1041).")]
    internal sealed class MyAnalyzer : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor Rule = new("MY0001", "Do not use this method", "Do not use this method", "Usage", DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(context =>
            {
                var method = (MethodDeclarationSyntax)context.Node;
                if (method.Identifier.ValueText is "DoNotUse")
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, method.Identifier.GetLocation()));
                }
            }, SyntaxKind.MethodDeclaration);
        }
    }

    [Fact]
    public void ReadmeExample_SourceGenerator()
    {
        #region SnapshotRoslyn_SourceGenerator
        var source = "partial class Customer; partial class Order;";
        MetadataReference[] references = [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)];

        var compilation = CSharpCompilation.Create("compilation", [CSharpSyntaxTree.ParseText(source)], references);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MyGenerator());
        driver = driver.RunGenerators(compilation);

        Snapshot.Validate(driver.GetRunResult());
        #endregion
    }

    [Fact]
    public void ReadmeExample_SyntaxRewriter()
    {
        #region SnapshotRoslyn_SyntaxRewriter
        var tree = CSharpSyntaxTree.ParseText("""
            public class Sample
            {
                public class Nested;
            }
            """);

        Snapshot.Validate(new MyRewriter().Visit(tree.GetRoot()));
        #endregion
    }

    [Fact]
    public void ReadmeExample_SourceText()
    {
        #region SnapshotRoslyn_SourceText
        var sourceText = SourceText.From("class Sample;\n");
        Snapshot.Validate(sourceText, SnapshotType.Create("cs"));
        #endregion
    }

    [Fact]
    public async Task ReadmeExample_Diagnostics()
    {
        #region SnapshotRoslyn_Diagnostics
        var source = """
            class Sample
            {
                void DoNotUse() { }
            }
            """;
        var compilation = CSharpCompilation.Create("compilation", [CSharpSyntaxTree.ParseText(source, path: "Sample.cs")]);

        var diagnostics = await compilation.WithAnalyzers([new MyAnalyzer()]).GetAnalyzerDiagnosticsAsync();

        Snapshot.Validate(diagnostics);
        #endregion
    }

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
        Assert.Equal("warning SG0001: Sample message 'first'\nwarning SG0001: Sample message 'second'\n", GetText(data[1]));
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

        Assert.Equal(count + 3, settings.Serializers.Count);
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

    [Fact]
    public void Serialize_DiagnosticKeepsItsLocation()
    {
        var tree = CSharpSyntaxTree.ParseText("class Sample;", path: "Sample.cs");
        var diagnostic = Diagnostic.Create(SampleDescriptor, tree.GetRoot().GetLocation(), "here");
        var data = CreateSettings().Serializers.Serialize(SnapshotType.Default, diagnostic).Data;

        var snapshot = Assert.Single(data);
        Assert.Equal("Sample.cs(1,1): warning SG0001: Sample message 'here'\n", GetText(snapshot));
    }

    [Fact]
    public void Serialize_DiagnosticCollectionIsSorted()
    {
        var data = CreateSettings().Serializers.Serialize(SnapshotType.Default, ImmutableArray.Create(
            Diagnostic.Create(SampleDescriptor, Location.None, "second"),
            Diagnostic.Create(SampleDescriptor, Location.None, "first"))).Data;

        var snapshot = Assert.Single(data);
        Assert.Equal("warning SG0001: Sample message 'first'\nwarning SG0001: Sample message 'second'\n", GetText(snapshot));
    }

    [Fact]
    public void Serialize_EmptyDiagnosticCollectionIsAnEmptyFile()
    {
        var data = CreateSettings().Serializers.Serialize(SnapshotType.Default, ImmutableArray<Diagnostic>.Empty).Data;

        var snapshot = Assert.Single(data);
        Assert.Empty(snapshot.Data);
    }

    [Fact]
    public void Serialize_SourceTextLeavesTheExtensionToTheRequestedSnapshotType()
    {
        var data = CreateSettings().Serializers.Serialize(SnapshotType.Default, SourceText.From("class Sample;\r\n")).Data;

        var snapshot = Assert.Single(data);
        Assert.Null(snapshot.Extension);
        Assert.Equal("class Sample;\n", GetText(snapshot));
    }

    [Fact]
    public void Validate_WritesSourceTextWithTheRequestedSnapshotType()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateSettings();
        settings.AutoDetectContinuousEnvironment = false;
        settings.SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure;
        settings.SnapshotPathStrategy = context => directory / ("snapshot_" + context.Index.ToString(CultureInfo.InvariantCulture) + ".verified." + context.Extension);

        Snapshot.Validate(SourceText.From("class Sample;"), SnapshotType.Create("cs"), settings);

        var file = Assert.Single(Directory.GetFiles(directory.FullPath));
        Assert.EndsWith(".verified.cs", file);
    }

    [Fact]
    public void Serialize_TokensAndTrivia()
    {
        var root = CSharpSyntaxTree.ParseText("class Sample\n{\n    // comment\n    void Method() { }\n}\n").GetRoot();
        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var settings = CreateSettings();

        Assert.Equal("    // comment\n    void ", GetText(Assert.Single(settings.Serializers.Serialize(SnapshotType.Default, method.ReturnType.GetFirstToken()).Data)));
        Assert.Equal("    // comment\n    ", GetText(Assert.Single(settings.Serializers.Serialize(SnapshotType.Default, method.GetLeadingTrivia()).Data)));
        Assert.Equal("Method", GetText(Assert.Single(settings.Serializers.Serialize(SnapshotType.Default, (SyntaxNodeOrToken)method.Identifier).Data)));
    }

    [Fact]
    public void Serialize_ConvertersFormatNestedRoslynValues()
    {
        var tree = CSharpSyntaxTree.ParseText("class Sample;", path: "Sample.cs");
        var value = new
        {
            Diagnostic = Diagnostic.Create(SampleDescriptor, tree.GetRoot().GetLocation(), "here"),
            Location = tree.GetRoot().GetLocation(),
            tree.GetRoot().Span,
            LinePosition = new LinePosition(3, 5),
        };

        var text = GetText(Assert.Single(CreateSettings().Serializers.Serialize(SnapshotType.Default, value).Data));

        Assert.Contains("Diagnostic: Sample.cs(1,1): warning SG0001: Sample message 'here'", text);
        Assert.Contains("Location: Sample.cs(0,0)-(0,13)", text);
        Assert.Contains("Span: [0..13)", text);
        Assert.Contains("LinePosition: 3,5", text);
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

    [Fact]
    public void DiagnosticCollection_IsSortedSoTheOutputIsDeterministic()
    {
        var descriptor = new DiagnosticDescriptor("SG0001", "Title", "{0}", "Usage", DiagnosticSeverity.Warning, isEnabledByDefault: true);
        var diagnostics = new[]
        {
            Diagnostic.Create(descriptor, Location.None, "zulu"),
            Diagnostic.Create(descriptor, Location.None, "alpha"),
            Diagnostic.Create(descriptor, Location.None, "mike"),
        };

        var settings = new SnapshotSettings();
        settings.AddRoslyn();

        var snapshot = Assert.Single(settings.Serializers.Serialize(SnapshotType.Default, diagnostics).Data);

        Assert.Equal(
            """
            warning SG0001: alpha
            warning SG0001: mike
            warning SG0001: zulu

            """.ReplaceLineEndings("\n"),
            Encoding.UTF8.GetString(snapshot.Data));
    }

    [Fact]
    public void DiagnosticCollection_OrderDoesNotDependOnTheInputOrder()
    {
        var descriptor = new DiagnosticDescriptor("SG0001", "Title", "{0}", "Usage", DiagnosticSeverity.Warning, isEnabledByDefault: true);
        var settings = new SnapshotSettings();
        settings.AddRoslyn();

        string Serialize(params string[] messages)
        {
            var diagnostics = messages.Select(message => Diagnostic.Create(descriptor, Location.None, message)).ToArray();
            return Encoding.UTF8.GetString(Assert.Single(settings.Serializers.Serialize(SnapshotType.Default, diagnostics).Data).Data);
        }

        Assert.Equal(Serialize("zulu", "alpha", "mike"), Serialize("mike", "zulu", "alpha"));
    }

    [Fact]
    public void DiagnosticCollection_IsSortedByPositionBeforeId()
    {
        var tree = CSharpSyntaxTree.ParseText("class C { }", path: "Sample.cs");
        var later = new DiagnosticDescriptor("SG0001", "Title", "later", "Usage", DiagnosticSeverity.Warning, isEnabledByDefault: true);
        var earlier = new DiagnosticDescriptor("SG9999", "Title", "earlier", "Usage", DiagnosticSeverity.Warning, isEnabledByDefault: true);

        var diagnostics = new[]
        {
            Diagnostic.Create(later, Location.Create(tree, TextSpan.FromBounds(6, 7))),
            Diagnostic.Create(earlier, Location.Create(tree, TextSpan.FromBounds(0, 5))),
        };

        var settings = new SnapshotSettings();
        settings.AddRoslyn();

        var snapshot = Encoding.UTF8.GetString(Assert.Single(settings.Serializers.Serialize(SnapshotType.Default, diagnostics).Data).Data);

        // The lower Id sorts second because position wins over Id.
        Assert.StartsWith("Sample.cs(1,1): warning SG9999: earlier", snapshot);
    }
}
