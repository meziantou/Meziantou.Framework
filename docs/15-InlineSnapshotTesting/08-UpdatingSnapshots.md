---
title: Updating snapshots
description: Choose how inline snapshots are updated in your source code, for every test or for a single run.
---

When a snapshot does not match, the test fails and the failure message shows the difference between the expected and the
actual value. `InlineSnapshotSettings.SnapshotUpdateStrategy` decides whether the library also updates the snapshot in
your source code:

| Strategy | Source file | Test |
| --- | --- | --- |
| `SnapshotUpdateStrategy.Disallow` (default) | Never changed | Fails |
| `SnapshotUpdateStrategy.Overwrite` | The snapshot is rewritten | Fails |
| `SnapshotUpdateStrategy.OverwriteWithoutFailure` | The snapshot is rewritten | Passes |
| `SnapshotUpdateStrategy.MergeTool` | A merge tool opens on the change, and you accept it or not | Fails |
| `SnapshotUpdateStrategy.MergeToolSync` | A merge tool opens on the change; the test waits for it to close | Fails |

With `Disallow`, copy the actual value from the failure message into the test, or run the tests again with another
strategy. Whatever the strategy, snapshots are never updated on a build server, in a continuous testing runner or when
the tests are run by an LLM agent (see [Continuous integration](10-ContinuousIntegration.md)).

## Choose a strategy for a run

Set the `INLINESNAPSHOTTESTING_STRATEGY` environment variable to the name of a strategy, ignoring case (`Overwrite`,
`mergetool`, `OVERWRITEWITHOUTFAILURE`). It sets the default value of `SnapshotUpdateStrategy`; a strategy set in code
takes precedence. A value that is not the name of a strategy is ignored, and the failure message says so.

```bash
INLINESNAPSHOTTESTING_STRATEGY=Overwrite dotnet test
```

## Choose a strategy in code

To change the strategy of every test, set it in a module initializer (see
[Settings](03-Settings.md#configure-every-test)):

<!-- ref:../../samples/Meziantou.Framework.InlineSnapshotTesting.Samples/ReadmeSnippets.cs;region=InlineSnapshot_UseMergeTool;format=md-fenced-code-block;dedent=true -->
```csharp
InlineSnapshotSettings.Default = InlineSnapshotSettings.Default with
{
    SnapshotUpdateStrategy = SnapshotUpdateStrategy.MergeTool,
};
```
<!-- endref -->

To change it for one assertion:

<!-- ref:../../tests/Meziantou.Framework.InlineSnapshotTesting.Tests/InlineSnapshotTests.cs;region=InlineSnapshot_Disallow;format=md-fenced-code-block;dedent=true -->
```csharp
InlineSnapshot
    .WithSettings(settings => settings.SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow)
    .Validate(new { Name = "meziantou" }, "Name: meziantou");
```
<!-- endref -->

## Which one to use

- **`Disallow`** never changes your source code during a test run: the right default for shared machines and automation,
  and for teams that prefer to edit snapshots by hand.
- **`MergeTool`** and **`MergeToolSync`** suit an interactive session in an IDE: each change opens in your diff tool, and
  you decide. See [Merge tools](09-MergeTools.md).
- **`Overwrite`** suits a change that updates many snapshots on purpose: run the tests once, review the diff of your
  source files in your version control tool, and run them again. The first run fails, so you cannot miss that snapshots
  changed.
- **`OverwriteWithoutFailure`** writes new snapshots in a single run. Use it for a run you review afterwards, never as a
  permanent setting: it hides every regression.

## Rewrite every snapshot

`ForceUpdateSnapshots` rewrites the snapshots even when they match, for instance to apply new
[string formats](06-StringFormatsAndSourceFiles.md) or a new indentation to existing snapshots. Combine it with
`OverwriteWithoutFailure`:

<!-- ref:../../samples/Meziantou.Framework.InlineSnapshotTesting.Samples/ReadmeSnippets.cs;region=InlineSnapshot_ForceUpdateSnapshots;format=md-fenced-code-block;dedent=true -->
```csharp
InlineSnapshotSettings.Default = InlineSnapshotSettings.Default with
{
    ForceUpdateSnapshots = true, // Override the snapshot even if the value matches the expected value
    SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
};
```
<!-- endref -->

## Migrate from version 3

Since version 4, snapshot updates are disabled by default (`Disallow`). Version 3 opened a merge tool. To keep the
behavior of version 3, set the strategy to `MergeTool`, as shown above, or to `MergeToolSync` to wait for the merge tool
to close before the test continues.
