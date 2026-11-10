---
title: Limitations
description: The dependencies the compiled code does not show, and the other limits of the library.
---

The library reads the compiled code, so it does not see the uses that leave no trace in it:

- **Constants**: the compiler copies the value of a `const` field and of an enum value into the code that uses it, so
  using `Limits.Max` gives no dependency on `Limits`. A default parameter value is a constant too.
- **`nameof`**: `nameof(Order)` is a string in the compiled code.
- **Reflection and configuration**: a type created with `Activator.CreateInstance`, found by name, or registered from a
  configuration file is not a dependency of the code that does it.
- **Types named in strings**: an attribute argument that names a type in a string is not a dependency. A `typeof`
  argument is.

Other limits:

- The types marked with `CompilerGeneratedAttribute` are not part of the architecture, including the types a source
  generator marks with it.
- The source locations require a portable PDB. Windows PDBs, produced by old projects with `DebugType` set to `full` or
  `pdbonly` on .NET Framework, give no location.
- The base types and interfaces defined outside the architecture are read from the referenced assemblies, searched in
  the folders of the loaded assemblies, the folders of
  [ReferenceSearchDirectories](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureLoadOptions.ReferenceSearchDirectories),
  and the assemblies of the running .NET. When an assembly is not found, the hierarchy stops at the type it defines.
