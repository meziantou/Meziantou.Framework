using System.Runtime.Versioning;

namespace Meziantou.Framework.Unix.ControlGroups;

/// <summary>
/// Extension methods for cpuset controller on CGroup2.
/// </summary>
[SupportedOSPlatform("linux")]
public static class CGroup2CpusetExtensions
{
    /// <summary>
    /// Sets the CPUs that tasks in this cgroup can use.
    /// </summary>
    /// <param name="cgroup">The cgroup.</param>
    /// <param name="cpus">Array of CPU numbers (e.g., [0, 1, 2] for CPUs 0-2).</param>
    public static void SetCpusetCpus(this CGroup2 cgroup, params int[] cpus)
    {
        ArgumentNullException.ThrowIfNull(cgroup);
        ArgumentNullException.ThrowIfNull(cpus);

        if (cpus.Length == 0)
        {
            SetCpusetCpusRaw(cgroup, string.Empty);
            return;
        }

        var ranges = ConvertToRanges(cpus);
        SetCpusetCpusRaw(cgroup, ranges);
    }

    /// <summary>
    /// Sets the CPUs using a raw format string (e.g., "0-3,6,8-10").
    /// </summary>
    /// <param name="cgroup">The cgroup.</param>
    /// <param name="cpuList">CPU list in cgroup format.</param>
    public static void SetCpusetCpusRaw(this CGroup2 cgroup, string cpuList)
    {
        ArgumentNullException.ThrowIfNull(cgroup);
        cgroup.WriteFileDirect("cpuset.cpus", cpuList ?? string.Empty);
    }

    /// <summary>
    /// Gets the CPUs that tasks in this cgroup can use.
    /// </summary>
    /// <param name="cgroup">The cgroup.</param>
    /// <returns>Array of CPU numbers.</returns>
    public static int[]? GetCpusetCpus(this CGroup2 cgroup)
    {
        ArgumentNullException.ThrowIfNull(cgroup);

        var content = cgroup.ReadFileDirect("cpuset.cpus");
        if (string.IsNullOrWhiteSpace(content))
            return null;

        return ParseCpuList(content.Trim());
    }

    /// <summary>
    /// Gets the effective CPUs (actually granted by parent).
    /// </summary>
    /// <param name="cgroup">The cgroup.</param>
    /// <returns>Array of CPU numbers.</returns>
    public static int[]? GetCpusetCpusEffective(this CGroup2 cgroup)
    {
        ArgumentNullException.ThrowIfNull(cgroup);

        var content = cgroup.ReadFileDirect("cpuset.cpus.effective");
        if (string.IsNullOrWhiteSpace(content))
            return null;

        return ParseCpuList(content.Trim());
    }

    /// <summary>
    /// Sets the memory nodes that tasks in this cgroup can use.
    /// </summary>
    /// <param name="cgroup">The cgroup.</param>
    /// <param name="nodes">Array of memory node numbers.</param>
    public static void SetCpusetMems(this CGroup2 cgroup, params int[] nodes)
    {
        ArgumentNullException.ThrowIfNull(cgroup);
        ArgumentNullException.ThrowIfNull(nodes);

        if (nodes.Length == 0)
        {
            SetCpusetMemsRaw(cgroup, string.Empty);
            return;
        }

        var ranges = ConvertToRanges(nodes);
        SetCpusetMemsRaw(cgroup, ranges);
    }

    /// <summary>
    /// Sets the memory nodes using a raw format string.
    /// </summary>
    /// <param name="cgroup">The cgroup.</param>
    /// <param name="nodeList">Memory node list in cgroup format.</param>
    public static void SetCpusetMemsRaw(this CGroup2 cgroup, string nodeList)
    {
        ArgumentNullException.ThrowIfNull(cgroup);
        cgroup.WriteFileDirect("cpuset.mems", nodeList ?? string.Empty);
    }

    /// <summary>
    /// Gets the memory nodes that tasks in this cgroup can use.
    /// </summary>
    /// <param name="cgroup">The cgroup.</param>
    /// <returns>Array of memory node numbers.</returns>
    public static int[]? GetCpusetMems(this CGroup2 cgroup)
    {
        ArgumentNullException.ThrowIfNull(cgroup);

        var content = cgroup.ReadFileDirect("cpuset.mems");
        if (string.IsNullOrWhiteSpace(content))
            return null;

        return ParseCpuList(content.Trim());
    }

