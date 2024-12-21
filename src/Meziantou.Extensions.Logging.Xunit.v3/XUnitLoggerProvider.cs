using Microsoft.Extensions.Logging;
using Xunit;

#pragma warning disable IDE1006 // Naming Styles
namespace Meziantou.Extensions.Logging.Xunit.v3;
#pragma warning restore IDE1006 // Naming Styles

public sealed class XUnitLoggerProvider : ILoggerProvider
{
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly XUnitLoggerOptions _options;
    private readonly LoggerExternalScopeProvider _scopeProvider = new();

    public XUnitLoggerProvider(ITestOutputHelper testOutputHelper)
        : this(testOutputHelper, options: null)
    {
    }

    public XUnitLoggerProvider(ITestOutputHelper testOutputHelper, bool appendScope)
        : this(testOutputHelper, new XUnitLoggerOptions { IncludeScopes = appendScope })
    {
    }

    public XUnitLoggerProvider(ITestOutputHelper testOutputHelper, XUnitLoggerOptions? options)
    {
        _testOutputHelper = testOutputHelper;
        _options = options ?? new XUnitLoggerOptions();
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new XUnitLogger(_testOutputHelper, _scopeProvider, categoryName, _options);
    }

    public void Dispose()
    {
    }
}
