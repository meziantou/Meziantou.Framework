---
title: Introduction
description: A source generator that writes a class for each .resx file, with a property per resource and a method per format string.
---

`Meziantou.Framework.ResxSourceGenerator` is a C# source generator for `.resx` resource files. For each file, it
writes a class with a property for each resource, and, for each string that contains placeholders such as `{0}`, a
method that formats it:

```xml
<data name="Hello" xml:space="preserve">
  <value>Hello {0}!</value>
</data>
```

<!-- ref:../../tests/Meziantou.Framework.ResxSourceGenerator.GeneratorTests/ResxGeneratorTests.cs;region=ResxSourceGenerator_Format;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.Equal("Hello {0}!", Sample.Hello);
Assert.Equal("Hello meziantou!", Sample.FormatHello("meziantou"));
```
<!-- endref -->

The generated class reads the resources from the assembly with a `ResourceManager`, so the localized `.resx` files
(`Sample.fr.resx`...) work as usual. The class is regenerated when the `.resx` file changes, in the IDE as in the build,
without a designer file to keep in sync.

## What you can do with it

- **Read strings, files and objects** with typed properties, and format the strings with typed methods. See
  [Generated members](03-GeneratedMembers.md).
- **Name and type the parameters** of the format methods, and document them, in the `.resx` file. See
  [Format parameters](04-FormatParameters.md).
- **Choose the namespace, the class name and the visibility** of each file. See [Configuration](05-Configuration.md).
- **Refer to resource names** without string literals, with the generated `Names` class.

## Next steps

- [Getting started](02-GettingStarted.md): install the package and enable the generator for the `.resx` files.
- [Diagnostics](06-Diagnostics.md): the warnings reported when a file cannot be processed.
