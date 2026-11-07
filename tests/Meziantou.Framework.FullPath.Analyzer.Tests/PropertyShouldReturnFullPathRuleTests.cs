using PropertyShouldReturnFullPathAnalyzerType = Meziantou.Framework.Analyzers.FullPath.PropertyShouldReturnFullPathAnalyzer;

namespace Meziantou.Framework.Tests;

public sealed class PropertyShouldReturnFullPathRuleTests : FullPathAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_ForExpressionBodiedProperty()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public sealed class TestClass
                {
                    private readonly FullPath _path;

                    public string {|MFFP0012:Path|} => _path;
                }
            }
            """;

        await CreateAnalyzerTest<PropertyShouldReturnFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_WhenAllReturnsAreFullPathInGetter()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public sealed class TestClass
                {
                    private readonly FullPath _path;

                    public string {|MFFP0012:Path|}
                    {
                        get
                        {
                            if (_path.IsEmpty)
                                return FullPath.Empty;

                            return _path;
                        }
                    }
                }
            }
            """;

        await CreateAnalyzerTest<PropertyShouldReturnFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_ForFullPathValueAccess()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public sealed class TestClass
                {
                    private readonly FullPath _path;

                    public string {|MFFP0012:Path|} => _path.Value;
                }
            }
            """;

        await CreateAnalyzerTest<PropertyShouldReturnFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_WhenAnyReturnIsNotFullPath()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public sealed class TestClass
                {
                    private readonly FullPath _path;

                    public string Path
                    {
                        get
                        {
                            if (_path.IsEmpty)
                                return "";

                            return _path;
                        }
                    }
                }
            }
            """;

        await CreateAnalyzerTest<PropertyShouldReturnFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_WhenPropertyHasSetter()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public sealed class TestClass
                {
                    private FullPath _path;

                    public string Path
                    {
                        get => _path;
                        set => _path = FullPath.FromPath(value);
                    }
                }
            }
            """;

        await CreateAnalyzerTest<PropertyShouldReturnFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_WhenPropertyImplementsAnInterface()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public interface IHasPath
                {
                    string Path { get; }
                }

                public sealed class TestClass : IHasPath
                {
                    private readonly FullPath _path;

                    public string Path => _path;
                }
            }
            """;

        await CreateAnalyzerTest<PropertyShouldReturnFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_WhenPropertyOverridesABaseMember()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public abstract class BaseClass
                {
                    public abstract string Path { get; }
                }

                public sealed class TestClass : BaseClass
                {
                    private readonly FullPath _path;

                    public override string Path => _path;
                }
            }
            """;

        await CreateAnalyzerTest<PropertyShouldReturnFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
