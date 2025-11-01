using System.Runtime.InteropServices;

namespace Meziantou.Framework;

/// <summary>
/// A disposable struct that safely holds a reference to a <see cref="SafeHandle"/> and its underlying handle value.
/// Automatically increments the reference count on creation and decrements it on disposal.
/// </summary>
/// <example>
/// <code>
/// using var scope = safeHandle.CreateHandleScope();
/// nint handle = scope.Value;
/// // Use handle safely
/// </code>
/// </example>
#if PUBLIC_SAFEHANDLEVALUE
public
#else
internal
#endif
struct SafeHandleValue : IDisposable
{
    private bool _hasValue;
    private SafeHandle _safeHandle;

    /// <summary>Gets a value indicating whether the handle has a valid value.</summary>
    public readonly bool HasValue => _safeHandle is not null && _hasValue && !_safeHandle.IsClosed;

    /// <summary>Gets the underlying handle value.</summary>
    public nint Value
    {
        readonly get
        {
            ObjectDisposedException.ThrowIf(!HasValue, _safeHandle);

            if (!HasValue)
                throw new InvalidOperationException("Handle must have a value");

            return field;
        }
        private set;
    }

    public SafeHandleValue(SafeHandle safeHandle)
    {
        ArgumentNullException.ThrowIfNull(safeHandle);
        if (safeHandle.IsInvalid)
            throw new ArgumentException("Handle is invalid", nameof(safeHandle));

        _safeHandle = safeHandle;
        _hasValue = false;
        Value = IntPtr.Zero;

        // Throw if cannot add a reference
        _safeHandle.DangerousAddRef(ref _hasValue);
        if (_hasValue)
        {
            Value = _safeHandle.DangerousGetHandle();
        }
    }

    public void Dispose()
    {
        if (_safeHandle is not null)
        {
            if (_hasValue)
            {
                _safeHandle.DangerousRelease();
                _hasValue = false;
                Value = default;
            }

            _safeHandle = null;
        }
    }

    public static implicit operator nint(SafeHandleValue handle) => handle.Value;
}
