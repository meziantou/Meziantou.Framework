---
title: Settings
description: Configure every snapshot test of a project in one place, or change the settings of a single assertion.
---

[SnapshotSettings](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings) holds everything that controls an assertion: the serializers, the comparers, the scrubbers, the file
names and the update strategy. [Snapshot.Validate](cref:Meziantou.Framework.SnapshotTesting.Snapshot.Validate) uses [SnapshotSettings.Default](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings.Default) unless you pass settings.

## Configure every test

Configure [SnapshotSettings.Default](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings.Default) once, before any test runs. A module initializer runs when the test assembly is
loaded (`ModuleInitializer` is in the `System.Runtime.CompilerServices` namespace):

<!-- ref:../../samples/Meziantou.Framework.SnapshotTesting.Samples/SnapshotConfiguration.cs;region=Snapshot_ModuleInitializer;format=md-fenced-code-block;dedent=true -->
```csharp
internal static class SnapshotConfiguration
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Runs once, before any test of the assembly
        SnapshotSettings.Default.SnapshotUpdateStrategy = SnapshotUpdateStrategy.MergeTool;
        SnapshotSettings.Default.MergeTools = [MergeTool.VisualStudioCode];
        SnapshotSettings.Default.ScrubUserName();
    }
}
```
<!-- endref -->

[SnapshotSettings.Default](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings.Default) is shared by every test of the process, and tests usually run in parallel. Do not change it
from a test: the change would apply to the tests running at the same time, and registering a serializer or a scrubber
from a test adds it again every time the test runs.

## Change the settings of one assertion

[SnapshotSettings](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings) is a record: `with` creates a copy with its own serializers, comparers and scrubbers, which you can
change without affecting the other tests. [Clone()](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettingsCloneExtensions.Clone) does the same. Pass the copy to [Snapshot.Validate](cref:Meziantou.Framework.SnapshotTesting.Snapshot.Validate):

<!-- ref:../../tests/Meziantou.Framework.SnapshotTesting.Tests/SnapshotTests.cs;region=Snapshot_Customization;format=md-fenced-code-block;dedent=true -->
```csharp
var value = new { Name = "John", Age = 42 };
var settings = SnapshotSettings.Default with
{
    SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
};

Snapshot.Validate(value, SnapshotType.Default, settings);
```
<!-- endref -->

## Properties

| Property | Default | Description |
| --- | --- | --- |
| [SnapshotUpdateStrategy](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy) | [Disallow](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy.Disallow), or the `SNAPSHOTTESTING_STRATEGY` environment variable | What happens when a snapshot does not match. See [Update strategies](06-UpdatingSnapshots/02-UpdateStrategies.md) |
| [ForceUpdateSnapshots](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings.ForceUpdateSnapshots) | `false` | Updates the verified files even when they match, as far as the update strategy allows |
| [AutoDetectContinuousEnvironment](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings.AutoDetectContinuousEnvironment) | `true`, unless `SNAPSHOTTESTING_AUTODETECT_CONTINUOUS_ENVIRONMENT` is `false` | Never updates snapshots on a build server, in a continuous testing runner or in an LLM agent. See [Continuous integration](06-UpdatingSnapshots/05-ContinuousIntegration.md) |
| [MergeTools](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings.MergeTools) | The configured and detected tools | The tools started by the merge tool strategies. See [Merge tools](06-UpdatingSnapshots/04-MergeTools.md) |
| [Serializers](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings.Serializers) | Objects, `byte[]` and `Stream` | Convert a value to the content of the snapshot files. See [Serializers and comparers](05-SerializersAndComparers.md) |
| [Comparers](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings.Comparers) | Text formats ignore line endings, other formats are compared byte for byte | Decide whether a verified file and an actual file match. See [Serializers and comparers](05-SerializersAndComparers.md) |
| [Scrubbers](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings.Scrubbers) | Empty | Rewrite the text snapshots after serialization. See [Deterministic snapshots](03-WritingTests/06-DeterministicSnapshots.md) |
| [SnapshotNamingStrategy](cref:Meziantou.Framework.SnapshotTesting.SnapshotNamingStrategy) | [SnapshotNamingStrategies.ClassName\_TestName](cref:Meziantou.Framework.SnapshotTesting.SnapshotNamingStrategies.ClassName_TestName) | Builds the name of the snapshot from the test. See [Naming snapshots](03-WritingTests/04-NamingSnapshots.md) |
| [SnapshotPathStrategy](cref:Meziantou.Framework.SnapshotTesting.SnapshotPathStrategy) | `__snapshots__` next to the test file | Builds the full path of the verified file. See [Snapshot files](03-WritingTests/03-SnapshotFiles.md) |
| [MaxSnapshotFileNameLength](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings.MaxSnapshotFileNameLength) | `128` | The longest file name before a hash shortens it. At least 20 |

## Extension methods

| Method | Description |
| --- | --- |
| `ConfigureHumanReadableSerializer(options => ...)` | Changes the options of the human-readable serializer, which writes objects as text |
| `AddConverter(converter)` | Adds a [HumanReadableConverter](cref:Meziantou.Framework.HumanReadable.HumanReadableConverter) that controls how a type is written |
| [ScrubLines](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettingsScrubberExtensions.ScrubLines), [ScrubLinesContaining](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettingsScrubberExtensions.ScrubLinesContaining), [ScrubLinesMatching](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettingsScrubberExtensions.ScrubLinesMatching), [ScrubLinesWithReplace](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettingsScrubberExtensions.ScrubLinesWithReplace), [ScrubUserName](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettingsScrubberExtensions.ScrubUserName), [ScrubMachineName](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettingsScrubberExtensions.ScrubMachineName) | Add a scrubber |
| [Clone()](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettingsCloneExtensions.Clone) | Returns a copy of the settings |

The extension packages add their own methods, such as [AddImaging()](cref:Meziantou.Framework.SnapshotTesting.Imaging.SnapshotSettingsImagingExtensions.AddImaging), [AddPdf()](cref:Meziantou.Framework.SnapshotTesting.Pdf.SnapshotSettingsPdfExtensions.AddPdf) and [AddRoslyn()](cref:Meziantou.Framework.SnapshotTesting.Roslyn.SnapshotSettingsRoslynExtensions.AddRoslyn). See
[Extensions](07-Extensions/01-Overview.md).

## Environment variables

| Variable | Effect |
| --- | --- |
| `SNAPSHOTTESTING_STRATEGY` | The default update strategy: the case-insensitive name of a [SnapshotUpdateStrategy](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy) property, such as [Overwrite](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy.Overwrite) or `mergetool` |
| `SNAPSHOTTESTING_AUTODETECT_CONTINUOUS_ENVIRONMENT` | `false`, `0`, `no` or `off` allows updates on a build server, in a continuous testing runner and in an LLM agent |
| `DiffEngine_Tool` | The merge tool to try first, as the case-insensitive name of a [MergeTool](cref:Meziantou.Framework.SnapshotTesting.MergeTool) property |
| `DiffEngine_Disabled` | `true` or `1` disables every merge tool |

The first two variables are read when settings are created, and [SnapshotSettings.Default](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings.Default) is created the first time it
is used: set them before the tests run, for example in your shell.
