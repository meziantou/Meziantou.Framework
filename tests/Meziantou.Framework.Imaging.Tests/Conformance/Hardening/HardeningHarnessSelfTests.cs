using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests.Conformance.Hardening;

/// <summary>
/// Self-tests of the hardening harness with deliberate defects: the pool audit must report leaks, writes after return and
/// live scopes, and inject failures at the requested rental. The fuzz harness has its own self-tests
/// (fuzz/Meziantou.Framework.Imaging.FuzzTests/FuzzHarnessSelfTests).
/// </summary>
public sealed class HardeningHarnessSelfTests
{
    [Fact]
    public void PoolAuditReportsLeakedBuffersAndLiveScopes()
    {
        using var audit = new PoolAudit();
        var scope = AllocationScope.Create(ImageConfiguration.Default, "self-test");
        var buffer = scope.Rent(100, AllocationKind.Temporary);
        Assert.Equal(1, audit.Outstanding);
        Assert.Equal(audit.OutstandingBytes, audit.LiveBytes);
        var problems = audit.Verify();
        Assert.NotNull(problems);
        Assert.Contains("still rented", problems, StringComparison.Ordinal);
        Assert.Contains("live bytes", problems, StringComparison.Ordinal);
        buffer.Dispose();
        Assert.Null(audit.Verify());
    }

    [Fact]
    public void DirtySlabPoolHandsOutBuffersFilledWithThePattern()
    {
        var dirty = new DirtySlabPool();
        var scope = new AllocationScope(ImageResourceLimits.Default, dirty.Pool, "self-test");
        using (var first = scope.Rent(1000, AllocationKind.Temporary, clear: false))
        {
            Assert.Equal(-1, first.Span.IndexOfAnyExcept(DirtySlabPool.Pattern));
            first.Span.Clear();
        }

        // The buffer that was just returned, and a size class that was never used
        foreach (var length in new[] { 1000, 70_000 })
        {
            using var buffer = scope.Rent(length, AllocationKind.Temporary, clear: false);
            Assert.Equal(-1, buffer.Span.IndexOfAnyExcept(DirtySlabPool.Pattern));
        }

        using (var cleared = scope.Rent(1000, AllocationKind.Temporary, clear: true))
        {
            Assert.Equal(-1, cleared.Span[..1000].IndexOfAnyExcept((byte)0));
        }

        Assert.Equal(4, dirty.Rentals);
        dirty.AssertEveryRentalWasDirty("self-test");
    }

    [Fact]
    public void PoolAuditReportsWritesAfterReturn()
    {
        using var audit = new PoolAudit();
        var scope = AllocationScope.Create(ImageConfiguration.Default, "self-test");
        var buffer = scope.Rent(100, AllocationKind.Temporary);
        var raw = buffer.RawBuffer;
        buffer.Dispose();
        Assert.Null(audit.Verify());
        raw[42] = 1; // use after return
        var problems = audit.Verify();
        Assert.NotNull(problems);
        Assert.Contains("written after it was returned", problems, StringComparison.Ordinal);
    }

    [Fact]
    public void PoolAuditInjectsFailuresAtTheRequestedRental()
    {
        using var audit = new PoolAudit();
        var injected = new InsufficientMemoryException("injected");
        audit.FailureInjector = attempt => attempt == 2 ? injected : null;
        var scope = AllocationScope.Create(ImageConfiguration.Default, "self-test");
        using (scope.Rent(10, AllocationKind.Temporary))
        {
            Assert.Same(injected, Assert.Throws<InsufficientMemoryException>(() => scope.Rent(10, AllocationKind.Temporary)));
        }

        Assert.Equal(2, audit.Attempts);
        Assert.Equal(0, scope.LiveBytes); // the failed rental released its charge
        Assert.Null(audit.Verify());
    }
}
