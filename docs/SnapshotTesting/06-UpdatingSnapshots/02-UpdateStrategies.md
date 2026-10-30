---
title: Update strategies
description: Choose what happens when a snapshot does not match, for every test or for a single run.
---

[SnapshotSettings.SnapshotUpdateStrategy](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings.SnapshotUpdateStrategy) decides what happens when a snapshot does not match:

| Strategy | Verified files | Test |
| --- | --- | --- |
| [SnapshotUpdateStrategy.Disallow](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy.Disallow) (default) | Never changed | Fails |
| [SnapshotUpdateStrategy.Overwrite](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy.Overwrite) | Replaced by the actual files | Fails |
| [SnapshotUpdateStrategy.OverwriteWithoutFailure](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy.OverwriteWithoutFailure) | Replaced by the actual files | Passes |
| [SnapshotUpdateStrategy.MergeTool](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy.MergeTool) | Merged in a merge tool, which is started for each changed file | Fails |
| [SnapshotUpdateStrategy.MergeToolSync](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy.MergeToolSync) | Merged in a merge tool; the test waits for the tool to close | Fails |

In every case, the actual files are written first. [Overwrite](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy.Overwrite) and [OverwriteWithoutFailure](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy.OverwriteWithoutFailure) then replace the verified
files, delete the verified files the assertion no longer produces, and delete the actual files. The merge tool
strategies never delete verified files: the failure message lists the ones that are no longer produced.

## Choose a strategy for a run

The strategy usually depends on who runs the tests rather than on the test. Set the `SNAPSHOTTESTING_STRATEGY`
environment variable to the name of a strategy, ignoring case ([Overwrite](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy.Overwrite), `mergetool`, `OVERWRITEWITHOUTFAILURE`).
It sets the default value of [SnapshotUpdateStrategy](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy); a strategy set in code takes precedence. A value that is not the
name of a strategy is ignored, and the default stays [Disallow](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy.Disallow).

```bash
SNAPSHOTTESTING_STRATEGY=OverwriteWithoutFailure dotnet test
```

## Choose a strategy in code

To change the strategy of every test, set it on [SnapshotSettings.Default](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings.Default) in a module initializer (see
[Settings](../04-Settings.md#configure-every-test)). To change it for one assertion, pass a copy of the settings:

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Tests/SnapshotTests.cs;region=Snapshot_Customization;format=md-fenced-code-block;dedent=true -->
```csharp
var value = new { Name = "John", Age = 42 };
var settings = SnapshotSettings.Default with
{
    SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
};

Snapshot.Validate(value, SnapshotType.Default, settings);
```
<!-- endref -->

## Which one to use

- **[Disallow](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy.Disallow)** keeps the test run free of side effects: approve the actual files with the
  [approval tool](03-ApprovalTool.md) when you are happy with them. It is the right default for shared machines and
  automation.
- **[Overwrite](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy.Overwrite)** suits a change that updates many snapshots on purpose: run the tests once, review the diff in your
  version control tool, and run them again to check they pass. The first run fails, so you cannot miss that snapshots
  changed.
- **[OverwriteWithoutFailure](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy.OverwriteWithoutFailure)** records new snapshots in a single run. Use it for a run you review afterwards, never as a
  permanent setting: it hides every regression.
- **[MergeTool](cref:Meziantou.Framework.SnapshotTesting.MergeTool)** and **[MergeToolSync](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy.MergeToolSync)** suit an interactive session in an IDE: each change opens in your diff tool, and
  you keep the parts you want. See [Merge tools](04-MergeTools.md).

## Rewrite every snapshot

[ForceUpdateSnapshots](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings.ForceUpdateSnapshots) updates the verified files even when they match, for instance after changing the serializer
settings to reformat every snapshot. Combine it with a strategy that writes files:

<!-- ref:../../../samples/Meziantou.Framework.SnapshotTesting.Samples/SettingsSnippets.cs;region=Snapshot_ForceUpdateSnapshots;format=md-fenced-code-block;dedent=true -->
```csharp
SnapshotSettings.Default.ForceUpdateSnapshots = true; // Rewrite the snapshots, even the ones that match
SnapshotSettings.Default.SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure;
```
<!-- endref -->

## Custom strategies

[SnapshotUpdateStrategy](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy) is an abstract class. Derive from it to decide, with [CanUpdateSnapshot](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy.CanUpdateSnapshot), whether the verified
files of an assertion can be updated, with [MustReportError](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy.MustReportError) whether the test fails, and with [UpdateFile](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy.UpdateFile) or
[UpdateFiles](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy.UpdateFiles) how the files are updated.
