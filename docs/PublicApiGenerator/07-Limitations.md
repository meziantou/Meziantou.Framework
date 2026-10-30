---
title: Limitations
description: What the public API generator does not do, and how to work around it.
---

- **It describes an API, it does not judge it.** The generator writes the current public API; it does not classify a
  change as breaking or not. Reviewers read the diff of the API files to decide.
- **Removed types leave files behind** with the `OneFilePerNamespace` and `OneFilePerType` layouts: the MSBuild package,
  the tool and [PublicApi.GenerateToDirectory](cref:Meziantou.Framework.PublicApiGenerator.PublicApi.GenerateToDirectory) never delete a file. The verify mode reports these files. Delete the
  directory before generating, or use the `SingleFile` layout. See
  [Removed types leave files behind](04-MSBuild.md#removed-types-leave-files-behind).
- **The `new` modifier depends on the referenced assemblies.** It is not stored in metadata: when the assembly that
  declares a base type cannot be found next to the assembly being read or in the runtime, the members that hide its
  members are not marked `new`. See [The `new` modifier](06-Library/03-ReadingTheApiModel.md#the-new-modifier).
- **Some members cannot be written in C#.** Members with names that are not valid C# identifiers (such as `<Clone>$`),
  static constructors and compiler-generated types are not part of the API files or of the model. C# 14 extension
  blocks are represented by the static methods the compiler generates for them.
- **Some attributes are not written to the API files**, such as `EditorBrowsableAttribute` or `NullableAttribute`, and
  `[RequiresPreviewFeatures]` is the only assembly attribute written. The
  [model](06-Library/03-ReadingTheApiModel.md#what-the-model-contains) has all of them.
- **File names must be distinct ignoring case.** With a layout that writes several files, two namespaces or types whose
  names only differ by case make the generation fail. Use another layout.
- **Each target framework needs a preprocessor symbol.** Merging several builds fails when two of them have the same
  target framework, or when a target framework cannot be converted to a preprocessor symbol (`NET10_0`,
  `NETSTANDARD2_0`, `NET48`...).
