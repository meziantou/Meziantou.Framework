# Meziantou.Framework.Win32.RestartManager

A .NET library that wraps the Windows Restart Manager API to detect which processes are locking files and manage application restarts.

## Usage

This library provides functionality to interact with the Windows Restart Manager (RM) API, which helps identify processes that have locks on files and manage graceful shutdown and restart of applications.

### Check if a File is Locked

The simplest way to check if a file is currently locked by any process:

```csharp
using Meziantou.Framework.Win32;

var path = @"C:\path\to\file.txt";
if (RestartManager.IsFileLocked(path))
{
    Console.WriteLine("File is locked by one or more processes");
}
```

### Find Processes Locking a File

Get a list of all processes that have locks on a specific file:

```csharp
using Meziantou.Framework.Win32;

var path = @"C:\path\to\file.txt";
var processes = RestartManager.GetProcessesLockingFile(path);

foreach (var process in processes)
{
    Console.WriteLine($"Process {process.ProcessName} (PID: {process.Id}) is locking the file");
}
```

### Manual Session Management

For more control, you can create and manage a Restart Manager session manually:

```csharp
using Meziantou.Framework.Win32;

// Create a new session
using var session = RestartManager.CreateSession();

// Register one or more files to monitor
session.RegisterFile(@"C:\path\to\file.txt");
session.RegisterFiles(new[] { @"C:\path\to\file1.txt", @"C:\path\to\file2.txt" });

// Check if any registered resources are locked
if (session.IsResourcesLocked())
{
    // Get the processes locking the resources
    var processes = session.GetProcessesLockingResources();
    foreach (var process in processes)
    {
        Console.WriteLine($"Locked by: {process.ProcessName}");
    }
}
```

### Join an Existing Session

You can join an existing Restart Manager session using its session key:

```csharp
var sessionKey = "existing-session-key";
using var session = RestartManager.JoinSession(sessionKey);
```

### Shutdown and Restart Applications

The Restart Manager can shut down and restart applications that are locking resources:

```csharp
using var session = RestartManager.CreateSession();
session.RegisterFile(@"C:\path\to\file.txt");

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

### Shutdown Types

The `RestartManagerShutdownType` enum provides options for how applications should be shut down:

- `ForceShutdown` - Forces unresponsive applications and services to shut down after a timeout period (30 seconds for applications, 20 seconds for services)
- `ShutdownOnlyRegistered` - Only shuts down applications that have been registered for restart using `RegisterApplicationRestart`. If any processes cannot be restarted, no shutdown occurs

## Platform Support

This library is **Windows-only** and requires Windows Vista or later (Windows 6.0+).

## Additional Resources

- [Restart Manager API (Windows)](https://learn.microsoft.com/en-us/windows/win32/rstmgr/restart-manager-portal?WT.mc_id=DT-MVP-5003978)
- [Using Restart Manager](https://learn.microsoft.com/en-us/windows/win32/rstmgr/using-restart-manager?WT.mc_id=DT-MVP-5003978)
