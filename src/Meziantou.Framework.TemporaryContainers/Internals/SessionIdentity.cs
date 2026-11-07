using System.Diagnostics;

namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>Identifies the run that created a resource: the process, on the machine it ran on.</summary>
/// <param name="SessionId">An identifier of the run, regenerated every time the library is loaded.</param>
/// <param name="Host">The machine the run happened on.</param>
/// <param name="ProcessId">The id of the process.</param>
/// <param name="ProcessStartTime">The start time of the process, in milliseconds since the Unix epoch, or an empty string when it could not be read. Process ids are reused, so the start time is what tells the owner apart from an unrelated process that inherited its id.</param>
internal sealed record SessionIdentity(string SessionId, string Host, string ProcessId, string ProcessStartTime)
{
    /// <summary>The identity of the current run.</summary>
    public static SessionIdentity Current { get; } = new(
        Guid.NewGuid().ToString("N"),
        Environment.MachineName,
        Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
        GetCurrentProcessStartTime())
    {
        MachineId = GetMachineId(),
        ProcessStartTicks = GetProcessStartTicks(Environment.ProcessId) ?? "",
    };

    /// <summary>What tells two machines, or two process id spaces of one machine, apart when they share a host name: a process in a container that runs with the network of its host, WSL and the Windows it runs on. An empty string when nothing better than the host name is known.</summary>
    public string MachineId { get; init; } = "";

    /// <summary>The start time of the process as the Linux kernel records it: clock ticks since boot, which, unlike a wall-clock time, does not move when the clock of the machine is adjusted. An empty string on the other platforms.</summary>
    public string ProcessStartTicks { get; init; } = "";

    private static string GetCurrentProcessStartTime()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException or NotSupportedException)
        {
            // Without a start time the owner can still be identified by its process id, only less reliably.
            return "";
        }
    }

    private static string GetMachineId()
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                // The boot id tells machines apart; the process id namespace tells apart the containers of one machine,
                // whose processes all look like processes of the machine to each other otherwise.
                var bootId = File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim();
                var pidNamespace = new FileInfo("/proc/self/ns/pid").LinkTarget ?? "";
                return "linux:" + bootId + ":" + pidNamespace;
            }

            if (OperatingSystem.IsWindows())
            {
                var machineGuid = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", defaultValue: null) as string;
                return string.IsNullOrEmpty(machineGuid) ? "" : "windows:" + machineGuid;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
        }

        return "";
    }

    /// <summary>Reads the start time of a process, in clock ticks since boot, from <c>/proc/&lt;pid&gt;/stat</c>. Returns <see langword="null"/> when the process does not exist or the platform is not Linux.</summary>
    internal static string? GetProcessStartTicks(int processId)
    {
        if (!OperatingSystem.IsLinux())
            return null;

        try
        {
            var stat = File.ReadAllText(string.Create(CultureInfo.InvariantCulture, $"/proc/{processId}/stat"));
            return ParseStartTicks(stat);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Reads the start time out of the content of <c>/proc/&lt;pid&gt;/stat</c>. The name of the process comes second, between parentheses, and can itself contain spaces and parentheses, so the fields are counted from the last closing one: the state is field 3 and the start time field 22.</summary>
    internal static string? ParseStartTicks(string stat)
    {
        var nameEnd = stat.LastIndexOf(')', StringComparison.Ordinal);
        if (nameEnd < 0)
            return null;

        var fields = stat[(nameEnd + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        const int StartTimeIndex = 22 - 3;
        if (fields.Length <= StartTimeIndex)
            return null;

        // A zombie is a process that already exited and waits for its parent to collect its exit code.
        if (fields[0] is "Z" or "X")
            return null;

        return fields[StartTimeIndex];
    }
}
