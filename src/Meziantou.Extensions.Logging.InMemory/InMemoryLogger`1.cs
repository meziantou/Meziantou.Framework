using Microsoft.Extensions.Logging;

namespace Meziantou.Extensions.Logging.InMemory;
internal sealed class InMemoryLogger<T> : InMemoryLogger, IInMemoryLogger<T>
{
    /// <inheritdoc />
    public InMemoryLogger(InMemoryLogCollection logs, IExternalScopeProvider scopeProvider
#if NET8_0_OR_GREATER
       , TimeProvider timeProvider
#endif
       ) : base(GetCategoryName(), logs, scopeProvider
#if NET8_0_OR_GREATER
       , timeProvider
#endif
           )
    {
    }

    private static string GetCategoryName() => TypeNameHelper.GetTypeDisplayName(typeof(T), includeGenericParameters: false, nestedTypeDelimiter: '.');
}
