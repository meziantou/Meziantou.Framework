using System.ComponentModel;
using System.Diagnostics;

namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>Reads the ownership labels of a resource to tell whether the run that created it is still alive.</summary>
internal static class ResourceOwnership
{
    // Both sides of the comparison come from the operating system, but they travel through a millisecond timestamp and,
    // on some platforms, through a value the kernel only reports at second precision.
    private static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(5);

    private enum OwnerStatus
    {
        /// <summary>No process with this id is running.</summary>
        Gone,

        /// <summary>A process with this id is running, and its start time is known.</summary>
        Running,

        /// <summary>A process with this id may be running, but nothing more can be said about it.</summary>
        Unknown,
    }

    /// <summary>Determines whether the process that created a resource is still running.</summary>
    /// <param name="labels">The labels of the resource.</param>
    /// <returns><see langword="true"/> when the owner is known to be gone; <see langword="false"/> when it is still running, or when the labels do not identify it well enough to tell.</returns>
    public static bool IsOwnerGone(IReadOnlyDictionary<string, string> labels)
    {
        // A daemon can be shared between machines, and a process id only means something on the machine that recorded
        // it. A resource created elsewhere is therefore never reported as orphaned.
        if (!labels.TryGetValue(ResourceLabels.Host, out var host) || !string.Equals(host, SessionIdentity.Current.Host, StringComparison.Ordinal))
            return false;

        if (!labels.TryGetValue(ResourceLabels.ProcessId, out var processIdText) ||
            !int.TryParse(processIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var processId))
        {
            return false;
        }

        var status = GetOwnerStatus(processId, out var startTime);
        if (status is OwnerStatus.Gone)
            return true;

        if (status is OwnerStatus.Unknown)
            return false;

        // The process id is in use again. Whether it belongs to the owner is decided by the start time; without one in
        // the labels, the resource is left alone rather than removed from under a process that may well be the owner.
        if (!labels.TryGetValue(ResourceLabels.ProcessStartTime, out var ownerStartTimeText) ||
            !long.TryParse(ownerStartTimeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ownerStartTimeMilliseconds))
        {
            return false;
        }

        var ownerStartTime = DateTimeOffset.FromUnixTimeMilliseconds(ownerStartTimeMilliseconds);
        return (startTime - ownerStartTime).Duration() > StartTimeTolerance;
    }

    private static OwnerStatus GetOwnerStatus(int processId, out DateTimeOffset startTime)
    {
        startTime = default;
        try
        {
            using var process = Process.GetProcessById(processId);

            // A process that already exited can still be looked up on Unix, where it stays around as a zombie until
            // its parent reaps it.
            if (process.HasExited)
                return OwnerStatus.Gone;

            startTime = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
            return OwnerStatus.Running;
        }
        catch (ArgumentException)
        {
            // No process with this id is running.
            return OwnerStatus.Gone;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or PlatformNotSupportedException or Win32Exception)
        {
            // The process exists but cannot be inspected, for instance because it belongs to another user. Removing
            // the resources of what may be a live run is worse than leaving them behind.
            return OwnerStatus.Unknown;
        }
    }
}
