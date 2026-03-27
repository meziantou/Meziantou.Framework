using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;

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

    internal static class UnixMemoryProtection
    {
        public const int PROT_NONE = 0;
        public const int PROT_READ = 1;
        public const int PROT_WRITE = 2;

        private const int MAP_PRIVATE = 0x02;
        private const int MAP_ANON_LINUX = 0x20;
        private const int MAP_ANON_MACOS = 0x1000;
        private static readonly IntPtr MmapFailed = new(-1);

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        public static IntPtr Allocate(nuint length)
        {
            var handle = Interop.mmap(IntPtr.Zero, length, PROT_READ | PROT_WRITE, MAP_PRIVATE | GetMapAnonymousFlag(), -1, 0);
            if (handle == MmapFailed)
            {
                Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
            }

            return handle;
        }

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        public static bool Free(IntPtr address, nuint length)
        {
            return Interop.munmap(address, length) == 0;
        }

        public static nuint GetAlignedSize(nuint size)
        {
            var pageSize = checked((nuint)Environment.SystemPageSize);
            return (size + pageSize - 1) / pageSize * pageSize;
        }

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        public static bool TryLock(IntPtr address, nuint length)
        {
            return Interop.mlock(address, length) == 0;
        }

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        public static bool TryProtect(IntPtr address, nuint length, int protection)
        {
            return Interop.mprotect(address, length, protection) == 0;
        }

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        public static bool TryUnlock(IntPtr address, nuint length)
        {
            return Interop.munlock(address, length) == 0;
        }

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        private static int GetMapAnonymousFlag()
        {
            if (OperatingSystem.IsMacOS())
                return MAP_ANON_MACOS;

            return MAP_ANON_LINUX;
        }

        private static class Interop
        {
            private const string Libc = "libc";

            [DllImport(Libc, EntryPoint = "mlock", SetLastError = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            internal static extern int mlock(IntPtr addr, nuint len);

            [DllImport(Libc, EntryPoint = "mmap", SetLastError = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            internal static extern IntPtr mmap(IntPtr addr, nuint len, int prot, int flags, int fd, nint offset);

            [DllImport(Libc, EntryPoint = "mprotect", SetLastError = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            internal static extern int mprotect(IntPtr addr, nuint len, int prot);

            [DllImport(Libc, EntryPoint = "munlock", SetLastError = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            internal static extern int munlock(IntPtr addr, nuint len);

            [DllImport(Libc, EntryPoint = "munmap", SetLastError = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            internal static extern int munmap(IntPtr addr, nuint len);
        }
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
        // Use unmanaged memory so the data remains at a stable address
        // and can be cleared during Dispose.
        _data = new NativeMemorySafeHandle();
        _data.Allocate(contents.Length);
        contents.CopyTo(_data.GetSpan());
        _data.Protect();
    }

    /// <summary>Returns the length (in elements) of this buffer.</summary>
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
        lock (_data.SyncLock)
        {
            _data.Unprotect();
            try
            {
                var span = _data.GetSpan();
                span.CopyTo(destination);
                return span.Length;
            }
            finally
            {
                _data.Protect();
            }
        }
    }

    /// <summary>
    /// Copies the contents of this <see cref="SensitiveData{T}"/> instance to a new array.
    /// </summary>
    /// <exception cref="ObjectDisposedException">This instance has already been disposed.</exception>
    public T[] RevealToArray()
    {
        ThrowIfDisposed();
        lock (_data.SyncLock)
        {
            _data.Unprotect();
            try
            {
                return _data.GetSpan().ToArray();
            }
            finally
            {
                _data.Protect();
            }
        }
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
        lock (_data.SyncLock)
        {
            _data.Unprotect();
            try
            {
                var span = _data.GetSpan();
                spanAction(span, arg);
            }
            finally
            {
                _data.Protect();
            }
        }
    }

    /// <summary>Creates a new copy of this <see cref="SensitiveData{T}"/> instance.</summary>
    /// <returns>A new <see cref="SensitiveData{T}"/> instance containing a copy of the data.</returns>
    /// <exception cref="ObjectDisposedException">This instance has already been disposed.</exception>
    public SensitiveData<T> Clone()
    {
        ThrowIfDisposed();
        lock (_data.SyncLock)
        {
            _data.Unprotect();
            try
            {
                var span = _data.GetSpan();
                return new(span);
            }
            finally
            {
                _data.Protect();
            }
        }
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
        private const int MemoryProtectionReadWrite = SensitiveData.UnixMemoryProtection.PROT_READ | SensitiveData.UnixMemoryProtection.PROT_WRITE;

        private nuint _byteCount;
        private nuint _allocatedBytes;
        private IntPtr _xorKey;
        private ProtectionMode _protectionMode;
        private bool _unixMemoryLocked;
        private bool _unixMemoryProtected;

        public NativeMemorySafeHandle()
            : base(invalidHandleValue: Invalid, ownsHandle: true)
        {
        }

        public int Length { get; private set; }

        public Lock SyncLock { get; } = new();

        public override bool IsInvalid => handle == Invalid;

        public void Allocate(int count)
        {
            Length = count;
            var byteCount = (nuint)count * (nuint)sizeof(T);
            _byteCount = byteCount;

            if (byteCount == 0)
                return;

            if (OperatingSystem.IsWindows())
            {
                _protectionMode = ProtectionMode.Windows;
                _allocatedBytes = WindowsHeap.GetAlignedSize(byteCount);
                SetHandle(WindowsHeap.Allocate(_allocatedBytes));
                if (handle == IntPtr.Zero)
                    ThrowOutOfMemory();
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                _protectionMode = ProtectionMode.Unix;
                _allocatedBytes = SensitiveData.UnixMemoryProtection.GetAlignedSize(byteCount);
                SetHandle(SensitiveData.UnixMemoryProtection.Allocate(_allocatedBytes));
                _unixMemoryLocked = SensitiveData.UnixMemoryProtection.TryLock(handle, _allocatedBytes);
            }
            else
            {
                _protectionMode = ProtectionMode.Xor;
                _allocatedBytes = byteCount;
                SetHandle((IntPtr)NativeMemory.Alloc(byteCount));

                _xorKey = (IntPtr)NativeMemory.Alloc(byteCount);
                RandomNumberGenerator.Fill(new Span<byte>((void*)_xorKey, checked((int)byteCount)));
            }

            GetAllocatedByteSpan().Clear();
        }

        public Span<T> GetSpan()
        {
            return new Span<T>((void*)handle, Length);
        }

        public void Protect()
        {
            if (_allocatedBytes == 0)
                return;

            if (OperatingSystem.IsWindows() && _protectionMode is ProtectionMode.Windows)
            {
                WindowsHeap.ProtectMemory(handle, _allocatedBytes);
            }
            else if ((OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) && _protectionMode is ProtectionMode.Unix)
            {
                _unixMemoryProtected = SensitiveData.UnixMemoryProtection.TryProtect(handle, _allocatedBytes, SensitiveData.UnixMemoryProtection.PROT_NONE);
            }
            else if (_protectionMode is ProtectionMode.Xor)
            {
                XorWithKey();
            }
        }

        public void Unprotect()
        {
            if (_allocatedBytes == 0)
                return;

            if (OperatingSystem.IsWindows() && _protectionMode is ProtectionMode.Windows)
            {
                WindowsHeap.UnprotectMemory(handle, _allocatedBytes);
            }
            else if ((OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) && _protectionMode is ProtectionMode.Unix)
            {
                if (!_unixMemoryProtected)
                    return;

                if (!SensitiveData.UnixMemoryProtection.TryProtect(handle, _allocatedBytes, MemoryProtectionReadWrite))
                {
                    ThrowLastPInvokeError();
                }

                _unixMemoryProtected = false;
            }
            else if (_protectionMode is ProtectionMode.Xor)
            {
                XorWithKey();
            }
        }

        protected override bool ReleaseHandle()
        {
            if (_allocatedBytes == 0)
                return true;

            switch (_protectionMode)
            {
                case ProtectionMode.Windows when OperatingSystem.IsWindows():
                    GetAllocatedByteSpan().Clear();
                    return WindowsHeap.Free(handle);

                case ProtectionMode.Unix when OperatingSystem.IsLinux() || OperatingSystem.IsMacOS():
                    if (_unixMemoryProtected)
                    {
                        _unixMemoryProtected = !SensitiveData.UnixMemoryProtection.TryProtect(handle, _allocatedBytes, MemoryProtectionReadWrite);
                    }

                    if (!_unixMemoryProtected)
                    {
                        GetAllocatedByteSpan().Clear();

                        if (_unixMemoryLocked)
                        {
                            SensitiveData.UnixMemoryProtection.TryUnlock(handle, _allocatedBytes);
                            _unixMemoryLocked = false;
                        }
                    }

                    return SensitiveData.UnixMemoryProtection.Free(handle, _allocatedBytes);

                case ProtectionMode.Xor:
                    GetAllocatedByteSpan().Clear();

                    if (_xorKey != IntPtr.Zero)
                    {
                        new Span<byte>((void*)_xorKey, checked((int)_byteCount)).Clear();
                        NativeMemory.Free((void*)_xorKey);
                        _xorKey = IntPtr.Zero;
                    }

                    NativeMemory.Free((void*)handle);
                    return true;

                default:
                    return true;
            }
        }

        private void XorWithKey()
        {
            var data = new Span<byte>((void*)handle, (int)_allocatedBytes);
            var key = new ReadOnlySpan<byte>((void*)_xorKey, (int)_allocatedBytes);
            for (var i = 0; i < data.Length; i++)
            {
                data[i] ^= key[i];
            }
        }

        private Span<byte> GetAllocatedByteSpan()
        {
            return new Span<byte>((void*)handle, checked((int)_allocatedBytes));
        }

        private static void ThrowLastPInvokeError()
        {
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
        }

        private static void ThrowOutOfMemory()
        {
            Marshal.ThrowExceptionForHR(unchecked((int)0x8007000E));
        }

        private enum ProtectionMode
        {
            None,
            Windows,
            Unix,
            Xor,
        }
    }
}
