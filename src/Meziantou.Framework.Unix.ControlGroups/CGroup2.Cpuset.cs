using System.Runtime.Versioning;

namespace Meziantou.Framework.Unix.ControlGroups;

/// <summary>
/// Extension methods for cpuset controller on CGroup2.
/// </summary>
[SupportedOSPlatform("linux")]
public partial class CGroup2
{
    /// <summary>
    /// Sets the CPUs that tasks in this cgroup can use.
    /// </summary>
    /// <param name="cpus">Array of CPU numbers (e.g., [0, 1, 2] for CPUs 0-2).</param>
    public void SetCpusetCpus(params ReadOnlySpan<int> cpus)
    {
        if (cpus.Length == 0)
        {
            SetCpusetCpusRaw("");
            return;
        }

        var ranges = ConvertToRanges(cpus);
        SetCpusetCpusRaw(ranges);
    }

    /// <summary>
    /// Sets the CPUs using a raw format string (e.g., "0-3,6,8-10").
    /// </summary>
    /// <param name="cpuList">CPU list in cgroup format.</param>
    public void SetCpusetCpusRaw(string cpuList)
    {
        WriteFile("cpuset.cpus", cpuList ?? "");
    }

    /// <summary>
    /// Gets the CPUs that tasks in this cgroup can use.
    /// </summary>
    /// <returns>Array of CPU numbers.</returns>
    public int[]? GetCpusetCpus()
    {
        var content = ReadFile("cpuset.cpus");
        if (string.IsNullOrWhiteSpace(content))
            return null;

        return ParseCpuList(content.Trim());
    }

    /// <summary>
    /// Gets the effective CPUs (actually granted by parent).
    /// </summary>
    /// <returns>Array of CPU numbers.</returns>
    public int[]? GetCpusetCpusEffective()
    {
        var content = ReadFile("cpuset.cpus.effective");
        if (string.IsNullOrWhiteSpace(content))
            return null;

        return ParseCpuList(content.Trim());
    }

    /// <summary>
    /// Sets the memory nodes that tasks in this cgroup can use.
    /// </summary>
    /// <param name="nodes">Array of memory node numbers.</param>
    public void SetCpusetMems(params int[] nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        if (nodes.Length == 0)
        {
            SetCpusetMemsRaw("");
            return;
        }

        var ranges = ConvertToRanges(nodes);
        SetCpusetMemsRaw(ranges);
    }

    /// <summary>
    /// Sets the memory nodes using a raw format string.
    /// </summary>
    /// <param name="nodeList">Memory node list in cgroup format.</param>
    public void SetCpusetMemsRaw(string nodeList)
    {
        WriteFile("cpuset.mems", nodeList ?? "");
    }

    /// <summary>
    /// Gets the memory nodes that tasks in this cgroup can use.
    /// </summary>
    /// <returns>Array of memory node numbers.</returns>
    public int[]? GetCpusetMems()
    {
        var content = ReadFile("cpuset.mems");
        if (string.IsNullOrWhiteSpace(content))
            return null;

        return ParseCpuList(content.Trim());
    }

    /// <summary>
    /// Gets the effective memory nodes (actually granted by parent).
    /// </summary>
    /// <returns>Array of memory node numbers.</returns>
    public int[]? GetCpusetMemsEffective()
    {
        var content = ReadFile("cpuset.mems.effective");
        if (string.IsNullOrWhiteSpace(content))
            return null;

        return ParseCpuList(content.Trim());
    }

    /// <summary>
    /// Sets the cpuset partition type.
    /// </summary>
    /// <param name="partitionType">The partition type ("member", "root", or "isolated").</param>
    public void SetCpusetPartition(string partitionType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partitionType);

        WriteFile("cpuset.cpus.partition", partitionType);
    }

    /// <summary>
    /// Gets the cpuset partition type.
    /// </summary>
    /// <returns>The partition type.</returns>
    public string? GetCpusetPartition()
    {
        var content = ReadFile("cpuset.cpus.partition");
        return string.IsNullOrWhiteSpace(content) ? null : content.Trim();
    }

    private static string ConvertToRanges(ReadOnlySpan<int> numbers)
    {
        if (numbers.IsEmpty)
            return "";

        var orderedNumbers = numbers.ToArray();
        Array.Sort(orderedNumbers);
        var sb = new StringBuilder();
        var rangeStart = orderedNumbers[0];
        var rangeEnd = orderedNumbers[0];

        for (var i = 1; i < orderedNumbers.Length; i++)
        {
            if (orderedNumbers[i] == rangeEnd + 1)
            {
                rangeEnd = orderedNumbers[i];
            }
            else
            {
                AppendRange(sb, rangeStart, rangeEnd);
                rangeStart = orderedNumbers[i];
                rangeEnd = orderedNumbers[i];
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
}
