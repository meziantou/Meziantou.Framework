namespace Meziantou.Framework.Unix.ControlGroups;

/// <summary>Represents memory statistics for a cgroup.</summary>
public sealed class MemoryStat
{
    /// <summary>Memory used in anonymous mappings (bytes).</summary>
    public long Anon { get; private set; }

    /// <summary>Memory used to cache filesystem data (bytes).</summary>
    public long File { get; private set; }

    /// <summary>Total kernel memory (bytes).</summary>
    public long Kernel { get; private set; }

    /// <summary>Memory allocated to kernel stacks (bytes).</summary>
    public long KernelStack { get; private set; }

    /// <summary>Memory allocated for page tables (bytes).</summary>
    public long PageTables { get; private set; }

    /// <summary>Memory used for storing per-cpu kernel data structures (bytes).</summary>
    public long PerCpu { get; private set; }

    /// <summary>Memory used in network transmission buffers (bytes).</summary>
    public long Sock { get; private set; }

    /// <summary>Memory cached in swap (bytes).</summary>
    public long SwapCached { get; private set; }

    /// <summary>Cached filesystem data mapped with mmap (bytes).</summary>
    public long FileMapped { get; private set; }

    /// <summary>Cached filesystem data that was modified but not yet written back (bytes).</summary>
    public long FileDirty { get; private set; }

    /// <summary>Cached filesystem data being written back to disk (bytes).</summary>
    public long FileWriteback { get; private set; }

    /// <summary>Inactive anonymous memory (bytes).</summary>
    public long InactiveAnon { get; private set; }

    /// <summary>Active anonymous memory (bytes).</summary>
    public long ActiveAnon { get; private set; }

    /// <summary>Inactive file-backed memory (bytes).</summary>
    public long InactiveFile { get; private set; }

    /// <summary>Active file-backed memory (bytes).</summary>
    public long ActiveFile { get; private set; }

    /// <summary>Unevictable memory (bytes).</summary>
    public long Unevictable { get; private set; }

    /// <summary>Reclaimable slab memory (bytes).</summary>
    public long SlabReclaimable { get; private set; }

    /// <summary>Unreclaimable slab memory (bytes).</summary>
    public long SlabUnreclaimable { get; private set; }

    /// <summary>Total slab memory (bytes).</summary>
    public long Slab { get; private set; }

    /// <summary>Number of pages swapped in.</summary>
    public long? PageSwapIn { get; private set; }

    /// <summary>Number of pages swapped out.</summary>
    public long? PageSwapOut { get; private set; }

    /// <summary>Number of page faults.</summary>
    public long? PageFault { get; private set; }

    /// <summary>Number of major page faults.</summary>
    public long? PageMajorFault { get; private set; }

    internal static MemoryStat Parse(string content)
    {
        var stat = new MemoryStat();

        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                continue;

            var key = parts[0];
            var valueStr = parts[1];

            if (!long.TryParse(valueStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                continue;

            switch (key)
            {
                case "anon":
                    stat.Anon = value;
                    break;
                case "file":
                    stat.File = value;
                    break;
                case "kernel":
                    stat.Kernel = value;
                    break;
                case "kernel_stack":
                    stat.KernelStack = value;
                    break;
                case "pagetables":
                    stat.PageTables = value;
                    break;
                case "percpu":
                    stat.PerCpu = value;
                    break;
                case "sock":
                    stat.Sock = value;
                    break;
                case "swapcached":
                    stat.SwapCached = value;
                    break;
                case "file_mapped":
                    stat.FileMapped = value;
                    break;
                case "file_dirty":
                    stat.FileDirty = value;
                    break;
                case "file_writeback":
                    stat.FileWriteback = value;
                    break;
                case "inactive_anon":
                    stat.InactiveAnon = value;
                    break;
                case "active_anon":
                    stat.ActiveAnon = value;
                    break;
                case "inactive_file":
                    stat.InactiveFile = value;
                    break;
                case "active_file":
                    stat.ActiveFile = value;
                    break;
                case "unevictable":
                    stat.Unevictable = value;
                    break;
                case "slab_reclaimable":
                    stat.SlabReclaimable = value;
                    break;
                case "slab_unreclaimable":
                    stat.SlabUnreclaimable = value;
                    break;
                case "slab":
                    stat.Slab = value;
                    break;
                case "pswpin":
                    stat.PageSwapIn = value;
                    break;
                case "pswpout":
                    stat.PageSwapOut = value;
                    break;
                case "pgfault":
                    stat.PageFault = value;
                    break;
                case "pgmajfault":
                    stat.PageMajorFault = value;
                    break;
            }
        }

        return stat;
    }
}
