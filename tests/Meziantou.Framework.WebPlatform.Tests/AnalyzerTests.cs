using System.Collections.Immutable;
using Meziantou.Framework.WebPlatform.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Meziantou.Framework.WebPlatform.Tests;

public sealed class AnalyzerTests
{
    [Fact]
    public async Task MissingIdlMember_IsReported()
    {
        var diagnostics = await GetDiagnostics("""
            using Meziantou.Framework.WebPlatform;
            [IdlInterface("Foo", "https://example.com/#foo")]
            public class Foo
            {
                [IdlMember("bar", "https://example.com/#bar")]
                public int Bar { get; }
                public int Baz { get; }
                [NotInIdl]
                public int Qux { get; }
                internal int Internal { get; }
                public override string ToString() => "";
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("HTMLIDL001", diagnostic.Id);
        Assert.Contains("Baz", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidSpecUrl_IsReported()
    {
        var diagnostics = await GetDiagnostics("""
            using Meziantou.Framework.WebPlatform;
            [IdlInterface("Foo", "http://example.com/")]
            public class Foo
            {
                [IdlMember("bar", "https://example.com/")]
                public int Bar { get; }
            }
            """);

        Assert.Equal(["HTMLIDL002", "HTMLIDL002"], diagnostics.Select(d => d.Id));
    }

    [Fact]
    public async Task IdlMemberOutsideIdlType_IsReported()
    {
        var diagnostics = await GetDiagnostics("""
            using Meziantou.Framework.WebPlatform;
            public class Foo
            {
                [IdlMember("bar", "https://example.com/#bar")]
                public int Bar { get; }
            }
            """);

        Assert.Equal("HTMLIDL003", Assert.Single(diagnostics).Id);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnostics(string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(IdlMemberAttribute).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Empty(errors);

        var withAnalyzers = compilation.WithAnalyzers([new IdlAttributeAnalyzer()]);
        return await withAnalyzers.GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
    }
}
