using System.Collections.Immutable;
using Meziantou.Framework.TaggedValues;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using ValueTagAnalyzerType = Meziantou.Framework.TaggedValues.Analyzer.ValueTagAnalyzer;

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

    private static readonly ReferenceAssemblies Net11 = new("net11.0", new PackageIdentity("Microsoft.NETCore.App.Ref", "11.0.0-rc.3.26522.148"), Path.Combine("ref", "net11.0"));

    protected static Task VerifyAsync(string source, bool inferTagsFromNames = false, params DiagnosticResult[] expected)
    {
        var test = CreateAnalyzerTest(source, inferTagsFromNames);
        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync(XunitCancellationToken);
    }

    /// <summary>
    /// Verifies the diagnostics with <c>taggedvalues.strict = true</c>.
    /// </summary>
    protected static Task VerifyStrictAsync(string source, params DiagnosticResult[] expected)
    {
        var test = CreateAnalyzerTest(source, inferTagsFromNames: false, strict: true);
        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync(XunitCancellationToken);
    }

    protected static CSharpAnalyzerTest<ValueTagAnalyzerType, DefaultVerifier> CreateAnalyzerTest(string source, bool inferTagsFromNames = false, bool strict = false)
    {
        var test = new CSharpAnalyzerTest<ValueTagAnalyzerType, DefaultVerifier>
        {
            TestCode = Usings + source,
            ReferenceAssemblies = Net11,
        };

        Configure(test, inferTagsFromNames, strict);
        return test;
    }

    protected static Task VerifyCodeFixAsync<TCodeFixProvider>(string source, string fixedSource, bool inferTagsFromNames = false, bool strict = false)
        where TCodeFixProvider : CodeFixProvider, new()
    {
        var test = new CSharpCodeFixTest<ValueTagAnalyzerType, TCodeFixProvider, DefaultVerifier>
        {
            TestCode = Usings + source,
            FixedCode = Usings + fixedSource,
            ReferenceAssemblies = Net11,
        };

        Configure(test, inferTagsFromNames, strict);
        return test.RunAsync(XunitCancellationToken);
    }

    /// <summary>
    /// Verifies a code fix on several files. <paramref name="batchFixedSources"/> is the result of Fix All, when it differs from the
    /// result of the fixes applied one by one. <paramref name="fixOne"/> applies a single fix instead of fixing until no diagnostic remains.
    /// </summary>
    protected static Task VerifyCodeFixAsync<TCodeFixProvider>(string[] sources, string[] fixedSources, string[]? batchFixedSources = null, bool fixOne = false, bool strict = false)
        where TCodeFixProvider : CodeFixProvider, new()
    {
        var test = new CSharpCodeFixTest<ValueTagAnalyzerType, TCodeFixProvider, DefaultVerifier>
        {
            ReferenceAssemblies = Net11,
        };

        if (fixOne)
        {
            test.CodeFixTestBehaviors |= CodeFixTestBehaviors.FixOne;
        }

        foreach (var source in sources)
        {
            test.TestState.Sources.Add(Usings + source);
        }

        foreach (var source in fixedSources)
        {
            test.FixedState.Sources.Add(Usings + source);
        }

        foreach (var source in batchFixedSources ?? [])
        {
            test.BatchFixedState.Sources.Add(Usings + source);
        }

        // The diagnostics that remain after the fixes are declared by the markup of the fixed sources
        test.FixedState.MarkupHandling = MarkupMode.Allow;
        test.BatchFixedState.MarkupHandling = MarkupMode.Allow;

        Configure(test, inferTagsFromNames: false, strict);
        return test.RunAsync(XunitCancellationToken);
    }

    protected static DiagnosticResult Diagnostic(string id)
    {
        return new DiagnosticResult(id, DiagnosticSeverity.Warning);
    }

    /// <summary>
    /// Resolves the references of a compilation: the .NET reference assemblies and Meziantou.Framework.TaggedValues.
    /// </summary>
    protected static async Task<ImmutableArray<MetadataReference>> GetReferencesAsync()
    {
        var frameworkReferences = await Net11.ResolveAsync(LanguageNames.CSharp, XunitCancellationToken);
        return [.. frameworkReferences, GetTaggedValuesReference()];
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

    private static void Configure(AnalyzerTest<DefaultVerifier> test, bool inferTagsFromNames, bool strict)
    {
        test.TestState.AdditionalReferences.Add(GetTaggedValuesReference());
        if (inferTagsFromNames || strict)
        {
            test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", $"""
                root = true

                [*.cs]
                taggedvalues.infer_tags_from_names = {(inferTagsFromNames ? "true" : "false")}
                taggedvalues.strict = {(strict ? "true" : "false")}
                """));
        }
    }

    private static PortableExecutableReference GetTaggedValuesReference()
    {
        return MetadataReference.CreateFromFile(typeof(ValueTagAttribute).Assembly.Location);
    }
}
