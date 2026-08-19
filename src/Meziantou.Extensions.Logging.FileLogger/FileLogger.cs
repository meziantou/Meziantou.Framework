using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meziantou.Extensions.Logging;

/// <summary>A logger that writes to a file via the FileLoggerProvider.</summary>
internal sealed class FileLogger(FileLoggerProvider provider, string categoryName) : ILogger
{
    // Keep the cached writer small, so a single big message doesn't retain a big buffer for the lifetime of the thread
    private const int MaxCachedCapacity = 4 * 1024;

    [ThreadStatic]
    private static StringWriter? s_stringWriter;

    private readonly string _shortCategoryName = GetShortCategoryName(categoryName);

    public bool IsEnabled(LogLevel logLevel) => logLevel is not LogLevel.None && logLevel >= provider.CurrentOptions.MinLevel;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => provider.ScopeProvider.Push(state);

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The StringWriter is cached in a thread static field and holds no unmanaged resource")]
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var options = provider.CurrentOptions;
        if (logLevel is LogLevel.None || logLevel < options.MinLevel)
            return;

        var timestamp = options.UseUtcTimestamp ? provider.TimeProvider.GetUtcNow() : provider.TimeProvider.GetLocalNow();
        var category = options.UseShortCategoryName ? _shortCategoryName : categoryName;
        var logEntry = new LogEntry<TState>(logLevel, category, eventId, state, exception, formatter);

        // The message is formatted on the current thread, so the formatters can use the ambient state such as Activity.Current
        var writer = s_stringWriter;
        if (writer is null)
        {
            writer = new StringWriter(CultureInfo.InvariantCulture);
        }
        else
        {
            // Detach the cached writer in case a formatter logs a message
            s_stringWriter = null;
        }

        try
        {
            var builder = writer.GetStringBuilder();
            options.GetFormatter().Write(in logEntry, timestamp, options, options.IncludeScopes ? provider.ScopeProvider : null, writer);
            if (builder.Length is 0)
                return;

            provider.WriteLog(builder.ToString());
        }
        finally
        {
            var builder = writer.GetStringBuilder();
            var canReuse = builder.Capacity <= MaxCachedCapacity;
            builder.Clear();
            if (canReuse)
            {
                s_stringWriter = writer;
            }
        }
    }

    private static string GetShortCategoryName(string categoryName)
    {
        var lastDotIndex = categoryName.LastIndexOf('.', StringComparison.Ordinal);
        return lastDotIndex >= 0 ? categoryName.Substring(lastDotIndex + 1) : categoryName;
    }
}
