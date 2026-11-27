using System.Security.Cryptography;
using System.Text;

namespace Meziantou.Framework.WasmEngine.Wasi.Internal;

/// <summary>
/// The functions of WASI Preview 1 on the bytes of the memory of the module: they return the error code of WASI, and
/// throw a <see cref="WasiException"/> for the errors (caught by <see cref="WasiFunctions"/>), or a
/// <see cref="WasiExitException"/> for <c>proc_exit</c>. The pointers and the lengths are unsigned.
/// </summary>
internal sealed partial class WasiImplementation : IDisposable
{
    internal const int MaxFileDescriptors = 4096;
    internal const int MaxPathLength = 4096;
    internal const int MaxSymbolicLinks = 32;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly byte[][] _arguments;
    private readonly byte[][] _environment;
    private readonly TimeProvider _timeProvider;
    private readonly WasiRandomSource? _randomSource;
    private readonly long _startTimestamp;
    private readonly List<WasiFd?> _fds = [];

    public WasiImplementation(WasiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _arguments = options.Arguments.Select(argument => EncodeString(argument, nameof(options))).ToArray();
        _environment = options.EnvironmentVariables.Select(variable =>
        {
            if (variable.Key.Length == 0 || variable.Key.Contains('=', StringComparison.Ordinal))
                throw new ArgumentException("The name of the environment variable '" + variable.Key + "' is empty or contains '='", nameof(options));

            return EncodeString(variable.Key + "=" + variable.Value, nameof(options));
        }).ToArray();

        _timeProvider = options.TimeProvider ?? throw new ArgumentException("The time provider is null", nameof(options));
        _randomSource = options.RandomSource;
        _startTimestamp = _timeProvider.GetTimestamp();

        _fds.Add(new WasiStreamFd(options.StandardInput, isInput: true));
        _fds.Add(new WasiStreamFd(options.StandardOutput, isInput: false));
        _fds.Add(new WasiStreamFd(options.StandardError, isInput: false));
        foreach (var directory in options.PreopenedDirectories)
        {
            ArgumentNullException.ThrowIfNull(directory, nameof(options));
            _fds.Add(new WasiDirectoryFd(directory.FileSystem, "", directory.GuestPath, WasiRights.Directory, WasiRights.DirectoryInheriting));
        }
    }

    private static byte[] EncodeString(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Contains('\0', StringComparison.Ordinal))
            throw new ArgumentException("The string '" + value + "' contains a null character", parameterName);

