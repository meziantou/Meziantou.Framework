---
title: Combining conditions
description: Combine conditions in one attribute, on a class and its tests, or in an attribute of your own.
---

## Several conditions in one attribute

The conditions of one attribute are combined with a logical AND:

- `RunIf` runs the test only when **every** condition matches;
- `SkipIf` skips the test only when **every** condition matches.

<!-- ref:../../samples/Meziantou.Xunit.Samples/ConditionSamples.cs;region=RunIf_SeveralConditions;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class FormattingTests
{
    // Runs only when every condition matches: on Linux, with the culture data available
    [Fact]
    [RunIf(TestOperatingSystems.Linux, TestGlobalizationMode.NotInvariant)]
    public void Runs_on_linux_with_culture_data()
    {
    }

    // Skipped only when every condition matches: on GitHub Actions, in invariant globalization mode
    [Fact]
    [SkipIf(ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions, GlobalizationMode = TestGlobalizationMode.Invariant)]
    public void Skipped_on_github_actions_in_invariant_mode()
    {
    }
}
```
<!-- endref -->

A target accepts one `RunIf` and one `SkipIf` at most. To run a test on several operating systems, combine the values
in one condition with `|`. To skip a test in two unrelated environments, put a `SkipIf` on the class and another one on
the test, or add an [attribute of your own](#write-your-own-attribute).

## Attributes on the class and on its tests

Both attributes can be applied to a test class, where they apply to every test of the class. A test is affected by the
attributes of its class and by its own:

- with `RunIf` on both, the test runs only when both match;
- with `SkipIf` on both, the test is skipped when either matches;
- with `RunIf` on one and `SkipIf` on the other, the test runs when the `RunIf` matches and the `SkipIf` does not.

<!-- ref:../../samples/Meziantou.Xunit.Samples/ConditionSamples.cs;region=RunIf_ClassAndMethod;format=md-fenced-code-block;dedent=true -->
```csharp
[RunIf(TestOperatingSystems.Windows | TestOperatingSystems.MacOS)]
public sealed class DesktopTests
{
    [Fact]
    public void Runs_on_windows_or_macos()
    {
    }

    // The attributes of the class and of the method both apply
    [Fact]
    [SkipIf(ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions)]
    public void Runs_on_windows_or_macos_except_on_github_actions()
    {
    }
}
```
<!-- endref -->

## Write your own attribute

`RunIf` and `SkipIf` derive from `ConditionalTestAttributeBase`. Derive from it to give a name to a condition you use in
many places: set the conditions in the constructor, and override `InvertCondition`. `false` runs the test when the
conditions match, like `RunIf`; `true` skips it when they match, like `SkipIf`.

<!-- ref:../../samples/Meziantou.Xunit.Samples/ConditionSamples.cs;region=ConditionalTestAttributeBase_CustomAttribute;format=md-fenced-code-block;dedent=true -->
```csharp
// A condition you use in many places, under a name of your own
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RunOnLinuxCIAttribute : ConditionalTestAttributeBase
{
    public RunOnLinuxCIAttribute()
        : base(TestOperatingSystems.Linux)
    {
        ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions;
    }

    // false: the test runs when the conditions match, like RunIf; true: it is skipped, like SkipIf
    protected override bool InvertCondition => false;
}

public sealed class DeploymentTests
{
    [Fact]
    [RunOnLinuxCI]
    public void Publishes_the_container_image()
    {
    }
}
```
<!-- endref -->

The base class has the constructors of `RunIf` and `SkipIf`, as protected constructors, and their four condition
properties. It evaluates the conditions the same way, and reports the same skip reasons.
