---
title: Settings
description: Configure every inline snapshot test in one place, or change the settings of a single assertion.
---

[InlineSnapshotSettings](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotSettings) holds everything that controls an assertion: the serializer, the comparer, the scrubbers, how
the snapshot is written in the source file, and the update strategy. [InlineSnapshot.Validate](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshot.Validate) uses
[InlineSnapshotSettings.Default](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotSettings.Default) unless you pass settings.

## Configure every test

Replace [InlineSnapshotSettings.Default](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotSettings.Default) before any test runs. A module initializer runs when the test assembly is loaded
(`ModuleInitializer` is in the `System.Runtime.CompilerServices` namespace):

<!-- ref:../../samples/Meziantou.Framework.InlineSnapshotTesting.Samples/AssemblyInitializer.cs;region=InlineSnapshot_ModuleInitializer;format=md-fenced-code-block;dedent=true -->
```csharp
internal static class AssemblyInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        InlineSnapshotSettings.Default = InlineSnapshotSettings.Default with
        {
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.MergeTool,
            MergeTools = [MergeTool.VisualStudioCode],
        };
    }
}
```
<!-- endref -->

[InlineSnapshotSettings](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotSettings) is a record: `with` creates a copy, with its own list of scrubbers. [Clone()](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotSettingsCloneExtensions.Clone) does the same.

## Change the settings of one assertion

Pass settings to [Validate](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshot.Validate):

<!-- ref:../../tests/Meziantou.Framework.InlineSnapshotTesting.Tests/InlineSnapshotTests.cs;region=InlineSnapshot_SettingsPerAssert;format=md-fenced-code-block;dedent=true -->
```csharp
var data = new { FirstName = "Gérald", LastName = "Barré" };

// InlineSnapshotSettings is a record, so you can use the "with" keyword to create a new instance
var settings = InlineSnapshotSettings.Default with
{
    SnapshotUpdateStrategy = SnapshotUpdateStrategy.Overwrite,
};

InlineSnapshot.Validate(data, settings, """
    FirstName: Gérald
    LastName: Barré
    """);
```
<!-- endref -->

Or use the builder syntax, with an instance of the settings:

<!-- ref:../../tests/Meziantou.Framework.InlineSnapshotTesting.Tests/InlineSnapshotTests.cs;region=InlineSnapshot_SettingsBuilder;format=md-fenced-code-block;dedent=true -->
```csharp
InlineSnapshot
    .WithSettings(settings)
    .Validate(data, """
        FirstName: Gérald
        LastName: Barré
        """);
```
<!-- endref -->

Or with a function that changes a copy of the default settings:

<!-- ref:../../tests/Meziantou.Framework.InlineSnapshotTesting.Tests/InlineSnapshotTests.cs;region=InlineSnapshot_SettingsInline;format=md-fenced-code-block;dedent=true -->
```csharp
InlineSnapshot
    .WithSettings(settings => settings.SnapshotUpdateStrategy = SnapshotUpdateStrategy.Overwrite)
    .Validate(data, """
        FirstName: Gérald
        LastName: Barré
        """);
```
<!-- endref -->

[InlineSnapshot.WithSettings](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshot.WithSettings) and [InlineSnapshot.WithSerializer](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshot.WithSerializer) return an [InlineSnapshotBuilder](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotBuilder); chain
[WithSettings](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotBuilder.WithSettings) and [WithSerializer](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotBuilder.WithSerializer) calls, then call [Validate](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotBuilder.Validate).

## Properties

