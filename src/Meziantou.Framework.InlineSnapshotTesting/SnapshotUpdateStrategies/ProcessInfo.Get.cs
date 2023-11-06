using System.Diagnostics;
using Meziantou.Framework.InlineSnapshotTesting.Utils;

namespace Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;

internal sealed partial record ProcessInfo
{
    private static readonly HashSet<string> IdeProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "devenv.exe", "devenv",
        "rider64.exe", "rider64",
        "code.exe", "code",
    };

    internal static ProcessInfo? GetContextProcess()
    {
#if NETSTANDARD2_0
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
#else
        if (OperatingSystem.IsWindows())
#endif
        {
            var contextProcess = Process.GetCurrentProcess().GetAncestorProcesses()
                .FirstOrDefault(p => IdeProcessNames.Contains(p.ProcessName));

            if (contextProcess != null)
            {
                var startTime = new DateTimeOffset(contextProcess.StartTime);
                // Trim milliseconds to avoid some comparison issues
                startTime = new DateTimeOffset(startTime.Ticks - startTime.Ticks % TimeSpan.TicksPerMillisecond, startTime.Offset);

                return new ProcessInfo
                {
                    ProcessId = contextProcess.Id,
                    ProcessName = contextProcess.ProcessName,
                    ProcessStartedAt = startTime,
                };
            }
        }

        return null;
    }
}