        var bytes = new byte[Encoding.UTF8.GetByteCount(value) + 1];
        Encoding.UTF8.GetBytes(value, bytes);
        return bytes;
    }

    public void Dispose()
    {
        foreach (var fd in _fds)
        {
            fd?.Close();
        }

        _fds.Clear();
    }

    // ------------------------------------------------------------------------------------------------------------
    // File descriptors
    // ------------------------------------------------------------------------------------------------------------

    internal int FileDescriptorCount => _fds.Count(fd => fd is not null);

    private WasiFd GetFd(int fd) => (uint)fd < (uint)_fds.Count && _fds[fd] is { } entry ? entry : throw new WasiException(WasiErrno.BadFileDescriptor);

    private int AddFd(WasiFd entry)
    {
        for (var i = 0; i < _fds.Count; i++)
        {
            if (_fds[i] is null)
            {
                _fds[i] = entry;
                return i;
            }
        }

        if (_fds.Count >= MaxFileDescriptors)
        {
            entry.Close();
            throw new WasiException(WasiErrno.TooManyOpenFiles);
        }

        _fds.Add(entry);
        return _fds.Count - 1;
    }

    // ------------------------------------------------------------------------------------------------------------
    // Arguments and environment variables
    // ------------------------------------------------------------------------------------------------------------

    public WasiErrno ArgsSizesGet(Span<byte> memory, uint countAddress, uint bufferSizeAddress) => GetSizes(new WasiMemory(memory), _arguments, countAddress, bufferSizeAddress);

    public WasiErrno ArgsGet(Span<byte> memory, uint pointersAddress, uint bufferAddress) => GetStrings(new WasiMemory(memory), _arguments, pointersAddress, bufferAddress);

    public WasiErrno EnvironSizesGet(Span<byte> memory, uint countAddress, uint bufferSizeAddress) => GetSizes(new WasiMemory(memory), _environment, countAddress, bufferSizeAddress);

    public WasiErrno EnvironGet(Span<byte> memory, uint pointersAddress, uint bufferAddress) => GetStrings(new WasiMemory(memory), _environment, pointersAddress, bufferAddress);

    private static WasiErrno GetSizes(WasiMemory memory, byte[][] strings, uint countAddress, uint bufferSizeAddress)
    {
        var size = 0L;
        foreach (var value in strings)
        {
            size += value.Length;
        }

        if (size > uint.MaxValue)
            return WasiErrno.Overflow;

        memory.WriteUInt32(countAddress, (uint)strings.Length);
        memory.WriteUInt32(bufferSizeAddress, (uint)size);
        return WasiErrno.Success;
    }

    private static WasiErrno GetStrings(WasiMemory memory, byte[][] strings, uint pointersAddress, uint bufferAddress)
    {
        var address = (ulong)bufferAddress;
        for (var i = 0; i < strings.Length; i++)
        {
            var value = strings[i];
            value.CopyTo(memory.Slice(address, (ulong)value.Length));
            memory.WriteUInt32(pointersAddress + ((ulong)i * 4), (uint)address);
            address += (ulong)value.Length;
        }

        return WasiErrno.Success;
    }

    // ------------------------------------------------------------------------------------------------------------
    // Clocks, random numbers, processes
    // ------------------------------------------------------------------------------------------------------------

    private const uint ClockRealtime = 0;
    private const uint ClockMonotonic = 1;
    private const uint ClockProcessCpuTime = 2;
    private const uint ClockThreadCpuTime = 3;

    public WasiErrno ClockResGet(Span<byte> memory, uint clockId, uint resolutionAddress)
    {
        ulong resolution;
        switch (clockId)
        {
            case ClockRealtime:
                resolution = 100; // The resolution of DateTimeOffset
                break;

            case ClockMonotonic or ClockProcessCpuTime or ClockThreadCpuTime:
                resolution = Math.Max(1UL, (ulong)(1_000_000_000 / Math.Max(1, _timeProvider.TimestampFrequency)));
                break;

            default:
                return WasiErrno.InvalidArgument;
        }

        new WasiMemory(memory).WriteUInt64(resolutionAddress, resolution);
        return WasiErrno.Success;
    }

    public WasiErrno ClockTimeGet(Span<byte> memory, uint clockId, ulong precision, uint timeAddress)
    {
        _ = precision;
        if (!TryGetTime(clockId, out var time))
            return WasiErrno.InvalidArgument;

        new WasiMemory(memory).WriteUInt64(timeAddress, time);
        return WasiErrno.Success;
    }

    /// <summary>Returns the time of a clock in nanoseconds.</summary>
    private bool TryGetTime(uint clockId, out ulong time)
    {
        switch (clockId)
        {
            case ClockRealtime:
                time = ToTimestamp(_timeProvider.GetUtcNow());
                return true;

            case ClockMonotonic or ClockProcessCpuTime or ClockThreadCpuTime:
                // The time since the context was created (a timestamp in nanoseconds can overflow 64 bits): the host
                // does not measure the time the module uses the processor
                time = TimestampToNanoseconds(_timeProvider.GetTimestamp() - _startTimestamp);
                return true;

            default:
                time = 0;
                return false;
        }
    }

    private ulong TimestampToNanoseconds(long timestamp)
    {
        if (timestamp <= 0)
            return 0;

        var nanoseconds = (UInt128)(ulong)timestamp * 1_000_000_000 / (ulong)Math.Max(1, _timeProvider.TimestampFrequency);
        return nanoseconds > ulong.MaxValue ? ulong.MaxValue : (ulong)nanoseconds;
    }

    internal static ulong ToTimestamp(DateTimeOffset value)
    {
        var ticks = value.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks;
        return ticks <= 0 ? 0 : (ulong)ticks * 100;
    }

    internal static DateTimeOffset FromTimestamp(ulong nanoseconds) => DateTimeOffset.UnixEpoch.AddTicks((long)(nanoseconds / 100));

    public WasiErrno RandomGet(Span<byte> memory, uint bufferAddress, uint length)
    {
        var buffer = new WasiMemory(memory).Slice(bufferAddress, length);
        if (_randomSource is { } source)
        {
            source(buffer);
        }
        else
        {
            RandomNumberGenerator.Fill(buffer);
        }

        return WasiErrno.Success;
    }

#pragma warning disable CA1822 // The functions are members of the implementation
    public void ProcExit(uint code) => throw new WasiExitException((int)code);

    public WasiErrno ProcRaise(uint signal)
    {
        _ = signal;
        return WasiErrno.NotImplemented;
    }

    public WasiErrno SchedYield() => WasiErrno.Success;
