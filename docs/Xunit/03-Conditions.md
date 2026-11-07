---
title: Conditions
description: Test the operating system, the globalization mode, the continuous integration server and the Windows user.
---

`RunIf` and `SkipIf` accept the same four conditions. Each one has a default value that leaves it out of the decision,
so an attribute only tests the conditions you set.

| Condition | Type | Values | Default (not tested) |
| --- | --- | --- | --- |
| [OperatingSystem](cref:Meziantou.Xunit.ConditionalTestAttributeBase.OperatingSystem) | [TestOperatingSystems](cref:Meziantou.Xunit.TestOperatingSystems) | [Windows](cref:Meziantou.Xunit.TestOperatingSystems.Windows), [Linux](cref:Meziantou.Xunit.TestOperatingSystems.Linux), [MacOS](cref:Meziantou.Xunit.TestOperatingSystems.MacOS), [All](cref:Meziantou.Xunit.TestOperatingSystems.All) (flags) | [None](cref:Meziantou.Xunit.TestOperatingSystems.None) |
| [GlobalizationMode](cref:Meziantou.Xunit.ConditionalTestAttributeBase.GlobalizationMode) | [TestGlobalizationMode](cref:Meziantou.Xunit.TestGlobalizationMode) | [Invariant](cref:Meziantou.Xunit.TestGlobalizationMode.Invariant), [NotInvariant](cref:Meziantou.Xunit.TestGlobalizationMode.NotInvariant) | [Any](cref:Meziantou.Xunit.TestGlobalizationMode.Any) |
| [ContinuousIntegration](cref:Meziantou.Xunit.ConditionalTestAttributeBase.ContinuousIntegration) | [ContinuousIntegrationEnvironments](cref:Meziantou.Xunit.ContinuousIntegrationEnvironments) | [GitHubActions](cref:Meziantou.Xunit.ContinuousIntegrationEnvironments.GitHubActions) (flags) | [None](cref:Meziantou.Xunit.ContinuousIntegrationEnvironments.None) |
| [WindowsGroup](cref:Meziantou.Xunit.ConditionalTestAttributeBase.WindowsGroup) | [WindowsGroups](cref:Meziantou.Xunit.WindowsGroups) | [User](cref:Meziantou.Xunit.WindowsGroups.User), [Administrator](cref:Meziantou.Xunit.WindowsGroups.Administrator) | [Any](cref:Meziantou.Xunit.WindowsGroups.Any) |

Set them with the constructor parameters or the properties of the attribute:

- `RunIf(TestOperatingSystems)`, `RunIf(TestGlobalizationMode)`, `RunIf(WindowsGroups)` and
  `RunIf(TestOperatingSystems, TestGlobalizationMode)`, and the same constructors for `SkipIf`;
- the [OperatingSystem](cref:Meziantou.Xunit.ConditionalTestAttributeBase.OperatingSystem), [GlobalizationMode](cref:Meziantou.Xunit.ConditionalTestAttributeBase.GlobalizationMode), [ContinuousIntegration](cref:Meziantou.Xunit.ConditionalTestAttributeBase.ContinuousIntegration) and [WindowsGroup](cref:Meziantou.Xunit.ConditionalTestAttributeBase.WindowsGroup) properties, for instance
  `[SkipIf(ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions)]`.

An attribute that sets no condition has no effect: the test runs.

## Operating system

[TestOperatingSystems](cref:Meziantou.Xunit.TestOperatingSystems) is a flags enumeration. The condition matches when the test runs on one of the listed systems:
`RunIf(TestOperatingSystems.Windows | TestOperatingSystems.MacOS)` runs on Windows and macOS, and skips the test on
Linux. On any other operating system, the condition does not match.

## Globalization mode

In [invariant globalization mode](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/globalization), .NET
does not use the culture data of the operating system: culture-sensitive operations behave as if only the invariant
culture existed. The mode is enabled by the `InvariantGlobalization` MSBuild property, or by the
`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT` environment variable.

- [TestGlobalizationMode.NotInvariant](cref:Meziantou.Xunit.TestGlobalizationMode.NotInvariant) matches when the culture data is available. Use it with `RunIf` for a test that
  formats or compares text with a named culture.
- [TestGlobalizationMode.Invariant](cref:Meziantou.Xunit.TestGlobalizationMode.Invariant) matches in invariant mode.

The mode is resolved like the runtime does: the `System.Globalization.Invariant` switch, set by the MSBuild property,
takes precedence over the environment variable.

## Continuous integration

[ContinuousIntegrationEnvironments.GitHubActions](cref:Meziantou.Xunit.ContinuousIntegrationEnvironments.GitHubActions) matches when the `GITHUB_ACTIONS` environment variable has a
non-empty value, which GitHub Actions sets on its runners. Use it with `SkipIf` for a test that needs something the
runner lacks, or with `RunIf` for a test that only makes sense there.

## Windows user

[WindowsGroups](cref:Meziantou.Xunit.WindowsGroups) tests the group membership of the current Windows user. Any value other than [Any](cref:Meziantou.Xunit.WindowsGroups.Any) also requires
Windows: on Linux and macOS, the condition does not match.

- [WindowsGroups.Administrator](cref:Meziantou.Xunit.WindowsGroups.Administrator) matches when the process runs elevated as a member of the built-in `Administrators`
  group.
- [WindowsGroups.User](cref:Meziantou.Xunit.WindowsGroups.User) matches when the user is a member of the built-in `Users` group and the process is **not**
  elevated. Administrators are members of `Users` too, so an elevated process does not match.

So `RunIf(WindowsGroups.Administrator)` runs the test on Windows, in an elevated process only, and
`SkipIf(WindowsGroups.Administrator)` runs it everywhere except in an elevated process on Windows.

## Test the environment from code

[TestEnvironment](cref:Meziantou.Xunit.TestEnvironment) exposes the checks the attributes use, for code that adapts to the environment instead of skipping
the test:

- [TestEnvironment.IsGlobalizationInvariant()](cref:Meziantou.Xunit.TestEnvironment.IsGlobalizationInvariant)
- [TestEnvironment.IsOnGitHubActions()](cref:Meziantou.Xunit.TestEnvironment.IsOnGitHubActions)
- `TestEnvironment.IsOnContinuousIntegration(ContinuousIntegrationEnvironments)`

<!-- ref:../../samples/Meziantou.Xunit.Samples/ConditionSamples.cs;region=TestEnvironment_IsOnGitHubActions;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class ImportTests
{
    [Fact]
    public async Task Imports_the_file_within_the_timeout()
    {
        // Shared CI runners are slower than a developer machine
        var timeout = TestEnvironment.IsOnGitHubActions() ? TimeSpan.FromMinutes(2) : TimeSpan.FromSeconds(30);
        using var cancellationTokenSource = new CancellationTokenSource(timeout);

        await File.WriteAllTextAsync(Path.GetTempFileName(), "content", cancellationTokenSource.Token);
    }
}
```
<!-- endref -->
