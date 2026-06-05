using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Meziantou.Framework.Tests;

public abstract class FullPathAnalyzerTestBase
{
    protected static CSharpAnalyzerTest<TAnalyzer, DefaultVerifier> CreateAnalyzerTest<TAnalyzer>(string source)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
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
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
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
