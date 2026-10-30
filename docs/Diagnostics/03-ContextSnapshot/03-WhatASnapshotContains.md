---
title: What a snapshot contains
description: The keys of a snapshot, the method that adds each one, and the type and content of its value.
---

## Read a value

The values of the snapshot are typed objects. Cast a value to its type to read its properties:

<!-- ref:../../../tests/Meziantou.Framework.Diagnostics.ContextSnapshot.Tests/SnapshotTests.cs;region=CpuSnapshot_AddCpu;format=md-fenced-code-block;dedent=true -->
```csharp
var snapshot = new ContextSnapshotBuilder().AddCpu().BuildSnapshot();
var cpuInfo = Assert.IsType<CpuSnapshot>(snapshot["CPU"]);

// A value is null when it is not available on the current platform
Assert.NotEqual("", cpuInfo.ProcessorName);
Assert.True(cpuInfo.PhysicalCoreCount is null or > 0);
Assert.True(cpuInfo.LogicalCoreCount is null or > 0);
Assert.True(cpuInfo.MaxFrequency is null or > 0); // in hertz
```
<!-- endref -->

A property is `null` when the platform does not provide the information, here when the CPU details cannot be read.

## The machine and the operating system

| Key | Method | Value |
| --- | --- | --- |
| `OperatingSystem` | `AddOperatingSystem` | `OperatingSystemSnapshot`: architecture, `PlatformID`, version and service pack, an `IsWindows`, `IsLinux`... property per platform, the update build revision on Windows, and the name of the distribution on Linux (from `/etc/os-release`) |
| `MachineName` | `AddMachineName` | The name of the machine |
| `ProcessorCount` | `AddProcessorCount` | The number of logical processors available to the process |
| `CPU` | `AddCpu` | `CpuSnapshot`: processor name, processor, core and logical core counts, nominal, minimum and maximum frequencies in hertz, and the vector sizes and instruction sets (x86, Arm, WebAssembly) that the runtime can use |
| `Drives` | `AddDrives` | An array of `DriveSnapshot`: name, format, type, volume label, free space and total size, root directory |
| `LocalTimeZone` | `AddLocalTimeZone` | `TimeZoneSnapshot`: identifier, standard name, base UTC offset, and whether it has daylight saving time |
| `Hypervisor` | `AddHypervisor` | Windows only: `HypervisorSnapshot` with `Hyper-V`, `VirtualBox` or `VMWare`; `null` when none of them is detected, and on the other platforms |
| `PowerManagement` | `AddPowerManagement` | Windows only: `PowerManagementSnapshot` with the identifier and the name of the active power plan; `null` on the other platforms |
| `Antivirus` | `AddSecurityProviders` | `SecurityProvidersSnapshot`: on Windows 8 and later, the antivirus, firewall and anti-spyware products registered in the Windows Security Center, with their state, and the health of the antivirus protection; empty on the other platforms |

## The .NET runtime

| Key | Method | Value |
| --- | --- | --- |
| `GC` | `AddGarbageCollector` | `GarbageCollectorSnapshot`: maximum generation, total allocated bytes, bytes currently allocated, total pause duration, and the configuration variables of the garbage collector |
| `ThreadPool` | `AddThreadPool` | `ThreadPoolSnapshot`: the number of threads, and the available, minimum and maximum worker and I/O completion port threads |
| `AssemblyLoadContexts` | `AddAssemblyLoadContexts` | An array of `AssemblyLoadContextSnapshot`: the name of each load context, whether it is collectible, and its assemblies with their name, version, location, public key token, culture and modules |
| `RuntimeFeatures` | `AddRuntimeFeatures` | One Boolean per feature of `RuntimeFeature`, such as `IsDynamicCodeSupported`. The type of this value is not public: serialize the snapshot to read it |
| `AppContext.<name>` | `AddAppContextData` | One key per `AppContext` switch (a Boolean) and per `AppContext` data value |
| `Dotnet` | `AddDotnetRuntime` | `DotnetRuntimeSnapshot`: runtime identifier, framework description, and whether the runtime is Mono. Not added by `AddDefault` |

## The process

| Key | Method | Value |
| --- | --- | --- |
| `Process` | `AddCurrentProcess` | `CurrentProcessSnapshot`: identifier, name, start time, main module and loaded modules with their versions, processor times, memory counters, handle count, priority, command line, executable path, architecture, and whether the process runs elevated or as root |
| `CurrentDirectory` | `AddCurrentDirectory` | The current directory |
| `User` | `AddUser` | `UserSnapshot`: user name, domain name, and whether the process runs in interactive mode |
| `Culture` | `AddCulture` | `CultureSnapshot`: the current culture and UI culture of the calling thread, the default cultures of new threads, and whether the globalization invariant mode and the predefined-cultures-only mode are enabled |
| `Console` | `AddConsole` | `ConsoleSnapshot`: whether the input, output and error streams are redirected, their encodings, the buffer and window sizes, the colors, and the title on Windows |
| `NewLine` | `AddNewLine` | `Environment.NewLine` |
| `SpecialFolders` | `AddSpecialFolderPaths` | `SpecialFolderSnapshot`: one property per value of `Environment.SpecialFolder`, an empty string when the folder does not exist on the platform |
| `EnvironmentVariables.Process` | `AddEnvironmentVariables` | The environment variables of the process, sorted by name, with the secrets redacted. `PATH` is split into an array of directories |
| `EnvironmentVariables.User` and `EnvironmentVariables.Machine` | `AddEnvironmentVariables(target)` | The user and machine environment variables stored in the registry on Windows; empty on the other platforms |

`AddDefault` adds all the keys of these tables but `Dotnet`. It reads the environment variables of the three
targets, with the default redaction described in [Sensitive data](04-SensitiveData.md).

Most values that cannot be read, because of the platform or of the permissions of the process, are left at their
default value: `null`, `0`, `false` or empty.
