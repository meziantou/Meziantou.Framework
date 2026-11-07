using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests.Conformance.Hardening;

/// <summary>
/// A private <see cref="SlabPool"/> installed for every allocation scope of the current flow (<see cref="AllocationTracking"/>)
/// that audits buffer lifetimes through the public API:
/// <list type="bullet">
/// <item><description>every rented buffer is recorded; a buffer returned twice, or returned without being rented, fails;</description></item>
/// <item><description>returned buffers are never reused: they are filled with <see cref="Poison"/> and quarantined, and
/// <see cref="Verify"/> fails when any of them was written after its return (use after return);</description></item>
/// <item><description>after the caller disposed everything it owns, no buffer may still be rented and every recorded scope
/// must be back to zero live bytes (no leak, including after failures);</description></item>
/// <item><description><see cref="FailureInjector"/> simulates allocation failures (or cancellation) at a chosen rental.</description></item>
/// </list>
/// </summary>
internal sealed class PoolAudit : IDisposable
{
    /// <summary>The byte written into returned buffers.</summary>
    public const byte Poison = 0xDB;

    private readonly Lock _lock = new();
    private readonly HashSet<byte[]> _outstanding = new(ReferenceEqualityComparer.Instance);
    private readonly List<byte[]> _quarantine = [];
    private readonly List<string> _violations = [];
    private readonly AllocationTracking _tracking;
    private long _quarantinedBytes;
    private int _attempts;

    public PoolAudit()
    {
        Pool = new SlabPool
        {
            RentFailureInjector = InjectFailure,
            RentObserver = OnRent,
            ReturnInterceptor = OnReturn,
        };
        _tracking = new AllocationTracking(Pool);
    }

    /// <summary>
    /// Gets or sets a hook called before every rental with its 1-based index among the rentals attempted since the hook was
    /// set; returning an exception makes that rental fail with it (simulated allocation failure). It may also cancel a token.
    /// </summary>
    public Func<int, Exception?>? FailureInjector
    {
        get;
        set
        {
            field = value;
            _attempts = 0;
        }
    }

    /// <summary>Gets the number of rentals attempted since <see cref="FailureInjector"/> was last set.</summary>
    public int Attempts => _attempts;

    /// <summary>Gets the maximum number of returned bytes kept in quarantine (older buffers are checked and dropped beyond it).</summary>
    public static long MaxQuarantinedBytes { get; } = 16L * 1024 * 1024;

    public SlabPool Pool { get; }

    /// <summary>Gets the number of buffers rented so far.</summary>
    public int Rentals { get; private set; }

    /// <summary>Gets the scopes created while the audit was installed.</summary>
    public IReadOnlyCollection<AllocationScope> Scopes => _tracking.Scopes;

    /// <summary>Gets the number of buffers currently rented.</summary>
    public int Outstanding
    {
        get
        {
            lock (_lock)
            {
                return _outstanding.Count;
            }
        }
    }

    /// <summary>Gets the total capacity of the buffers currently rented.</summary>
    public long OutstandingBytes
    {
        get
        {
            lock (_lock)
            {
                return _outstanding.Sum(buffer => (long)buffer.Length);
            }
        }
    }

    /// <summary>Gets the total live bytes of every recorded scope.</summary>
    public long LiveBytes => _tracking.LiveBytes;

    /// <summary>
    /// Fails when a violation was recorded, when a returned buffer was modified after its return, or (when
    /// <paramref name="expectReleased"/> is true) when a buffer is still rented or a scope still holds bytes.
    /// </summary>
    /// <returns>A description of every problem, or <see langword="null"/>.</returns>
    public string? Verify(bool expectReleased = true)
    {
        var problems = new List<string>();
        lock (_lock)
        {
            problems.AddRange(_violations);
            foreach (var buffer in _quarantine)
            {
                CheckPoison(buffer, problems);
            }

            if (expectReleased && _outstanding.Count != 0)
            {
                problems.Add($"{_outstanding.Count} pooled buffer(s) still rented after everything was disposed ({_outstanding.Sum(buffer => (long)buffer.Length)} bytes)");
            }
        }

        if (expectReleased && _tracking.LiveBytes != 0)
        {
            problems.Add($"allocation scopes still hold {_tracking.LiveBytes} live bytes: {_tracking.DescribeLiveScopes()}");
        }

        return problems.Count == 0 ? null : string.Join("; ", problems);
    }

    /// <summary>Asserts <see cref="Verify"/>.</summary>
    public void AssertClean(string context, bool expectReleased = true)
    {
        var problems = Verify(expectReleased);
        if (problems is not null)
            Assert.Fail($"{context}: {problems}");
    }

    public void Dispose()
    {
        FailureInjector = null;
        _tracking.Dispose();
    }

    private Exception? InjectFailure(int length)
    {
        var attempt = Interlocked.Increment(ref _attempts);
        return FailureInjector?.Invoke(attempt);
    }

    private static void CheckPoison(byte[] buffer, List<string> problems)
    {
        var index = buffer.AsSpan().IndexOfAnyExcept(Poison);
        if (index >= 0)
        {
            problems.Add($"a buffer of {buffer.Length} bytes was written after it was returned to the pool (offset {index}, value 0x{buffer[index]:X2})");
        }
    }

    private void OnRent(byte[] buffer)
    {
        lock (_lock)
        {
            Rentals++;
            if (!_outstanding.Add(buffer))
            {
                _violations.Add($"a buffer of {buffer.Length} bytes was handed out while already rented");
            }
        }
    }

    private bool OnReturn(byte[] buffer)
    {
        lock (_lock)
        {
            if (!_outstanding.Remove(buffer))
            {
                _violations.Add($"a buffer of {buffer.Length} bytes was returned twice or was not rented from this pool");
                return true;
            }

            buffer.AsSpan().Fill(Poison);
            _quarantine.Add(buffer);
            _quarantinedBytes += buffer.Length;
            while (_quarantinedBytes > MaxQuarantinedBytes && _quarantine.Count > 1)
            {
                var oldest = _quarantine[0];
                CheckPoison(oldest, _violations);
                _quarantine.RemoveAt(0);
                _quarantinedBytes -= oldest.Length;
            }
        }

        // Never retained: a later rental can never alias a buffer the library might still use
        return true;
    }
}
