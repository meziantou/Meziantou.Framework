---
title: Getting started
description: Install the package, create a control group, limit its resources and run a process in it.
---

## Install the package

Add the `Meziantou.Framework.Unix.ControlGroups` package to a project:

```bash
dotnet add package Meziantou.Framework.Unix.ControlGroups
```

The examples of this documentation omit the `using` directives: they need
`using Meziantou.Framework.Unix.ControlGroups;`, and `using System.Diagnostics;` for `Process`. They change the
control groups of the machine, so they need Linux and root.

## Run a process with limited resources

<!-- ref:../../tests/Meziantou.Framework.Unix.ControlGroups.Tests/CGroup2Tests.cs;region=CGroup2_GettingStarted;format=md-fenced-code-block;dedent=true -->
```csharp
// Controllers are enabled on a parent cgroup, and apply to its children
var parent = CGroup2.Root.CreateOrGetChild("myapp");
parent.SetControllers("cpu", "memory", "pids");

// The processes go into a child cgroup, which holds the limits
var worker = parent.CreateOrGetChild("worker");
worker.SetCpuMax(50_000, 100_000);         // half of a CPU
worker.SetMemoryMax(256L * 1024 * 1024);   // 256 MB
worker.SetPidsMax(20);                     // 20 processes

using var process = Process.Start("sleep", "60");
worker.AssociateProcess(process);
Assert.Contains(process.Id, worker.GetProcesses());

// A cgroup can be deleted only when it has no process and no child cgroup
worker.Kill();
process.WaitForExit();
worker.Delete();
parent.Delete();
```
<!-- endref -->

- [CreateOrGetChild](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.CreateOrGetChild) creates a control group, a folder of `/sys/fs/cgroup`, or returns the existing one.
- [SetControllers](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.SetControllers) enables the controllers on `parent`, so its children get the files that set the limits. The
  controllers must be enabled on the parent of `parent` too: the root enables the common controllers on most
  distributions.
- `parent` holds no process, and `worker` holds no child: a control group cannot do both, except the root.
- The limits apply to all the processes of `worker`, together, including the processes they start.
- [Kill](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.Kill) stops every process of the control group. [Delete](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.Delete) removes an empty control group.

Moving the current process into a control group limits the application itself: call
`AssociateProcess(Environment.ProcessId)`.

## Next steps

- [Resource limits](03-ResourceLimits.md): every limit you can set.
- [Reading values](04-ReadingValues.md): read the limits and the usage.
