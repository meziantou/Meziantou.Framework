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
        Assert.Equal(200, weight);

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
        Assert.Equal(limit, actualLimit);

        child.Delete();
    }

    [Fact]
    public void GetMemoryCurrent_ShouldReturnCurrentUsage()
    {
        var child = _testRoot.CreateOrGetChild("mem_current_test");
        _testRoot.SetControllers("memory");

        var current = child.GetMemoryCurrent();

        Assert.NotNull(current);
        Assert.True(current >= 0);

        child.Delete();
    }

    [Fact]
    public void SetPidsMax_ShouldSetLimit()
    {
        var child = _testRoot.CreateOrGetChild("pids_test");
        _testRoot.SetControllers("pids");

        child.SetPidsMax(50);

        var limit = child.GetPidsMax();
        Assert.Equal(50, limit);

        child.Delete();
    }

    [Fact]
    public void GetCpuStat_ShouldReturnStatistics()
    {
        _testRoot.SetControllers("cpu");

        var stat = _testRoot.GetCpuStat();

        Assert.NotNull(stat);
        Assert.True(stat.UsageMicroseconds >= 0);
        Assert.True(stat.UserMicroseconds >= 0);
        Assert.True(stat.SystemMicroseconds >= 0);
    }

    [Fact]
    public void GetMemoryStat_ShouldReturnStatistics()
    {
        _testRoot.SetControllers("memory");

        var stat = _testRoot.GetMemoryStat();

        Assert.NotNull(stat);
        Assert.True(stat.Anon >= 0);
        Assert.True(stat.File >= 0);
    }

    [Fact]
    public void Freeze_ShouldFreezeProcesses()
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
                Thread.Sleep(25);
            }
        }

        Assert.True(isFrozen);

        child.Unfreeze();
        child.Delete();
    }
}
