using System.Diagnostics;
using Meziantou.Xunit;

namespace Meziantou.Framework.Unix.ControlGroups.Tests;

public sealed class CGroup2Tests : IDisposable
{
    private readonly CGroup2 _testRoot;
    private readonly string _testGroupName;
    private readonly ITestOutputHelper _testOutputHelper;

    public CGroup2Tests(ITestOutputHelper testOutputHelper)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new Exception("$XunitDynamicSkip$Test runs only on Linux");
        }

        if (!Directory.Exists("/sys/fs/cgroup"))
        {
            throw new Exception("$XunitDynamicSkip$cgroup v2 not available");
        }

        if (!Environment.IsPrivilegedProcess)
        {
            throw new Exception("$XunitDynamicSkip$Test requires elevated privileges");
        }

        if (TestEnvironment.IsOnGitHubActions())
        {
            throw new Exception("$XunitDynamicSkip$Test cannot run in GitHub Actions");
        }

        // Create a unique test group name
        _testOutputHelper = testOutputHelper;
        _testGroupName = $"test_cgroup_{Guid.NewGuid():N}";
        _testRoot = CGroup2.Root.CreateOrGetChild(_testGroupName);
        _testOutputHelper.WriteLine($"Using test cgroup: {_testRoot.Path}");
        foreach (var entry in Directory.GetFileSystemEntries(_testRoot.Path).Order(StringComparer.Ordinal))
        {
            _testOutputHelper.WriteLine($" - {entry}");
        }
    }

    public void Dispose()
    {
        // Cleanup: remove test cgroup
        try
        {
            if (_testRoot.Exists())
            {
                _testRoot.Delete();
            }
        }
        catch (Exception ex)
        {
            // A cgroup that still holds processes cannot be removed. Report it instead of leaking it silently.
            _testOutputHelper.WriteLine($"Cannot delete the test cgroup '{_testRoot}': {ex}");
        }
    }

    [Fact]
    public void CreateChild_ShouldCreateNewCGroup()
    {
        var child = _testRoot.CreateOrGetChild("child1");

        Assert.True(child.Exists());
        Assert.Equal("child1", child.Name);
        Assert.Equal(_testRoot, child.Parent);

        child.Delete();
    }

    [Fact]
    public void CreateOrGetChild_ShouldCreateIfNotExists()
    {
        var child1 = _testRoot.CreateOrGetChild("child2");
        var child2 = _testRoot.CreateOrGetChild("child2");

        Assert.True(child1.Exists());
        Assert.Equal(child1.Path, child2.Path);

        child1.Delete();
    }

    [Fact]
    public void AddProcess_ShouldAddProcessToCGroup()
    {
        // Move a short-lived child process, never the test host: a failed assertion must not leave the
        // runner confined to the test cgroup, where it would break sibling tests and block cleanup.
        using var process = Process.Start("sleep", "30");
        try
        {
            _testRoot.AssociateProcess(process);

            var processes = _testRoot.GetProcesses().ToList();
            Assert.Contains(process.Id, processes);
        }
        finally
        {
            process.Kill();
            process.WaitForExit();
        }
    }

    [Fact]
    public void EnableController_ShouldEnableController()
    {
        var availableControllers = _testRoot.GetAvailableControllers().ToList();
        if (!availableControllers.Contains("cpu", StringComparer.Ordinal))
            throw new Exception("$XunitDynamicSkip$CPU controller not available");

        _testRoot.SetControllers("cpu");

        var enabledControllers = _testRoot.GetEnabledControllers().ToList();
        Assert.Contains("cpu", enabledControllers);
    }

    [Fact]
    public void SetCpuWeight_ShouldSetWeight()
    {
        var child = _testRoot.CreateOrGetChild("cpu_test");
        _testRoot.SetControllers("cpu");

        child.SetCpuWeight(200);

        var weight = child.GetCpuWeight();
        Assert.Equal(CGroupValueState.Configured, weight.State);
        Assert.Equal(200, weight.Value);

        child.Delete();
    }

    [Fact]
    public void SetCpuWeight_ShouldThrowForInvalidWeight()
    {
        var child = _testRoot.CreateOrGetChild("cpu_test2");

        Assert.Throws<ArgumentOutOfRangeException>(() => child.SetCpuWeight(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => child.SetCpuWeight(10001));

        child.Delete();
    }

    [Fact]
    public void SetMemoryMax_ShouldSetLimit()
    {
        var child = _testRoot.CreateOrGetChild("mem_test");
        _testRoot.SetControllers("memory");

        var limit = 100L * 1024 * 1024; // 100 MB
        child.SetMemoryMax(limit);

        var actualLimit = child.GetMemoryMax();
        Assert.Equal(CGroupValueState.Configured, actualLimit.State);
        Assert.Equal(limit, actualLimit.Value);

        child.SetMemoryMax(null);
        Assert.Equal(CGroupValueState.NotConfigured, child.GetMemoryMax().State);

        child.Delete();
    }

    [Fact]
    public void GetMemoryCurrent_ShouldReturnCurrentUsage()
    {
        var child = _testRoot.CreateOrGetChild("mem_current_test");
        _testRoot.SetControllers("memory");

        var current = child.GetMemoryCurrent();

        Assert.Equal(CGroupValueState.Configured, current.State);
        Assert.True(current.Value >= 0);

        child.Delete();
    }

    [Fact]
    public void SetPidsMax_ShouldSetLimit()
    {
        var child = _testRoot.CreateOrGetChild("pids_test");
        _testRoot.SetControllers("pids");

        child.SetPidsMax(50);

        var limit = child.GetPidsMax();
        Assert.Equal(CGroupValueState.Configured, limit.State);
        Assert.Equal(50, limit.Value);

        child.Delete();
    }

    [Fact]
    public void GetCpuStat_ShouldReturnStatistics()
    {
        _testRoot.SetControllers("cpu");

        var stat = _testRoot.GetCpuStat();

        Assert.True(stat.IsConfigured);
        Assert.True(stat.Value.UsageMicroseconds >= 0);
        Assert.True(stat.Value.UserMicroseconds >= 0);
        Assert.True(stat.Value.SystemMicroseconds >= 0);
    }

    [Fact]
    public void GetMemoryStat_ShouldReturnStatistics()
    {
        _testRoot.SetControllers("memory");

        var stat = _testRoot.GetMemoryStat();

        Assert.True(stat.IsConfigured);
        Assert.True(stat.Value.Anon >= 0);
        Assert.True(stat.Value.File >= 0);
    }

    [Fact]
    public async Task Freeze_ShouldFreezeProcesses()
    {
        if (!File.Exists(Path.Combine(_testRoot.Path, "cgroup.freeze")))
            throw new Exception("$XunitDynamicSkip$Freezer not available");

        var child = _testRoot.CreateOrGetChild("freeze_test");

        child.Freeze();

        // Freezing is asynchronous, so poll instead of racing a fixed sleep against a loaded machine.
        var isFrozen = false;
        for (var i = 0; i < 200 && !isFrozen; i++)
        {
            isFrozen = child.IsFrozen();
            if (!isFrozen)
            {
                await Task.Delay(25, XunitCancellationToken);
            }
        }

        Assert.True(isFrozen);

        child.Unfreeze();
        child.Delete();
    }

    // The documentation examples create cgroups of their own, under the root cgroup, with names that differ from one
    // example to the other because the tests of a class run in parallel.
    [Fact]
    public void DocumentationExample_GettingStarted()
    {
        #region CGroup2_GettingStarted
        // Controllers are enabled on a parent cgroup, and apply to its children
        var parent = CGroup2.Root.CreateOrGetChild("myapp");
        parent.SetControllers("cpu", "memory", "pids");

        // The processes go into a child cgroup, which holds the limits
        var worker = parent.CreateOrGetChild("worker");
        worker.SetCpuMax(50_000, 100_000);         // half of a CPU
        worker.SetMemoryMax(256L * 1024 * 1024);   // 256 MB
        worker.SetPidsMax(20);                     // 20 processes

        using var process = Process.Start("sleep", "60");
        worker.AssociateProcess(process);
        Assert.Contains(process.Id, worker.GetProcesses());

        // A cgroup can be deleted only when it has no process and no child cgroup
        worker.Kill();
        process.WaitForExit();
        worker.Delete();
        parent.Delete();
        #endregion
    }

    [Fact]
    public void DocumentationExample_ReadValues()
    {
        #region CGroup2_ReadValues
        var parent = CGroup2.Root.CreateOrGetChild("myapp-values");
        parent.SetControllers("memory");
        var worker = parent.CreateOrGetChild("worker");

        // memory.max holds "max": there is no limit
        var memoryMax = worker.GetMemoryMax();
        Assert.Equal(CGroupValueState.NotConfigured, memoryMax.State);
        Assert.Equal("max", memoryMax.RawValue);

        worker.SetMemoryMax(512L * 1024 * 1024);
        memoryMax = worker.GetMemoryMax();
        Assert.Equal(CGroupValueState.Configured, memoryMax.State);
        Assert.Equal(512L * 1024 * 1024, memoryMax.Value);

        // The pids controller is not enabled on the parent, so pids.max does not exist
        var pidsMax = worker.GetPidsMax();
        Assert.Equal(CGroupValueState.Unavailable, pidsMax.State);
        Assert.False(pidsMax.TryGetValue(out _));
        Assert.Equal(long.MaxValue, pidsMax.GetValueOrDefault(long.MaxValue));

        worker.Delete();
        parent.Delete();
        #endregion
    }

    [Fact]
    public void DocumentationExample_ResourceLimits()
    {
        #region CGroup2_ResourceLimits
        var parent = CGroup2.Root.CreateOrGetChild("myapp-limits");
        parent.SetControllers("cpu", "memory", "pids");
        var worker = parent.CreateOrGetChild("worker");

        // CPU: a relative weight (1 to 10000, 100 by default) and a hard limit of 2 CPUs
        worker.SetCpuWeight(50);
        worker.SetCpuMax(200_000, 100_000);
        Assert.Equal(new CpuMax(200_000, 100_000), worker.GetCpuMax().Value);

        // Memory: throttled above 1.5 GB, never more than 2 GB
        worker.SetMemoryHigh(1536L * 1024 * 1024);
        worker.SetMemoryMax(2048L * 1024 * 1024);

        // Processes: at most 100
        worker.SetPidsMax(100);

        // null removes a limit
        worker.SetMemoryHigh(null);
        worker.SetPidsMax(null);
        worker.RemoveCpuMax();
        Assert.Equal(CGroupValueState.NotConfigured, worker.GetPidsMax().State);

        worker.Delete();
        parent.Delete();
        #endregion
    }

    [Fact]
    public void DocumentationExample_Statistics()
    {
        #region CGroup2_Statistics
        var group = CGroup2.Root.CreateOrGetChild("myapp-statistics");

        // TryGetValue when only a value matters
        if (group.GetCpuStat().TryGetValue(out var cpuStat))
        {
            var cpuTime = TimeSpan.FromMicroseconds(cpuStat.UsageMicroseconds);
            Assert.True(cpuTime >= TimeSpan.Zero);
        }

        // GetValueOrDefault when a fallback is good enough
        var memoryInBytes = group.GetMemoryCurrent().GetValueOrDefault(0);
        Assert.True(memoryInBytes >= 0);

        group.Delete();
        #endregion
    }

    [Fact]
    public void DocumentationExample_Freeze()
    {
        #region CGroup2_Freeze
        var group = CGroup2.Root.CreateOrGetChild("myapp-freezer");
        using var process = Process.Start("sleep", "60");
        group.AssociateProcess(process);

        // Freezing is asynchronous: IsFrozen returns true once every process of the cgroup is stopped
        group.Freeze();
        Assert.True(SpinWait.SpinUntil(group.IsFrozen, TimeSpan.FromSeconds(10)));

        group.Unfreeze();
        Assert.False(group.IsFrozen());

        // Kill all the processes of the cgroup and of its descendants
        group.Kill();
        process.WaitForExit();
        Assert.Empty(group.GetProcesses());
        group.Delete();
        #endregion
    }
}
