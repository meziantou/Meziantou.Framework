---
title: Comparing paths
description: Compare paths with the case sensitivity of the operating system or the one you choose, use them in collections, check that a path is inside a folder, and compute relative paths.
---

## Equality and case sensitivity

<!-- ref:../../tests/Meziantou.Framework.FullPath.Tests/FullPathTests.cs;region=FullPath_Equality;format=md-fenced-code-block;dedent=true -->
```csharp
FullPath upper = FullPath.GetTempPath() / "Reports" / "summary.txt";
FullPath lower = FullPath.GetTempPath() / "reports" / "summary.txt";

// == and Equals ignore case on Windows, macOS, iOS and tvOS, and are case-sensitive elsewhere
Assert.Equal(!FullPathComparer.Default.IsCaseSensitive, upper == lower);

// Choose the case sensitivity when the result must not depend on the operating system
Assert.True(upper.Equals(lower, ignoreCase: true));
Assert.False(upper.Equals(lower, ignoreCase: false));
Assert.True(FullPathComparer.CaseInsensitive.Equals(upper, lower));
```
<!-- endref -->

- `==`, `!=`, `Equals` and `GetHashCode` use [FullPathComparer.Default](cref:Meziantou.Framework.FullPathComparer.Default). It compares the paths ordinally, and ignores
  case on Windows, macOS, iOS and tvOS only.
- `Equals(other, ignoreCase)`, `CompareTo(other, ignoreCase)` and `GetHashCode(ignoreCase)` use the case sensitivity
  you choose.
- The default follows the operating system, not the file system: a case-sensitive folder on Windows or a
  case-insensitive volume on Linux is not detected. Choose the case sensitivity explicitly when you know it.

Compare [FullPath](cref:Meziantou.Framework.FullPath) values, not their strings. `fullPath == "C:\\temp"` compares two strings ordinally and is
case-sensitive on every operating system, and `fullPath.Equals("C:\\temp")` is always `false`. The analyzer reports
both: see [Comparisons and checks](10-Analyzer/04-ComparisonsAndChecks.md).

## Collections and sorting

[FullPathComparer](cref:Meziantou.Framework.FullPathComparer) implements `IEqualityComparer<FullPath>` and `IComparer<FullPath>`. Pass one of its instances to a
`HashSet`, a `Dictionary` or a sort:

<!-- ref:../../tests/Meziantou.Framework.FullPath.Tests/FullPathTests.cs;region=FullPath_Collections;format=md-fenced-code-block;dedent=true -->
```csharp
FullPath root = FullPath.GetTempPath();

// Use a FullPathComparer to choose the case sensitivity of a set or a dictionary
var visited = new HashSet<FullPath>(FullPathComparer.CaseInsensitive);
Assert.True(visited.Add(root / "Data"));
Assert.False(visited.Add(root / "data"));

// Sort paths with the same comparers
List<FullPath> paths = [root / "b.txt", root / "A.txt", root / "a.txt"];
paths.Sort(FullPathComparer.CaseSensitive);
Assert.Equal([root / "A.txt", root / "a.txt", root / "b.txt"], paths);
```
<!-- endref -->

| Comparer | Case sensitivity |
| --- | --- |
| [FullPathComparer.Default](cref:Meziantou.Framework.FullPathComparer.Default) | The one of the operating system |
| [FullPathComparer.CaseSensitive](cref:Meziantou.Framework.FullPathComparer.CaseSensitive) | Case-sensitive |
| [FullPathComparer.CaseInsensitive](cref:Meziantou.Framework.FullPathComparer.CaseInsensitive) | Case-insensitive |

[IsCaseSensitive](cref:Meziantou.Framework.FullPathComparer.IsCaseSensitive) tells which one a comparer is. Without a comparer, collections use [FullPathComparer.Default](cref:Meziantou.Framework.FullPathComparer.Default), and
the `<`, `<=`, `>` and `>=` operators compare with it too. Sorting is ordinal: it does not depend on the culture.

## Keep a path inside a folder

[IsChildOf](cref:Meziantou.Framework.FullPath.IsChildOf) tells whether a path is inside a folder, at any depth. Use it to check a path built from user input before
you read or write the file:

<!-- ref:../../tests/Meziantou.Framework.FullPath.Tests/FullPathTests.cs;region=FullPath_IsChildOf;format=md-fenced-code-block;dedent=true -->
```csharp
FullPath uploads = FullPath.GetTempPath() / "uploads";

FullPath image = uploads / "images" / "logo.png";
Assert.True(image.IsChildOf(uploads));

// ".." is resolved before the check, so the path cannot escape the folder
FullPath escaped = uploads / "../secrets.txt";
Assert.False(escaped.IsChildOf(uploads));

// A folder is not its own child, and a sibling that starts with the same name is not a child
Assert.False(uploads.IsChildOf(uploads));
Assert.False((FullPath.GetTempPath() / "uploads-old").IsChildOf(uploads));
```
<!-- endref -->

- The check uses the case sensitivity of [FullPathComparer.Default](cref:Meziantou.Framework.FullPathComparer.Default).
- It compares the paths only: it does not follow symbolic links. A link inside the folder can point outside of it;
  when this matters, compare the canonical paths: see [Symbolic links](07-SymbolicLinks.md).
- It throws an `InvalidOperationException` when the path is empty, and an `ArgumentException` when the folder is empty.

## Relative paths

[MakePathRelativeTo](cref:Meziantou.Framework.FullPath.MakePathRelativeTo) returns the path relative to a folder, as a string. It uses `Path.GetRelativePath`, so the result
can start with `..` segments:

<!-- ref:../../tests/Meziantou.Framework.FullPath.Tests/FullPathTests.cs;region=FullPath_MakePathRelativeTo;format=md-fenced-code-block;dedent=true -->
```csharp
FullPath project = FullPath.GetTempPath() / "project";
FullPath program = project / "src" / "Program.cs";

Assert.Equal(Path.Combine("src", "Program.cs"), program.MakePathRelativeTo(project));
Assert.Equal(Path.Combine("..", "..", "docs"), (project / "docs").MakePathRelativeTo(project / "src" / "app"));
Assert.Equal(".", project.MakePathRelativeTo(project));
```
<!-- endref -->

- The result uses the directory separator of the operating system.
- It is `.` when both paths are equal, and the full path when the folder is empty.
- It throws an `InvalidOperationException` when the path itself is empty.
