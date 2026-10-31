---
title: Getting started
description: Install a Win32 package, find its namespace, and call it safely from an application that also runs on Linux or macOS.
---

## Install a package

Add the package you need to a project, for instance:

<InstallNuGetPackage Id="Meziantou.Framework.Win32.MarkOfTheWeb" />

The other packages are listed in the [introduction](01-Introduction.md#packages).

The types are in the `Meziantou.Framework.Win32` namespace, except those of the projected file system, which are in
[Meziantou.Framework.Win32.ProjectedFileSystem](cref:Meziantou.Framework.Win32.ProjectedFileSystem). The examples of this documentation omit the `using` directives.

## Windows only

The packages do not target a Windows-specific framework (`-windows`): a cross-platform library or
application can reference them. Their methods only work on Windows, and the types are annotated with
`[SupportedOSPlatform]`, so the platform compatibility analyzer (CA1416) reports the calls that are not guarded.

Check the operating system before calling a package from code that can run elsewhere:

<!-- ref:../../tests/Meziantou.Framework.Win32.MarkOfTheWeb.Tests/MarkOfTheWebTests.cs;region=MarkOfTheWeb_PlatformGuard;format=md-fenced-code-block;dedent=true -->
```csharp
static bool IsFromInternet(string path)
{
    // The package only works on Windows: check the operating system before calling it
    if (!OperatingSystem.IsWindows())
        return false;

    return MarkOfTheWeb.IsUntrusted(path);
}
```
<!-- endref -->

- `OperatingSystem.IsWindows()` satisfies the analyzer for the packages that work on every version of Windows that .NET
  supports.
- Some features need a recent version of Windows. Guard them with `OperatingSystem.IsWindowsVersionAtLeast`:

  | Feature | Minimum version |
  | --- | --- |
  | [Antimalware scanning](04-AntimalwareScanning.md) | Windows 10 (`10.0.10240`) |
  | [Projected file system](12-ProjectedFileSystem/01-Overview.md) | Windows 10 version 1809 (`10.0.17763`) |
  | [JobObject.SetIoLimits](cref:Meziantou.Framework.Win32.JobObject.SetIoLimits) of the [job objects](08-JobObjects.md) | Windows 10 (`10.0.10240`) |

- An application that only runs on Windows can declare it once instead, with `[assembly: SupportedOSPlatform("windows")]`
  or by targeting `net10.0-windows`.

## Errors

Most methods report a failure of the underlying Windows API with a `System.ComponentModel.Win32Exception` whose
`NativeErrorCode` is the Windows error code, such as `5` (access denied) when the process lacks the required rights. The
pages of each package describe the exceptions and return values that are specific to it.

## Administrator rights

Some operations require an elevated process: reading the change journal in privileged mode, writing LSA private data,
or enabling a privilege that only administrators hold. The pages say when an example needs it. To tell whether the
current process is elevated, see [Access tokens](03-AccessTokens.md#check-whether-the-process-is-elevated).
