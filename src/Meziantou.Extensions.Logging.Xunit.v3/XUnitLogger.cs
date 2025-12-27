using Microsoft.Extensions.Logging;
using Xunit;

#pragma warning disable IDE1006 // Naming Styles
namespace Meziantou.Extensions.Logging.Xunit.v3;
#pragma warning restore IDE1006 // Naming Styles

/// <summary>Provides an implementation of <see cref="ILogger"/> that writes logs to xUnit.net's <see cref="ITestOutputHelper"/>.</summary>
/// <example>
/// <code>
/// public class MyTests(ITestOutputHelper testOutputHelper)
/// {
///     [Fact]
///     public void MyTest()
///     {
///         var logger = XUnitLogger.CreateLogger(testOutputHelper);
///         logger.LogInformation("This message will appear in the test output");
///     }
/// }
/// </code>
/// </example>
public class XUnitLogger : ILogger
{
    private readonly ITestOutputHelper? _testOutputHelper;
    private readonly string? _categoryName;
    private readonly XUnitLoggerOptions _options;
    private readonly LoggerExternalScopeProvider _scopeProvider;

    /// <summary>Creates a new logger instance without a test output helper.</summary>
    /// <returns>A new <see cref="ILogger"/> instance.</returns>
    public static ILogger CreateLogger() => CreateLogger(testOutputHelper: null);

    /// <summary>Creates a new logger instance with the specified test output helper.</summary>
    /// <param name="testOutputHelper">The xUnit.net test output helper.</param>
    /// <returns>A new <see cref="ILogger"/> instance.</returns>
    public static ILogger CreateLogger(ITestOutputHelper? testOutputHelper) => CreateLogger(testOutputHelper, options: null);

    /// <summary>Creates a new logger instance with the specified test output helper.</summary>
    /// <param name="testOutputHelper">The xUnit.net test output helper.</param>
    /// <param name="options">The logger options.</param>
    /// <returns>A new <see cref="ILogger"/> instance.</returns>
    public static ILogger CreateLogger(ITestOutputHelper? testOutputHelper, XUnitLoggerOptions? options) => new XUnitLogger(testOutputHelper, new LoggerExternalScopeProvider(), "", options);

    /// <summary>Creates a new typed logger instance without a test output helper.</summary>
    /// <typeparam name="T">The type whose name is used for the logger category.</typeparam>
    /// <returns>A new <see cref="ILogger{T}"/> instance.</returns>
    public static ILogger<T> CreateLogger<T>() => new XUnitLogger<T>(testOutputHelper: null, new LoggerExternalScopeProvider());

    /// <summary>Creates a new typed logger instance with the specified test output helper.</summary>
    /// <typeparam name="T">The type whose name is used for the logger category.</typeparam>
    /// <param name="testOutputHelper">The xUnit.net test output helper.</param>
    /// <returns>A new <see cref="ILogger{T}"/> instance.</returns>
    public static ILogger<T> CreateLogger<T>(ITestOutputHelper? testOutputHelper) => CreateLogger<T>(testOutputHelper, options: null);

    /// <summary>Creates a new typed logger instance with the specified test output helper.</summary>
    /// <typeparam name="T">The type whose name is used for the logger category.</typeparam>
    /// <param name="testOutputHelper">The xUnit.net test output helper.</param>
    /// <param name="options">The logger options [optional].</param>
    /// <returns>A new <see cref="ILogger{T}"/> instance.</returns>
    public static ILogger<T> CreateLogger<T>(ITestOutputHelper? testOutputHelper, XUnitLoggerOptions? options) => new XUnitLogger<T>(testOutputHelper, new LoggerExternalScopeProvider(), options);

    /// <summary>Initializes a new instance of the <see cref="XUnitLogger"/> class.</summary>
    /// <param name="testOutputHelper">The xUnit.net test output helper.</param>
    /// <param name="scopeProvider">The scope provider.</param>
    /// <param name="categoryName">The category name for messages produced by the logger.</param>
    public XUnitLogger(ITestOutputHelper? testOutputHelper, LoggerExternalScopeProvider scopeProvider, string? categoryName)
        : this(testOutputHelper, scopeProvider, categoryName, appendScope: true)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="XUnitLogger"/> class.</summary>
    /// <param name="testOutputHelper">The xUnit.net test output helper.</param>
    /// <param name="scopeProvider">The scope provider.</param>
    /// <param name="categoryName">The category name for messages produced by the logger.</param>
    /// <param name="appendScope">Whether to include scopes when logging.</param>
    public XUnitLogger(ITestOutputHelper? testOutputHelper, LoggerExternalScopeProvider scopeProvider, string? categoryName, bool appendScope)
        : this(testOutputHelper, scopeProvider, categoryName, options: new XUnitLoggerOptions { IncludeScopes = appendScope })
    {
    }

    /// <summary>Initializes a new instance of the <see cref="XUnitLogger"/> class.</summary>
    /// <param name="testOutputHelper">The xUnit.net test output helper.</param>
    /// <param name="scopeProvider">The scope provider.</param>
    /// <param name="categoryName">The category name for messages produced by the logger.</param>
    /// <param name="options">The logger options.</param>
    public XUnitLogger(ITestOutputHelper? testOutputHelper, LoggerExternalScopeProvider scopeProvider, string? categoryName, XUnitLoggerOptions? options)
    {
        _testOutputHelper = testOutputHelper;
        _scopeProvider = scopeProvider;
        _categoryName = categoryName;
        _options = options ?? new();
    }

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => logLevel is not LogLevel.None;

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _scopeProvider.Push(state);

    /// <inheritdoc/>
    [SuppressMessage("ApiDesign", "RS0030:Do not use banned APIs")]
    [SuppressMessage("Usage", "MA0011:IFormatProvider is missing")]
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var testOutputHelper = _testOutputHelper ?? TestContext.Current.TestOutputHelper;
        if (testOutputHelper is null)
            return;

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
            testOutputHelper.WriteLine(sb.ToString());
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
