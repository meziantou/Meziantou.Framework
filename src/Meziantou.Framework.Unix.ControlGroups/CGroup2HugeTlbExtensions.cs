using System.Runtime.Versioning;

namespace Meziantou.Framework.Unix.ControlGroups;

/// <summary>
/// Extension methods for HugeTLB controller on CGroup2.
/// </summary>
[SupportedOSPlatform("linux")]
public static class CGroup2HugeTlbExtensions
{
    /// <summary>
    /// Sets the HugeTLB usage limit for a specific page size.
    /// </summary>
    /// <param name="cgroup">The cgroup.</param>
    /// <param name="pageSize">The huge page size (e.g., "2MB", "1GB").</param>
    /// <param name="bytes">Maximum usage in bytes, or null for no limit.</param>
    public static void SetHugeTlbMax(this CGroup2 cgroup, string pageSize, long? bytes)
    {
        ArgumentNullException.ThrowIfNull(cgroup);
        ArgumentException.ThrowIfNullOrWhiteSpace(pageSize);

        if (bytes.HasValue && bytes.Value < 0)
            throw new ArgumentOutOfRangeException(nameof(bytes), "Limit must be non-negative.");

        var value = bytes.HasValue
       ? bytes.Value.ToString(CultureInfo.InvariantCulture)
                 : "max";

        var fileName = $"hugetlb.{pageSize}.max";
        CGroup2CpusetExtensions.WriteFileDirect(cgroup, fileName, value);
    }

    /// <summary>
    /// Gets the HugeTLB usage limit for a specific page size.
    /// </summary>
    /// <param name="cgroup">The cgroup.</param>
    /// <param name="pageSize">The huge page size (e.g., "2MB", "1GB").</param>
    /// <returns>The limit in bytes, or null if set to max.</returns>
    public static long? GetHugeTlbMax(this CGroup2 cgroup, string pageSize)
    {
        ArgumentNullException.ThrowIfNull(cgroup);
        ArgumentException.ThrowIfNullOrWhiteSpace(pageSize);

        var fileName = $"hugetlb.{pageSize}.max";
        var content = CGroup2CpusetExtensions.ReadFileDirect(cgroup, fileName);

        if (string.IsNullOrWhiteSpace(content))
            return null;

        content = content.Trim();
        if (content.Equals("max", StringComparison.OrdinalIgnoreCase))
            return null;

        if (long.TryParse(content, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;

        return null;
    }

    /// <summary>
    /// Gets the current HugeTLB usage for a specific page size.
    /// </summary>
    /// <param name="cgroup">The cgroup.</param>
    /// <param name="pageSize">The huge page size (e.g., "2MB", "1GB").</param>
    /// <returns>Current usage in bytes.</returns>
    public static long? GetHugeTlbCurrent(this CGroup2 cgroup, string pageSize)
    {
        ArgumentNullException.ThrowIfNull(cgroup);
        ArgumentException.ThrowIfNullOrWhiteSpace(pageSize);

        var fileName = $"hugetlb.{pageSize}.current";
        var content = CGroup2CpusetExtensions.ReadFileDirect(cgroup, fileName);

        if (string.IsNullOrWhiteSpace(content))
            return null;

        if (long.TryParse(content.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;

        return null;
    }

    /// <summary>
    /// Gets the number of times the HugeTLB limit was hit.
    /// </summary>
    /// <param name="cgroup">The cgroup.</param>
    /// <param name="pageSize">The huge page size (e.g., "2MB", "1GB").</param>
    /// <returns>Number of limit hits.</returns>
    public static long? GetHugeTlbEventsMax(this CGroup2 cgroup, string pageSize)
    {
        ArgumentNullException.ThrowIfNull(cgroup);
        ArgumentException.ThrowIfNullOrWhiteSpace(pageSize);

        var fileName = $"hugetlb.{pageSize}.events";
        var content = CGroup2CpusetExtensions.ReadFileDirect(cgroup, fileName);

        if (string.IsNullOrWhiteSpace(content))
            return null;

        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[0] == "max")
            {
                if (long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                    return value;
            }
        }

        return null;
    }
}
