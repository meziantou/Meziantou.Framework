using PathChangeExtensionWithFullPathAnalyzerType = Meziantou.Framework.Analyzers.FullPath.PathChangeExtensionWithFullPathAnalyzer;
using PathChangeExtensionWithFullPathCodeFixProviderType = Meziantou.Framework.Analyzers.FullPath.PathChangeExtensionWithFullPathCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class PathChangeExtensionWithFullPathRuleTests : FullPathAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForPathChangeExtensionOnFullPath()
    {
        var source = """
            using System.IO;
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static string M(FullPath fullPath, string extension)
                    {
                        return {|MFFP0009:Path.ChangeExtension(fullPath, extension)|};
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
                    public static string M(FullPath fullPath, string extension)
                    {
                        return fullPath.ChangeExtension(extension);
                    }
                }
            }
            """;

        await CreateCodeFixTest<PathChangeExtensionWithFullPathAnalyzerType, PathChangeExtensionWithFullPathCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForPathChangeExtensionOnString()
    {
        var source = """
            using System.IO;

            namespace Sample
            {
                public static class TestClass
                {
                    public static string M(string path, string extension)
                    {
                        return Path.ChangeExtension(path, extension);
                    }
                }
            }
            """;

        await CreateAnalyzerTest<PathChangeExtensionWithFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
