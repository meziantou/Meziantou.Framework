using System.ComponentModel;
using System.Diagnostics;
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
    public void RegisterFiles_WithNullPath_ThrowsArgumentException()
    {
        using var session = RestartManager.CreateSession();

        var exception = Assert.Throws<ArgumentException>(() => session.RegisterFiles([@"C:\does-not-matter.txt", null!]));
        Assert.Equal("paths", exception.ParamName);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void RegisterFiles_WithNullPath_DoesNotSilentlySkipTheRemainingPaths()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                using var session = RestartManager.CreateSession();

                Assert.Throws<ArgumentException>(() => session.RegisterFiles([path, null!]));

                // Nothing was registered, so the locked file must not be reported as locked by this session.
                Assert.False(session.IsResourcesLocked());
            }
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

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void JoinSession_WithNullKey_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => RestartManager.JoinSession(sessionKey: null!));
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

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void Operations_AfterDispose_ThrowObjectDisposedException()
    {
        var session = RestartManager.CreateSession();
        session.Dispose();

        Assert.Throws<ObjectDisposedException>(() => session.RegisterFiles([@"C:\does-not-matter.txt"]));
        Assert.Throws<ObjectDisposedException>(() => _ = session.GetProcessesLockingResources());
        Assert.Throws<ObjectDisposedException>(() => _ = session.GetLockingProcesses());
        Assert.Throws<ObjectDisposedException>(() => session.Shutdown(RestartManagerShutdownType.ForceShutdown));
        Assert.Throws<ObjectDisposedException>(() => session.Restart());
        Assert.Throws<ObjectDisposedException>(() => session.CancelCurrentTask());
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void IsResourcesLocked_ReflectsTheCurrentStateOfRegisteredFiles()
    {
        var unlockedPath = Path.GetTempFileName();
        var lockedPath = Path.GetTempFileName();
        try
        {
            using var session = RestartManager.CreateSession();
            session.RegisterFiles([unlockedPath, lockedPath]);

            Assert.False(session.IsResourcesLocked());

            using (File.Open(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.True(session.IsResourcesLocked());
            }
        }
        finally
        {
            File.Delete(unlockedPath);
            File.Delete(lockedPath);
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetLockingProcesses_ReportsTheDetailsOfTheLockingProcess()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                using var session = RestartManager.CreateSession();
                session.RegisterFile(path);

                var info = Assert.Single(session.GetLockingProcesses(), item => item.ProcessId == _currentProcessId);
                Assert.NotEmpty(info.ApplicationName);

                using var currentProcess = Process.GetCurrentProcess();
                var difference = (currentProcess.StartTime - info.StartTime).Duration();
                Assert.True(difference < TimeSpan.FromSeconds(1), $"Expected {currentProcess.StartTime:O} but got {info.StartTime:O}");
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void RebootReason_IsRefreshedWhenTheSessionIsQueried()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                using var session = RestartManager.CreateSession();
                session.RegisterFile(path);

                Assert.Equal(RestartManagerRebootReason.None, session.RebootReason);

                Assert.True(session.IsResourcesLocked());

                // The session and the lock live in the same process, which is what DetectedSelf reports.
                Assert.Equal(RestartManagerRebootReason.DetectedSelf, session.RebootReason);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
}
