﻿using System.Diagnostics;
using Meziantou.Framework.InlineSnapshotTesting.Utils;

namespace Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;

internal sealed record ProcessInfo
{
    public int ProcessId { get; init; }
    public string ProcessName { get; init; }

    // Process Id can be reused, so you need to also check the start time
    public DateTimeOffset ProcessStartedAt { get; init; }

    private static readonly HashSet<string> IdeProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "devenv.exe",
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
                return new ProcessInfo
                {
                    ProcessId = contextProcess.Id,
                    ProcessName = contextProcess.ProcessName,

                    // Trim milliseconds to avoid some comparison issues
                    ProcessStartedAt = new DateTime(contextProcess.StartTime.Ticks % TimeSpan.TicksPerMillisecond),
                };
            }
        }

        return null;
    }
}
