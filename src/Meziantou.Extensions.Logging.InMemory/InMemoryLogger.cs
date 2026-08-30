using Microsoft.Extensions.Logging;

namespace Meziantou.Extensions.Logging.InMemory;

/// <summary>Provides an in-memory logger implementation that captures log entries for testing purposes.</summary>
/// <example>
/// <code>
/// // Create a logger directly
/// var logger = InMemoryLogger.CreateLogger("MyCategory");
/// logger.LogInformation("Test message");
/// 
/// // Access captured logs
/// Assert.Single(logger.Logs.Informations);
/// Assert.Contains(logger.Logs, log => log.Message == "Test message");
/// </code>
/// </example>
public class InMemoryLogger : IInMemoryLogger
{
    // Beyond this capacity, the thread-local buffer is dropped instead of being reused,
    // so an unusually deep scope stack doesn't keep a large array alive on the thread forever.
    private const int MaxScopeBufferCapacity = 64;

    [ThreadStatic]
    private static List<object?>? s_scopeBuffer;

    private readonly string? _category;
    private readonly IExternalScopeProvider _scopeProvider;
    private readonly TimeProvider _timeProvider;

    /// <summary>Gets the collection of log entries captured by this logger.</summary>
    public InMemoryLogCollection Logs { get; }

    /// <summary>Creates a new in-memory logger instance with the specified category name.</summary>
    /// <param name="category">The logger category name.</param>
    /// <param name="logs">The log collection to use, or <see langword="null"/> to create a new collection.</param>
    /// <param name="scopeProvider">The external scope provider to use, or <see langword="null"/> to create a new instance.</param>
    /// <param name="timeProvider">The time provider to use for timestamping log entries, or <see langword="null"/> to use the system time provider.</param>
    /// <returns>A new instance of <see cref="IInMemoryLogger"/>.</returns>
    public static IInMemoryLogger CreateLogger(string category, InMemoryLogCollection? logs = null, IExternalScopeProvider? scopeProvider = null, TimeProvider? timeProvider = null)
    {
        logs ??= [];
        scopeProvider ??= new LoggerExternalScopeProvider();
        timeProvider ??= TimeProvider.System;
        return new InMemoryLogger(category, logs, scopeProvider, timeProvider);
    }

    /// <summary>Creates a new generic in-memory logger instance.</summary>
    /// <typeparam name="T">The type whose name is used for the logger category name.</typeparam>
    /// <param name="logs">The log collection to use, or <see langword="null"/> to create a new collection.</param>
    /// <param name="scopeProvider">The external scope provider to use, or <see langword="null"/> to create a new instance.</param>
    /// <param name="timeProvider">The time provider to use for timestamping log entries, or <see langword="null"/> to use the system time provider.</param>
    /// <returns>A new instance of <see cref="IInMemoryLogger{T}"/>.</returns>
    public static IInMemoryLogger<T> CreateLogger<T>(InMemoryLogCollection? logs = null, IExternalScopeProvider? scopeProvider = null, TimeProvider? timeProvider = null)
    {
        logs ??= [];
        scopeProvider ??= new LoggerExternalScopeProvider();
        timeProvider ??= TimeProvider.System;
        return new InMemoryLogger<T>(logs, scopeProvider, timeProvider);
    }

    internal InMemoryLogger(string category, InMemoryLogCollection logs, IExternalScopeProvider scopeProvider, TimeProvider timeProvider)
    {
        _category = category;
        Logs = logs;
        _scopeProvider = scopeProvider;
        _timeProvider = timeProvider;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return _scopeProvider.Push(state);
    }

    public bool IsEnabled(LogLevel logLevel) => logLevel is not LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var now = _timeProvider.GetUtcNow();
        var scopes = GetScopes();
        Logs.Add(new InMemoryLogEntry(now, _category, logLevel, eventId, scopes, state, exception, formatter(state, exception)));
    }

    private object?[] GetScopes()
    {
        // Collect the scopes in a thread-local buffer, so no allocation occurs when there is no scope,
        // and a single right-sized array is allocated otherwise.
        var buffer = s_scopeBuffer ??= [];
        try
        {
            _scopeProvider.ForEachScope((current, scopes) => scopes.Add(current), buffer);
            return buffer.Count is 0 ? [] : buffer.ToArray();
        }
        finally
        {
            if (buffer.Capacity > MaxScopeBufferCapacity)
            {
                s_scopeBuffer = null;
            }
            else
            {
                buffer.Clear();
            }
        }
    }
}
