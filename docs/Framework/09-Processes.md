---
title: Processes
description: Find an executable in PATH, and find the parent, children and descendants of a process.
---

## Find an executable

<!-- ref:../../tests/Meziantou.Framework.Tests/ExecutableFinderTests.cs;region=ExecutableFinder_GetFullExecutablePath;format=md-fenced-code-block;dedent=true -->
```csharp
string? gitPath = ExecutableFinder.GetFullExecutablePath("git");
if (gitPath is not null)
{
    Assert.True(File.Exists(gitPath));
}
```
<!-- endref -->

[ExecutableFinder.GetFullExecutablePath](cref:Meziantou.Framework.ExecutableFinder.GetFullExecutablePath) looks for an executable in the folders of the `PATH` environment variable, like
a shell does, and returns its full path, or `null` when it is not found:

- On Windows, a name without extension is completed with the extensions of `PATHEXT` (`.COM`, `.EXE`, `.BAT` and `.CMD`
  when it is not set), and an extensionless file is never returned. As with `cmd.exe`, the quotes around a `PATH` entry,
  such as `"C:\Program Files\Tool"`, are removed, a quoted entry can contain `;`, and empty entries are ignored.
- On Linux and macOS, only files that are executable are returned.
- When you pass a working directory, it is searched before the folders of `PATH`. Pass it only when the folder is
  trusted: a file in it would take precedence over the executable of `PATH`.

To run the executable, see [ProcessWrapper](../ProcessWrapper/01-Introduction.md), which uses the same lookup.

## Find related processes

<!-- ref:../../tests/Meziantou.Framework.Tests/ProcessExtensionsTests.cs;region=ProcessExtensions_ParentProcess;format=md-fenced-code-block;dedent=true -->
```csharp
using var currentProcess = Process.GetCurrentProcess();
if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
{
    int? parentId = currentProcess.GetParentProcessId();
    Assert.NotNull(parentId);

    using var parent = currentProcess.GetParentProcess();
    Assert.NotNull(parent);
}
```
<!-- endref -->

| Method | Returns | Platforms |
| --- | --- | --- |
| [GetParentProcessId](cref:Meziantou.Framework.ProcessExtensions.GetParentProcessId) | The id of the parent process, or `null` when it is unknown | Windows, Linux |
| [GetParentProcess](cref:Meziantou.Framework.ProcessExtensions.GetParentProcess) | The parent process, or `null` | Windows, Linux |
| [GetAncestorProcessIds](cref:Meziantou.Framework.ProcessExtensions.GetAncestorProcessIds), [GetAncestorProcesses](cref:Meziantou.Framework.ProcessExtensions.GetAncestorProcesses) | The parent, its parent, and so on | Windows |
| [GetChildProcesses](cref:Meziantou.Framework.ProcessExtensions.GetChildProcesses) | The processes started by the process | Windows |
| [GetDescendantProcesses](cref:Meziantou.Framework.ProcessExtensions.GetDescendantProcesses) | The children, their children, and so on | Windows |
| [ProcessExtensions.GetProcesses](cref:Meziantou.Framework.ProcessExtensions.GetProcesses) | Every process, as a [ProcessEntry](cref:Meziantou.Framework.ProcessEntry) with its id and the id of its parent | Windows |

On other operating systems, the Windows-only methods throw a `PlatformNotSupportedException`, and [GetParentProcessId](cref:Meziantou.Framework.ProcessExtensions.GetParentProcessId)
returns `null` on macOS. The `Process` instances these methods return must be disposed.

A process id can be reused once the process exits, so [GetParentProcessId](cref:Meziantou.Framework.ProcessExtensions.GetParentProcessId) can return the id of an unrelated process
when the parent has exited. [GetParentProcess](cref:Meziantou.Framework.ProcessExtensions.GetParentProcess) returns `null` when the parent has exited, or when the process with that id
started after the child or its start time cannot be read. For the same reason, [GetAncestorProcesses](cref:Meziantou.Framework.ProcessExtensions.GetAncestorProcesses) stops at the first
ancestor that started after its child, and [GetChildProcesses](cref:Meziantou.Framework.ProcessExtensions.GetChildProcesses) and [GetDescendantProcesses](cref:Meziantou.Framework.ProcessExtensions.GetDescendantProcesses) skip the processes that exit while
they are listed.
