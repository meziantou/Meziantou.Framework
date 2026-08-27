using System.ComponentModel;
using Meziantou.Xunit;

namespace Meziantou.Framework.Win32.Tests;

public class RestartManagerTests
{
    private readonly int _currentProcessId;

    public RestartManagerTests()
    {
        _currentProcessId = System.Environment.ProcessId;
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetProcessesLockingFile()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                var processes = RestartManager.GetProcessesLockingFile(path);
                Assert.Equal(_currentProcessId, processes.Single().Id);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void IsFileLocked_True()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.True(RestartManager.IsFileLocked(path));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void IsFileLocked_False()
    {
        var path = Path.GetTempFileName();
        try
        {
            Assert.False(RestartManager.IsFileLocked(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void JoinSession_WithUnknownKey_ReportsTheFailingFunction()
    {
        var exception = Assert.Throws<Win32Exception>(() => RestartManager.JoinSession("00000000000000000000000000000000"));
        Assert.StartsWith("RmJoinSession failed", exception.Message);
    }
}
