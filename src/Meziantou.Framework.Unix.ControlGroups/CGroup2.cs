using System.Diagnostics;
using System.Runtime.Versioning;

namespace Meziantou.Framework.Unix.ControlGroups;

/// <summary>
/// Represents a cgroup v2 control group for managing and limiting resource usage of processes.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed partial class CGroup2
{
    private const string CGroupV2MountPoint = "/sys/fs/cgroup";

    private readonly string _path;

    /// <summary>
    /// Gets the name of the cgroup.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the full path to the cgroup directory.
    /// </summary>
    internal string Path => _path;

    /// <summary>
    /// Gets the parent cgroup, or null if this is the root cgroup.
    /// </summary>
    public CGroup2? Parent { get; }

    /// <summary>
    /// Creates a new cgroup instance at the specified path.
    /// </summary>
    /// <param name="name">The name of the cgroup (relative to parent, or absolute path from /sys/fs/cgroup).</param>
    /// <param name="parent">The parent cgroup, or null for root-level cgroups.</param>
    private CGroup2(string name, CGroup2? parent)
    {
        Name = name;
        Parent = parent;

        if (parent is null)
        {
            // Root level cgroup or absolute path
            _path = name.StartsWith('/') ? name : System.IO.Path.Combine(CGroupV2MountPoint, name);
        }
        else
        {
            _path = System.IO.Path.Combine(parent._path, name);
        }
    }

    /// <summary>
    /// Gets the root cgroup.
    /// </summary>
    public static CGroup2 Root => new(CGroupV2MountPoint, parent: null);

    /// <summary>
    /// Gets an existing child cgroup without creating it.
    /// </summary>
    /// <param name="name">The name of the child cgroup.</param>
    /// <returns>The child cgroup.</returns>
    public CGroup2? GetChild(string name)
    {
        if (!Directory.Exists(System.IO.Path.Combine(_path, name)))
            return null;

        return new CGroup2(name, this);
    }

    /// <summary>
    /// Creates or gets a child cgroup.
    /// </summary>
    /// <param name="name">The name of the child cgroup.</param>
    /// <returns>The child cgroup.</returns>
    public CGroup2 CreateOrGetChild(string name)
    {
        var child = new CGroup2(name, this);
        if (!Directory.Exists(child._path))
        {
            Directory.CreateDirectory(child._path);
        }
        return child;
    }

    /// <summary>
    /// Deletes this cgroup. The cgroup must be empty (no processes and no child cgroups).
    /// </summary>
    /// <exception cref="IOException">If the cgroup cannot be deleted.</exception>
    public void Delete()
    {
        Directory.Delete(_path);
    }

    /// <summary>
    /// Checks if this cgroup exists.
    /// </summary>
    public bool Exists() => Directory.Exists(_path);

    #region Process Management

    /// <summary>
    /// Adds a process to this cgroup.
    /// </summary>
    /// <param name="process">The process to add.</param>
    public void AddProcess(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        AddProcess(process.Id);
    }

    /// <summary>
    /// Adds a process to this cgroup by its PID.
    /// </summary>
    /// <param name="pid">The process ID.</param>
    public void AddProcess(int pid)
    {
        WriteFile("cgroup.procs", pid.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Adds a thread to this cgroup by its TID.
    /// </summary>
    /// <param name="tid">The thread ID.</param>
    public void AddThread(int tid)
    {
        WriteFile("cgroup.threads", tid.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Gets all process IDs in this cgroup.
    /// </summary>
    public IEnumerable<int> GetProcesses()
    {
        var content = ReadFile("cgroup.procs");
        if (string.IsNullOrWhiteSpace(content))
            yield break;

        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
            {
                yield return pid;
            }
        }
    }

    /// <summary>
    /// Gets all thread IDs in this cgroup.
    /// </summary>
    public IEnumerable<int> GetThreads()
    {
        var content = ReadFile("cgroup.threads");
        if (string.IsNullOrWhiteSpace(content))
            yield break;

        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tid))
            {
                yield return tid;
            }
        }
    }

    #endregion

    #region Controllers

    /// <summary>
    /// Gets the list of available controllers.
    /// </summary>
    public IEnumerable<string> GetAvailableControllers()
    {
        var content = ReadFile("cgroup.controllers");
        if (string.IsNullOrWhiteSpace(content))
            return [];

        return content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Gets the list of enabled controllers in the subtree.
    /// </summary>
    public IEnumerable<string> GetEnabledControllers()
    {
        var content = ReadFile("cgroup.subtree_control");
        if (string.IsNullOrWhiteSpace(content))
            return [];

        return content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Enables a controller in the subtree.
    /// </summary>
    /// <param name="controller">The controller name (e.g., "cpu", "memory", "io").</param>
    public void EnableController(string controller)
    {
        WriteFile("cgroup.subtree_control", $"+{controller}");
    }

    /// <summary>
    /// Disables a controller in the subtree.
    /// </summary>
    /// <param name="controller">The controller name.</param>
    public void DisableController(string controller)
    {
        WriteFile("cgroup.subtree_control", $"-{controller}");
    }

    /// <summary>
    /// Enables multiple controllers in the subtree.
    /// </summary>
    /// <param name="controllers">The controller names.</param>
    public void EnableControllers(params ReadOnlySpan<string> controllers)
    {
        var sb = new StringBuilder();
        foreach (var controller in controllers)
        {
            if (sb.Length > 0)
                sb.Append(' ');
            sb.Append('+').Append(controller);
        }
        WriteFile("cgroup.subtree_control", sb.ToString());
    }

    #endregion

    #region CPU Controller

    /// <summary>
    /// Sets the CPU weight (relative share of CPU time).
    /// </summary>
    /// <param name="weight">Weight value between 1 and 10000 (default is 100).</param>
    public void SetCpuWeight(int weight)
    {
        if (weight < 1 || weight > 10000)
            throw new ArgumentOutOfRangeException(nameof(weight), "CPU weight must be between 1 and 10000.");

        WriteFile("cpu.weight", weight.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Gets the CPU weight.
    /// </summary>
    public int? GetCpuWeight()
    {
        var content = ReadFile("cpu.weight");
        if (string.IsNullOrWhiteSpace(content))
            return null;

        if (int.TryParse(content.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var weight))
            return weight;

        return null;
    }

    /// <summary>
    /// Sets the CPU maximum bandwidth limit.
    /// </summary>
    /// <param name="maxMicroseconds">Maximum time in microseconds that the cgroup can run during one period.</param>
    /// <param name="periodMicroseconds">Period in microseconds (default is 100000 = 100ms).</param>
    public void SetCpuMax(long? maxMicroseconds, long periodMicroseconds = 100000)
    {
        if (periodMicroseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(periodMicroseconds), "Period must be positive.");

        var maxStr = maxMicroseconds.HasValue
      ? maxMicroseconds.Value.ToString(CultureInfo.InvariantCulture)
      : "max";

        WriteFile("cpu.max", $"{maxStr} {periodMicroseconds.ToString(CultureInfo.InvariantCulture)}");
    }

    /// <summary>
    /// Removes the CPU maximum bandwidth limit.
    /// </summary>
    public void RemoveCpuMax()
    {
        SetCpuMax(null, 100000);
    }

    #endregion

    #region Memory Controller

    /// <summary>
    /// Sets the memory maximum limit in bytes.
    /// </summary>
    /// <param name="bytes">Maximum memory in bytes, or null for no limit.</param>
    public void SetMemoryMax(long? bytes)
    {
        if (bytes.HasValue && bytes.Value < 0)
            throw new ArgumentOutOfRangeException(nameof(bytes), "Memory limit must be non-negative.");

        var value = bytes.HasValue ? bytes.Value.ToString(CultureInfo.InvariantCulture) : "max";

        WriteFile("memory.max", value);
    }

    /// <summary>
    /// Gets the memory maximum limit in bytes.
    /// </summary>
    public long? GetMemoryMax()
    {
        var content = ReadFile("memory.max");
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
    /// Sets the memory high limit (soft limit with throttling).
    /// </summary>
    /// <param name="bytes">High memory limit in bytes, or null for no limit.</param>
    public void SetMemoryHigh(long? bytes)
    {
        if (bytes.HasValue && bytes.Value < 0)
            throw new ArgumentOutOfRangeException(nameof(bytes), "Memory limit must be non-negative.");

        var value = bytes.HasValue
           ? bytes.Value.ToString(CultureInfo.InvariantCulture)
               : "max";

        WriteFile("memory.high", value);
    }

    /// <summary>
    /// Sets the memory low limit (best-effort protection).
    /// </summary>
    /// <param name="bytes">Low memory limit in bytes, or null for no protection.</param>
    public void SetMemoryLow(long? bytes)
    {
        if (bytes.HasValue && bytes.Value < 0)
            throw new ArgumentOutOfRangeException(nameof(bytes), "Memory limit must be non-negative.");

        var value = bytes.HasValue
     ? bytes.Value.ToString(CultureInfo.InvariantCulture)
            : "0";

        WriteFile("memory.low", value);
    }

    /// <summary>
    /// Sets the memory min limit (hard protection).
    /// </summary>
    /// <param name="bytes">Min memory limit in bytes, or null for no protection.</param>
    public void SetMemoryMin(long? bytes)
    {
        if (bytes.HasValue && bytes.Value < 0)
            throw new ArgumentOutOfRangeException(nameof(bytes), "Memory limit must be non-negative.");

        var value = bytes.HasValue ? bytes.Value.ToString(CultureInfo.InvariantCulture) : "0";
        WriteFile("memory.min", value);
    }

    /// <summary>
    /// Gets the current memory usage in bytes.
    /// </summary>
    public long? GetMemoryCurrent()
    {
        var content = ReadFile("memory.current");
        if (string.IsNullOrWhiteSpace(content))
            return null;

        if (long.TryParse(content.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;

        return null;
    }

    /// <summary>
    /// Sets the swap maximum limit in bytes.
    /// </summary>
    /// <param name="bytes">Maximum swap in bytes, or null for no limit.</param>
    public void SetSwapMax(long? bytes)
    {
        if (bytes.HasValue && bytes.Value < 0)
            throw new ArgumentOutOfRangeException(nameof(bytes), "Swap limit must be non-negative.");

        var value = bytes.HasValue ? bytes.Value.ToString(CultureInfo.InvariantCulture) : "max";

        WriteFile("memory.swap.max", value);
    }

    #endregion

    #region IO Controller

    /// <summary>
    /// Sets the IO weight for a device.
    /// </summary>
    /// <param name="major">Device major number.</param>
    /// <param name="minor">Device minor number.</param>
    /// <param name="weight">Weight value between 1 and 10000 (default is 100).</param>
    public void SetIoWeight(int major, int minor, int weight)
    {
        if (weight < 1 || weight > 10000)
            throw new ArgumentOutOfRangeException(nameof(weight), "IO weight must be between 1 and 10000.");

        WriteFile("io.weight", $"{major.ToString(CultureInfo.InvariantCulture)}:{minor.ToString(CultureInfo.InvariantCulture)} {weight.ToString(CultureInfo.InvariantCulture)}");
    }

    /// <summary>
    /// Sets the default IO weight.
    /// </summary>
    /// <param name="weight">Weight value between 1 and 10000 (default is 100).</param>
    public void SetDefaultIoWeight(int weight)
    {
        if (weight < 1 || weight > 10000)
            throw new ArgumentOutOfRangeException(nameof(weight), "IO weight must be between 1 and 10000.");

        WriteFile("io.weight", $"default {weight.ToString(CultureInfo.InvariantCulture)}");
    }

    /// <summary>
    /// Sets IO bandwidth limits for a device.
    /// </summary>
    /// <param name="major">Device major number.</param>
    /// <param name="minor">Device minor number.</param>
    /// <param name="readBytesPerSecond">Read bandwidth limit in bytes per second, or null for no limit.</param>
    /// <param name="writeBytesPerSecond">Write bandwidth limit in bytes per second, or null for no limit.</param>
    /// <param name="readIopsPerSecond">Read IOPS limit, or null for no limit.</param>
    /// <param name="writeIopsPerSecond">Write IOPS limit, or null for no limit.</param>
    public void SetIoMax(int major, int minor, long? readBytesPerSecond = null, long? writeBytesPerSecond = null,
long? readIopsPerSecond = null, long? writeIopsPerSecond = null)
    {
        var parts = new List<string>
        {
            $"{major.ToString(CultureInfo.InvariantCulture)}:{minor.ToString(CultureInfo.InvariantCulture)}",
        };

        if (readBytesPerSecond.HasValue)
            parts.Add($"rbps={readBytesPerSecond.Value.ToString(CultureInfo.InvariantCulture)}");

        if (writeBytesPerSecond.HasValue)
            parts.Add($"wbps={writeBytesPerSecond.Value.ToString(CultureInfo.InvariantCulture)}");

        if (readIopsPerSecond.HasValue)
            parts.Add($"riops={readIopsPerSecond.Value.ToString(CultureInfo.InvariantCulture)}");

        if (writeIopsPerSecond.HasValue)
            parts.Add($"wiops={writeIopsPerSecond.Value.ToString(CultureInfo.InvariantCulture)}");

        WriteFile("io.max", string.Join(" ", parts));
    }

    /// <summary>
    /// Removes IO limits for a device.
    /// </summary>
    /// <param name="major">Device major number.</param>
    /// <param name="minor">Device minor number.</param>
    public void RemoveIoMax(int major, int minor)
    {
        WriteFile("io.max", $"{major.ToString(CultureInfo.InvariantCulture)}:{minor.ToString(CultureInfo.InvariantCulture)} rbps=max wbps=max riops=max wiops=max");
    }

    #endregion

    #region PID Controller

    /// <summary>
    /// Sets the maximum number of processes (PIDs) allowed.
    /// </summary>
    /// <param name="max">Maximum number of processes, or null for no limit.</param>
    public void SetPidsMax(long? max)
    {
        if (max.HasValue && max.Value < 0)
            throw new ArgumentOutOfRangeException(nameof(max), "PIDs limit must be non-negative.");

        var value = max.HasValue
              ? max.Value.ToString(CultureInfo.InvariantCulture)
    : "max";

        WriteFile("pids.max", value);
    }

    /// <summary>
    /// Gets the maximum number of processes allowed.
    /// </summary>
    public long? GetPidsMax()
    {
        var content = ReadFile("pids.max");
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
    /// Gets the current number of processes.
    /// </summary>
    public long? GetPidsCurrent()
    {
        var content = ReadFile("pids.current");
        if (string.IsNullOrWhiteSpace(content))
            return null;

        if (long.TryParse(content.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;

        return null;
    }

    #endregion

    #region Freezer

    /// <summary>
    /// Freezes all processes in this cgroup.
    /// </summary>
    public void Freeze()
    {
        WriteFile("cgroup.freeze", "1");
    }

    /// <summary>
    /// Unfreezes all processes in this cgroup.
    /// </summary>
    public void Unfreeze()
    {
        WriteFile("cgroup.freeze", "0");
    }

    /// <summary>
    /// Gets whether the cgroup is frozen.
    /// </summary>
    public bool IsFrozen()
    {
        var events = ReadFile("cgroup.events");
        if (string.IsNullOrWhiteSpace(events))
            return false;

        foreach (var line in events.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[0] == "frozen")
            {
                return parts[1] == "1";
            }
        }

        return false;
    }

    #endregion

    #region Kill

    /// <summary>
    /// Kills all processes in this cgroup and its descendants.
    /// </summary>
    public void Kill()
    {
        WriteFile("cgroup.kill", "1");
    }

    #endregion

    #region Helper Methods

    private string ReadFile(string fileName)
    {
        var filePath = System.IO.Path.Combine(_path, fileName);

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

    private void WriteFile(string fileName, string content)
    {
        var filePath = System.IO.Path.Combine(_path, fileName);
        File.WriteAllText(filePath, content);
    }

    #endregion

    #region Statistics

    /// <summary>
    /// Gets CPU statistics for this cgroup.
    /// </summary>
    public CpuStat? GetCpuStat()
    {
        var content = ReadFile("cpu.stat");
        if (string.IsNullOrWhiteSpace(content))
            return null;

        return CpuStat.Parse(content);
    }

    /// <summary>
    /// Gets memory statistics for this cgroup.
    /// </summary>
    public MemoryStat? GetMemoryStat()
    {
        var content = ReadFile("memory.stat");
        if (string.IsNullOrWhiteSpace(content))
            return null;

        return MemoryStat.Parse(content);
    }

    #endregion

    /// <summary>
    /// Returns a string representation of this cgroup.
    /// </summary>
    public override string ToString() => _path;
}
