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
    public void ReadmeExample_IsFileLocked()
    {
        #region RestartManager_IsFileLocked
        var path = Path.GetTempFileName();
        using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.True(RestartManager.IsFileLocked(path));
        }

        Assert.False(RestartManager.IsFileLocked(path));
        #endregion

        File.Delete(path);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void ReadmeExample_GetProcessesLockingFile()
    {
        #region RestartManager_GetProcessesLockingFile
        var path = Path.GetTempFileName();
        using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var processes = RestartManager.GetProcessesLockingFile(path);
            Assert.Equal(Environment.ProcessId, Assert.Single(processes).Id);

            // The caller owns the returned Process instances
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
        #endregion

        File.Delete(path);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void ReadmeExample_GetProcessesLockingFiles()
    {
        #region RestartManager_GetProcessesLockingFiles
        var path1 = Path.GetTempFileName();
        var path2 = Path.GetTempFileName();
        using (File.Open(path2, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var processes = RestartManager.GetProcessesLockingFiles([path1, path2]);

            Assert.Contains(Environment.ProcessId, processes.Select(process => process.Id));
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
        #endregion

        File.Delete(path1);
        File.Delete(path2);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void ReadmeExample_ManualSession()
    {
        var path = Path.GetTempFileName();
        var path1 = Path.GetTempFileName();
        var path2 = Path.GetTempFileName();
        using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            #region RestartManager_ManualSession
            // Create a new session
            using var session = RestartManager.CreateSession();

            // Register one or more files to monitor
            session.RegisterFile(path);
            session.RegisterFiles([path1, path2]);

            // Check if any registered resources are locked
            Assert.True(session.IsResourcesLocked());

            // Get the processes locking the resources
            var processes = session.GetProcessesLockingResources();
            Assert.Contains(Environment.ProcessId, processes.Select(process => process.Id));
            foreach (var process in processes)
            {
                process.Dispose();
            }
            #endregion
        }

        File.Delete(path);
        File.Delete(path1);
        File.Delete(path2);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void ReadmeExample_JoinSession()
    {
        using var existingSession = RestartManager.CreateSession();
        var sessionKey = existingSession.SessionKey;

        #region RestartManager_JoinSession
        using var session = RestartManager.JoinSession(sessionKey);
        #endregion

        Assert.Equal(sessionKey, session.SessionKey);
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
