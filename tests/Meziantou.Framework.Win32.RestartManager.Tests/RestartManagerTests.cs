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
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var session = RestartManager.CreateSession();
        session.Dispose();
        session.Dispose();
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void RegisterFile_AfterDispose_ThrowsObjectDisposedException()
    {
        var session = RestartManager.CreateSession();
        session.Dispose();

        Assert.Throws<ObjectDisposedException>(() => session.RegisterFile(@"C:\does-not-matter.txt"));
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void IsResourcesLocked_AfterDispose_ThrowsObjectDisposedException()
    {
        var session = RestartManager.CreateSession();
        session.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = session.IsResourcesLocked());
    }
}
