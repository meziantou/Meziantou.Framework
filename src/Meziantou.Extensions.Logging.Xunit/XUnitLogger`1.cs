using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Meziantou.Extensions.Logging.Xunit;

internal sealed class XUnitLogger<T> : XUnitLogger, ILogger<T>
{
    public XUnitLogger(ITestOutputHelper testOutputHelper, LoggerExternalScopeProvider scopeProvider)
        : this(testOutputHelper, scopeProvider, options: null)
    {
    }

    public XUnitLogger(ITestOutputHelper testOutputHelper, LoggerExternalScopeProvider scopeProvider, XUnitLoggerOptions? options)
        : base(testOutputHelper, scopeProvider, GetCategoryName(), options)
    {
    }

    private static string GetCategoryName() => TypeNameHelper.GetTypeDisplayName(typeof(T), includeGenericParameters: false, nestedTypeDelimiter: '.');
}