| Property | Default | Description |
| --- | --- | --- |
| [SnapshotUpdateStrategy](cref:Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategy) | [Disallow](cref:Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategy.Disallow), or the `INLINESNAPSHOTTESTING_STRATEGY` environment variable | What happens when a snapshot does not match. See [Updating snapshots](08-UpdatingSnapshots.md) |
| [ForceUpdateSnapshots](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotSettings.ForceUpdateSnapshots) | `false` | Rewrites snapshots even when they match, for instance to reformat them |
| [AutoDetectContinuousEnvironment](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotSettings.AutoDetectContinuousEnvironment) | `true`, unless `INLINESNAPSHOTTESTING_AUTODETECT_CONTINUOUS_ENVIRONMENT` is `false` | Never updates snapshots on a build server, in a continuous testing runner or in an LLM agent. See [Continuous integration](10-ContinuousIntegration.md) |
| [MergeTools](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotSettings.MergeTools) | The configured and detected tools | The tools started by the merge tool strategies. See [Merge tools](09-MergeTools.md) |
| [SnapshotSerializer](cref:Meziantou.Framework.InlineSnapshotTesting.Serialization.SnapshotSerializer) | The human-readable serializer | Converts the value to text. See [Serializers](04-Serializers.md) |
| [Scrubbers](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotSettings.Scrubbers) | Empty | Rewrite the text after serialization. See [Deterministic snapshots](05-DeterministicSnapshots.md) |
| [SnapshotComparer](cref:Meziantou.Framework.InlineSnapshotTesting.SnapshotComparer) | [SnapshotComparer.Default](cref:Meziantou.Framework.InlineSnapshotTesting.SnapshotComparer.Default) | Decides whether the actual and expected values match. See [Comparing snapshots](#comparing-snapshots) |
| [ErrorMessageFormatter](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotSettings.ErrorMessageFormatter) | An inline diff | Formats the difference shown in the failure message |
| [AllowedStringFormats](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotSettings.AllowedStringFormats) | [CSharpStringFormats.Default](cref:Meziantou.Framework.InlineSnapshotTesting.CSharpStringFormats.Default) | The C# string literals the library may write. See [String formats and source files](06-StringFormatsAndSourceFiles.md) |
| [Indentation](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotSettings.Indentation), [EndOfLine](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotSettings.EndOfLine), [FileEncoding](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotSettings.FileEncoding) | `null`: detected from the source file | How the source file is written |
| [ValidateSourceFilePathUsingPdbInfoWhenAvailable](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotSettings.ValidateSourceFilePathUsingPdbInfoWhenAvailable) | `true` | Checks the file to edit against the PDB |
| [ValidateLineNumberUsingPdbInfoWhenAvailable](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotSettings.ValidateLineNumberUsingPdbInfoWhenAvailable) | `false` | Checks the line to edit against the PDB |

## Comparing snapshots

[SnapshotComparer.Default](cref:Meziantou.Framework.InlineSnapshotTesting.SnapshotComparer.Default) normalizes both values before comparing them: line endings become `\n`, tabs become four
spaces, and lines that contain only whitespace become empty. A snapshot whose line endings or indentation were changed
by an editor still matches.

To compare differently, derive from [SnapshotComparer](cref:Meziantou.Framework.InlineSnapshotTesting.SnapshotComparer): [NormalizeValue](cref:Meziantou.Framework.InlineSnapshotTesting.SnapshotComparer.NormalizeValue) prepares each value (it returns the value
unchanged by default), and [AreEqual](cref:Meziantou.Framework.InlineSnapshotTesting.SnapshotComparer.AreEqual) compares the normalized values. The normalized values are also the ones shown in
the failure message.

## Deterministic source paths

When your build uses deterministic paths, `[CallerFilePath]` returns a mapped path such as `/_/tests/MyTests.cs`. The
package registers the source roots of the project from its MSBuild files, so the path is mapped back to the file on
disk. You can also register a mapping yourself:

<!-- ref:../../samples/Meziantou.Framework.InlineSnapshotTesting.Samples/ReadmeSnippets.cs;region=InlineSnapshot_RegisterSourceRootMapping;format=md-fenced-code-block;dedent=true -->
```csharp
InlineSnapshot.RegisterSourceRootMapping("/_/", @"C:/agent/_work/1/s/");
```
<!-- endref -->

## Environment variables

| Variable | Effect |
| --- | --- |
| `INLINESNAPSHOTTESTING_STRATEGY` | The default update strategy: the case-insensitive name of a [SnapshotUpdateStrategy](cref:Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategy) property, such as [Overwrite](cref:Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategy.Overwrite) or `mergetool` |
| `INLINESNAPSHOTTESTING_AUTODETECT_CONTINUOUS_ENVIRONMENT` | `false`, `0`, `no` or `off` allows updates on a build server, in a continuous testing runner and in an LLM agent |
| `DiffEngine_Tool` | The merge tool to try first, as the case-insensitive name of a [MergeTool](cref:Meziantou.Framework.InlineSnapshotTesting.MergeTool) property |
| `DiffEngine_Disabled` | `true` or `1` disables every merge tool |
