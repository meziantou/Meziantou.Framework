using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests.Conformance.Hardening;

/// <summary>
/// A private <see cref="SlabPool"/> whose buffers are never handed out zeroed by chance: every pooled rental returns a
/// buffer that is entirely filled with <see cref="Pattern"/>, unless the caller asks for a cleared buffer. A result that
/// depends on memory the library did not initialize therefore changes, where a fresh buffer of the garbage collector
/// (zeros) or the leftovers of the same decoding would hide the defect.
/// <list type="bullet">
/// <item><description>before every rental, a buffer of the requested size class filled with the pattern is put into the
/// pool, which hands out the buffer returned last;</description></item>
/// <item><description>every returned buffer is filled with the pattern before it is retained, so that a buffer reused
/// when its size class is full is dirty too;</description></item>
/// <item><description><see cref="AssertEveryRentalWasDirty"/> proves that no rental was served by a new allocation.</description></item>
/// </list>
/// It is built on the test hooks of the pool (the ones <see cref="PoolAudit"/> uses): nothing in the library knows about it.
/// Pass <see cref="Pool"/> to the <see cref="AllocationScope"/> of the code under test.
/// </summary>
internal sealed class DirtySlabPool
{
    /// <summary>The byte every rented buffer is filled with: a 16-bit sample made of it (0xA5A5) is out of range for every bit depth.</summary>
    public const byte Pattern = 0xA5;

    public DirtySlabPool()
    {
        // Every length is pooled, and the retained bytes are not bounded: a seeded buffer is only dropped when its size
        // class is full of dirty buffers
        Pool = new SlabPool(new SlabPoolOptions { MaxPooledLength = 1 << 30, MaxRetainedBytes = long.MaxValue })
        {
            RentFailureInjector = Seed,
            RentObserver = _ => Rentals++,
            ReturnInterceptor = Dirty,
        };
    }

    public SlabPool Pool { get; }

    /// <summary>Gets the number of buffers handed out so far.</summary>
    public int Rentals { get; private set; }

    /// <summary>Fails when a rental was served by a new allocation instead of a buffer filled with the pattern.</summary>
    public void AssertEveryRentalWasDirty(string context)
    {
        var diagnostics = Pool.GetDiagnostics();
        Assert.True(Rentals > 0, $"{context}: nothing was rented from the dirty pool.");
        Assert.True(diagnostics is { NewPooledAllocations: 0, UnpooledAllocations: 0 } && diagnostics.ReusedRentals == Rentals,
            $"{context}: {diagnostics.NewPooledAllocations} pooled and {diagnostics.UnpooledAllocations} unpooled rental(s) out of {Rentals} were new allocations, which the dirty pool cannot fill.");
    }

    private Exception? Seed(int length)
    {
        if (length > 0 && length <= Pool.Options.MaxPooledLength)
        {
            // Returned to the pool, so filled by Dirty, then handed out by the rental that follows
            Pool.Return(new byte[Pool.GetRentCapacity(length)]);
        }

        return null;
    }

    private static bool Dirty(byte[] buffer)
    {
        buffer.AsSpan().Fill(Pattern);

        // Not taken over: the pool retains the buffer and reuses it
        return false;
    }
}
