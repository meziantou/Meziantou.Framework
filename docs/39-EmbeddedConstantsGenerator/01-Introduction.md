---
title: Introduction
description: An MSBuild task that turns the files you list into string constants and byte spans of a generated class.
---

`Meziantou.Framework.EmbeddedConstantsGenerator` reads files when the project builds and writes their content into a
generated C# class: a text file becomes a `const string`, a binary file a `ReadOnlySpan<byte>` property. You list the
files in the project file, and use the members like any other constant:

<!-- ref:../../tests/Meziantou.Framework.EmbeddedConstantsGenerator.Tests/EmbeddedConstantsGeneratorTests.cs;region=EmbeddedConstants_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
using MyApp.Generated;

namespace MyApp;

public static class Assets
{
    public static string Greeting => EmbeddedFiles.HelloText;

    public static byte[] GetLogo() => EmbeddedFiles.LogoBytes.ToArray();

    public static string ConfigurationJson => EmbeddedFiles.ConfigJsonText;

    public static int ConfigurationLength => EmbeddedFiles.ConfigJsonBytes.Length;
}
```
<!-- endref -->

Use it for small assets that the code needs as values: a SQL script, a JSON schema, a default configuration file, a
template, a small image. There is no resource to look up and no stream to read: the value is part of the compiled
code, and a `const string` can be used where C# requires a constant, such as an attribute argument or a `switch` case.

## Principles

- **The content is read at build time.** The file is not needed at run time and is not copied to the output folder.
  Editing the file and building the project again updates the constants.
- **Text stays text, bytes stay bytes.** Text files must be valid UTF-8 and become a `const string`. Binary files are
  copied byte for byte into a `ReadOnlySpan<byte>`, which does not allocate when you read it.
- **The generated class is plain C#.** It is a `static partial class` written to the `obj` folder and compiled with the
  project: no reflection, and no runtime dependency on the package.
- **Mistakes fail the build.** An invalid name, two files that produce the same member name or a text file that is not
  UTF-8 are reported as [MSBuild errors](04-Errors.md), not discovered at run time.

## Next steps

- [Getting started](02-GettingStarted.md): install the package, list the files and use the generated members.
- [Configuration](03-Configuration.md): the namespace, the class name, the visibility, the member names and the
  output file.
- [Errors](04-Errors.md): the errors the build reports and how to fix them.
