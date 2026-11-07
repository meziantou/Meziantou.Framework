using System.Runtime.InteropServices;

namespace Meziantou.Framework;

/// <summary>
/// A disposable struct that safely holds a reference to a <see cref="SafeHandle"/> and its underlying handle value.
/// Automatically increments the reference count on creation and decrements it on disposal.
/// </summary>
/// <remarks>
/// The copies of a value share the reference: disposing any of them releases it, once.
/// </remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Tests/SafeHandleExtensionsTests.cs" ref:region="SafeHandle_CreateHandleScope" ref:dedent="true">
/// using var safeHandle = File.OpenHandle(Path.GetTempFileName(), FileMode.Open, FileAccess.Read, FileShare.None, FileOptions.DeleteOnClose);
///
/// // The SafeHandle cannot be released while the scope is alive
/// using var scope = safeHandle.CreateHandleScope();
/// nint handle = scope.Value;
/// Assert.NotEqual(0, handle); // Use handle safely, for instance in a P/Invoke call
/// </code>
/// </example>
#if PUBLIC_SAFEHANDLEVALUE
public
#else
internal
#endif
readonly struct SafeHandleValue : IDisposable
{
    // The reference is held by an object shared by the copies of the struct: a flag in the struct itself would be
    // copied with it, and each copy would release the reference again
    private readonly Lease? _lease;
    private readonly nint _value;

    /// <summary>Gets a value indicating whether the handle has a valid value.</summary>
    [MemberNotNullWhen(true, nameof(_lease))]
    public bool HasValue => _lease is { IsReleased: false } lease && !lease.SafeHandle.IsClosed;

    /// <summary>Gets the underlying handle value.</summary>
    public nint Value
    {
        get
        {
            ObjectDisposedException.ThrowIf(!HasValue, typeof(SafeHandleValue));

            return _value;
        }
    }

    public SafeHandleValue(SafeHandle safeHandle)
    {
        ArgumentNullException.ThrowIfNull(safeHandle);
        if (safeHandle.IsInvalid)
            throw new ArgumentException("Handle is invalid", nameof(safeHandle));

        // Allocate before adding the reference, so that nothing can fail between adding it and storing it
        var lease = new Lease(safeHandle);

        // Throw if cannot add a reference
        var success = false;
        safeHandle.DangerousAddRef(ref success);
        if (success)
        {
            _lease = lease;
            _value = safeHandle.DangerousGetHandle();
        }
    }

    public void Dispose()
    {
        _lease?.Release();
    }

    public static implicit operator nint(SafeHandleValue handle) => handle.Value;

    private sealed class Lease(SafeHandle safeHandle)
    {
        private int _released;

        public SafeHandle SafeHandle { get; } = safeHandle;

        public bool IsReleased => Volatile.Read(ref _released) is not 0;

        public void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) is 0)
            {
                SafeHandle.DangerousRelease();
            }
        }
    }
}
