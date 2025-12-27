using Microsoft.Extensions.Logging;
using Xunit;

#pragma warning disable IDE1006 // Naming Styles
namespace Meziantou.Extensions.Logging.Xunit.v3;
#pragma warning restore IDE1006 // Naming Styles

internal sealed class XUnitLogger<T> : XUnitLogger, ILogger<T>
{
    public XUnitLogger(ITestOutputHelper? testOutputHelper, LoggerExternalScopeProvider scopeProvider)
        : this(testOutputHelper, scopeProvider, options: null)
    {
    }

    public XUnitLogger(ITestOutputHelper? testOutputHelper, LoggerExternalScopeProvider scopeProvider, XUnitLoggerOptions? options)
        : base(testOutputHelper, scopeProvider, GetCategoryName(), options)
    {
    }

    private static string GetCategoryName() => TypeNameHelper.GetTypeDisplayName(typeof(T), includeGenericParameters: false, nestedTypeDelimiter: '.');
}
