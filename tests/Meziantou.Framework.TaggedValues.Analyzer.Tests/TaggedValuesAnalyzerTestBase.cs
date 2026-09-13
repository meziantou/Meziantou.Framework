using Meziantou.Framework.TaggedValues;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using ValueTagAnalyzerType = Meziantou.Framework.Analyzers.TaggedValues.ValueTagAnalyzer;

namespace Meziantou.Framework.Tests;

public abstract class TaggedValuesAnalyzerTestBase
{
    private const string Usings = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading.Tasks;
        using Meziantou.Framework.TaggedValues;

        """;

    private static readonly ReferenceAssemblies Net11 = new("net11.0", new PackageIdentity("Microsoft.NETCore.App.Ref", "11.0.0-rc.1.26425.128"), Path.Combine("ref", "net11.0"));

    protected static Task VerifyAsync(string source, bool inferTagsFromNames = false, params DiagnosticResult[] expected)
    {
        var test = CreateAnalyzerTest(source, inferTagsFromNames);
        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync(XunitCancellationToken);
    }

    protected static CSharpAnalyzerTest<ValueTagAnalyzerType, DefaultVerifier> CreateAnalyzerTest(string source, bool inferTagsFromNames = false)
    {
        var test = new CSharpAnalyzerTest<ValueTagAnalyzerType, DefaultVerifier>
        {
            TestCode = Usings + source,
            ReferenceAssemblies = Net11,
        };

        Configure(test, inferTagsFromNames);
        return test;
    }

    protected static Task VerifyCodeFixAsync<TCodeFixProvider>(string source, string fixedSource, bool inferTagsFromNames = false)
        where TCodeFixProvider : CodeFixProvider, new()
    {
        var test = new CSharpCodeFixTest<ValueTagAnalyzerType, TCodeFixProvider, DefaultVerifier>
        {
            TestCode = Usings + source,
            FixedCode = Usings + fixedSource,
            ReferenceAssemblies = Net11,
        };

        Configure(test, inferTagsFromNames);
        return test.RunAsync(XunitCancellationToken);
    }

    protected static DiagnosticResult Diagnostic(string id)
    {
        return new DiagnosticResult(id, DiagnosticSeverity.Warning);
    }

    /// <summary>
    /// Compiles <paramref name="source"/> into an in-memory assembly, so the analyzer reads its tags from metadata.
    /// </summary>
    protected static async Task<MetadataReference> CreateLibraryReferenceAsync(string source)
    {
        var frameworkReferences = await Net11.ResolveAsync(LanguageNames.CSharp, XunitCancellationToken);
        var compilation = CSharpCompilation.Create(
            "Library" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(Usings + source, new CSharpParseOptions(LanguageVersion.Preview), cancellationToken: XunitCancellationToken)],
            [.. frameworkReferences, GetTaggedValuesReference()],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream, cancellationToken: XunitCancellationToken);
        if (!result.Success)
            throw new InvalidOperationException("The library does not compile: " + string.Join(Environment.NewLine, result.Diagnostics));

        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static void Configure(AnalyzerTest<DefaultVerifier> test, bool inferTagsFromNames)
    {
        test.TestState.AdditionalReferences.Add(GetTaggedValuesReference());
        if (inferTagsFromNames)
        {
            test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", """
                root = true

                [*.cs]
                taggedvalues.infer_tags_from_names = true
                """));
        }
    }

    private static PortableExecutableReference GetTaggedValuesReference()
    {
        return MetadataReference.CreateFromFile(typeof(ValueTagAttribute).Assembly.Location);
    }
}
