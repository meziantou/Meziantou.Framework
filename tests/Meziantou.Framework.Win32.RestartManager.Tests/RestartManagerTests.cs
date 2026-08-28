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
    public void RegisterFiles_WithManyPaths()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var paths = new string[2000];
            for (var i = 0; i < paths.Length; i++)
            {
                paths[i] = Path.Combine(directory.FullName, $"file{i}.txt");
            }

            File.WriteAllText(paths[^1], "");
            using (File.Open(paths[^1], FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                using var session = RestartManager.CreateSession();
                session.RegisterFiles(paths);

                Assert.Contains(_currentProcessId, session.GetProcessesLockingResources().Select(process => process.Id));
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void JoinSession_WithUnknownKey_ReportsTheFailingFunction()
    {
        var exception = Assert.Throws<Win32Exception>(() => RestartManager.JoinSession("00000000000000000000000000000000"));
        Assert.StartsWith("RmJoinSession failed", exception.Message);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetProcessesLockingFiles()
    {
        var unlockedPath = Path.GetTempFileName();
        var lockedPath = Path.GetTempFileName();
        try
        {
            using (File.Open(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var processes = RestartManager.GetProcessesLockingFiles([unlockedPath, lockedPath]);
                Assert.Contains(_currentProcessId, processes.Select(process => process.Id));
            }
        }
        finally
        {
            File.Delete(unlockedPath);
            File.Delete(lockedPath);
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
