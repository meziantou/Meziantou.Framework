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
public sealed class InMemoryLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly MutableExternalScopeProvider _scopeProvider;
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
        _scopeProvider = new MutableExternalScopeProvider(scopeProvider ?? new LoggerExternalScopeProvider());
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
        _scopeProvider = new MutableExternalScopeProvider(scopeProvider ?? new LoggerExternalScopeProvider());
    }

    /// <summary>Creates a new <see cref="InMemoryLoggerProvider"/>.</summary>
    /// <param name="logs">The log collection to use, or <see langword="null"/> to create a new collection.</param>
    /// <param name="scopeProvider">The external scope provider to use, or <see langword="null"/> to create a new instance.</param>
    /// <param name="timeProvider">The time provider to use for timestamping log entries, or <see langword="null"/> to use the system time provider.</param>
    /// <returns>A new instance of <see cref="InMemoryLoggerProvider"/>.</returns>
    /// <remarks>
    /// Prefer this over the constructors whenever an argument is <see langword="null"/>. The constructor
    /// overloads take three independently nullable types, so a bare <see langword="null"/> cannot be
    /// resolved to one of them and the call fails to compile. The parameter order matches
    /// <see cref="InMemoryLogger.CreateLogger(string, InMemoryLogCollection?, IExternalScopeProvider?, TimeProvider?)"/>.
    /// </remarks>
    public static InMemoryLoggerProvider Create(InMemoryLogCollection? logs = null, IExternalScopeProvider? scopeProvider = null, TimeProvider? timeProvider = null)
    {
        return new InMemoryLoggerProvider(timeProvider, logs, scopeProvider);
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

    /// <summary>Sets the external scope provider supplied by the logger factory.</summary>
    /// <param name="scopeProvider">The scope provider the factory uses to track scopes.</param>
    /// <remarks>
    /// Implementing <see cref="ISupportExternalScope"/> is what makes the factory route its scopes
    /// through this provider, including the ones it synthesises from the current activity when
    /// <c>LoggerFactoryOptions.ActivityTrackingOptions</c> is set.
    /// </remarks>
    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        _scopeProvider.Current = scopeProvider;
    }

    public void Dispose()
    {
    }
}
