---
title: Troubleshooting
description: The exceptions the library throws when the kernel refuses an operation, and how to fix their cause.
---

The library reads and writes the files of `/sys/fs/cgroup`, so the kernel reports its refusals as file system errors:

| Exception | Usual cause | Fix |
| --- | --- | --- |
| `UnauthorizedAccessException` | The process may not change this part of the hierarchy | Run as root, or work in the part of the hierarchy delegated to your user or service |
| `UnauthorizedAccessException` when setting a limit | The file of the limit does not exist: the controller is not enabled on the parent, or the kernel does not support the feature | Enable the controller on the parent; [GetAvailableControllers](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.GetAvailableControllers) lists the controllers you can enable |
| `IOException` when enabling a controller | The control group contains processes | Move the processes to a child control group, and enable the controller on the group above them |
| `IOException` when setting a limit | The kernel refuses the value, such as a CPU that does not exist in [SetCpusetCpus](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.SetCpusetCpus) | Check the value against the machine |
| `IOException` from [Delete](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.Delete) | The control group still has processes or children | Kill the processes, wait for them to exit, and delete the children first |
| `DirectoryNotFoundException` when reading | The control group no longer exists | Create it again, or drop the stale [CGroup2](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2) instance |

- Reading a value of a controller that is not enabled does not throw: the value is [Unavailable](cref:Meziantou.Framework.Unix.ControlGroups.CGroupValueState.Unavailable). See
  [Reading values](04-ReadingValues.md).
- A control group that no longer exists is not reported as a group without controllers: reading it throws a
  `DirectoryNotFoundException`, so a stale instance is noticed.
- The library supports cgroups v2 only. On a machine that still mounts cgroups v1, `/sys/fs/cgroup` does not have the
  files the library uses.
