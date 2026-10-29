---
title: Introduction
description: Write the public API of a .NET assembly as C# files, so every API change shows up in your diffs and pull requests.
---

`Meziantou.Framework.PublicApiGenerator` reads a .NET assembly and writes its public API as C# files: every type and
member that code outside the assembly can use, with stubs in place of the method bodies. Commit these files with your
code: every change of the public API, such as a new method, a removed overload or a nullable annotation that changed,
then shows up in the diff of the pull request, where reviewers see it.

## Three packages

| Package | Use it to |
| --- | --- |
| `Meziantou.Framework.PublicApiGenerator.MSBuild` | Generate the API files on every `dotnet build`, and check in CI that they are up to date. Start here. |
| `Meziantou.Framework.PublicApiGenerator.Tool` | Generate or check the API files of any `.dll` or `.exe` from the command line. |
| `Meziantou.Framework.PublicApiGenerator` | Generate the API files from your own code, or read a structured model of the public API. |

The three packages produce the same files: the MSBuild package and the tool use the library.

## What you can do with it

- **Track the public API of a library**: the MSBuild package writes the API file after each build. See
  [Getting started](02-GettingStarted.md) and [Generating API files in the build](04-MSBuild.md).
- **Fail CI when the API files are out of date**: the verify mode compares the files with the build output instead of
  writing them. See [Check the API files in CI](04-MSBuild.md#check-the-api-files-in-ci).
- **Handle multi-targeted libraries**: the builds for all the target frameworks are merged into one set of files, and
  the declarations that differ are wrapped in `#if` blocks. See
  [Multi-targeted libraries](03-ApiFiles.md#multi-targeted-libraries).
- **Inspect a public API from code**: read the types, members, signatures, attributes and documentation IDs of an
  assembly, compare its builds for several target frameworks, and format its declarations, for instance to generate
  documentation. See [Library](06-Library/01-Overview.md).

## Principles

- **The assembly is never executed.** Reading a file uses its metadata only: the assembly, its attributes and its
  references are never loaded or run.
- **The output only depends on the public API.** Declarations are sorted, so moving a method in the source code, or to
  another file of a partial type, changes nothing. Reading a file or the same assembly loaded in memory gives the same
  output.
- **The files are C#.** They are written with the C# syntax and can be compiled, so they read like the code they come
  from.
- **Diffs stay small.** For a multi-targeted library, only the declarations that differ between target frameworks are
  wrapped in conditional blocks, so adding a target framework produces a small diff.

## Next steps

- [Getting started](02-GettingStarted.md): generate the API file of a library on build and check it in CI.
- [The API files](03-ApiFiles.md): what the generated files contain.
- [Limitations](07-Limitations.md): what the generator does not do.
