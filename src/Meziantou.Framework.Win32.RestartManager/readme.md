# Meziantou.Framework.Win32.RestartManager

A .NET library that wraps the Windows Restart Manager API to detect which processes are locking files and manage application restarts.

## Usage

This library provides functionality to interact with the Windows Restart Manager (RM) API, which helps identify processes that have locks on files and manage graceful shutdown and restart of applications.

The types are in the `Meziantou.Framework.Win32` namespace.

### Check if a File is Locked

The simplest way to check if a file is currently locked by any process:

<!-- ref:../../tests/Meziantou.Framework.Win32.RestartManager.Tests/RestartManagerTests.cs;region=RestartManager_IsFileLocked;format=md-fenced-code-block;dedent=true -->
```csharp
var path = Path.GetTempFileName();
using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
{
    Assert.True(RestartManager.IsFileLocked(path));
}

Assert.False(RestartManager.IsFileLocked(path));
```
<!-- endref -->

### Find Processes Locking a File

Get a list of all processes that have locks on a specific file:

The caller owns the returned `Process` instances and should dispose them.

<!-- ref:../../tests/Meziantou.Framework.Win32.RestartManager.Tests/RestartManagerTests.cs;region=RestartManager_GetProcessesLockingFile;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

### Find Processes Locking Several Files

Registering resources performs relatively expensive write operations, so checking many files with one
session is significantly cheaper than calling `GetProcessesLockingFile` in a loop:

Registrations are persisted to the registry, so one session can only hold a bounded number of paths. A very
large set fails with `ERROR_WRITE_FAULT` (29) and should be split across several sessions.

<!-- ref:../../tests/Meziantou.Framework.Win32.RestartManager.Tests/RestartManagerTests.cs;region=RestartManager_GetProcessesLockingFiles;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

### Manual Session Management

For more control, you can create and manage a Restart Manager session manually:

<!-- ref:../../tests/Meziantou.Framework.Win32.RestartManager.Tests/RestartManagerTests.cs;region=RestartManager_ManualSession;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

### Join an Existing Session

You can join an existing Restart Manager session using its session key (`RestartManager.SessionKey`):

<!-- ref:../../tests/Meziantou.Framework.Win32.RestartManager.Tests/RestartManagerTests.cs;region=RestartManager_JoinSession;format=md-fenced-code-block;dedent=true -->
```csharp
using var session = RestartManager.JoinSession(sessionKey);
```
<!-- endref -->

### Shutdown and Restart Applications

The Restart Manager can shut down and restart applications that are locking resources:

<!-- ref:../../samples/Meziantou.Framework.Win32.RestartManager.Samples/Program.cs;region=RestartManager_ShutdownAndRestart;format=md-fenced-code-block;dedent=true -->
```csharp
using var session = RestartManager.CreateSession();
session.RegisterFile(path);

// A non-zero reboot reason means shutting applications down cannot free the resources,
// and that a system restart is required instead. Check it before shutting anything down.
if (session.IsResourcesLocked() && session.RebootReason != RestartManagerRebootReason.None)
{
    Console.WriteLine($"A system restart is required: {session.RebootReason}");
    return;
}

// Shutdown applications with options
session.Shutdown(RestartManagerShutdownType.ForceShutdown);

// Or with progress callback
session.Shutdown(RestartManagerShutdownType.ForceShutdown, percentComplete =>
{
    Console.WriteLine($"Shutdown progress: {percentComplete}%");
});

// Restart applications after shutdown
session.Restart(percentComplete =>
{
    Console.WriteLine($"Restart progress: {percentComplete}%");
});
```
<!-- endref -->

### Shutdown Types

The `RestartManagerShutdownType` enum provides options for how applications should be shut down:

- `ForceShutdown` - Forces unresponsive applications and services to shut down after a timeout period (30 seconds for applications, 20 seconds for services)
- `ShutdownOnlyRegistered` - Only shuts down applications that have been registered for restart using `RegisterApplicationRestart`. If any processes cannot be restarted, no shutdown occurs

## Platform Support

This library is **Windows-only** and requires Windows Vista or later (Windows 6.0+).

## Additional Resources

- [Restart Manager API (Windows)](https://learn.microsoft.com/en-us/windows/win32/rstmgr/restart-manager-portal?WT.mc_id=DT-MVP-5003978)
- [Using Restart Manager](https://learn.microsoft.com/en-us/windows/win32/rstmgr/using-restart-manager?WT.mc_id=DT-MVP-5003978)
