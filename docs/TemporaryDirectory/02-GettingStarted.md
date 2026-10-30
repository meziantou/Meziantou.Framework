---
title: Getting started
description: Install the package, create a temporary folder, work in it, and let disposal delete it.
---

## Install the package

Add the `Meziantou.Framework.TemporaryDirectory` package to a project that targets .NET 10 or later:

```bash
dotnet add package Meziantou.Framework.TemporaryDirectory
```

The package depends on [`Meziantou.Framework.FullPath`](../FullPath/01-Introduction.md), which provides the path type
the examples use. The examples of this documentation omit the `using` directives: they need
`using Meziantou.Framework;`.

## Create a temporary folder

<!-- ref:../../tests/Meziantou.Framework.TemporaryDirectory.Tests/TemporaryDirectoryTests.cs;region=TemporaryDirectory_GettingStarted;format=md-fenced-code-block;dedent=true -->
```csharp
FullPath directoryPath;
using (var directory = TemporaryDirectory.Create())
{
    directoryPath = directory.FullPath;

    // Create files and folders inside the directory
    var input = directory.CreateTextFile("input/data.csv", "id,name");
    var output = directory / "output" / "result.txt";
    output.CreateParentDirectory();
    File.Copy(input, output);

    Assert.Equal("id,name", File.ReadAllText(output));
}

// Disposing the instance deletes the directory and everything it contains
Assert.False(Directory.Exists(directoryPath));
```
<!-- endref -->

- `TemporaryDirectory.Create` creates a new, empty folder with a unique name in the temporary folder of the user.
- `CreateTextFile` creates a file in it, and the folders that contain the file.
- The `/` operator combines the folder with a relative path and returns a `FullPath`, without creating anything.
- Disposing the `TemporaryDirectory` deletes the folder and its content.

## Use it in tests

A temporary folder per test keeps tests independent from each other and from the machine: each test gets an empty
folder, and tests that run in parallel never share one. Declare it with `using` at the start of the test, so it is
deleted even when an assertion fails.

## Next steps

- [Temporary directories](03-TemporaryDirectories.md): create files and folders, and choose where the folder is created.
- [Temporary files](04-TemporaryFiles.md): when you only need one file.
