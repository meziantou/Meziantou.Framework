---
title: Introduction
description: Manage Linux control groups v2 from .NET to limit and measure the resources of groups of processes.
---

`Meziantou.Framework.Unix.ControlGroups` manages Linux control groups (cgroups v2). A control group is a set of
processes that the kernel limits and accounts for as a whole: how much CPU time they get, how much memory they can
use, how many processes they can start. With this library, an application can run a build, a test, a plugin or a
user's job in a control group of its own, cap its resources, watch its usage, and stop all its processes at once.

The library works with the files of the cgroup file system (`/sys/fs/cgroup`): it has no native dependency.

## What you can do with it

- **Create and delete control groups**, and move processes into them. See [Getting started](02-GettingStarted.md) and
  [Managing processes](05-ManagingProcesses.md).
- **Limit resources**: CPU time and CPU set, memory and swap, IO bandwidth, number of processes, huge pages. See
  [Resource limits](03-ResourceLimits.md).
- **Read the limits and the usage**, and tell an unlimited value from a controller that is not enabled. See
  [Reading values](04-ReadingValues.md).
- **Freeze, resume and kill** all the processes of a control group. See
  [Managing processes](05-ManagingProcesses.md).

## Requirements

- **Linux only.** The types are marked `[SupportedOSPlatform("linux")]`, so the platform compatibility analyzer reports
  calls that are not guarded by `OperatingSystem.IsLinux()`.
- **cgroups v2**, mounted at `/sys/fs/cgroup`, which is the default on current distributions. cgroups v1 is not
  supported.
- **Linux 4.5 or later.** Some features need a later kernel: threads ([AssociateThread](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.AssociateThread), [GetThreads](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.GetThreads)) need 4.14, the
  freezer ([Freeze](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.Freeze), [Unfreeze](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.Unfreeze), [IsFrozen](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.IsFrozen)) 5.2, and [Kill](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.Kill) 5.14. The statistics the kernel reports also vary by
  version.
- **Permissions.** Creating control groups and changing their limits usually needs root, or a part of the hierarchy
  delegated to the user, such as the one systemd delegates to a service.

## How control groups work

- Control groups form a tree, whose root is `/sys/fs/cgroup`. [CGroup2.Root](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.Root) represents it, and each [CGroup2](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2) is a
  folder of that tree.
- A **controller** handles one resource: `cpu`, `memory`, `io`, `pids`, `cpuset`, `hugetlb`. A controller is enabled
  on a control group for its **children**: to limit the memory of a group, enable `memory` on its parent.
- **No internal processes**: a control group other than the root cannot both contain processes and enable controllers
  such as `memory` or `io` for its children. Put the processes in the leaves of the tree, and enable the controllers on
  the groups above them.
- Limits are hierarchical: a child never gets more than its parent allows.

## Namespace

Every type is in the [Meziantou.Framework.Unix.ControlGroups](cref:Meziantou.Framework.Unix.ControlGroups) namespace.

## Next steps

- [Getting started](02-GettingStarted.md): install the package and run a process with limited resources.
- [Troubleshooting](06-Troubleshooting.md): the errors the kernel reports, and what they mean.
