---
title: String formats and source files
description: Choose the C# string literals the snapshots are written with, and how the source file is edited.
---

When the library writes a snapshot, it edits the source file of the test: it replaces the expected string of the call,
or adds it when it is missing. These settings control how.

## String formats

[AllowedStringFormats](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotSettings.AllowedStringFormats) lists the C# string literals the library may write, as [CSharpStringFormats](cref:Meziantou.Framework.InlineSnapshotTesting.CSharpStringFormats) flags:

| Flag | Literal |
| --- | --- |
| [Quoted](cref:Meziantou.Framework.InlineSnapshotTesting.CSharpStringFormats.Quoted) | `"Name: meziantou"`, for a single line without escape sequences |
| [Verbatim](cref:Meziantou.Framework.InlineSnapshotTesting.CSharpStringFormats.Verbatim) | `@"..."`, for several lines, backslashes or quotes |
| [Raw](cref:Meziantou.Framework.InlineSnapshotTesting.CSharpStringFormats.Raw) | A raw string literal, `"""..."""`, indented like the calling code (C# 11 or later) |
| [LeftAlignedRaw](cref:Meziantou.Framework.InlineSnapshotTesting.CSharpStringFormats.LeftAlignedRaw) | A raw string literal whose content and closing quotes start at the first column of the file |
| [DetermineFeatureFromPdb](cref:Meziantou.Framework.InlineSnapshotTesting.CSharpStringFormats.DetermineFeatureFromPdb) | Reads the C# version of the project from the PDB, and does not use a literal it does not support. Requires a portable PDB |

The default, [CSharpStringFormats.Default](cref:Meziantou.Framework.InlineSnapshotTesting.CSharpStringFormats.Default), is `Quoted | Verbatim | Raw | DetermineFeatureFromPdb`: a quoted string for
a simple single line, and for several lines a raw string when the project uses C# 11 or later, a verbatim string
otherwise.

<!-- ref:../../tests/Meziantou.Framework.InlineSnapshotTesting.Tests/InlineSnapshotTests.cs;region=InlineSnapshot_AllowedStringFormats;format=md-fenced-code-block;dedent=true -->
```csharp
InlineSnapshot
    .WithSettings(settings => settings.AllowedStringFormats = CSharpStringFormats.Quoted | CSharpStringFormats.Verbatim | CSharpStringFormats.Raw)
    .Validate(new { Name = "meziantou" }, "Name: meziantou");
```
<!-- endref -->

With [Raw](cref:Meziantou.Framework.InlineSnapshotTesting.CSharpStringFormats.Raw) only, even a single line is written as a raw string:

<!-- ref:../../tests/Meziantou.Framework.InlineSnapshotTesting.Tests/InlineSnapshotTests.cs;region=InlineSnapshot_RawString;format=md-fenced-code-block;dedent=true -->
```csharp
InlineSnapshot
    .WithSettings(settings => settings.AllowedStringFormats = CSharpStringFormats.Raw)
    .Validate(new object(), """
        {}
        """);
```
<!-- endref -->

With [LeftAlignedRaw](cref:Meziantou.Framework.InlineSnapshotTesting.CSharpStringFormats.LeftAlignedRaw), the content and the closing quotes of the snapshot start at the first column of the file, whatever
the indentation of the calling code:

<!-- ref:../../tests/Meziantou.Framework.InlineSnapshotTesting.Tests/InlineSnapshotTests.cs;region=InlineSnapshot_LeftAlignedRawString;format=md-fenced-code-block -->
```csharp
        InlineSnapshot
            .WithSettings(settings => settings.AllowedStringFormats = CSharpStringFormats.LeftAlignedRaw)
            .Validate(new object(), """
{}
""");
```
<!-- endref -->

## Indentation, line endings and encoding

By default, the indentation, the line endings and the encoding are detected from the source file, so an edit looks like
the code around it. Set [Indentation](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotSettings.Indentation), [EndOfLine](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotSettings.EndOfLine) and [FileEncoding](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotSettings.FileEncoding) to choose them:

<!-- ref:../../tests/Meziantou.Framework.InlineSnapshotTesting.Tests/InlineSnapshotTests.cs;region=InlineSnapshot_FileFormat;format=md-fenced-code-block;dedent=true -->
```csharp
InlineSnapshot
    .WithSettings(settings =>
    {
        settings.Indentation = "    ";
        settings.EndOfLine = "\r\n";
        settings.FileEncoding = Encoding.UTF8;
    })
    .Validate(new { Name = "meziantou" }, "Name: meziantou");
```
<!-- endref -->

## How the call is found

The library finds the call to update from the `[CallerFilePath]` and `[CallerLineNumber]` that the compiler passes to
[Validate](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshot.Validate), and parses the source file to find the expected argument of that call.

- **The source file must be on disk.** The tests must run where the source code is, from a build of that code.
- **The PDB is used to check the location.** By default ([ValidateSourceFilePathUsingPdbInfoWhenAvailable](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotSettings.ValidateSourceFilePathUsingPdbInfoWhenAvailable)), the file is
  checked against the call stack read from the PDB, and the update fails when they differ, which may happen with a
  Release build. Set the property to `false` to skip the check.
  [ValidateLineNumberUsingPdbInfoWhenAvailable](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotSettings.ValidateLineNumberUsingPdbInfoWhenAvailable), disabled by default, also checks the line. The PDB gives the line where
  the expression starts, and `[CallerLineNumber]` the line of the call, which differ when the expression spans several
  lines.
- **Deterministic paths are mapped back to the disk.** See
  [Deterministic source paths](03-Settings.md#deterministic-source-paths).
- **The expected argument must still hold the old value.** If the source file changed since the build, so that the
  argument no longer contains the expected value the test ran with, the update fails instead of editing the wrong code.
- **The call must be in the member that made it.** A call on the recorded line is only updated when it is in the
  member, and the type, that made the call. When the file changed since the build so that the line holds the call of
  another member, the library searches the calling member instead. When several members cannot be told apart, such as
  overloads with as many parameters, and more than one of them holds the old snapshot, the update fails: rebuild the
  project and run the test again.
- **Several test processes can update the same file.** A test project that targets several frameworks runs one process
  per framework: the processes take turns to edit a file.

When the call cannot be updated, the test fails with the reason and the snapshot difference, so you can copy the value
yourself.

A helper method that calls [Validate](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshot.Validate) must say which of its parameters holds the snapshot and forward the caller
information. See [Helper methods](07-HelperMethods.md).
