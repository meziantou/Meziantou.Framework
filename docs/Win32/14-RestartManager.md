---
title: Restart Manager
description: Find the processes that lock a file, describe them, and shut them down and restart them with the Windows Restart Manager.
---

When a file cannot be deleted or overwritten because another process has it open, the Windows Restart Manager tells
which processes use it, and can close them and start them again. Installers use it to update files that running
applications hold. `RestartManager` from the `Meziantou.Framework.Win32.RestartManager` package wraps it.

```bash
dotnet add package Meziantou.Framework.Win32.RestartManager
```

## Check whether a file is locked

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

## Find the processes that lock a file

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

The method returns `Process` instances: the caller owns them and should dispose them.

To check several files, pass them all at once. Each check registers the files in a Restart Manager session, which is
relatively expensive, so one call for all the files is much cheaper than one call per file:

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

Windows saves the registrations of a session in the registry, so a session can only hold a limited number of paths. A
very large set fails with a `Win32Exception` for `ERROR_WRITE_FAULT` (29): split it into several calls.

## Use a session

The static methods create a session for each call. Create the session yourself to register files, query them several
times, or shut the applications down:

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

`RestartManager` is disposable: disposing it ends the session.

### Describe the applications

`GetProcessesLockingResources` returns `Process` instances. `GetLockingProcesses` returns what the Restart Manager
knows about each application: its name, its type, its status, and whether it can be restarted. It also reports the
applications that have already exited:

<!-- ref:../../tests/Meziantou.Framework.Win32.RestartManager.Tests/RestartManagerTests.cs;region=RestartManager_GetLockingProcesses;format=md-fenced-code-block;dedent=true -->
```csharp
using var session = RestartManager.CreateSession();
session.RegisterFile(path);

// Describe the applications and services that use the file
var applications = session.GetLockingProcesses();
foreach (var application in applications)
{
    Console.WriteLine($"{application.ApplicationName} ({application.ProcessId}): {application.ApplicationType}, restartable: {application.IsRestartable}");
}

Assert.Contains(applications, application => application.ProcessId == Environment.ProcessId);
```
<!-- endref -->

| Property | Description |
| --- | --- |
| `ProcessId`, `StartTime` | The process. Windows reuses process identifiers, so only both together identify a process |
| `ApplicationName` | The friendly name of the application |
| `ApplicationType` | `MainWindow`, `OtherWindow`, `Service`, `Explorer`, `Console`, `Critical` or `Unknown` |
| `ServiceShortName` | The short name of the service, or an empty string for another kind of process |
| `Status` | Whether the application is running, was stopped or restarted by the session, or failed to stop or restart |
| `IsRestartable` | Whether the Restart Manager can restart the application after shutting it down |
| `TerminalServicesSessionId` | The session of the user that runs the process |

### Join a session

Another process, such as a secondary installer, can join a session with its `SessionKey` to register its own files:

<!-- ref:../../tests/Meziantou.Framework.Win32.RestartManager.Tests/RestartManagerTests.cs;region=RestartManager_JoinSession;format=md-fenced-code-block;dedent=true -->
```csharp
using var session = RestartManager.JoinSession(sessionKey);
```
<!-- endref -->

## Shut down and restart the applications

The session can close the applications that use the registered files, so the files can be replaced, and start them
again. The example closes applications, so it is compiled, not run, by the build of the repository:

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

- Check `RebootReason` first. It reflects the last call to `IsResourcesLocked`, `GetProcessesLockingResources` or
  `GetLockingProcesses`. When it is not `None`, shutting the applications down cannot free the files, for instance
  because a critical process or a process of another user holds them: ask the user to restart the computer instead.
- `RestartManagerShutdownType.ForceShutdown` forces the applications and services that do not respond to shut down
  after a timeout (30 seconds for the applications, 20 seconds for the services).
  `RestartManagerShutdownType.ShutdownOnlyRegistered` only shuts the applications down when all of them registered for
  restart with `RegisterApplicationRestart`; otherwise nothing is shut down. Pass at least one of them.
- `Restart` starts again the applications that were shut down and that registered for restart.
- `Shutdown` and `Restart` block until they complete. The progress callback receives the percentage completed; it is
  called by Windows and must not throw. To stop the operation, call `CancelCurrentTask` from another thread. Only the
  session created with `CreateSession` can cancel, not a session joined with `JoinSession`.
- The methods throw a `Win32Exception` when the Restart Manager fails.
