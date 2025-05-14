using Microsoft.Win32.SafeHandles;
using Xunit;

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
    }

    private sealed class TestSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public TestSafeHandle(nint value = 0) : base(true)
        {
            SetHandle(value);
        }

        protected override bool ReleaseHandle()
        {
            SetHandle(IntPtr.Zero);
            return true;
        }
    }
}
