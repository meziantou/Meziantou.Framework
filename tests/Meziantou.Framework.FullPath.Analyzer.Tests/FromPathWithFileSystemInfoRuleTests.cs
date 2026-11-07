using FromPathWithFileSystemInfoAnalyzerType = Meziantou.Framework.Analyzers.FullPath.FromPathWithFileSystemInfoAnalyzer;
using FromPathWithFileSystemInfoCodeFixProviderType = Meziantou.Framework.Analyzers.FullPath.FromPathWithFileSystemInfoCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class FromPathWithFileSystemInfoRuleTests : FullPathAnalyzerTestBase
{
    [Theory]
    [InlineData("FileInfo")]
    [InlineData("DirectoryInfo")]
    [InlineData("FileSystemInfo")]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForFromPathWithFullName(string typeName)
    {
        var source = $$"""
            using System.IO;
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static FullPath M({{typeName}} info)
                    {
                        return {|MFFP0020:FullPath.FromPath(info.FullName)|};
                    }
                }
            }
            """;

        var fixedSource = $$"""
            using System.IO;
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static FullPath M({{typeName}} info)
                    {
                        return FullPath.FromFileSystemInfo(info);
                    }
                }
            }
            """;

        await CreateCodeFixTest<FromPathWithFileSystemInfoAnalyzerType, FromPathWithFileSystemInfoCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForFromPathWithString()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static FullPath M(string path)
                    {
                        return FullPath.FromPath(path);
                    }
                }
            }
            """;

        await CreateAnalyzerTest<FromPathWithFileSystemInfoAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForFullNameOfAnotherType()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public sealed class Descriptor
                {
                    public string FullName => "";
                }

                public static class TestClass
                {
                    public static FullPath M(Descriptor descriptor)
                    {
                        return FullPath.FromPath(descriptor.FullName);
                    }
                }
            }
            """;

        await CreateAnalyzerTest<FromPathWithFileSystemInfoAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
