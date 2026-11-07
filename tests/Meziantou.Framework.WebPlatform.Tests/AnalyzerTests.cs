using System.Globalization;
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
            """, "HTMLIDL001");

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
            """, "HTMLIDL002");

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
            """, "HTMLIDL003");

        Assert.Equal("HTMLIDL003", Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task MissingSeeAlso_IsReported()
    {
        var diagnostics = await GetDiagnostics("""
            using Meziantou.Framework.WebPlatform;
            /// <summary>Foo</summary>
            [IdlInterface("Foo", "https://example.com/#foo")]
            public class Foo
            {
                /// <summary>Bar</summary>
                /// <seealso href="https://example.com/#other"/>
                [IdlMember("bar", "https://example.com/#bar")]
                public int Bar { get; }

                [IdlMember("baz", "https://example.com/#baz")]
                public int Baz { get; }
            }
            """);

        Assert.Equal(["HTMLIDL005", "HTMLIDL005", "HTMLIDL005"], diagnostics.Select(d => d.Id));
        Assert.Equal(
            [
                "The XML documentation of 'Bar' must contain <seealso href=\"https://example.com/#bar\"/>",
                "The XML documentation of 'Baz' must contain <seealso href=\"https://example.com/#baz\"/>",
                "The XML documentation of 'Foo' must contain <seealso href=\"https://example.com/#foo\"/>",
            ],
            diagnostics.Select(d => d.GetMessage(CultureInfo.InvariantCulture)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task MissingSeeAlso_SeveralIdlMembers_ReportsMissingUrl()
    {
        var diagnostics = await GetDiagnostics("""
            using Meziantou.Framework.WebPlatform;
            /// <summary>Foo</summary>
            /// <seealso href="https://example.com/#foo"/>
            [IdlInterface("Foo", "https://example.com/#foo")]
            public class Foo
            {
                /// <summary>Bar</summary>
                /// <seealso href="https://example.com/#bar"/>
                [IdlMember("bar", "https://example.com/#bar")]
                [IdlMember("bar-dashed", "https://example.com/#bar-dashed")]
                public int Bar { get; }
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("HTMLIDL005", diagnostic.Id);
        Assert.Contains("https://example.com/#bar-dashed", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SeeAlso_IsNotReported()
    {
        var diagnostics = await GetDiagnostics("""
            using Meziantou.Framework.WebPlatform;
            /// <summary>Foo</summary>
            /// <seealso href="https://example.com/#foo">The specification</seealso>
            [IdlInterface("Foo", "https://example.com/#foo")]
            public class Foo
            {
                /// <summary>Bar</summary>
                /// <seealso href="https://example.com/#bar"/>
                /// <seealso href="https://example.com/#unrelated"/>
                [IdlMember("bar", "https://example.com/#bar")]
                public int Bar { get; }

                /// <inheritdoc/>
                /// <seealso href="https://example.com/#tostring"/>
                [IdlMember("toString", "https://example.com/#tostring")]
                public override string ToString() => "";
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task MissingSeeAlso_DocumentationModeNone_IsNotReported()
    {
        var diagnostics = await GetDiagnostics("""
            using Meziantou.Framework.WebPlatform;
            [IdlInterface("Foo", "https://example.com/#foo")]
            public class Foo
            {
                [IdlMember("bar", "https://example.com/#bar")]
                public int Bar { get; }
            }
            """, documentationMode: DocumentationMode.None);

        Assert.Empty(diagnostics);
    }

    private static async Task<Diagnostic[]> GetDiagnostics(string source, string? diagnosticId = null, DocumentationMode documentationMode = DocumentationMode.Parse)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(IdlMemberAttribute).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(documentationMode: documentationMode), cancellationToken: TestContext.Current.CancellationToken)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Empty(errors);

        var withAnalyzers = compilation.WithAnalyzers([new IdlAttributeAnalyzer()]);
        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
        return [.. diagnostics.Where(d => diagnosticId is null || d.Id == diagnosticId)];
    }
}
