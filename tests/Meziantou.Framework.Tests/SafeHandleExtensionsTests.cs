using Microsoft.Win32.SafeHandles;

namespace Meziantou.Framework.Tests;
public sealed class SafeHandleExtensionsTests
{
    [Fact]
    public void CreateHandleScope_Invalid()
    {
        using var handle = new TestSafeHandle();
        Assert.Throws<ArgumentException>(() => handle.CreateHandleScope());
    }

    [Fact]
    public void CreateHandleScope_Disposed()
    {
        var handle = new TestSafeHandle();
        handle.Dispose();
        Assert.Throws<ArgumentException>(() => handle.CreateHandleScope());
    }

    [Fact]
    public void CreateHandleScope()
    {
        using var handle = new TestSafeHandle(42);
        using var scope = handle.CreateHandleScope();
        Assert.Equal(42, scope.Value);
    }

    [Fact]
    public void CreateHandleScope_Example()
    {
        #region SafeHandle_CreateHandleScope
        using var safeHandle = File.OpenHandle(Path.GetTempFileName(), FileMode.Open, FileAccess.Read, FileShare.None, FileOptions.DeleteOnClose);

        // The SafeHandle cannot be released while the scope is alive
        using var scope = safeHandle.CreateHandleScope();
        nint handle = scope.Value;
        Assert.NotEqual(0, handle); // Use handle safely, for instance in a P/Invoke call
        #endregion
    }

    [Fact]
    public void UseAfterDisposeScope()
    {
        using var handle = new TestSafeHandle(42);
        var scope = handle.CreateHandleScope();
        scope.Dispose();
        Assert.Throws<ObjectDisposedException>(() => scope.Value);
    }

    [Fact]
    public void UseAfterSetHandleAsInvalid()
    {
        using var handle = new TestSafeHandle(42);
        var scope = handle.CreateHandleScope();
        handle.SetHandleAsInvalid();
        Assert.Throws<ObjectDisposedException>(() => scope.Value);
    }

    [Fact]
    public void UseAfterScopeAndHandleDispose()
    {
        var handle = new TestSafeHandle(42);
        var scope = handle.CreateHandleScope();
        scope.Dispose();
        handle.Dispose();
        Assert.Throws<ObjectDisposedException>(() => scope.Value);
    }

    [Fact]
    public void UseWhenHasValueIsFalse()
    {
        SafeHandleValue scope = default;
        Assert.Throws<ObjectDisposedException>(() => scope.Value);
        scope.Dispose();
    }

    [Fact]
    public void CopiesOfAScopeReleaseTheReferenceOnce()
    {
        using var handle = new TestSafeHandle(42);
        var scope = handle.CreateHandleScope();
        var copy = scope;
        copy.Dispose();
        scope.Dispose();
        copy.Dispose();

        // Releasing the reference of the scope more than once would release the reference of the owner of the handle
        Assert.Equal(0, handle.ReleaseCount);
        Assert.False(handle.IsClosed);
        Assert.Throws<ObjectDisposedException>(() => scope.Value);
        Assert.Throws<ObjectDisposedException>(() => copy.Value);

        handle.Dispose();
        Assert.Equal(1, handle.ReleaseCount);
    }

    [Fact]
    public void TheHandleIsReleasedWhenTheLastScopeIsDisposed()
    {
        var handle = new TestSafeHandle(42);
        var scope = handle.CreateHandleScope();
        handle.Dispose();
        Assert.Equal(0, handle.ReleaseCount);

        scope.Dispose();
        Assert.Equal(1, handle.ReleaseCount);
    }

    private sealed class TestSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public TestSafeHandle(nint value = 0) : base(true)
        {
            SetHandle(value);
        }

        public int ReleaseCount { get; private set; }

        protected override bool ReleaseHandle()
        {
            ReleaseCount++;
            SetHandle(IntPtr.Zero);
            return true;
        }
    }
}
