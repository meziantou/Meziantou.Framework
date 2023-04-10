using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Meziantou.Extensions.Logging.Xunit;

public sealed class XUnitLoggerProvider : ILoggerProvider
{
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly XUnitLoggerOptions _options;
    private readonly LoggerExternalScopeProvider _scopeProvider = new();

    public XUnitLoggerProvider(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    public XUnitLoggerProvider(ITestOutputHelper testOutputHelper, bool appendScope)
        : this(testOutputHelper, new XUnitLoggerOptions { IncludeScopes = appendScope })
    {
    }

    public XUnitLoggerProvider(ITestOutputHelper testOutputHelper, XUnitLoggerOptions options)
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
