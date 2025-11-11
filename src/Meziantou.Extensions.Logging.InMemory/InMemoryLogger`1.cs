using Microsoft.Extensions.Logging;

namespace Meziantou.Extensions.Logging.InMemory;

internal sealed class InMemoryLogger<T> : InMemoryLogger, IInMemoryLogger<T>
{
    /// <inheritdoc />
    public InMemoryLogger(InMemoryLogCollection logs, IExternalScopeProvider scopeProvider, TimeProvider timeProvider)
        : base(GetCategoryName(), logs, scopeProvider, timeProvider)
    {
    }

    private static string GetCategoryName() => TypeNameHelper.GetTypeDisplayName(typeof(T), includeGenericParameters: false, nestedTypeDelimiter: '.');
}
