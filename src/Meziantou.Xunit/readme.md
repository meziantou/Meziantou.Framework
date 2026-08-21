# Meziantou.Xunit

`Meziantou.Xunit` provides xUnit.v3 attributes to run or skip tests depending on the current environment.

## Installation

```bash
dotnet add package Meziantou.Xunit
```

## Usage

```c#
using Meziantou.Xunit;
using Xunit;

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

`RunIf` executes the test only when all specified conditions match. `SkipIf` skips the test when all specified conditions match.

Both attributes can be applied to a test method and to its test class, but only once per target. An attribute that specifies no condition has no effect.

Conditions are evaluated once the test class instance has been created. Class fixtures, the test class constructor and `IAsyncLifetime.InitializeAsync` therefore run even when the test ends up being skipped.

## Supported conditions

| Condition | Type | Values |
| :--- | :--- | :--- |
| `OperatingSystem` | `TestOperatingSystems` | `Windows`, `Linux`, `MacOS` (`Flags`) |
| `GlobalizationMode` | `TestGlobalizationMode` | `Any`, `Invariant`, `NotInvariant` |
| `ContinuousIntegration` | `ContinuousIntegrationEnvironments` | `GitHubActions` |
| `WindowsGroup` | `WindowsGroups` | `Any`, `User`, `Administrator` |

`GlobalizationMode` reflects [invariant globalization mode](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/globalization), which is enabled by the `InvariantGlobalization` MSBuild property or the `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT` environment variable. `Invariant` means culture data is **not** available; `NotInvariant` means it is.

`WindowsGroups.User` means the current user is a member of the built-in `Users` group and is **not** elevated as an administrator. Any value other than `Any` also requires Windows.
