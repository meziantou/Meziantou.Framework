---
title: Snapshot files
description: Where the verified and actual files are stored, how their names are built, and how to store them in git.
---

## Where snapshots are stored

Snapshots are stored in a `__snapshots__` folder next to the source file of the test:

- the expected snapshot is the *verified* file: `__snapshots__/SampleTests_ValidateUser.verified.txt`;
- when a snapshot does not match, the new content is written to the *actual* file:
  `__snapshots__/SampleTests_ValidateUser.actual.txt`.

The folder comes from the `[CallerFilePath]` of the call to `Snapshot.Validate`: the path of the source file, as the
compiler saw it. When your build uses deterministic paths (`/_/src/...`), the package registers the source roots of the
project from its MSBuild files, so the path is mapped back to the folder on disk. You can also register a mapping
yourself with `Snapshot.RegisterSourceRootMapping(mappedPath, realPath)`.

To store the snapshots somewhere else, set `SnapshotSettings.SnapshotPathStrategy` to a function that returns the full
path of the verified file from a `SnapshotPathContext` (the source file, the class and method names, the extension,
the index of the file, ...). See [Settings](../04-Settings.md).

## The lifetime of an actual file

- An actual file is written every time a snapshot does not match, including when the verified file does not exist yet.
- It is deleted once it is no longer relevant: when the snapshot matches again, when the assertion no longer produces
  that snapshot, and after an update strategy replaced the verified file. A stale actual file left by an earlier failed
  run can therefore never be approved over a correct snapshot.
- A test project that targets several frameworks runs one test process per framework, and their output may differ. An
  actual file is only deleted as stale when it was written before the current test process started (with a margin of a
  few seconds) or by the current process itself, so a process whose snapshot matches does not delete the actual file
  that the other framework's process just wrote.

## File names

The name of a snapshot file is built from the name of the test (see [Naming snapshots](04-NamingSnapshots.md)), an
optional suffix, `.verified.` or `.actual.`, and the extension of the [snapshot type](02-SnapshotTypes.md):

| Part | Example | When |
| --- | --- | --- |
| Test name | `SampleTests_ValidateUser` | Always |
| `~<ordinal>` | `SampleTests_ValidateUser~2` | The second, third, ... call to `Snapshot.Validate` in the same test |
| `_<index>` | `SampleTests_ValidateUser_0`, `_1` | One assertion that produces several files, such as the pages of a PDF document |
| Marker and extension | `.verified.txt`, `.actual.png` | Always |

The extension may contain dots (`Name.verified.g.cs`, `Name.actual.d.ts`): the `.verified.` and `.actual.` markers, not
the last dot, separate the name from the extension.

Names are made safe for every file system:

- Only letters, digits, `-`, `_` and `.` are kept. Any other character (a space, a parenthesis, a quote, a `/`) is
  replaced with `_`, and a stable hash of the original name is added, so that `Case_a/b` and `Case_a?b` do not claim the
  same file. The hash does not depend on where the repository is checked out.
- A hash is also added when the name is too long (more than `SnapshotSettings.MaxSnapshotFileNameLength` characters, 128
  by default, or more than the 255 UTF-8 bytes most file systems accept), when it ends with `.verified` or `.actual`,
  and when the part before its first `.` is a Windows device name (`CON`, `PRN`, `AUX`, `NUL`, `COM0`-`COM9`,
  `LPT0`-`LPT9`, in any case).
- A name never contains `.verified.` or `.actual.`, in any case: a test called `Parse.actual.value` is stored as
  `Parse_actual.value_<hash>.verified.txt`, so the approval tool cannot take its verified file for an actual file.

## Renamed tests

A file whose name only differs from the snapshot name by case (the test `ValidateUrl` was renamed `ValidateURL`) is still
found on a case-insensitive file system, the default on Windows and macOS, but not on Linux. The assertion reports it,
even when the content matches, and renames the file when the update strategy allows updates (`Overwrite`,
`OverwriteWithoutFailure`, `MergeTool` or `MergeToolSync`).

When the type of a call changes, the snapshot file with the old extension is normally deleted or reported. When the test
has several calls, such a file may be the snapshot of a call that did not run, so it is kept: remove it yourself when the
type of a call really changed.

## Storing snapshots in git

Commit the verified files with the tests, and leave the actual files out.

Snapshots are often binary: images, the PNG pages of a PDF document, and `.bin` files for values whose format is
unknown. Add the line `**/__snapshots__/** -text` to the `.gitattributes` file of your repository, so that git never
applies line-ending conversion to them.

Without it, a repository that uses the default `core.autocrlf=true` on Windows rewrites the line endings of every binary
snapshot on checkout. The snapshots then pass for whoever approved them and fail for everyone else. PNG files fail to
decode rather than mis-compare, because the PNG signature contains a `CR LF` pair to catch this, but the error does not
point at the cause.

## Snapshots and the build

Many snapshot extensions mean something to the build: `.cs` files are compiled, `.razor` and `.cshtml` files are
compiled, `.resx` files are embedded, `.xaml` files are compiled as pages, and the Web SDK copies `.json` files to the
output and publish folders. The package ships MSBuild targets that remove every file under `**/__snapshots__/**` from the
`Compile`, `EmbeddedResource`, `Content`, `Page`, `ApplicationDefinition`, `MauiXaml` and `AvaloniaXaml` items and add
them as `None` items: they still show up in the IDE, but are not part of the build output.

To opt out, set the `SnapshotTestingExcludeSnapshotFilesFromCompilation` MSBuild property to `false` in your test
project.
