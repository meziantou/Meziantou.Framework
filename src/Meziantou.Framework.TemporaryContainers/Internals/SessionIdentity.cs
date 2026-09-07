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
        GetCurrentProcessStartTime());

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
}
