using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Meziantou.Framework.Tests;

public abstract class FullPathAnalyzerTestBase
{
    private static readonly ReferenceAssemblies Net11 = new ReferenceAssemblies("net11.0", new PackageIdentity("Microsoft.NETCore.App.Ref", "11.0.0-preview.6.26359.118"), Path.Combine("ref", "net11.0"));

    protected static CSharpAnalyzerTest<TAnalyzer, DefaultVerifier> CreateAnalyzerTest<TAnalyzer>(string source)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Net11,
        };
        test.TestState.AdditionalReferences.Add(GetFullPathMetadataReference());
        return test;
    }

    protected static CSharpCodeFixTest<TAnalyzer, TCodeFixProvider, DefaultVerifier> CreateCodeFixTest<TAnalyzer, TCodeFixProvider>(string source, string fixedSource)
        where TAnalyzer : DiagnosticAnalyzer, new()
        where TCodeFixProvider : CodeFixProvider, new()
    {
        var test = new CSharpCodeFixTest<TAnalyzer, TCodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = Net11,
        };
        test.TestState.AdditionalReferences.Add(GetFullPathMetadataReference());
        return test;
    }

    private static PortableExecutableReference GetFullPathMetadataReference()
    {
        var fullPathAssembly = Assembly.Load("Meziantou.Framework.FullPath");
        return MetadataReference.CreateFromFile(fullPathAssembly.Location);
    }
}
