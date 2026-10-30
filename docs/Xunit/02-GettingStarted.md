---
title: Getting started
description: Install the package, run a test on some operating systems only, and read the skip reason.
---

## Install the package

Add the `Meziantou.Xunit` package to a test project that uses xUnit.v3:

```bash
dotnet add package Meziantou.Xunit
```

The examples of this documentation omit the `using` directives: they need `using Meziantou.Xunit;` and
`using Xunit;`.

## Run a test only in some environments

Add `RunIf` to a test, with the condition it needs:

<!-- ref:../../samples/Meziantou.Xunit.Samples/SampleTests.cs;region=SampleTests;format=md-fenced-code-block;dedent=true -->
```csharp
using Meziantou.Xunit;
using Xunit;

namespace MyProject.Tests;

public sealed class SampleTests
{
    [Fact]
    [RunIf(TestOperatingSystems.Windows | TestOperatingSystems.Linux)]
    public void Runs_on_windows_or_linux()
    {
    }

    [Fact]
    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void Runs_only_when_invariant_globalization_is_disabled()
    {
    }

    [Fact]
    [SkipIf(WindowsGroups.Administrator)]
    public void Skipped_when_running_as_administrator()
    {
    }

    [Fact]
    [RunIf(ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions)]
    public void Runs_only_on_github_actions()
    {
    }
}
```
<!-- endref -->

- `RunIf(TestOperatingSystems.Windows | TestOperatingSystems.Linux)` runs the test on Windows or Linux, and skips it on
  macOS. `TestOperatingSystems` is a flags enumeration: combine the values with `|`.
- `SkipIf(WindowsGroups.Administrator)` does the opposite: the test runs, unless the condition matches.
- The conditions without a constructor parameter, such as `ContinuousIntegration`, are set as named properties of the
  attribute.

The attributes work with `[Fact]` and `[Theory]` tests.

## Read the skip reason

A skipped test is reported with a reason that says which condition decided:

| Attribute | Example of reason |
| --- | --- |
| `RunIf(TestOperatingSystems.Windows)` on Linux | `Run only on Windows` |
| `RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)` in invariant mode | `Run only in non-invariant globalization mode` |
| `SkipIf(WindowsGroups.Administrator)` as an administrator | `Skip due to matching condition: WindowsGroup = Administrator` |

## When the conditions are evaluated

The conditions are evaluated when the test is about to run, after the test class instance has been created. Class
fixtures, the constructor of the test class and `IAsyncLifetime.InitializeAsync` therefore run even when the test ends
up being skipped. Keep them cheap, or make them work in every environment: an exception in a fixture or a constructor
fails the test instead of skipping it.

## Next steps

- [Conditions](03-Conditions.md): the operating system, the globalization mode, the CI server and the Windows user.
- [Combining conditions](04-CombiningConditions.md): several conditions, class attributes and attributes of your own.
