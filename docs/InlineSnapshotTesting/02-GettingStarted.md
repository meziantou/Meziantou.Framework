---
title: Getting started
description: Install the package, write a test without an expected value, and let the library write the snapshot.
---

## Install the package

Add the `Meziantou.Framework.InlineSnapshotTesting` package to a test project:

```bash
dotnet add package Meziantou.Framework.InlineSnapshotTesting
```

The examples of this documentation omit the `using` directives: they need
`using Meziantou.Framework.InlineSnapshotTesting;`.

## Write a test

Call [InlineSnapshot.Validate](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshot.Validate) with the value to check. You do not need to write the expected value:

<!-- ref:../../samples/Meziantou.Framework.InlineSnapshotTesting.Samples/ReadmeSnippets.cs;region=InlineSnapshot_GettingStarted;format=md-fenced-code-block;dedent=true -->
```csharp
var data = new
{
    FirstName = "Gérald",
    LastName = "Barré",
    NickName = "meziantou",
};

// No need to write the expected value
InlineSnapshot.Validate(data);
```
<!-- endref -->

## Run the test

The test fails: the serialized value does not match the missing snapshot. The failure message shows the difference
between the expected and the actual value.

By default, the library never changes your source code (the update strategy is [Disallow](cref:Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategy.Disallow)): copy the actual value into
the test yourself, or let the library do it. To have a merge tool open on each new or changed snapshot, set the update
strategy, for instance in a module initializer (see [Settings](03-Settings.md#configure-every-test)):

<!-- ref:../../samples/Meziantou.Framework.InlineSnapshotTesting.Samples/ReadmeSnippets.cs;region=InlineSnapshot_UseMergeTool;format=md-fenced-code-block;dedent=true -->
```csharp
InlineSnapshotSettings.Default = InlineSnapshotSettings.Default with
{
    SnapshotUpdateStrategy = SnapshotUpdateStrategy.MergeTool,
};
```
<!-- endref -->

Accept the change in the merge tool, or use the [Overwrite](cref:Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategy.Overwrite) strategy to write it directly. Either way, the source code of
the test becomes:

<!-- ref:../../tests/Meziantou.Framework.InlineSnapshotTesting.Tests/InlineSnapshotTests.cs;region=InlineSnapshot_GettingStartedUpdated;format=md-fenced-code-block;dedent=true -->
```csharp
var data = new
{
    FirstName = "Gérald",
    LastName = "Barré",
    NickName = "meziantou",
};

InlineSnapshot.Validate(data, """
    FirstName: Gérald
    LastName: Barré
    NickName: meziantou
    """);
```
<!-- endref -->

Run the test again: it passes. From now on, it fails whenever the serialized value differs from the string.

## Requirements

The library edits the source file of the test, so the tests must run on the machine that has the source code, from a
build of that code with its PDB files, typically a Debug build. It finds the call to update from `[CallerFilePath]` and
`[CallerLineNumber]` and checks them against the PDB. See
[String formats and source files](06-StringFormatsAndSourceFiles.md#how-the-call-is-found).

## Next steps

- [Settings](03-Settings.md): configure every test of a project in one place.
- [Serializers](04-Serializers.md): control how values are written.
- [Updating snapshots](08-UpdatingSnapshots.md): choose how and when snapshots are updated.
