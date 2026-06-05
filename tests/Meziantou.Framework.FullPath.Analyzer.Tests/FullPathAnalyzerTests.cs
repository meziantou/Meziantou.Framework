using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using FullPathAnalyzerType = Meziantou.Framework.Analyzers.FullPath.FullPathAnalyzer;
using FullPathCodeFixProviderType = Meziantou.Framework.Analyzers.FullPath.FullPathCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class FullPathAnalyzerTests
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_ForPathGetFullPathOnFullPath()
    {
        var source = """
            using System.IO;
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static void M(FullPath value)
                    {
                        _ = {|MFFP0001:Path.GetFullPath(value)|};
                    }
                }
            }
            """;

        await CreateAnalyzerTest(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForPathGetFullPathOnString()
    {
        var source = """
            using System.IO;

            namespace Sample
            {
                public static class TestClass
                {
                    public static void M(string value)
                    {
                        _ = Path.GetFullPath(value);
                    }
                }
            }
            """;

        await CreateAnalyzerTest(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task CodeFix_ReplacesPathGetFullPathCall()
    {
        var source = """
            using System.IO;
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static void M(FullPath value)
                    {
                        _ = {|MFFP0001:Path.GetFullPath(value)|};
                    }
                }
            }
            """;

        var fixedSource = """
            using System.IO;
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static void M(FullPath value)
                    {
                        _ = value;
                    }
                }
            }
            """;

        await CreateCodeFixTest(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_ForFullPathDivisionWithFullPathRight()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static FullPath M(FullPath left, FullPath right)
                    {
                        return {|MFFP0002:left / right|};
                    }
                }
            }
            """;

        await CreateAnalyzerTest(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForFullPathDivisionWithStringRight()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static FullPath M(FullPath left)
                    {
                        return left / "child";
                    }
                }
            }
            """;

        await CreateAnalyzerTest(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task CodeFix_ReplacesFullPathDivisionWithRightOperand()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static FullPath M(FullPath left, FullPath right)
                    {
                        return {|MFFP0002:left / right|};
                    }
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static FullPath M(FullPath left, FullPath right)
                    {
                        return right;
                    }
                }
            }
            """;

        await CreateCodeFixTest(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    private static CSharpAnalyzerTest<FullPathAnalyzerType, DefaultVerifier> CreateAnalyzerTest(string source)
    {
        var test = new CSharpAnalyzerTest<FullPathAnalyzerType, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.TestState.AdditionalReferences.Add(GetFullPathMetadataReference());
        return test;
    }

    private static CSharpCodeFixTest<FullPathAnalyzerType, FullPathCodeFixProviderType, DefaultVerifier> CreateCodeFixTest(string source, string fixedSource)
    {
        var test = new CSharpCodeFixTest<FullPathAnalyzerType, FullPathCodeFixProviderType, DefaultVerifier>
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
