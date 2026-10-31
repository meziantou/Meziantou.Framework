---
title: Getting started
description: Install the package, create paths, combine them and use them with the file APIs.
---

## Install the package

Add the `Meziantou.Framework.FullPath` package to a project:

<InstallNuGetPackage Id="Meziantou.Framework.FullPath" />

The package also adds the [analyzer](10-Analyzer/01-Overview.md) to C# projects. The examples of this documentation
omit the `using` directives: they need `using Meziantou.Framework;`.

## Create and use paths

Create a path, combine it with the `/` operator, and pass it to the file APIs:

<!-- ref:../../tests/Meziantou.Framework.FullPath.Tests/FullPathTests.cs;region=FullPath_GettingStarted;format=md-fenced-code-block;dedent=true -->
```csharp
FullPath root = FullPath.FromFileSystemInfo(Directory.CreateTempSubdirectory());

// Combine paths with the / operator
FullPath settings = root / "config" / "settings.json";
Assert.Equal("settings.json", settings.Name);
Assert.Equal(root / "config", settings.Parent);

// FullPath converts to string implicitly, so it works with every API that takes a path
settings.CreateParentDirectory();
File.WriteAllText(settings, "{}");
Assert.True(File.Exists(settings));

// Show the path relative to the root folder (config/settings.json, or config\settings.json on Windows)
Assert.Equal(Path.Combine("config", "settings.json"), settings.MakePathRelativeTo(root));
```
<!-- endref -->

- [FullPath.FromFileSystemInfo](cref:Meziantou.Framework.FullPath.FromFileSystemInfo) converts the `DirectoryInfo` that `Directory.CreateTempSubdirectory` returns.
  [FullPath.FromPath](cref:Meziantou.Framework.FullPath.FromPath) converts a string.
- The `/` operator appends one or more segments to a path. It returns a new [FullPath](cref:Meziantou.Framework.FullPath).
- [Name](cref:Meziantou.Framework.FullPath.Name) and [Parent](cref:Meziantou.Framework.FullPath.Parent) read the parts of the path, without accessing the file system.
- [CreateParentDirectory](cref:Meziantou.Framework.FullPath.CreateParentDirectory) creates the folder that contains the file, and its own parents, when they do not exist.
- `File.WriteAllText` takes a string: the [FullPath](cref:Meziantou.Framework.FullPath) converts to it implicitly.

## Store paths as FullPath

Declare your fields, properties, parameters and return values as [FullPath](cref:Meziantou.Framework.FullPath) rather than `string`. The type then
documents that the value is an absolute path, and the conversion happens once, where the path enters the application:
a command-line argument, a configuration value, a JSON document.

The analyzer finds the strings that only ever hold a [FullPath](cref:Meziantou.Framework.FullPath): see
[Declaring FullPath instead of string](10-Analyzer/03-DeclaringFullPath.md).

## Next steps

- [Creating paths](03-CreatingPaths.md): the ways to create a path, and how it is normalized.
- [Comparing paths](05-ComparingPaths.md): equality, case sensitivity and collections.
- [Analyzer](10-Analyzer/01-Overview.md): convert existing code.
