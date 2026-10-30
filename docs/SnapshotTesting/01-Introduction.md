---
title: Introduction
description: Validate objects, documents, images and generated code against snapshot files stored next to your tests.
---

`Meziantou.Framework.SnapshotTesting` replaces long lists of assertions with a single call. `Snapshot.Validate` serializes
the value under test and compares the result with a file committed next to the test, the *snapshot*. When the value
changes, the test fails and shows the difference: either the change is a bug, or you approve the new snapshot.

## How it works

1. `Snapshot.Validate(value)` serializes the value. Objects are written in a readable, YAML-like text; strings, byte
   arrays and streams are written as they are.
2. The result is compared with the *verified* file of the test, stored in a `__snapshots__` folder next to the test
   source file: `__snapshots__/UserTests_CreateUser.verified.txt`.
3. When the content differs, or when the verified file does not exist yet, the new content is written to an *actual* file
   (`UserTests_CreateUser.actual.txt`) and the test fails with the list of changed files.
4. You review the difference and approve it: the actual file replaces the verified file. You can approve with the
   [approval tool](06-UpdatingSnapshots/03-ApprovalTool.md), let an
   [update strategy](06-UpdatingSnapshots/02-UpdateStrategies.md) overwrite the verified files, or merge the change in a
   [merge tool](06-UpdatingSnapshots/04-MergeTools.md).

The verified files are part of your code: commit them, and review their changes like any other change.

## What you can do with it

- **Validate any object** as readable text, and configure how it is written. See
  [Serializers and comparers](05-SerializersAndComparers.md).
- **Validate files and documents** in their own format: JSON, YAML, XML, HTML, SVG, PNG, PDF, and any other extension.
  See [Snapshot types](03-WritingTests/02-SnapshotTypes.md).
- **Make snapshots deterministic** by scrubbing GUIDs, dates, user names and other unstable values. See
  [Deterministic snapshots](03-WritingTests/06-DeterministicSnapshots.md).
- **Compare images by their pixels**, exactly or by similarity, **compare PDF documents** page by page, and **test source
  generators, syntax rewriters and analyzers** with a single call. See [Extensions](07-Extensions/01-Overview.md).
- **Keep CI strict**: snapshots are never updated on a build server, in a continuous testing runner or when the tests
  are run by an LLM agent. See [Continuous integration](06-UpdatingSnapshots/05-ContinuousIntegration.md).

## Packages

| Package | Contains |
| --- | --- |
| `Meziantou.Framework.SnapshotTesting` | `Snapshot.Validate`, the settings, the serializers of objects, byte arrays and streams, the scrubbers and the merge tools |
| `Meziantou.Framework.SnapshotTesting.Imaging` | Image snapshots compared by their pixels, exactly or by similarity. See [Images](07-Extensions/02-Images.md) |
| `Meziantou.Framework.SnapshotTesting.Pdf` | PDF snapshots stored as one PNG image per page and a JSON file of metadata. See [PDF documents](07-Extensions/03-Pdf.md) |
| `Meziantou.Framework.SnapshotTesting.Roslyn` | Snapshots of source generator results, syntax trees and diagnostics. See [Roslyn](07-Extensions/04-Roslyn.md) |
| `Meziantou.Framework.SnapshotTesting.Tool` | A .NET tool that approves the actual files. See [The approval tool](06-UpdatingSnapshots/03-ApprovalTool.md) |

All the types of the main package are in the `Meziantou.Framework.SnapshotTesting` namespace.

## File snapshots or inline snapshots

[`Meziantou.Framework.InlineSnapshotTesting`](../InlineSnapshotTesting/01-Introduction.md) works the same way, but
stores the snapshot in the source code of the test, as a string argument of the call, instead of in a file. File
snapshots suit large values, binary files and formats with their own extension; inline snapshots suit small values you
want to read next to the code. See [File snapshots or inline snapshots](08-FileOrInlineSnapshots.md).

## Next steps

- [Getting started](02-GettingStarted.md): install the package and write your first snapshot test.
- [Writing tests](03-WritingTests/01-Overview.md): snapshot types, file names, helper methods and scrubbing.
- [Updating snapshots](06-UpdatingSnapshots/01-Overview.md): review and approve changes.
