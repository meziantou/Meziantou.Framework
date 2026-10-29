---
title: Platform requirements
description: The supported platforms, and what Linux, containers and macOS require for a process to dump itself.
---

A process that dumps itself uses the same operating system features as a debugger. Operating systems restrict them,
and the restrictions differ between Windows, Linux and macOS.

## Supported platforms

| Platform | Requirement |
| --- | --- |
| Windows | Windows 8.1 or later. The process must be x64, x86 or Arm64; other architectures throw `PlatformNotSupportedException` |
| Linux | The `createdump` tool of the .NET runtime, and the permission to attach to the process (see below) |
| macOS | The `createdump` tool of the .NET runtime, and an executable signed with the `get-task-allow` entitlement (see below) |

On the other operating systems, `Write` and `WriteAsync` throw `PlatformNotSupportedException`.

`createdump` ships with the .NET runtime. The package looks for it in the directory of the runtime, then in the
directory of the application, where self-contained and single-file applications have it. When it is in neither,
the dump throws a `FileNotFoundException`.

## Linux: ptrace

On Linux, `createdump` attaches to the process with ptrace. When Yama restricts ptrace
(`kernel.yama.ptrace_scope = 1`, the default on Ubuntu), a process can only attach to its descendants, and
`createdump`, a child process, is not allowed to attach to its parent.

- With `ptrace_scope = 1`, the package handles it: it calls `prctl(PR_SET_PTRACER, PR_SET_PTRACER_ANY)` before starting
  `createdump`, and resets it with `prctl(PR_SET_PTRACER, 0)` afterward. This overrides any value the application set
  with `PR_SET_PTRACER`.
- With `ptrace_scope = 2`, the process needs the `CAP_SYS_PTRACE` capability.
- With `ptrace_scope = 3`, ptrace is disabled and no dump can be created.

Check the current value with this command:

```bash
cat /proc/sys/kernel/yama/ptrace_scope
```

## Containers

In a container, ptrace may be blocked by the seccomp profile of the container runtime, or require the `SYS_PTRACE`
capability. When the dumps fail in a container and work on the host, grant the capability to the container, for
instance with `--cap-add=SYS_PTRACE` for `docker run`.

## macOS: the get-task-allow entitlement

On macOS, `createdump` can only attach to a process whose executable is signed with the
`com.apple.security.get-task-allow` entitlement.

- The `dotnet` host has it: `dotnet app.dll` can be dumped.
- The app host that the .NET SDK generates (`./app`) is signed without entitlements: the dump fails. This is also the
  case of a test project, whose tests run in the app host of the project.

Re-sign the app host with the entitlement to allow dumps. Create an entitlements file:

<!-- ref:../../../tests/Meziantou.Framework.Diagnostics.MemoryDump.Tests/apphost.entitlements;format=md-fenced-code-block;language=xml -->
```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>com.apple.security.get-task-allow</key>
    <true/>
</dict>
</plist>
```
<!-- endref -->

Then sign the executable with it, ad hoc:

```bash
codesign --force --sign - --entitlements entitlements.plist ./app
```

The SDK can write the app host again when it builds the project, so sign it after each build, and after each publish
for the published executable. The tests of this package do it with an MSBuild target that runs after the `Build`
target when the project is built on macOS. Apple does not notarize an application that has this entitlement: keep it
for the builds you test and diagnose.
