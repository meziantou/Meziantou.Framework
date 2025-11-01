using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Meziantou.Extensions.Logging.Xunit;

/// <summary>An <see cref="ILoggerProvider"/> implementation that creates loggers that write to xUnit's <see cref="ITestOutputHelper"/>.</summary>
/// <example>
/// <code>
/// public class MyTests(ITestOutputHelper output)
/// {
///     [Fact]
///     public void MyTest()
///     {
///         using var provider = new XUnitLoggerProvider(output);
///         var loggerFactory = LoggerFactory.Create(builder =>
///         {
///             builder.Services.AddSingleton{ILoggerProvider}(provider);
///         });
///         var logger = loggerFactory.CreateLogger{MyTests}();
///         logger.LogInformation("This message will appear in the test output");
///     }
/// }
/// </code>
/// </example>
public sealed class XUnitLoggerProvider : ILoggerProvider
{
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly XUnitLoggerOptions _options;
    private readonly LoggerExternalScopeProvider _scopeProvider = new();

    /// <summary>Initializes a new instance of the <see cref="XUnitLoggerProvider"/> class.</summary>
    /// <param name="testOutputHelper">The xUnit test output helper to write logs to.</param>
    public XUnitLoggerProvider(ITestOutputHelper testOutputHelper)
        : this(testOutputHelper, options: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="XUnitLoggerProvider"/> class.</summary>
    /// <param name="testOutputHelper">The xUnit test output helper to write logs to.</param>
    /// <param name="appendScope">A value indicating whether to include scopes in the log output.</param>
    public XUnitLoggerProvider(ITestOutputHelper testOutputHelper, bool appendScope)
        : this(testOutputHelper, new XUnitLoggerOptions { IncludeScopes = appendScope })
    {
    }

    /// <summary>Initializes a new instance of the <see cref="XUnitLoggerProvider"/> class.</summary>
    /// <param name="testOutputHelper">The xUnit test output helper to write logs to.</param>
    /// <param name="options">The logger options.</param>
    public XUnitLoggerProvider(ITestOutputHelper testOutputHelper, XUnitLoggerOptions? options)
    {
        _testOutputHelper = testOutputHelper;
        _options = options ?? new XUnitLoggerOptions();
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName)
    {
        return new XUnitLogger(_testOutputHelper, _scopeProvider, categoryName, _options);
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
