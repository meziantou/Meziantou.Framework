using Microsoft.Extensions.Logging;
using Xunit;

#pragma warning disable IDE1006 // Naming Styles
namespace Meziantou.Extensions.Logging.Xunit.v3;
#pragma warning restore IDE1006

/// <summary>Provides an implementation of <see cref="ILoggerProvider"/> that creates loggers writing to xUnit.net's <see cref="ITestOutputHelper"/>.</summary>
/// <example>
/// <code>
/// using Microsoft.Extensions.Hosting;
/// using Microsoft.Extensions.Logging;
///
/// using var provider = new XUnitLoggerProvider(testOutputHelper);
/// var host = new HostBuilder()
///     .ConfigureLogging(builder =>
///     {
///         builder.Services.AddSingleton&lt;ILoggerProvider&gt;(provider);
///     })
///     .Build();
/// </code>
/// </example>
public sealed class XUnitLoggerProvider : ILoggerProvider
{
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly XUnitLoggerOptions _options;
    private readonly LoggerExternalScopeProvider _scopeProvider = new();

    /// <summary>Initializes a new instance of the <see cref="XUnitLoggerProvider"/> class.</summary>
    public XUnitLoggerProvider()
        : this(testOutputHelper: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="XUnitLoggerProvider"/> class with the specified test output helper.</summary>
    /// <param name="testOutputHelper">The xUnit.net test output helper.</param>
    public XUnitLoggerProvider(ITestOutputHelper? testOutputHelper)
        : this(testOutputHelper, options: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="XUnitLoggerProvider"/> class with the specified test output helper and scope behavior.</summary>
    /// <param name="testOutputHelper">The xUnit.net test output helper.</param>
    /// <param name="appendScope">Whether to include scopes when logging.</param>
    public XUnitLoggerProvider(ITestOutputHelper? testOutputHelper, bool appendScope)
        : this(testOutputHelper, new XUnitLoggerOptions { IncludeScopes = appendScope })
    {
    }

    /// <summary>Initializes a new instance of the <see cref="XUnitLoggerProvider"/> class with the specified options.</summary>
    /// <param name="options">The logger options.</param>
    public XUnitLoggerProvider(XUnitLoggerOptions? options)
        : this(testOutputHelper: null, options)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="XUnitLoggerProvider"/> class with the specified test output helper and options.</summary>
    /// <param name="testOutputHelper">The xUnit.net test output helper.</param>
    /// <param name="options">The logger options.</param>
    public XUnitLoggerProvider(ITestOutputHelper? testOutputHelper, XUnitLoggerOptions? options)
    {
        _testOutputHelper = testOutputHelper;
        _options = options ?? new XUnitLoggerOptions();
    }

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName)
    {
        return new XUnitLogger(_testOutputHelper, _scopeProvider, categoryName, _options);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}
