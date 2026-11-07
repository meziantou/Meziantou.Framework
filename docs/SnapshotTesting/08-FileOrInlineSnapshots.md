---
title: File snapshots or inline snapshots
description: Choose between the snapshot files of SnapshotTesting and the inline snapshots of InlineSnapshotTesting.
---

Two packages implement snapshot testing. They serialize values the same way, but store the expected value in different
places:

- `Meziantou.Framework.SnapshotTesting` (this section) stores it in a file next to the test.
- [`Meziantou.Framework.InlineSnapshotTesting`](../InlineSnapshotTesting/01-Introduction.md) stores it in the source
  code of the test, as a string argument of the call, and rewrites that string when the snapshot is updated.

<!-- ref:../../tests/Meziantou.Framework.SnapshotTesting.Tests/SnapshotTests.cs;region=Snapshot_FileAndInlineSnapshots;format=md-fenced-code-block;dedent=true -->
```csharp
var user = new { Name = "John", Age = 42 };

// Meziantou.Framework.SnapshotTesting: the expected value is a file next to the test
Snapshot.Validate(user); // __snapshots__/SnapshotTests_DocumentationExample_FileAndInlineSnapshots.verified.txt

// Meziantou.Framework.InlineSnapshotTesting: the expected value is in the source code of the test
InlineSnapshot.Validate(user, """
    Name: John
    Age: 42
    """);
```
<!-- endref -->

## Differences

| | File snapshots | Inline snapshots |
| --- | --- | --- |
| Assertion | `Snapshot.Validate(value)` | `InlineSnapshot.Validate(value, "expected")` |
| Expected value | A `.verified.*` file in `__snapshots__` | A string literal in the test |
| Formats | Text, and any binary format: images, PDF documents, generated `.cs` files, ... | Text only |
| Several files per assertion | Yes, such as the pages of a PDF document | No |
| Serialization | Several serializers chosen by the type of the value; [extensions](07-Extensions/01-Overview.md) for images, PDF and Roslyn | One serializer: human-readable, JSON or your own |
| Comparison | A comparer per format: text, bytes, pixels, ... | One comparer for the text |
| Snapshot name | Built from the test; must be unique | None: the snapshot is where the call is |
| Failed assertion | Writes an `.actual` file | Shows the difference in the failure message |
| Approval | [Approval tool](06-UpdatingSnapshots/03-ApprovalTool.md), update strategy or merge tool | Update strategy or merge tool, which edit the source file |
| Settings | [SnapshotSettings](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings) | `InlineSnapshotSettings` |
| Environment variables | `SNAPSHOTTESTING_STRATEGY`, `SNAPSHOTTESTING_AUTODETECT_CONTINUOUS_ENVIRONMENT` | `INLINESNAPSHOTTESTING_STRATEGY`, `INLINESNAPSHOTTESTING_AUTODETECT_CONTINUOUS_ENVIRONMENT` |

Both packages share the same update strategies (`Disallow` by default, `Overwrite`, `OverwriteWithoutFailure`,
`MergeTool`, `MergeToolSync`), the same merge tool detection, the same line scrubbers, and never update snapshots on a
build server, in a continuous testing runner or in an LLM agent.

## Which one to choose

Choose **inline snapshots** when the serialized value is short: a few lines that you want to read next to the code that
produces them, without opening another file. The test documents the expected result by itself, and a code review shows
the code and its result together.

Choose **file snapshots** when:

- the value is large, such as an API response, a generated file or a long report: a file keeps the test readable;
- the value is binary or has a format of its own: an image, a PDF document, an SVG, JSON or C# you want to open with
  the right editor;
- an assertion produces several files, such as the output of a source generator;
- you want to review new snapshots before they are written, with the approval tool, rather than have the test run edit
  your source code.

You can use both packages in the same project, even in the same test.

## Use both packages in the same file

Some types have the same name in both packages: `SnapshotUpdateStrategy`, `MergeTool`, `Scrubber`, and the scrub
extension methods such as `ScrubGuid`. When a file imports both namespaces, these names are ambiguous. Import the
namespace you use the most, and give the type you need from the other one an alias, such as
`using InlineSnapshot = Meziantou.Framework.InlineSnapshotTesting.InlineSnapshot;`.
