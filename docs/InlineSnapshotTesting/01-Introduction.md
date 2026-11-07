---
title: Introduction
description: Validate objects against snapshots written in the source code of your tests, and let the library update them.
---

`Meziantou.Framework.InlineSnapshotTesting` replaces long lists of assertions with a single call whose expected value is
written in the test itself. [InlineSnapshot.Validate](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshot.Validate) serializes the value under test to readable text and compares it
with the string you pass. When they differ, the test fails and shows the difference: either the change is a bug, or the
snapshot must be updated, and the library can rewrite the string in your source file for you.

## How it works

1. Write `InlineSnapshot.Validate(value)` without an expected value, and run the test.
2. The value is serialized, for instance `FirstName: John` and `LastName: Doe` on two lines. It does not match the
   missing expected value, so the test fails and shows the difference.
3. Depending on the [update strategy](08-UpdatingSnapshots.md), the library writes the new snapshot in the source file,
   as a second argument of the call, or opens a merge tool to let you accept it. By default, it changes nothing and you
   copy the value from the failure message.
4. From now on, the test compares the serialized value with that string, and fails when they differ.

The snapshot is part of the test: you read the expected result next to the code that produces it, and a code review
shows both.

## What you can do with it

- **Validate any object** as readable text, or as JSON. See [Serializers](04-Serializers.md).
- **Make snapshots deterministic** by scrubbing GUIDs, dates, user names and other unstable values. See
  [Deterministic snapshots](05-DeterministicSnapshots.md).
- **Control how snapshots are written** in your code: quoted, verbatim or raw strings, indentation and line endings. See
  [String formats and source files](06-StringFormatsAndSourceFiles.md).
- **Write your own assertion methods** that update the snapshot of their caller. See
  [Helper methods](07-HelperMethods.md).
- **Keep CI strict**: snapshots are never updated on a build server, in a continuous testing runner or when the tests
  are run by an LLM agent. See [Continuous integration](10-ContinuousIntegration.md).

All the types are in the [Meziantou.Framework.InlineSnapshotTesting](cref:Meziantou.Framework.InlineSnapshotTesting) namespace, and the serializers in
[Meziantou.Framework.InlineSnapshotTesting.Serialization](cref:Meziantou.Framework.InlineSnapshotTesting.Serialization).

## Inline snapshots or file snapshots

[`Meziantou.Framework.SnapshotTesting`](../SnapshotTesting/01-Introduction.md) works the same way, but stores each
snapshot in a file next to the test. Inline snapshots suit short text values you want to read in the test; file
snapshots suit large values, binary files such as images and PDF documents, and generated code. See
[File snapshots or inline snapshots](../SnapshotTesting/08-FileOrInlineSnapshots.md).

## Next steps

- [Getting started](02-GettingStarted.md): install the package and write your first inline snapshot test.
- [Settings](03-Settings.md): configure every test in one place, or a single assertion.
- [Updating snapshots](08-UpdatingSnapshots.md): the update strategies and how the source file is rewritten.
