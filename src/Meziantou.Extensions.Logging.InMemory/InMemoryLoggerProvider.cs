using Microsoft.Extensions.Logging;

namespace Meziantou.Extensions.Logging.InMemory;

/// <summary>Provides a logger provider that stores log entries in memory.</summary>
/// <example>
/// <code>
/// // Use with dependency injection
/// var services = new ServiceCollection();
/// using var provider = new InMemoryLoggerProvider();
/// services.AddLogging(builder => builder.AddProvider(provider));
/// 
/// var serviceProvider = services.BuildServiceProvider();
/// var logger = serviceProvider.GetRequiredService&lt;ILogger&lt;MyClass&gt;&gt;();
/// logger.LogInformation("Test message");
/// 
/// // Access logs through the provider
/// var logs = provider.Logs.Informations;
/// </code>
/// </example>
public sealed class InMemoryLoggerProvider : ILoggerProvider
{
    private readonly IExternalScopeProvider _scopeProvider;
    private readonly TimeProvider _timeProvider;

    /// <summary>Gets the collection of log entries captured by all loggers created by this provider.</summary>
    public InMemoryLogCollection Logs { get; }

    /// <summary>Initializes a new instance of the <see cref="InMemoryLoggerProvider"/> class.</summary>
    public InMemoryLoggerProvider()
        : this(logs: null, scopeProvider: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="InMemoryLoggerProvider"/> class with a shared log collection.</summary>
    /// <param name="logs">The log collection to use, or <see langword="null"/> to create a new collection.</param>
    public InMemoryLoggerProvider(InMemoryLogCollection? logs)
        : this(logs, scopeProvider: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="InMemoryLoggerProvider"/> class with an external scope provider.</summary>
    /// <param name="scopeProvider">The external scope provider to use, or <see langword="null"/> to create a new instance.</param>
    public InMemoryLoggerProvider(IExternalScopeProvider? scopeProvider)
        : this(logs: null, scopeProvider)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="InMemoryLoggerProvider"/> class with a shared log collection and external scope provider.</summary>
    /// <param name="logs">The log collection to use, or <see langword="null"/> to create a new collection.</param>
    /// <param name="scopeProvider">The external scope provider to use, or <see langword="null"/> to create a new instance.</param>
    public InMemoryLoggerProvider(InMemoryLogCollection? logs, IExternalScopeProvider? scopeProvider)
    {
        Logs = logs ?? [];
        _scopeProvider = scopeProvider ?? new LoggerExternalScopeProvider();
        _timeProvider = TimeProvider.System;
    }

    /// <summary>Initializes a new instance of the <see cref="InMemoryLoggerProvider"/> class with a time provider.</summary>
    /// <param name="timeProvider">The time provider to use for timestamping log entries, or <see langword="null"/> to use the system time provider.</param>
    public InMemoryLoggerProvider(TimeProvider? timeProvider)
    : this(timeProvider, logs: null, scopeProvider: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="InMemoryLoggerProvider"/> class with a time provider and shared log collection.</summary>
    /// <param name="timeProvider">The time provider to use for timestamping log entries, or <see langword="null"/> to use the system time provider.</param>
    /// <param name="logs">The log collection to use, or <see langword="null"/> to create a new collection.</param>
    public InMemoryLoggerProvider(TimeProvider? timeProvider, InMemoryLogCollection? logs)
        : this(timeProvider, logs, scopeProvider: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="InMemoryLoggerProvider"/> class with a time provider and external scope provider.</summary>
    /// <param name="timeProvider">The time provider to use for timestamping log entries, or <see langword="null"/> to use the system time provider.</param>
    /// <param name="scopeProvider">The external scope provider to use, or <see langword="null"/> to create a new instance.</param>
    public InMemoryLoggerProvider(TimeProvider? timeProvider, IExternalScopeProvider? scopeProvider)
        : this(timeProvider, logs: null, scopeProvider)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="InMemoryLoggerProvider"/> class with a time provider, shared log collection, and external scope provider.</summary>
    /// <param name="timeProvider">The time provider to use for timestamping log entries, or <see langword="null"/> to use the system time provider.</param>
    /// <param name="logs">The log collection to use, or <see langword="null"/> to create a new collection.</param>
    /// <param name="scopeProvider">The external scope provider to use, or <see langword="null"/> to create a new instance.</param>
    public InMemoryLoggerProvider(TimeProvider? timeProvider, InMemoryLogCollection? logs, IExternalScopeProvider? scopeProvider)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        Logs = logs ?? [];
        _scopeProvider = scopeProvider ?? new LoggerExternalScopeProvider();
    }

    /// <summary>Creates a new logger instance with the specified category name.</summary>
    /// <param name="categoryName">The category name for messages produced by the logger.</param>
    /// <returns>A new instance of <see cref="ILogger"/>.</returns>
    public ILogger CreateLogger(string categoryName)
    {
        return new InMemoryLogger(categoryName, Logs, _scopeProvider, _timeProvider);
    }

    /// <summary>Creates a new generic logger instance.</summary>
    /// <typeparam name="T">The type whose name is used for the logger category name.</typeparam>
    /// <returns>A new instance of <see cref="ILogger{T}"/>.</returns>
    public ILogger<T> CreateLogger<T>()
    {
        return new InMemoryLogger<T>(Logs, _scopeProvider, _timeProvider);
    }

    public void Dispose()
    {
    }
}
