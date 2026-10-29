---
title: The approval tool
description: Approve the actual snapshot files from the command line, all at once or one by one.
---

`Meziantou.Framework.SnapshotTesting.Tool` approves snapshots after a test run: it renames each `*.actual.*` file to the
matching `*.verified.*` file, replacing the old snapshot.

## Install the tool

Install it as a global .NET tool:

```bash
dotnet tool install --global Meziantou.Framework.SnapshotTesting.Tool
```

## Approve every snapshot

Run the tests, review the failures, then approve every actual file under the current folder:

```bash
Meziantou.Framework.SnapshotTesting.Tool approve
```

The tool prints how many snapshots it found, approved, skipped and failed to approve. It exits with code 1 when a file
could not be approved, or when the folder does not exist.

## Approve snapshots one by one

With `--interactive`, the tool asks for each actual file. Answer `y` or `yes` to approve it; `n`, `no` or an empty answer
skips it and leaves the actual file in place:

```bash
Meziantou.Framework.SnapshotTesting.Tool approve --interactive
```

## Options

| Option | Description |
| --- | --- |
| `--interactive` | Asks before approving each snapshot |
| `--folder <folder>` | The folder to process. The current folder by default |
| `--recurse` | Searches the `__snapshots__` folders in every subfolder. Enabled by default |

The tool looks for actual files in the `__snapshots__` folders: the folder itself when you point it at a `__snapshots__`
folder, its `__snapshots__` subfolder otherwise, and every `__snapshots__` folder below it. Run it from the root of the
repository to approve the snapshots of every test project, or point `--folder` at one test project.

## What gets approved

- `Name.actual.ext` replaces `Name.verified.ext`, including for extensions that contain dots (`Name.actual.g.cs`).
- A file whose name contains `.verified.` is never approved, even when it also matches `*.actual.*`: it is the verified
  file of a test whose name contains `.actual.`, as earlier versions named them.
- The library deletes the actual files that are no longer relevant (see
  [The lifetime of an actual file](../03-WritingTests/03-SnapshotFiles.md#the-lifetime-of-an-actual-file)), so an actual
  file left by an earlier failed run cannot be approved over a correct snapshot.

The tool does not delete the verified files that an assertion no longer produces: the failure message lists them, and
you remove them yourself.

The tool only works with the file snapshots of `Meziantou.Framework.SnapshotTesting`. Inline snapshots are stored in the
source code; see [Updating inline snapshots](../../15-InlineSnapshotTesting/08-UpdatingSnapshots.md).
