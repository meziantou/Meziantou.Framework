using System.ComponentModel;
using System.Numerics;
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
public static partial class SensitiveData
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
        ArgumentNullException.ThrowIfNull(secret);
        return string.Create(secret.GetLength(), secret, (span, buffer) => buffer.RevealInto(span));
    }

    internal static partial class UnixMemoryProtection
    {
        public const int PROT_NONE = 0;
        public const int PROT_READ = 1;
        public const int PROT_WRITE = 2;

        private const int MAP_PRIVATE = 0x02;
        private const int MAP_ANON_LINUX = 0x20;
        private const int MAP_ANON_MACOS = 0x1000;
        private const int MADV_DONTDUMP_LINUX = 16;
        private static readonly IntPtr MmapFailed = new(-1);

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        public static IntPtr Allocate(nuint length)
        {
            var handle = Interop.mmap(IntPtr.Zero, length, PROT_READ | PROT_WRITE, MAP_PRIVATE | GetMapAnonymousFlag(), -1, 0);
            if (handle == MmapFailed)
            {
                ThrowLastError();
            }

            // Keep the pages out of core dumps, which would otherwise capture the contents in clear
            // whenever a dump is taken during a reveal. Best effort: macOS has no equivalent, and an old
            // kernel rejecting the advice is no reason to refuse to store the data.
            if (OperatingSystem.IsLinux())
            {
                _ = Interop.madvise(handle, length, MADV_DONTDUMP_LINUX);
            }

            return handle;
        }

        // errno values are not Win32 error codes, so Marshal.GetHRForLastWin32Error turns them into unrelated
        // HRESULTs (ENOMEM becomes "The access code is invalid."). Win32Exception formats errno with strerror on Unix.
        [DoesNotReturn]
        public static void ThrowLastError()
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
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
            return checked(size + pageSize - 1) / pageSize * pageSize;
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

        private static partial class Interop
        {
            private const string Libc = "libc";

            [LibraryImport(Libc, EntryPoint = "mlock", SetLastError = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            internal static partial int mlock(IntPtr addr, nuint len);

            [LibraryImport(Libc, EntryPoint = "madvise", SetLastError = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            internal static partial int madvise(IntPtr addr, nuint len, int advice);

            [LibraryImport(Libc, EntryPoint = "mmap", SetLastError = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            internal static partial IntPtr mmap(IntPtr addr, nuint len, int prot, int flags, int fd, nint offset);

            [LibraryImport(Libc, EntryPoint = "mprotect", SetLastError = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            internal static partial int mprotect(IntPtr addr, nuint len, int prot);

            [LibraryImport(Libc, EntryPoint = "munlock", SetLastError = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            internal static partial int munlock(IntPtr addr, nuint len);

            [LibraryImport(Libc, EntryPoint = "munmap", SetLastError = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            internal static partial int munmap(IntPtr addr, nuint len);
        }
    }
}

/// <summary>
/// Represent sensitive data which should be difficult to accidentally disclose, even accounting for some types of application bugs.
/// However, there's no effort to thwart <i>intentional</i> disclosure of these
/// contents, such as through a debugger or memory dump utility.
/// </summary>
/// <typeparam name="T">The unmanaged type of elements stored in this sensitive data buffer.</typeparam>
/// <remarks>
/// <para>The protection applied while the contents are at rest depends on the platform:</para>
/// <list type="bullet">
/// <item><description>
/// Windows: the buffer is allocated on a private heap and encrypted with <c>CryptProtectMemory</c>.
/// </description></item>
/// <item><description>
/// Linux and macOS: the buffer is mapped with <c>mmap</c> and made inaccessible with <c>mprotect(PROT_NONE)</c>.
/// It is also locked into physical memory with <c>mlock</c> on a best-effort basis. Locking commonly fails when
/// <c>RLIMIT_MEMLOCK</c> is low, which is the default in many container images; that failure is not reported, and
/// the contents may then be written to swap. The contents are never encrypted on these platforms. On Linux the pages
/// are excluded from core dumps with <c>madvise(MADV_DONTDUMP)</c>; macOS has no equivalent, so a core dump taken while
/// the buffer is unprotected contains them in clear. Each instance maps at least one whole page, and each reveal
/// changes the page protection twice, so these platforms suit a moderate number of long-lived secrets rather than
/// many small or frequently revealed ones.
/// </description></item>
/// <item><description>
/// Every other platform, which includes Android, iOS, tvOS, Mac Catalyst, FreeBSD and WebAssembly: the buffer
/// is combined with a random key of the same length. The key lives in the same process, so this only guards
/// against casual inspection. Note that <c>OperatingSystem.IsLinux()</c> returns <see langword="false"/> on
/// Android and <c>OperatingSystem.IsMacOS()</c> returns <see langword="false"/> on iOS and Mac Catalyst, so
/// those platforms take this path rather than the <c>mmap</c> one above even though they support the syscalls
/// it uses.
/// </description></item>
/// </list>
/// <para>
/// Failing to apply or remove the protection throws; the instance is never silently left unprotected.
/// </para>
/// </remarks>
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
/// secret.RevealAndUse(arg: Console.Out, static (span, output) =>
/// {
///     // Process the sensitive data here
///     output.WriteLine($"Processing {span.Length} characters");
/// });
/// </code>
/// </example>
[TypeConverter(typeof(SensitiveDataTypeConverter))]
public sealed class SensitiveData<T> : IDisposable
    where T : unmanaged
{
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Disposed through the local returned by Interlocked.Exchange in Dispose")]
    private NativeMemorySafeHandle? _data;

    /// <summary>
    /// Creates a new <see cref="SensitiveData{T}"/> from the provided contents.
    /// </summary>
    /// <param name="contents">The contents to copy into the new instance.</param>
    /// <remarks>
    /// The newly-returned <see cref="SensitiveData{T}"/> instance maintains its own copy of the data separate from <paramref name="contents"/>.
    /// </remarks>
    internal SensitiveData(ReadOnlySpan<T> contents)
        : this(contents, forceXorProtection: false)
    {
    }

    private SensitiveData(ReadOnlySpan<T> contents, bool forceXorProtection)
    {
        // Use unmanaged memory so the data remains at a stable address
        // and can be cleared during Dispose.
        var data = new NativeMemorySafeHandle();
        try
        {
            data.Allocate(contents.Length, forceXorProtection);
            contents.CopyTo(data.GetSpan());
            data.Protect();
        }
        catch
        {
            // Release the buffer now: when Protect fails it holds a copy of the contents in clear, which would
            // otherwise stay in memory until the finalizer runs.
            data.Dispose();
            throw;
        }

        _data = data;
    }

    private delegate TResult RevealFunc<TState, TResult>(ReadOnlySpan<T> contents, TState state)
        where TState : allows ref struct;

    /// <summary>
    /// Creates an instance that uses the XOR fallback protection whatever the platform.
    /// Windows, Linux and macOS all select one of the other modes, so without this the fallback
    /// is executed by no test on any platform CI runs on.
    /// </summary>
    internal static SensitiveData<T> CreateWithXorProtection(ReadOnlySpan<T> contents) => new(contents, forceXorProtection: true);

    /// <summary>
    /// Copies the bytes as they are stored while protected, so tests can assert the contents are
    /// really transformed at rest rather than only that <see cref="IsProtected"/> was set.
    /// Returns <see langword="false"/> under the Unix protection, where the pages are
    /// <c>PROT_NONE</c> and reading them would fault instead of returning anything.
    /// </summary>
    internal bool TryGetBytesAtRest(out byte[] bytes)
    {
        var data = GetHandle();
        lock (data.SyncLock)
        {
            ThrowIfReleased(data);
            return data.TryCopyBytesAtRest(out bytes);
        }
    }

    /// <summary>
    /// Makes the next attempt to apply the protection throw as if the platform call had failed.
    /// Those calls do not fail on demand, so without this the recovery from a failed re-protect
    /// is executed by no test.
    /// </summary>
    internal void FailNextProtectForTesting()
    {
        var data = GetHandle();
        lock (data.SyncLock)
        {
            data.FailNextProtect = true;
        }
    }

    /// <summary>Returns the length (in elements) of this buffer.</summary>
    public int GetLength()
    {
        return GetHandle().Length;
    }

    /// <summary>
    /// Indicates whether the platform protection is currently applied to the buffer.
    /// Exposed so tests can assert the protection is actually in effect rather than only that
    /// the contents round-trip.
    /// </summary>
    internal bool IsProtected
    {
        get
        {
            return GetHandle().IsProtected;
        }
    }

    /// <summary>
    /// Copies the contents of this <see cref="SensitiveData{T}"/> instance to a destination buffer.
    /// </summary>
    /// <param name="destination">The destination buffer which should receive the contents. This buffer must be at least <see cref="GetLength"/> elements in length.</param>
    /// <returns>The number of elements written to <paramref name="destination"/>, which is always <see cref="GetLength"/>. A larger destination is left untouched past that point.</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/>'s length is smaller than <see cref="GetLength"/>.</exception>
    /// <exception cref="ObjectDisposedException">This instance has already been disposed.</exception>
    public int RevealInto(Span<T> destination)
    {
        return Reveal(destination, (contents, buffer) =>
        {
            contents.CopyTo(buffer);
            return contents.Length;
        });
    }

    /// <summary>
    /// Copies the contents of this <see cref="SensitiveData{T}"/> instance to a new array.
    /// </summary>
    /// <exception cref="ObjectDisposedException">This instance has already been disposed.</exception>
    public T[] RevealToArray()
    {
        return Reveal(state: 0, (contents, _) => contents.ToArray());
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
        Reveal((arg, spanAction), (contents, state) =>
        {
            state.spanAction(contents, state.arg);
            return true;
        });
    }

    /// <summary>Creates a new copy of this <see cref="SensitiveData{T}"/> instance.</summary>
    /// <returns>A new <see cref="SensitiveData{T}"/> instance containing a copy of the data.</returns>
    /// <exception cref="ObjectDisposedException">This instance has already been disposed.</exception>
    public SensitiveData<T> Clone()
    {
        return Reveal(state: 0, (contents, _) => new SensitiveData<T>(contents));
    }

    /// <summary>
    /// Disposes of this instance, including any unmanaged resources.
    /// The contents will no longer be accessible once the instance is disposed.
    /// </summary>
    public void Dispose()
    {
        // Claim the handle atomically so two concurrent Dispose calls cannot both release it, and so
        // a reveal that has not read the field yet observes null and throws instead of racing.
        var data = Interlocked.Exchange(ref _data, value: null);
        if (data is null)
            return;

        // Wait for a reveal in progress on another thread, so that once Dispose returns the contents
        // really have been cleared. A reveal in progress on this thread - Dispose called from a
        // RevealAndUse callback - does not block, because the lock is reentrant; the reference that
        // reveal holds on the handle defers the release until it has finished.
        lock (data.SyncLock)
        {
            data.Dispose();
        }
    }

    private TResult Reveal<TState, TResult>(TState state, RevealFunc<TState, TResult> func)
        where TState : allows ref struct
    {
        var data = GetHandle();
        lock (data.SyncLock)
        {
            ThrowIfReleased(data);

            // The callback can dispose this instance, and the reentrant lock lets that Dispose release the
            // handle immediately. Holding a reference defers the release until the contents have been
            // protected again below; otherwise that would operate on memory that was already freed.
            var addedRef = false;
            try
            {
                data.DangerousAddRef(ref addedRef);
                data.Unprotect();

                TResult result;
                try
                {
                    result = func(data.GetSpan(), state);
                }
                catch (Exception ex)
                {
                    ProtectAfterFailure(data, ex);
                    throw;
                }

                data.Protect();
                return result;
            }
            finally
            {
                if (addedRef)
                {
                    data.DangerousRelease();
                }
            }
        }
    }

    // A failure to protect the contents again must not replace the exception that ended the reveal,
    // which is the one the caller can act on, but it must not go unreported either.
    private static void ProtectAfterFailure(NativeMemorySafeHandle data, Exception exception)
    {
        try
        {
            data.Protect();
        }
        catch (Exception protectException)
        {
            throw new AggregateException(exception, protectException);
        }
    }

    private NativeMemorySafeHandle GetHandle()
    {
        // Read the field once: a concurrent Dispose sets it to null, and re-reading it mid-method
        // would turn that race into a NullReferenceException.
        var data = _data;
        ObjectDisposedException.ThrowIf(data is null, this);
        return data;
    }

    // Dispose may have released the handle between GetHandle and the lock being acquired.
    private void ThrowIfReleased(NativeMemorySafeHandle data)
    {
        ObjectDisposedException.ThrowIf(data.IsClosed, this);
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
        private long _memoryPressure;

        // Whether the platform transformation is currently applied: encrypted on Windows, PROT_NONE on Unix,
        // combined with the key on the fallback path. Undoing it is only valid while it is set, so a Protect
        // that failed must leave it clear rather than let the next reveal undo a transformation never applied.
        private bool _isProtected;

        // Number of reveals currently in progress. Reveals nest: a callback passed to RevealAndUse can
        // reach another reveal on the same instance, and Lock is reentrant so the nested call is allowed
        // straight through. Only the outermost reveal may touch the platform protection - an inner one
        // that unprotected again would decrypt already-decrypted bytes on Windows, and an inner one that
        // re-protected on exit would revoke the page while the outer caller still holds a span over it.
        private int _revealCount;

        public NativeMemorySafeHandle()
            : base(invalidHandleValue: Invalid, ownsHandle: true)
        {
        }

        public int Length { get; private set; }

        public Lock SyncLock { get; } = new();

        public bool FailNextProtect { get; set; }

        // A zero-length buffer holds no contents, so there is nothing to protect and nothing to disclose.
        public bool IsProtected => _allocatedBytes == 0 || _isProtected;

        public override bool IsInvalid => handle == Invalid;

        public void Allocate(int count, bool forceXorProtection)
        {
            Length = count;

            // Unchecked, this wraps on 32-bit for a large element type, and copying the contents into the
            // smaller buffer that results would overrun it.
            var byteCount = checked((nuint)count * (nuint)sizeof(T));
            _byteCount = byteCount;

            if (byteCount == 0)
                return;

            if (!forceXorProtection && OperatingSystem.IsWindows())
            {
                _protectionMode = ProtectionMode.Windows;
                _allocatedBytes = WindowsHeap.GetAlignedSize(byteCount);
                SetHandle(WindowsHeap.Allocate(_allocatedBytes));
                if (handle == IntPtr.Zero)
                    ThrowOutOfMemory();
            }
            else if (!forceXorProtection && (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()))
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
                FillRandom((byte*)_xorKey, byteCount);
            }

            // The caller overwrites the first byteCount bytes with the contents, so only the alignment padding
            // needs clearing.
            NativeMemory.Clear((byte*)handle + byteCount, _allocatedBytes - byteCount);

            // The GC cannot see native memory, so without this an instance nobody disposes holds its pages
            // until an unrelated collection happens to run the finalizer.
            _memoryPressure = checked((long)(_protectionMode is ProtectionMode.Xor ? _allocatedBytes + byteCount : _allocatedBytes));
            GC.AddMemoryPressure(_memoryPressure);
        }

        public Span<T> GetSpan()
        {
            return new Span<T>((void*)handle, Length);
        }

        public bool TryCopyBytesAtRest(out byte[] bytes)
        {
            // Reading PROT_NONE pages faults, so the Unix protection cannot be observed this way.
            if (_protectionMode is ProtectionMode.Unix)
            {
                bytes = [];
                return false;
            }

            bytes = new ReadOnlySpan<byte>((void*)handle, checked((int)_allocatedBytes)).ToArray();
            return true;
        }

        public void Protect()
        {
            if (_allocatedBytes == 0)
                return;

            // An outer reveal is still in progress, so the contents must stay accessible.
            if (_revealCount > 1)
            {
                _revealCount--;
                return;
            }

            // This ends the outermost reveal whether or not the protection is applied below. Leaving the
            // count at 1 on failure would make every later reveal look nested, so none of them would ever
            // apply the protection again. _isProtected stays clear instead, and the next reveal retries.
            _revealCount = 0;

            if (FailNextProtect)
            {
                FailNextProtect = false;
                throw new Win32Exception("Simulated failure to protect the contents.");
            }

            switch (_protectionMode)
            {
                case ProtectionMode.Windows when OperatingSystem.IsWindows():
                    WindowsHeap.ProtectMemory(handle, _allocatedBytes);
                    break;

                case ProtectionMode.Unix when OperatingSystem.IsLinux() || OperatingSystem.IsMacOS():
                    if (!SensitiveData.UnixMemoryProtection.TryProtect(handle, _allocatedBytes, SensitiveData.UnixMemoryProtection.PROT_NONE))
                    {
                        SensitiveData.UnixMemoryProtection.ThrowLastError();
                    }

                    break;

                case ProtectionMode.Xor:
                    XorWithKey();
                    break;
            }

            _isProtected = true;
        }

        public void Unprotect()
        {
            if (_allocatedBytes == 0)
                return;

            // The contents are already accessible because an outer reveal is in progress. Unprotecting
            // again would decrypt already-decrypted bytes on Windows and re-apply the XOR key on the
            // fallback path, handing the caller ciphertext instead of the secret.
            if (_revealCount > 0)
            {
                _revealCount++;
                return;
            }

            // Skipped when a previous Protect failed and left the contents accessible, for the same reason.
            if (_isProtected)
            {
                switch (_protectionMode)
                {
                    case ProtectionMode.Windows when OperatingSystem.IsWindows():
                        WindowsHeap.UnprotectMemory(handle, _allocatedBytes);
                        break;

                    case ProtectionMode.Unix when OperatingSystem.IsLinux() || OperatingSystem.IsMacOS():
                        if (!SensitiveData.UnixMemoryProtection.TryProtect(handle, _allocatedBytes, MemoryProtectionReadWrite))
                        {
                            SensitiveData.UnixMemoryProtection.ThrowLastError();
                        }

                        break;

                    case ProtectionMode.Xor:
                        XorWithKey();
                        break;
                }

                _isProtected = false;
            }

            _revealCount = 1;
        }

        protected override bool ReleaseHandle()
        {
            if (_allocatedBytes == 0)
                return true;

            if (_memoryPressure > 0)
            {
                GC.RemoveMemoryPressure(_memoryPressure);
                _memoryPressure = 0;
            }

            switch (_protectionMode)
            {
                case ProtectionMode.Windows when OperatingSystem.IsWindows():
                    NativeMemory.Clear((void*)handle, _allocatedBytes);
                    return WindowsHeap.Free(handle);

                case ProtectionMode.Unix when OperatingSystem.IsLinux() || OperatingSystem.IsMacOS():
                    if (_isProtected)
                    {
                        _isProtected = !SensitiveData.UnixMemoryProtection.TryProtect(handle, _allocatedBytes, MemoryProtectionReadWrite);
                    }

                    if (!_isProtected)
                    {
                        NativeMemory.Clear((void*)handle, _allocatedBytes);

                        if (_unixMemoryLocked)
                        {
                            SensitiveData.UnixMemoryProtection.TryUnlock(handle, _allocatedBytes);
                            _unixMemoryLocked = false;
                        }
                    }

                    return SensitiveData.UnixMemoryProtection.Free(handle, _allocatedBytes);

                case ProtectionMode.Xor:
                    NativeMemory.Clear((void*)handle, _allocatedBytes);

                    if (_xorKey != IntPtr.Zero)
                    {
                        NativeMemory.Clear((void*)_xorKey, _byteCount);
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
            var data = (byte*)handle;
            var key = (byte*)_xorKey;
            nuint i = 0;

            if (Vector.IsHardwareAccelerated)
            {
                var width = (nuint)Vector<byte>.Count;
                for (; _allocatedBytes - i >= width; i += width)
                {
                    Vector.Store(Vector.Load(data + i) ^ Vector.Load(key + i), data + i);
                }
            }

            for (; i < _allocatedBytes; i++)
            {
                data[i] ^= key[i];
            }
        }

        // Span is limited to int.MaxValue elements, so the buffer is filled in chunks rather than
        // narrowing the byte count to int.
        private static void FillRandom(byte* destination, nuint byteCount)
        {
            while (byteCount > 0)
            {
                var chunk = (int)Math.Min(byteCount, (nuint)int.MaxValue);
                RandomNumberGenerator.Fill(new Span<byte>(destination, chunk));
                destination += chunk;
                byteCount -= (nuint)chunk;
            }
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
