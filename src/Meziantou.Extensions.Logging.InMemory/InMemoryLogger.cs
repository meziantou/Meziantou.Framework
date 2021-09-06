using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Meziantou.Extensions.Logging.InMemory
{
    public sealed class InMemoryLogger : ILogger
    {
        private readonly string? _category;
        private readonly LoggerExternalScopeProvider _scopeProvider = new();

        public InMemoryLogCollection Logs { get; } = new InMemoryLogCollection();

        public InMemoryLogger()
        {
        }

        public InMemoryLogger(string category)
        {
            _category = category;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return _scopeProvider.Push(state);
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var scopes = new List<object?>();
            _scopeProvider.ForEachScope((current, scopes) => scopes.Add(current), scopes);
            Logs.Add(new InMemoryLogEntry(_category, logLevel, eventId, scopes, state, exception, formatter(state, exception)));
        }
    }
}
