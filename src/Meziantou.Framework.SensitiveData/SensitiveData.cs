using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Meziantou.Framework;

// https://github.com/dotnet/designs/pull/147
// https://github.com/GrabYourPitchforks/runtime/pull/5/files
// https://github.com/GrabYourPitchforks/runtime/commit/7532410d14d7950241a87d5090af7bf1cb712e3b
// https://source.dot.net/#Microsoft.AspNetCore.DataProtection/Secret.cs,726e6ae00d63e382
/// <summary>
/// Provides factory methods for creating <see cref="SensitiveData{T}"/> instances.
/// </summary>
/// <example>
/// <code>
/// // Create sensitive data from a string
/// using var secret = SensitiveData.Create("my-password");
///
/// // Reveal the data when needed
/// string password = secret.RevealToString();
///
/// // Create sensitive data from a byte array
/// byte[] key = new byte[] { 1, 2, 3, 4, 5 };
/// using var sensitiveKey = SensitiveData.Create(key);
/// byte[] revealedKey = sensitiveKey.RevealToArray();
/// </code>
/// </example>
public static class SensitiveData
{
    /// <summary>Creates an instance of <see cref="SensitiveData{Char}" /> from a string.</summary>
    /// <param name="value">The sensitive string data to protect.</param>
    /// <returns>A new <see cref="SensitiveData{Char}"/> instance containing the string contents.</returns>
    public static SensitiveData<char> Create(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(value);
    }

    /// <summary>Creates an instance of <see cref="SensitiveData{T}" /> from an array.</summary>
    /// <typeparam name="T">The unmanaged type of elements in the buffer.</typeparam>
    /// <param name="buffer">The buffer containing sensitive data to protect.</param>
    /// <returns>A new <see cref="SensitiveData{T}"/> instance containing a copy of the buffer contents.</returns>
    public static SensitiveData<T> Create<T>(T[] buffer) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return new(buffer);
    }

    /// <summary>Creates an instance of <see cref="SensitiveData{T}" /> from a read-only span.</summary>
    /// <typeparam name="T">The unmanaged type of elements in the buffer.</typeparam>
    /// <param name="buffer">The read-only span containing sensitive data to protect.</param>
    /// <returns>A new <see cref="SensitiveData{T}"/> instance containing a copy of the buffer contents.</returns>
    public static SensitiveData<T> Create<T>(ReadOnlySpan<T> buffer) where T : unmanaged => new(buffer);

    /// <summary>Reveals the contents of a <see cref="SensitiveData{Char}"/> instance as a string.</summary>
    /// <param name="secret">The sensitive data to reveal.</param>
    /// <returns>A string containing the revealed sensitive data.</returns>
    /// <exception cref="ObjectDisposedException">The instance has already been disposed.</exception>
    public static string RevealToString(this SensitiveData<char> secret)
    {
        return string.Create(secret.GetLength(), secret, (span, buffer) => buffer.RevealInto(span));
    }
}

