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
    [RunIf(globalizationMode: TestGlobalizationMode.Disabled)]
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

## Supported conditions

| Condition | Type | Values |
| :--- | :--- | :--- |
| `OperatingSystem` | `TestOperatingSystems` | `Windows`, `Linux`, `MacOS` (`Flags`) |
| `GlobalizationMode` | `TestGlobalizationMode` | `Any`, `Enabled` (invariant mode), `Disabled` (non-invariant mode) |
| `ContinuousIntegration` | `ContinuousIntegrationEnvironments` | `GitHubActions` |
| `WindowsGroup` | `WindowsGroups` | `Any`, `User`, `Administrator` |
