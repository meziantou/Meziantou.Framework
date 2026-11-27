using System.Security.Cryptography;
using System.Text;
using Meziantou.Framework.WasmEngine.Components;

namespace Meziantou.Framework.WasmEngine.Wasi.Components;

/// <summary>The environment of a WASI component: the options, the clocks and the preopened directories, shared by the interfaces of WASI 0.2 and 0.3.</summary>
internal sealed class WasiComponentState
{
    private readonly long _startTimestamp;
    private (ulong, ulong)? _insecureSeed;

    public WasiComponentState(WasiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        foreach (var argument in options.Arguments)
        {
            ArgumentNullException.ThrowIfNull(argument, nameof(options));
        }

        foreach (var (name, value) in options.EnvironmentVariables)
        {
            if (name.Length == 0 || name.Contains('=', StringComparison.Ordinal))
                throw new ArgumentException("The name of the environment variable '" + name + "' is empty or contains '='", nameof(options));

            ArgumentNullException.ThrowIfNull(value, nameof(options));
        }

        Arguments = [.. options.Arguments];
        EnvironmentVariables = [.. options.EnvironmentVariables];
        StandardInput = options.StandardInput;
        StandardOutput = options.StandardOutput;
        StandardError = options.StandardError;
        TimeProvider = options.TimeProvider ?? throw new ArgumentException("The time provider is null", nameof(options));
        RandomSource = options.RandomSource;
        Preopens = [.. options.PreopenedDirectories.Select(directory => (directory.GuestPath, directory.FileSystem))];
        Network = options.Network;
        HttpClient = options.HttpClient;
        _startTimestamp = TimeProvider.GetTimestamp();
    }

    public string[] Arguments { get; }

    public KeyValuePair<string, string>[] EnvironmentVariables { get; }

    public Stream? StandardInput { get; }

    public Stream? StandardOutput { get; }

    public Stream? StandardError { get; }

    public TimeProvider TimeProvider { get; }

    public WasiRandomSource? RandomSource { get; }

    public (string GuestPath, WasiFileSystem FileSystem)[] Preopens { get; }

    public WasiNetwork? Network { get; }

    public WasiHttpClient? HttpClient { get; }

    /// <summary>The time of the monotonic clock, in nanoseconds since the creation of the context.</summary>
    public ulong MonotonicNow()
    {
        var elapsed = TimeProvider.GetTimestamp() - _startTimestamp;
        if (elapsed <= 0)
            return 0;

        var nanoseconds = (UInt128)(ulong)elapsed * 1_000_000_000 / (ulong)Math.Max(1, TimeProvider.TimestampFrequency);
        return nanoseconds > ulong.MaxValue ? ulong.MaxValue : (ulong)nanoseconds;
    }

    public ulong MonotonicResolution => Math.Max(1UL, (ulong)(1_000_000_000 / Math.Max(1, TimeProvider.TimestampFrequency)));

    public (long Seconds, uint Nanoseconds) SystemNow()
    {
        var ticks = TimeProvider.GetUtcNow().UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks;
        var seconds = Math.DivRem(ticks, TimeSpan.TicksPerSecond, out var remainder);
        if (remainder < 0)
        {
            seconds--;
            remainder += TimeSpan.TicksPerSecond;
        }

        return (seconds, (uint)(remainder * 100));
    }

    /// <summary>The largest number of random bytes that a component can ask for in one call.</summary>
    internal const int MaxRandomBytes = 64 * 1024 * 1024;

    /// <summary>Returns exactly <paramref name="length"/> random bytes: a larger request traps, instead of returning fewer bytes than the component expects.</summary>
    public byte[] GetRandomBytes(ulong length)
    {
        if (length > MaxRandomBytes)
            throw new WasmTrapException("The component asked for " + length.ToString(System.Globalization.CultureInfo.InvariantCulture) + " random bytes, more than the limit of " + MaxRandomBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var bytes = new byte[(int)length];
        if (RandomSource is { } source)
        {
            source(bytes);
        }
        else
        {
            RandomNumberGenerator.Fill(bytes);
        }

        return bytes;
    }

    public ulong GetRandomU64()
    {
        Span<byte> bytes = stackalloc byte[8];
        if (RandomSource is { } source)
        {
            source(bytes);
        }
        else
        {
            RandomNumberGenerator.Fill(bytes);
        }

        return BitConverter.ToUInt64(bytes);
    }

    /// <summary>The seed of <c>wasi:random/insecure-seed</c>: the same value for every call.</summary>
    public (ulong, ulong) InsecureSeed => _insecureSeed ??= (GetRandomU64(), GetRandomU64());

    /// <summary>The delay until the monotonic clock reaches a deadline, rounded up to the next tick (null when it is reached).</summary>
    private TimeSpan? GetDelay(ulong deadline)
    {
        var now = MonotonicNow();
        if (deadline <= now)
            return null;

        var remaining = deadline - now;
        return TimeSpan.FromTicks((long)Math.Min((remaining / 100) + (remaining % 100 == 0 ? 0UL : 1UL), (ulong)TimeSpan.FromDays(1).Ticks));
    }

    /// <summary>Waits until the monotonic clock reaches <paramref name="deadline"/> (in nanoseconds).</summary>
    public void SleepUntil(ulong deadline)
    {
        // The timers can fire a little before the clock reaches the deadline: wait again until it does
        while (GetDelay(deadline) is { } delay)
        {
            if (OperatingSystem.IsBrowser())
                throw new PlatformNotSupportedException("The clocks cannot block on the browser");

            if (TimeProvider == TimeProvider.System)
            {
                Thread.Sleep(delay);
                continue;
            }

            using var elapsed = new ManualResetEventSlim();
            using var timer = TimeProvider.CreateTimer(state => ((ManualResetEventSlim)state!).Set(), elapsed, delay, Timeout.InfiniteTimeSpan);
            elapsed.Wait();
        }
    }

    public async Task DelayUntilAsync(ulong deadline, CancellationToken cancellationToken)
    {
        while (GetDelay(deadline) is { } delay)
        {
            await Task.Delay(delay, TimeProvider, cancellationToken).ConfigureAwait(true);
        }
    }
}

/// <summary>The helpers of the interfaces of WASI: the values of the results, and the functions of the host.</summary>
internal static class WasiComponentValues
{
    public static ComponentValue Unit => ComponentValue.Ok();

    public static ComponentValue String(string value) => ComponentValue.FromString(value);

    public static ComponentValue Resource(ComponentResourceType type, object representation) => ComponentValue.FromResource(new ComponentResource(type, representation));

    public static T Get<T>(ComponentValue value) => (T)value.AsResource().Representation!;

    public static ComponentValue Strings(IEnumerable<string> values) => ComponentValue.FromList(values.Select(ComponentValue.FromString));

    public static ComponentValue Environment(IEnumerable<KeyValuePair<string, string>> variables) => ComponentValue.FromList(variables.Select(variable => ComponentValue.FromTuple(ComponentValue.FromString(variable.Key), ComponentValue.FromString(variable.Value))));

    public static bool HasFlag(ComponentValue flags, string label) => flags.AsFlags().Contains(label, StringComparer.Ordinal);

    public static ComponentFunction Function(ComponentHostCallback callback) => ComponentFunction.FromCallback(callback);

    public static ComponentFunction AsyncFunction(ComponentHostAsyncCallback callback) => ComponentFunction.FromAsyncCallback(callback);

    public static string Decode(ReadOnlySpan<byte> bytes) => Encoding.UTF8.GetString(bytes);
}