    /// <summary>
    /// Gets the effective memory nodes (actually granted by parent).
    /// </summary>
    /// <param name="cgroup">The cgroup.</param>
    /// <returns>Array of memory node numbers.</returns>
    public static int[]? GetCpusetMemsEffective(this CGroup2 cgroup)
    {
        ArgumentNullException.ThrowIfNull(cgroup);

        var content = cgroup.ReadFileDirect("cpuset.mems.effective");
        if (string.IsNullOrWhiteSpace(content))
            return null;

        return ParseCpuList(content.Trim());
    }

    /// <summary>
    /// Sets the cpuset partition type.
    /// </summary>
    /// <param name="cgroup">The cgroup.</param>
    /// <param name="partitionType">The partition type ("member", "root", or "isolated").</param>
    public static void SetCpusetPartition(this CGroup2 cgroup, string partitionType)
    {
        ArgumentNullException.ThrowIfNull(cgroup);
        ArgumentException.ThrowIfNullOrWhiteSpace(partitionType);

        cgroup.WriteFileDirect("cpuset.cpus.partition", partitionType);
    }

    /// <summary>
    /// Gets the cpuset partition type.
    /// </summary>
    /// <param name="cgroup">The cgroup.</param>
    /// <returns>The partition type.</returns>
    public static string? GetCpusetPartition(this CGroup2 cgroup)
    {
        ArgumentNullException.ThrowIfNull(cgroup);

        var content = cgroup.ReadFileDirect("cpuset.cpus.partition");
        return string.IsNullOrWhiteSpace(content) ? null : content.Trim();
    }

    private static string ConvertToRanges(int[] numbers)
    {
        if (numbers.Length == 0)
            return string.Empty;

        Array.Sort(numbers);
        var sb = new StringBuilder();
        var rangeStart = numbers[0];
        var rangeEnd = numbers[0];

        for (var i = 1; i < numbers.Length; i++)
        {
            if (numbers[i] == rangeEnd + 1)
            {
                rangeEnd = numbers[i];
            }
            else
            {
                AppendRange(sb, rangeStart, rangeEnd);
                rangeStart = numbers[i];
                rangeEnd = numbers[i];
            }
        }

        AppendRange(sb, rangeStart, rangeEnd);
        return sb.ToString();
    }

    private static void AppendRange(StringBuilder sb, int start, int end)
    {
        if (sb.Length > 0)
            sb.Append(',');

        if (start == end)
        {
            sb.Append(start.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            sb.Append(start.ToString(CultureInfo.InvariantCulture));
            sb.Append('-');
            sb.Append(end.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static int[] ParseCpuList(string cpuList)
    {
        if (string.IsNullOrWhiteSpace(cpuList))
            return [];

        var result = new List<int>();
        var parts = cpuList.Split(',', StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Contains('-', StringComparison.Ordinal))
            {
                var range = trimmed.Split('-', 2);
                if (range.Length == 2 &&
                    int.TryParse(range[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var start) &&
                    int.TryParse(range[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var end))
                {
                    for (var i = start; i <= end; i++)
                    {
                        result.Add(i);
                    }
                }
            }
            else if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cpu))
            {
                result.Add(cpu);
            }
        }

        return [.. result];
    }

    // Internal helper methods for file access
    internal static string ReadFileDirect(this CGroup2 cgroup, string fileName)
    {
        var filePath = System.IO.Path.Combine(cgroup.Path, fileName);

        try
        {
            return File.ReadAllText(filePath);
        }
        catch (FileNotFoundException)
        {
            return string.Empty;
        }
        catch (DirectoryNotFoundException)
        {
            return string.Empty;
        }
    }

    internal static void WriteFileDirect(this CGroup2 cgroup, string fileName, string content)
    {
        var filePath = System.IO.Path.Combine(cgroup.Path, fileName);
        File.WriteAllText(filePath, content);
    }
}
