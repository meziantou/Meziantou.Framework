using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Meziantou.Extensions.Logging.Xunit;

/// <summary>An <see cref="ILogger"/> implementation that writes logs to xUnit's <see cref="ITestOutputHelper"/>.</summary>
/// <example>
/// <code>
/// public class MyTests(ITestOutputHelper output)
/// {
///     [Fact]
///     public void MyTest()
///     {
///         var logger = XUnitLogger.CreateLogger(output);
///         logger.LogInformation("This message will appear in the test output");
///     }
/// }
/// </code>
/// </example>
public class XUnitLogger : ILogger
{
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly string? _categoryName;
    private readonly XUnitLoggerOptions _options;
    private readonly LoggerExternalScopeProvider _scopeProvider;

    /// <summary>Creates a non-generic logger that writes to the specified <see cref="ITestOutputHelper"/>.</summary>
    /// <param name="testOutputHelper">The xUnit test output helper to write logs to.</param>
    /// <returns>An <see cref="ILogger"/> instance.</returns>
    public static ILogger CreateLogger(ITestOutputHelper testOutputHelper) => new XUnitLogger(testOutputHelper, new LoggerExternalScopeProvider(), "");

    /// <summary>Creates a generic logger for type <typeparamref name="T"/> that writes to the specified <see cref="ITestOutputHelper"/>.</summary>
    /// <typeparam name="T">The type whose name will be used as the logger category.</typeparam>
    /// <param name="testOutputHelper">The xUnit test output helper to write logs to.</param>
    /// <returns>An <see cref="ILogger{T}"/> instance.</returns>
    public static ILogger<T> CreateLogger<T>(ITestOutputHelper testOutputHelper) => new XUnitLogger<T>(testOutputHelper, new LoggerExternalScopeProvider());

    /// <summary>Initializes a new instance of the <see cref="XUnitLogger"/> class.</summary>
    /// <param name="testOutputHelper">The xUnit test output helper to write logs to.</param>
    /// <param name="scopeProvider">The external scope provider for managing logging scopes.</param>
    /// <param name="categoryName">The category name for the logger.</param>
    public XUnitLogger(ITestOutputHelper testOutputHelper, LoggerExternalScopeProvider scopeProvider, string? categoryName)
        : this(testOutputHelper, scopeProvider, categoryName, appendScope: true)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="XUnitLogger"/> class.</summary>
    /// <param name="testOutputHelper">The xUnit test output helper to write logs to.</param>
    /// <param name="scopeProvider">The external scope provider for managing logging scopes.</param>
    /// <param name="categoryName">The category name for the logger.</param>
    /// <param name="appendScope">A value indicating whether to include scopes in the log output.</param>
    public XUnitLogger(ITestOutputHelper testOutputHelper, LoggerExternalScopeProvider scopeProvider, string? categoryName, bool appendScope)
        : this(testOutputHelper, scopeProvider, categoryName, options: new XUnitLoggerOptions { IncludeScopes = appendScope })
    {
    }

    /// <summary>Initializes a new instance of the <see cref="XUnitLogger"/> class.</summary>
    /// <param name="testOutputHelper">The xUnit test output helper to write logs to.</param>
    /// <param name="scopeProvider">The external scope provider for managing logging scopes.</param>
    /// <param name="categoryName">The category name for the logger.</param>
    /// <param name="options">The logger options.</param>
    public XUnitLogger(ITestOutputHelper testOutputHelper, LoggerExternalScopeProvider scopeProvider, string? categoryName, XUnitLoggerOptions? options)
    {
        _testOutputHelper = testOutputHelper;
        _scopeProvider = scopeProvider;
        _categoryName = categoryName;
        _options = options ?? new();
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _scopeProvider.Push(state);

    /// <inheritdoc />
    [SuppressMessage("ApiDesign", "RS0030:Do not use banned APIs")]
    [SuppressMessage("Usage", "MA0011:IFormatProvider is missing")]
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var sb = new StringBuilder();

        if (_options.TimestampFormat is not null)
        {
            var now = _options.UseUtcTimestamp ? DateTimeOffset.UtcNow : DateTimeOffset.Now;
            var timestamp = now.ToString(_options.TimestampFormat);
            sb.Append(timestamp).Append(' ');
        }

        if (_options.IncludeLogLevel)
        {
            sb.Append(GetLogLevelString(logLevel)).Append(' ');
        }

        if (_options.IncludeCategory)
        {
            sb.Append('[').Append(_categoryName).Append("] ");
        }

        sb.Append(formatter(state, exception));

        if (exception is not null)
        {
            sb.Append('\n').Append(exception);
        }

        // Append scopes
        if (_options.IncludeScopes)
        {
            _scopeProvider.ForEachScope((scope, state) =>
            {
                state.Append("\n => ");
                state.Append(scope);
            }, sb);
        }

        try
        {
            _testOutputHelper.WriteLine(sb.ToString());
        }
        catch
        {
            // This can happen when the test is not active
        }
    }

    private static string GetLogLevelString(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            _ => throw new ArgumentOutOfRangeException(nameof(logLevel)),
        };
    }
}
