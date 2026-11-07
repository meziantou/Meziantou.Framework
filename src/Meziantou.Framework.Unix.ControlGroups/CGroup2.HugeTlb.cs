namespace Meziantou.Framework.Unix.ControlGroups;

public sealed partial class CGroup2
{
    /// <summary>Sets the HugeTLB usage limit for a specific page size.</summary>
    /// <param name="pageSize">The huge page size (e.g., "2MB", "1GB").</param>
    /// <param name="bytes">Maximum usage in bytes, or null for no limit.</param>
    public void SetHugeTlbMax(string pageSize, long? bytes)
    {
        ValidateSegment(pageSize, nameof(pageSize));
        if (bytes.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(bytes.Value, nameof(bytes));
        }

        var value = bytes.HasValue ? bytes.Value.ToString(CultureInfo.InvariantCulture) : "max";
        var fileName = $"hugetlb.{pageSize}.max";
        WriteFile(fileName, value);
    }

    /// <summary>Gets the HugeTLB usage limit for a specific page size.</summary>
    /// <param name="pageSize">The huge page size (e.g., "2MB", "1GB").</param>
    /// <returns>
    /// The limit in bytes, <see cref="CGroupValueState.NotConfigured"/> when there is no limit, or
    /// <see cref="CGroupValueState.Unavailable"/> when the running kernel does not provide this page size.
    /// </returns>
    public CGroupValue<long> GetHugeTlbMax(string pageSize)
    {
        ValidateSegment(pageSize, nameof(pageSize));

        return ReadLimit($"hugetlb.{pageSize}.max");
    }

    /// <summary>Gets the current HugeTLB usage for a specific page size.</summary>
    /// <param name="pageSize">The huge page size (e.g., "2MB", "1GB").</param>
    /// <returns>Current usage in bytes, or <see cref="CGroupValueState.Unavailable"/> when the running kernel does not provide this page size.</returns>
    public CGroupValue<long> GetHugeTlbCurrent(string pageSize)
    {
        ValidateSegment(pageSize, nameof(pageSize));

        return ReadCount($"hugetlb.{pageSize}.current");
    }

    /// <summary>Gets the number of times the HugeTLB limit was hit.</summary>
    /// <param name="pageSize">The huge page size (e.g., "2MB", "1GB").</param>
    /// <returns>Number of limit hits, or <see cref="CGroupValueState.Unavailable"/> when the running kernel does not provide this page size.</returns>
    public CGroupValue<long> GetHugeTlbEventsMax(string pageSize)
    {
        ValidateSegment(pageSize, nameof(pageSize));

        return ParseHugeTlbEventsMax(ReadFileOrNull($"hugetlb.{pageSize}.events"));
    }

    /// <summary>Parses the content of a <c>hugetlb.&lt;size&gt;.events</c> interface file.</summary>
    /// <param name="content">The content of the interface file, or <see langword="null"/> when it does not exist.</param>
    internal static CGroupValue<long> ParseHugeTlbEventsMax(string? content)
    {
        if (content is null)
            return CGroupValue<long>.Unavailable();

        content = content.Trim();
        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is 2 && parts[0] is "max" && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                return CGroupValue<long>.Configured(value, content);
        }

        return CGroupValue<long>.Invalid(content);
    }
}
