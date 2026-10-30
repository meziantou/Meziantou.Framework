---
title: Settings
description: Configure every snapshot test of a project in one place, or change the settings of a single assertion.
---

`SnapshotSettings` holds everything that controls an assertion: the serializers, the comparers, the scrubbers, the file
names and the update strategy. `Snapshot.Validate` uses `SnapshotSettings.Default` unless you pass settings.

## Configure every test

Configure `SnapshotSettings.Default` once, before any test runs. A module initializer runs when the test assembly is
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

`SnapshotSettings.Default` is shared by every test of the process, and tests usually run in parallel. Do not change it
from a test: the change would apply to the tests running at the same time, and registering a serializer or a scrubber
from a test adds it again every time the test runs.

## Change the settings of one assertion

`SnapshotSettings` is a record: `with` creates a copy with its own serializers, comparers and scrubbers, which you can
change without affecting the other tests. `Clone()` does the same. Pass the copy to `Snapshot.Validate`:

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
| `SnapshotUpdateStrategy` | `Disallow`, or the `SNAPSHOTTESTING_STRATEGY` environment variable | What happens when a snapshot does not match. See [Update strategies](06-UpdatingSnapshots/02-UpdateStrategies.md) |
| `ForceUpdateSnapshots` | `false` | Updates the verified files even when they match, as far as the update strategy allows |
| `AutoDetectContinuousEnvironment` | `true`, unless `SNAPSHOTTESTING_AUTODETECT_CONTINUOUS_ENVIRONMENT` is `false` | Never updates snapshots on a build server, in a continuous testing runner or in an LLM agent. See [Continuous integration](06-UpdatingSnapshots/05-ContinuousIntegration.md) |
| `MergeTools` | The configured and detected tools | The tools started by the merge tool strategies. See [Merge tools](06-UpdatingSnapshots/04-MergeTools.md) |
| `Serializers` | Objects, `byte[]` and `Stream` | Convert a value to the content of the snapshot files. See [Serializers and comparers](05-SerializersAndComparers.md) |
| `Comparers` | Text formats ignore line endings, other formats are compared byte for byte | Decide whether a verified file and an actual file match. See [Serializers and comparers](05-SerializersAndComparers.md) |
| `Scrubbers` | Empty | Rewrite the text snapshots after serialization. See [Deterministic snapshots](03-WritingTests/06-DeterministicSnapshots.md) |
| `SnapshotNamingStrategy` | `SnapshotNamingStrategies.ClassName_TestName` | Builds the name of the snapshot from the test. See [Naming snapshots](03-WritingTests/04-NamingSnapshots.md) |
| `SnapshotPathStrategy` | `__snapshots__` next to the test file | Builds the full path of the verified file. See [Snapshot files](03-WritingTests/03-SnapshotFiles.md) |
| `MaxSnapshotFileNameLength` | `128` | The longest file name before a hash shortens it. At least 20 |

## Extension methods

| Method | Description |
| --- | --- |
| `ConfigureHumanReadableSerializer(options => ...)` | Changes the options of the human-readable serializer, which writes objects as text |
| `AddConverter(converter)` | Adds a `HumanReadableConverter` that controls how a type is written |
| `ScrubLines`, `ScrubLinesContaining`, `ScrubLinesMatching`, `ScrubLinesWithReplace`, `ScrubUserName`, `ScrubMachineName` | Add a scrubber |
| `Clone()` | Returns a copy of the settings |

The extension packages add their own methods, such as `AddImaging()`, `AddPdf()` and `AddRoslyn()`. See
[Extensions](07-Extensions/01-Overview.md).

## Environment variables

| Variable | Effect |
| --- | --- |
| `SNAPSHOTTESTING_STRATEGY` | The default update strategy: the case-insensitive name of a `SnapshotUpdateStrategy` property, such as `Overwrite` or `mergetool` |
| `SNAPSHOTTESTING_AUTODETECT_CONTINUOUS_ENVIRONMENT` | `false`, `0`, `no` or `off` allows updates on a build server, in a continuous testing runner and in an LLM agent |
| `DiffEngine_Tool` | The merge tool to try first, as the case-insensitive name of a `MergeTool` property |
| `DiffEngine_Disabled` | `true` or `1` disables every merge tool |

The first two variables are read when settings are created, and `SnapshotSettings.Default` is created the first time it
is used: set them before the tests run, for example in your shell.
