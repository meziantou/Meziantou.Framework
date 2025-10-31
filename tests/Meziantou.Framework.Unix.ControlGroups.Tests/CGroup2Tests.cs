using TestUtilities;

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

        if (SkipOnGitHubActionsAttribute.IsOnGitHubActions())
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
            if (_testRoot is not null && _testRoot.Exists())
            {
                _testRoot.Delete();
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public void CreateChild_ShouldCreateNewCGroup()
    {
        // Arrange & Act
        var child = _testRoot.CreateOrGetChild("child1");

        // Assert
        Assert.True(child.Exists());
        Assert.Equal("child1", child.Name);
        Assert.Equal(_testRoot, child.Parent);

        // Cleanup
        child.Delete();
    }

    [Fact]
    public void CreateOrGetChild_ShouldCreateIfNotExists()
    {
        // Arrange & Act
        var child1 = _testRoot.CreateOrGetChild("child2");
        var child2 = _testRoot.CreateOrGetChild("child2");

        // Assert
        Assert.True(child1.Exists());
        Assert.Equal(child1.Path, child2.Path);

        // Cleanup
        child1.Delete();
    }

    [Fact]
    public void AddProcess_ShouldAddProcessToCGroup()
    {
        // Arrange
        var currentPid = Environment.ProcessId;

        // Act
        _testRoot.AssociateProcess(currentPid);

        // Assert
        var processes = _testRoot.GetProcesses().ToList();
        Assert.Contains(currentPid, processes);

        // Note: Moving back to root is done in cleanup
        CGroup2.Root.AssociateProcess(currentPid);
    }

    [Fact]
    public void EnableController_ShouldEnableController()
    {
        var availableControllers = _testRoot.GetAvailableControllers().ToList();
        if (!availableControllers.Contains("cpu", StringComparer.Ordinal))
            throw new Exception("$XunitDynamicSkip$CPU controller not available");

        // Act
        _testRoot.SetControllers("cpu");

        // Assert
        var enabledControllers = _testRoot.GetEnabledControllers().ToList();
        Assert.Contains("cpu", enabledControllers);
    }

    [Fact]
    public void SetCpuWeight_ShouldSetWeight()
    {
        // Arrange
        var child = _testRoot.CreateOrGetChild("cpu_test");
        _testRoot.SetControllers("cpu");

        // Act
        child.SetCpuWeight(200);

        // Assert
        var weight = child.GetCpuWeight();
        Assert.Equal(200, weight);

        // Cleanup
        child.Delete();
    }

    [Fact]
    public void SetCpuWeight_ShouldThrowForInvalidWeight()
    {
        // Arrange
        var child = _testRoot.CreateOrGetChild("cpu_test2");

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => child.SetCpuWeight(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => child.SetCpuWeight(10001));

        // Cleanup
        child.Delete();
    }

    [Fact]
    public void SetMemoryMax_ShouldSetLimit()
    {
        // Arrange
        var child = _testRoot.CreateOrGetChild("mem_test");
        _testRoot.SetControllers("memory");

        // Act
        var limit = 100L * 1024 * 1024; // 100 MB
        child.SetMemoryMax(limit);

        // Assert
        var actualLimit = child.GetMemoryMax();
        Assert.Equal(limit, actualLimit);

        // Cleanup
        child.Delete();
    }

    [Fact]
    public void GetMemoryCurrent_ShouldReturnCurrentUsage()
    {
        // Arrange
        var child = _testRoot.CreateOrGetChild("mem_current_test");
        _testRoot.SetControllers("memory");

        // Act
        var current = child.GetMemoryCurrent();

        // Assert
        Assert.NotNull(current);
        Assert.True(current >= 0);

        // Cleanup
        child.Delete();
    }

    [Fact]
    public void SetPidsMax_ShouldSetLimit()
    {
        // Arrange
        var child = _testRoot.CreateOrGetChild("pids_test");
        _testRoot.SetControllers("pids");

        // Act
        child.SetPidsMax(50);

        // Assert
        var limit = child.GetPidsMax();
        Assert.Equal(50, limit);

        // Cleanup
        child.Delete();
    }

    [Fact]
    public void GetCpuStat_ShouldReturnStatistics()
    {
        // Arrange
        _testRoot.SetControllers("cpu");

        // Act
        var stat = _testRoot.GetCpuStat();

        // Assert
        Assert.NotNull(stat);
        Assert.True(stat.UsageMicroseconds >= 0);
        Assert.True(stat.UserMicroseconds >= 0);
        Assert.True(stat.SystemMicroseconds >= 0);
    }

    [Fact]
    public void GetMemoryStat_ShouldReturnStatistics()
    {
        // Arrange
        _testRoot.SetControllers("memory");

        // Act
        var stat = _testRoot.GetMemoryStat();

        // Assert
        Assert.NotNull(stat);
        Assert.True(stat.Anon >= 0);
        Assert.True(stat.File >= 0);
    }

    [Fact]
    public void Freeze_ShouldFreezeProcesses()
    {
        if (!File.Exists(Path.Combine(_testRoot.Path, "cgroup.freeze")))
            throw new Exception("$XunitDynamicSkip$Freezer not available");

        // Arrange
        var child = _testRoot.CreateOrGetChild("freeze_test");

        // Act
        child.Freeze();
        Thread.Sleep(100); // Give it a moment to freeze

        // Assert
        var isFrozen = child.IsFrozen();
        Assert.True(isFrozen);

        // Cleanup
        child.Unfreeze();
        child.Delete();
    }
}
