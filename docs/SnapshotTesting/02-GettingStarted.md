---
title: Getting started
description: Install the package, write your first snapshot test, approve the snapshot and commit it.
---

## Install the package

Add the `Meziantou.Framework.SnapshotTesting` package to a test project:

```bash
dotnet add package Meziantou.Framework.SnapshotTesting
```

It works with xUnit v2 and v3, NUnit, MSTest and TUnit. The examples of this documentation omit the `using`
directives: they need `using Meziantou.Framework.SnapshotTesting;`.

## Write a test

Call `Snapshot.Validate` with the value to check, where you would write your assertions:

<!-- ref:../../tests/Meziantou.Framework.SnapshotTesting.Tests/SnapshotTests.cs;region=Snapshot_BasicUsage;format=md-fenced-code-block;dedent=true -->
```csharp
var value = new { Name = "John", Age = 42 };
Snapshot.Validate(value);
```
<!-- endref -->

The snapshot of this test is the file `__snapshots__/SnapshotTests_ReadmeExample_BasicUsage.verified.txt`, in the folder
of the test source file. It is named after the test class and the test method.

## Run the test and approve the snapshot

The first run fails: the verified file does not exist yet. The library writes what it got to
`__snapshots__/SnapshotTests_ReadmeExample_BasicUsage.actual.txt` and the failure message lists the files to review.
The actual file contains the serialized value, one property per line:

<!-- ref:../../tests/Meziantou.Framework.SnapshotTesting.Tests/__snapshots__/SnapshotTests_ReadmeExample_BasicUsage.verified.txt;format=md-fenced-code-block;language=text -->
```text
Name: John
Age: 42
```
<!-- endref -->

When the content is what you expect, approve it: the actual file becomes the verified file. The quickest way is the
approval tool, which approves every actual file under the current folder:

```bash
dotnet tool install --global Meziantou.Framework.SnapshotTesting.Tool
```

```bash
Meziantou.Framework.SnapshotTesting.Tool approve
```

You can also rename the file yourself, or let the library update the verified files while you work: see
[Updating snapshots](06-UpdatingSnapshots/01-Overview.md).

Run the test again: it passes. From now on, it fails whenever the serialized value differs from the verified file.

## Commit the snapshots

Commit the `__snapshots__` folder with the test. Do not commit the `.actual` files: they are the output of a failed run,
and the library deletes them once they are no longer relevant.

Snapshots are often binary (images, PDF pages, `.bin` files). Add the line `**/__snapshots__/** -text` to your
`.gitattributes` file so that git never converts their line endings. Without it, a repository that uses
`core.autocrlf=true` on Windows rewrites the binary snapshots on checkout, and the snapshots pass for whoever approved
them but fail for everyone else. See [Snapshot files](03-WritingTests/03-SnapshotFiles.md#storing-snapshots-in-git).

## Validate a file in its own format

Pass a `SnapshotType` to store the value with its own extension. Here a PNG image and an SVG document:

<!-- ref:../../tests/Meziantou.Framework.SnapshotTesting.Tests/SnapshotTests.cs;region=Snapshot_TypedSnapshots;format=md-fenced-code-block;dedent=true -->
```csharp
var imageBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGP4z8AAAAMBAQDJ/pLvAAAAAElFTkSuQmCC"); // A 1x1 PNG image
var svgText = """<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10"><rect width="10" height="10" fill="red" /></svg>""";

Snapshot.Validate(imageBytes, SnapshotType.Png);
Snapshot.Validate(svgText, SnapshotType.Svg);
```
<!-- endref -->

The first call creates `SnapshotTests_ReadmeExample_TypedSnapshots.verified.png` and the second one
`SnapshotTests_ReadmeExample_TypedSnapshots~2.verified.svg`: each call of a test gets its own file. See
[Snapshot types](03-WritingTests/02-SnapshotTypes.md) and [Naming snapshots](03-WritingTests/04-NamingSnapshots.md).

## Next steps

- [Writing tests](03-WritingTests/01-Overview.md): snapshot types, file names, helper methods and scrubbing.
- [Settings](04-Settings.md): configure every test of a project in one place.
- [Updating snapshots](06-UpdatingSnapshots/01-Overview.md): the update strategies, merge tools and the approval tool.