#pragma warning restore CA1822

    // ------------------------------------------------------------------------------------------------------------
    // Sockets: the module cannot open sockets, so no file descriptor is a socket
    // ------------------------------------------------------------------------------------------------------------

    public WasiErrno SockAccept(int fd)
    {
        _ = GetFd(fd);
        return WasiErrno.NotSocket;
    }

    public WasiErrno SockRecv(int fd)
    {
        _ = GetFd(fd);
        return WasiErrno.NotSocket;
    }

    public WasiErrno SockSend(int fd)
    {
        _ = GetFd(fd);
        return WasiErrno.NotSocket;
    }

    public WasiErrno SockShutdown(int fd)
    {
        _ = GetFd(fd);
        return WasiErrno.NotSocket;
    }

    // ------------------------------------------------------------------------------------------------------------
    // poll_oneoff
    // ------------------------------------------------------------------------------------------------------------

    private const int SubscriptionSize = 48;
    private const int EventSize = 32;
    private const byte EventTypeClock = 0;
    private const byte EventTypeFdRead = 1;
    private const byte EventTypeFdWrite = 2;
    private const ushort SubscriptionClockAbsoluteTime = 1;

    public WasiErrno PollOneoff(Span<byte> bytes, uint subscriptionsAddress, uint eventsAddress, uint count, uint eventCountAddress)
    {
        var memory = new WasiMemory(bytes);
        if (count == 0)
            return WasiErrno.InvalidArgument;

        var subscriptions = memory.Slice(subscriptionsAddress, (ulong)count * SubscriptionSize).ToArray();
        _ = memory.Slice(eventsAddress, (ulong)count * EventSize);
        _ = memory.Slice(eventCountAddress, 4);

        // The files are always ready: the events of the files, and the clocks that expired, are reported at once; else the
        // function waits for the first clock
        var events = new List<(ulong UserData, WasiErrno Error, byte Type, ulong Bytes)>();
        var clocks = new List<(ulong UserData, ulong Delay)>();
        for (var i = 0; i < count; i++)
        {
            var subscription = new WasiMemory(subscriptions.AsSpan(i * SubscriptionSize, SubscriptionSize));
            var userData = subscription.ReadUInt64(0);
            var type = subscription.ReadByte(8);
            switch (type)
            {
                case EventTypeClock:
                    var clockId = subscription.ReadUInt32(16);
                    var timeout = subscription.ReadUInt64(24);
                    var flags = subscription.ReadUInt16(40);
                    if (!TryGetTime(clockId, out var now))
                    {
                        events.Add((userData, WasiErrno.InvalidArgument, EventTypeClock, 0));
                    }
                    else
                    {
                        var delay = (flags & SubscriptionClockAbsoluteTime) != 0 ? (timeout > now ? timeout - now : 0) : timeout;
                        clocks.Add((userData, delay));
                    }

                    break;

                case EventTypeFdRead or EventTypeFdWrite:
                    var fd = (int)subscription.ReadUInt32(16);
                    events.Add(GetFdEvent(userData, type, fd));
                    break;

                default:
                    return WasiErrno.InvalidArgument;
            }
        }

        if (events.Count == 0)
        {
            var delay = clocks.Min(clock => clock.Delay);
            Sleep(delay);
            foreach (var clock in clocks)
            {
                if (clock.Delay <= delay)
                {
                    events.Add((clock.UserData, WasiErrno.Success, EventTypeClock, 0));
                }
            }
        }
        else
        {
            foreach (var clock in clocks)
            {
                if (clock.Delay == 0)
                {
                    events.Add((clock.UserData, WasiErrno.Success, EventTypeClock, 0));
                }
            }
        }

        for (var i = 0; i < events.Count; i++)
        {
            var (userData, error, type, available) = events[i];
            var address = eventsAddress + ((ulong)i * EventSize);
            memory.Slice(address, EventSize).Clear();
            memory.WriteUInt64(address, userData);
            memory.WriteUInt16(address + 8, (ushort)error);
            memory.WriteByte(address + 10, type);
            memory.WriteUInt64(address + 16, available);
        }

        memory.WriteUInt32(eventCountAddress, (uint)events.Count);
        return WasiErrno.Success;
    }

    private (ulong UserData, WasiErrno Error, byte Type, ulong Bytes) GetFdEvent(ulong userData, byte type, int fd)
    {
        if ((uint)fd >= (uint)_fds.Count || _fds[fd] is not { } entry)
            return (userData, WasiErrno.BadFileDescriptor, type, 0);

        if ((entry.RightsBase & WasiRights.PollFdReadwrite) == 0)
            return (userData, WasiErrno.NotCapable, type, 0);

        return entry switch
        {
            WasiFileFd file when type == EventTypeFdRead => (userData, WasiErrno.Success, type, GetRemainingBytes(file.Stream)),
            WasiFileFd or WasiStreamFd => (userData, WasiErrno.Success, type, 0),
            _ => (userData, WasiErrno.BadFileDescriptor, type, 0),
        };
    }

    private static ulong GetRemainingBytes(Stream stream)
    {
        try
        {
            var remaining = stream.Length - stream.Position;
            return remaining > 0 ? (ulong)remaining : 0;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ObjectDisposedException)
        {
            return 0;
        }
    }

    private void Sleep(ulong nanoseconds)
    {
        // The delays longer than a day are slept one day at a time: the clock fires only once its whole delay elapsed
        var remaining = nanoseconds / 100;
        while (remaining > 0)
        {
            var delay = TimeSpan.FromTicks((long)Math.Min(remaining, (ulong)TimeSpan.FromDays(1).Ticks));
            remaining -= (ulong)delay.Ticks;
            if (OperatingSystem.IsBrowser())
                throw new WasiException(WasiErrno.NotSupported);

            if (_timeProvider == TimeProvider.System)
            {
                Thread.Sleep(delay);
                continue;
            }

            using var elapsed = new ManualResetEventSlim();
            using var timer = _timeProvider.CreateTimer(state => ((ManualResetEventSlim)state!).Set(), elapsed, delay, Timeout.InfiniteTimeSpan);
            elapsed.Wait();
        }
    }
}