/// <summary>
/// Represent sensitive data which should be difficult to accidentally disclose, even accounting for some types of application bugs.
/// However, there's no effort to thwart <i>intentional</i> disclosure of these
/// contents, such as through a debugger or memory dump utility.
/// </summary>
/// <typeparam name="T">The unmanaged type of elements stored in this sensitive data buffer.</typeparam>
/// <example>
/// <code>
/// // Create and use sensitive data
/// using var secret = SensitiveData.Create("my-secret-password");
///
/// // Get the length without revealing the data
/// int length = secret.GetLength();
///
/// // Reveal data into an existing buffer
/// char[] buffer = new char[length];
/// secret.RevealInto(buffer);
///
/// // Or reveal to a new array
/// char[] revealed = secret.RevealToArray();
///
/// // Use the data with a callback to avoid keeping it in memory
/// secret.RevealAndUse(state: null, (span, _) => {
///     // Process the sensitive data here
///     Console.WriteLine($"Processing {span.Length} characters");
/// });
/// </code>
/// </example>
[TypeConverter(typeof(SensitiveDataTypeConverter))]
public sealed unsafe class SensitiveData<T> : IDisposable
    where T : unmanaged
{
    private NativeMemorySafeHandle? _data;

    /// <summary>
    /// Creates a new <see cref="SensitiveData{T}"/> from the provided contents.
    /// </summary>
    /// <param name="contents">The contents to copy into the new instance.</param>
    /// <remarks>
    /// The newly-returned <see cref="SensitiveData{T}"/> instance maintains its own copy of the data separate from <paramref name="contents"/>.
    /// </remarks>
    internal SensitiveData(ReadOnlySpan<T> contents)
    {
        // Use pinned to avoid the data from being moved in the memory.
        // So, Dispose can zeroed the buffer.
        _data = new NativeMemorySafeHandle();
        _data.Allocate(contents.Length);
        contents.CopyTo(_data.GetSpan());
    }

    /// <summary>
    /// Returns the length (in elements) of this buffer.
    /// </summary>
    public int GetLength()
    {
        ThrowIfDisposed();
        return _data.Length;
    }

    /// <summary>
    /// Copies the contents of this <see cref="SensitiveData{T}"/> instance to a destination buffer.
    /// </summary>
    /// <param name="destination">The destination buffer which should receive the contents. This buffer must be at least <see cref="GetLength"/> elements in length.</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/>'s length is smaller than <see cref="GetLength"/>.</exception>
    /// <exception cref="ObjectDisposedException">This instance has already been disposed.</exception>
    public int RevealInto(Span<T> destination)
    {
        ThrowIfDisposed();
        var span = _data.GetSpan();
        span.CopyTo(destination);
        return span.Length;
    }

    /// <summary>
    /// Copies the contents of this <see cref="SensitiveData{T}"/> instance to a new array.
    /// </summary>
    /// <exception cref="ObjectDisposedException">This instance has already been disposed.</exception>
    public T[] RevealToArray()
    {
        ThrowIfDisposed();
        return _data.GetSpan().ToArray();
    }

    /// <summary>Reveals the contents and invokes a callback action with the data.</summary>
    /// <typeparam name="TArg">The type of the argument to pass to the callback.</typeparam>
    /// <param name="arg">The argument to pass to the callback action.</param>
    /// <param name="spanAction">The callback action to invoke with the revealed data and argument.</param>
    /// <exception cref="ArgumentNullException"><paramref name="spanAction"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">This instance has already been disposed.</exception>
    public void RevealAndUse<TArg>(TArg arg, System.Buffers.ReadOnlySpanAction<T, TArg> spanAction)
    {
        ArgumentNullException.ThrowIfNull(spanAction);
        ThrowIfDisposed();
        var span = _data.GetSpan();
        spanAction(span, arg);
    }

    /// <summary>Creates a new copy of this <see cref="SensitiveData{T}"/> instance.</summary>
    /// <returns>A new <see cref="SensitiveData{T}"/> instance containing a copy of the data.</returns>
    /// <exception cref="ObjectDisposedException">This instance has already been disposed.</exception>
    public SensitiveData<T> Clone()
    {
        ThrowIfDisposed();
        var span = _data.GetSpan();
        return new(span);
    }

    /// <summary>
    /// Disposes of this instance, including any unmanaged resources.
    /// The contents will no longer be accessible once the instance is disposed.
    /// </summary>
    public void Dispose()
    {
        if (_data is not null)
        {
            _data.Dispose();
            _data = null;
        }
    }

    [MemberNotNull(nameof(_data))]
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_data is null, this);
    }

    private sealed unsafe class NativeMemorySafeHandle : SafeHandle
    {
        private const nint Invalid = 0;

        public NativeMemorySafeHandle()
            : base(invalidHandleValue: Invalid, ownsHandle: true)
        {
        }

        public int Length { get; private set; }

        public override bool IsInvalid => handle == Invalid;

        public void Allocate(int count)
        {
            Length = count;
            if (OperatingSystem.IsWindows())
            {
                SetHandle(WindowsHeap.Allocate((nuint)count * (nuint)sizeof(T)));
            }
            else
            {
                SetHandle((IntPtr)NativeMemory.Alloc((nuint)count * (nuint)sizeof(T)));
            }
        }

        public Span<T> GetSpan()
        {
            return new Span<T>((void*)handle, Length);
        }

        protected override bool ReleaseHandle()
        {
            GetSpan().Clear();

            if (OperatingSystem.IsWindows())
            {
                return WindowsHeap.Free(handle);
            }
            else
            {
                NativeMemory.Free((void*)handle);
                return true;
            }
        }
    }
}
