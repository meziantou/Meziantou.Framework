---
title: Managing processes
description: Move processes and threads into a control group, list them, freeze, kill, and delete control groups.
---

## Navigate the hierarchy

| Member | What it does |
| --- | --- |
| `CGroup2.Root` | The root control group, `/sys/fs/cgroup` |
| `CreateOrGetChild(name)` | Creates a child control group, or returns the existing one |
| `GetChild(name)` | Returns an existing child control group, or `null` |
| `Exists()` | Tells whether the control group still exists |
| `Name`, `Parent` | The name of the control group and its parent, `null` for the root |
| `Delete()` | Deletes the control group |

A name is a single folder name: an empty name, `.`, `..` or a name containing `/` throws an `ArgumentException`, so a
name cannot leave the parent control group.

## Move processes into a control group

- `AssociateProcess(process)` and `AssociateProcess(pid)` move a process, with all its threads. The processes it starts
  afterward start in the same control group.
- `AssociateThread(tid)` moves a single thread, within the rules of the thread mode of the kernel (Linux 4.14 or
  later).
- `GetProcesses()` and `GetThreads()` list the process and thread identifiers of the control group.

A process belongs to exactly one control group: moving it removes it from the previous one.

## Freeze and kill

<!-- ref:../../tests/Meziantou.Framework.Unix.ControlGroups.Tests/CGroup2Tests.cs;region=CGroup2_Freeze;format=md-fenced-code-block;dedent=true -->
```csharp
var group = CGroup2.Root.CreateOrGetChild("myapp-freezer");
using var process = Process.Start("sleep", "60");
group.AssociateProcess(process);

// Freezing is asynchronous: IsFrozen returns true once every process of the cgroup is stopped
group.Freeze();
Assert.True(SpinWait.SpinUntil(group.IsFrozen, TimeSpan.FromSeconds(10)));

group.Unfreeze();
Assert.False(group.IsFrozen());

// Kill all the processes of the cgroup and of its descendants
group.Kill();
process.WaitForExit();
Assert.Empty(group.GetProcesses());
group.Delete();
```
<!-- endref -->

- `Freeze` stops all the processes of the control group and of its descendants, until `Unfreeze`. The kernel freezes
  them asynchronously: `IsFrozen` returns `true` once it is done.
- `IsFrozen` returns `false` on a kernel without the freezer.
- `Kill` kills all the processes of the control group and of its descendants. It is the reliable way to stop a job,
  including the processes it started and those that left its process tree.

## Delete a control group

`Delete` removes a control group that has no process and no child control group: it throws an `IOException` otherwise.
Kill the processes and wait for them to exit, delete the children, then delete the group.

## Enable controllers

| Method | What it does |
| --- | --- |
| `GetAvailableControllers()` | The controllers the control group can enable for its children, which its parent enabled |
| `GetEnabledControllers()` | The controllers enabled for its children |
| `SetControllers(names)` | Enables controllers for the children; a name prefixed with `-`, such as `-io`, disables one |

A control group other than the root cannot enable controllers such as `memory` or `io` while it contains processes: move
them to a child first.
