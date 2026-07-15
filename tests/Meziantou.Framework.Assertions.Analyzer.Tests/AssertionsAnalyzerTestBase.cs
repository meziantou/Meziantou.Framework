using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Meziantou.Framework.Tests;

public abstract class AssertionsAnalyzerTestBase
{
    private static readonly ReferenceAssemblies Net11 = new ReferenceAssemblies("net11.0", new PackageIdentity("Microsoft.NETCore.App.Ref", "11.0.0-preview.6.26359.118"), Path.Combine("ref", "net11.0"));

    protected static CSharpAnalyzerTest<TAnalyzer, DefaultVerifier> CreateAnalyzerTest<TAnalyzer>(string source, bool addAssertionsReference = true)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Net11,
        };
        if (addAssertionsReference)
        {
            test.TestState.AdditionalReferences.Add(GetAssertionsMetadataReference());
        }

        return test;
    }

    protected static CSharpCodeFixTest<TAnalyzer, TCodeFixProvider, DefaultVerifier> CreateCodeFixTest<TAnalyzer, TCodeFixProvider>(string source, string fixedSource, bool addAssertionsReference = true)
        where TAnalyzer : DiagnosticAnalyzer, new()
        where TCodeFixProvider : CodeFixProvider, new()
    {
        var test = new CSharpCodeFixTest<TAnalyzer, TCodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = Net11,
        };
        if (addAssertionsReference)
        {
            test.TestState.AdditionalReferences.Add(GetAssertionsMetadataReference());
        }

        return test;
    }

    private static PortableExecutableReference GetAssertionsMetadataReference()
    {
        var assertionsAssembly = Assembly.Load("Meziantou.Framework.Assertions");
        return MetadataReference.CreateFromFile(assertionsAssembly.Location);
    }
}
