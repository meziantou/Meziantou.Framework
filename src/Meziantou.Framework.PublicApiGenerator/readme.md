# Meziantou.Framework.PublicApiGenerator

Generate compilable C# public API stubs from .NET assemblies.

The library supports two readers:

- `System.Reflection.Metadata` (`PEReader`/`MetadataReader`)
- loaded `Assembly` reflection

Both readers produce the same intermediate model that is consumed by the C# emitter.
