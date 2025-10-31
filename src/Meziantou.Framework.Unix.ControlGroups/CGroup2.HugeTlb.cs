namespace Meziantou.Framework.Unix.ControlGroups;

/// <summary>
/// Extension methods for HugeTLB controller on CGroup2.
/// </summary>
public partial class CGroup2
{
    /// <summary>
    /// Sets the HugeTLB usage limit for a specific page size.
    /// </summary>
    /// <param name="pageSize">The huge page size (e.g., "2MB", "1GB").</param>
    /// <param name="bytes">Maximum usage in bytes, or null for no limit.</param>
    public void SetHugeTlbMax(string pageSize, long? bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageSize);

        if (bytes.HasValue && bytes.Value < 0)
            throw new ArgumentOutOfRangeException(nameof(bytes), "Limit must be non-negative.");

        var value = bytes.HasValue ? bytes.Value.ToString(CultureInfo.InvariantCulture) : "max";
        var fileName = $"hugetlb.{pageSize}.max";
        WriteFile(fileName, value);
    }

    /// <summary>
    /// Gets the HugeTLB usage limit for a specific page size.
    /// </summary>
    /// <param name="pageSize">The huge page size (e.g., "2MB", "1GB").</param>
    /// <returns>The limit in bytes, or null if set to max.</returns>
    public long? GetHugeTlbMax(string pageSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageSize);

        var fileName = $"hugetlb.{pageSize}.max";
        var content = ReadFile(fileName);

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
    /// <param name="pageSize">The huge page size (e.g., "2MB", "1GB").</param>
    /// <returns>Current usage in bytes.</returns>
    public long? GetHugeTlbCurrent(string pageSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageSize);

        var fileName = $"hugetlb.{pageSize}.current";
        var content = ReadFile(fileName);

        if (string.IsNullOrWhiteSpace(content))
            return null;

        if (long.TryParse(content.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;

        return null;
    }

    /// <summary>
    /// Gets the number of times the HugeTLB limit was hit.
    /// </summary>
    /// <param name="pageSize">The huge page size (e.g., "2MB", "1GB").</param>
    /// <returns>Number of limit hits.</returns>
    public long? GetHugeTlbEventsMax(string pageSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageSize);

        var fileName = $"hugetlb.{pageSize}.events";
        var content = ReadFile(fileName);

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
