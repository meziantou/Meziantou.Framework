using System.Collections.Concurrent;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A test-only ambient hook: while an instance is installed on the current
/// asynchronous flow, every allocation scope created by <see cref="AllocationScope.Create"/> (images, eager loads, readers,
/// writers, identification) uses <see cref="Pool"/> instead of <see cref="SlabPool.Shared"/> and is recorded, so that fault
/// injection and fuzz tests driving the public API can inject allocation failures, audit rentals and returns, and check
/// that every scope returns to zero live bytes once the caller disposed what it owns.
/// </summary>
/// <remarks>
/// The hook flows with <see cref="ExecutionContext"/> (<see cref="AsyncLocal{T}"/>), so concurrent tests do not observe each
/// other. Production code never installs it; when it is absent, scope creation costs one ambient read.
/// </remarks>
internal sealed class AllocationTracking : IDisposable
{
    private static readonly AsyncLocal<AllocationTracking?> CurrentTracking = new();

    private readonly AllocationTracking? _previous;
    private readonly ConcurrentQueue<AllocationScope> _scopes = new();
    private bool _disposed;

    /// <summary>Installs a tracking hook on the current asynchronous flow until it is disposed.</summary>
    /// <param name="pool">The pool used by the scopes created while installed, or <see langword="null"/> to keep the shared pool.</param>
    public AllocationTracking(SlabPool? pool = null)
    {
        Pool = pool;
        _previous = CurrentTracking.Value;
        CurrentTracking.Value = this;
    }

    /// <summary>Gets the pool of the recorded scopes, if any.</summary>
    public SlabPool? Pool { get; }

    /// <summary>Gets the scopes created while the hook was installed, in creation order.</summary>
    public IReadOnlyCollection<AllocationScope> Scopes => _scopes;

    /// <summary>Gets the total live bytes of every recorded scope.</summary>
    public long LiveBytes => _scopes.Sum(scope => scope.LiveBytes);

    internal static AllocationTracking? Current => CurrentTracking.Value;

    /// <summary>Gets a description of the recorded scopes that still hold bytes, for diagnostics.</summary>
    public string DescribeLiveScopes()
        => string.Join(", ", _scopes.Where(scope => scope.LiveBytes != 0).Select(scope => $"{scope}: {scope.GetDiagnostics()}"));

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CurrentTracking.Value = _previous;
    }

    internal void Register(AllocationScope scope) => _scopes.Enqueue(scope);
}
