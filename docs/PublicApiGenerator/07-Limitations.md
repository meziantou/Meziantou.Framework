---
title: Limitations
description: What the public API generator does not do, and how to work around it.
---

- **It describes an API, it does not judge it.** The generator writes the current public API; it does not classify a
  change as breaking or not. Reviewers read the diff of the API files to decide.
- **Removed types leave files behind by default** with the `OneFilePerNamespace` and `OneFilePerType` layouts. Turn
  on the option that deletes the unexpected files, which requires a directory that only contains the API files. See
  [Files of removed types](04-MSBuild.md#files-of-removed-types).
- **The `new` modifier depends on the referenced assemblies.** It is not stored in metadata: when the assembly that
  declares a base type cannot be found next to the assembly being read or in the runtime, the members that hide its
  members are not marked `new`. See [The `new` modifier](06-Library/03-ReadingTheApiModel.md#the-new-modifier).
- **Some members cannot be written in C#.** Members with names that are not valid C# identifiers (such as `<Clone>$`),
  static constructors and compiler-generated types are not part of the API files or of the model.
- **Extension blocks are grouped by receiver.** The compiler merges the C# 14 extension blocks that declare the same
  receiver, so the API files cannot tell whether the members were declared in one block or in several. See
  [Extension members](03-ApiFiles.md#extension-members).
- **Some attributes are not written to the API files.** By default, `EditorBrowsableAttribute`, the attributes of other
  namespaces than `System` and most assembly attributes are not written: turn on the option that writes them. The
  attributes represented by the C# syntax (such as `NullableAttribute`), the attributes that describe the
  implementation, and attributes whose type is nested or generic are never written. See
  [Attributes](03-ApiFiles.md#attributes). The [model](06-Library/03-ReadingTheApiModel.md#what-the-model-contains)
  has all of them.
- **File names that differ only by case get a suffix.** With a layout that writes several files, two namespaces or
  types whose names only differ by case are written to `Demo.Foo.g.cs` and `Demo.foo~2.g.cs`. The name of a file
  can change when the other type is added or removed. See [File layouts](03-ApiFiles.md#file-layouts).
- **Each target framework needs a preprocessor symbol.** Merging several builds fails when two of them have the same
  target framework, or when a target framework cannot be converted to a preprocessor symbol (`NET10_0`,
  `NETSTANDARD2_0`, `NET48`...).
