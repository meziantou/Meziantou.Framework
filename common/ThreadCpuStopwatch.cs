using System.Runtime.InteropServices;

namespace Meziantou.Framework;

/// <summary>
/// Measures the CPU time of the current thread, for the tests that check that an operation does not take quadratic time.
/// Unlike wall-clock time, it does not include the time spent waiting for a core, which is most of the time of a test
/// on a busy CI runner where the other tests and test processes compete for the cores. It must be started, stopped and read
/// on the same thread.
/// </summary>
internal sealed class ThreadCpuStopwatch
{
    // CLOCK_THREAD_CPUTIME_ID
    private const int LinuxThreadCpuTimeClock = 3;
    private const int MacOSThreadCpuTimeClock = 16;

    private readonly TimeSpan _start;
    private TimeSpan? _elapsed;

    private ThreadCpuStopwatch()
    {
        _start = GetCurrentThreadCpuTime();
    }

    public TimeSpan Elapsed => _elapsed ?? GetCurrentThreadCpuTime() - _start;

    public static ThreadCpuStopwatch StartNew() => new();

    public void Stop()
    {
        _elapsed ??= GetCurrentThreadCpuTime() - _start;
    }

    public static TimeSpan GetCurrentThreadCpuTime()
    {
        if (OperatingSystem.IsWindows())
        {
            // FILETIMEs: 100-nanosecond units, like ticks.
            var kernel32 = NativeLibrary.Load("kernel32.dll");
            var currentThread = unsafe(((delegate* unmanaged<nint>)NativeLibrary.GetExport(kernel32, "GetCurrentThread"))());
            long creationTime, exitTime, kernelTime, userTime;
            var succeeded = unsafe(((delegate* unmanaged<nint, long*, long*, long*, long*, int>)NativeLibrary.GetExport(kernel32, "GetThreadTimes"))(currentThread, &creationTime, &exitTime, &kernelTime, &userTime));
            if (succeeded is 0)
                throw new InvalidOperationException("GetThreadTimes failed.");

            return TimeSpan.FromTicks(kernelTime + userTime);
        }

        var clockGetTime = NativeLibrary.GetExport(NativeLibrary.GetMainProgramHandle(), "clock_gettime");
        TimeSpec time;
        var result = unsafe(((delegate* unmanaged<int, TimeSpec*, int>)clockGetTime)(OperatingSystem.IsMacOS() ? MacOSThreadCpuTimeClock : LinuxThreadCpuTimeClock, &time));
        if (result is not 0)
            throw new InvalidOperationException("clock_gettime failed.");

        return TimeSpan.FromTicks((time.Seconds * TimeSpan.TicksPerSecond) + (time.Nanoseconds / 100));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TimeSpec
    {
        public long Seconds;
        public long Nanoseconds;
    }
}
