---
title: Reading the API model
description: Read the types, members, signatures and attributes of the public API of an assembly, without generating files.
---

## Read an assembly

`PublicApi.ReadAssembly` returns a `PublicApiAssembly`, a structured model of the public API. This example lists the
methods of `Meziantou.Framework.PublicApiGenerator.dll` and their parameters:

<!-- ref:../../../tests/Meziantou.Framework.PublicApiGenerator.Tests/PublicApiGeneratorTests.cs;region=PublicApi_ReadAssembly;format=md-fenced-code-block;dedent=true -->
```csharp
var assembly = PublicApi.ReadAssembly(typeof(PublicApi).Assembly.Location, new PublicApiReadOptions
{
    TargetFramework = "net10.0",                          // Optional, inferred from TargetFrameworkAttribute
    InputIdentity = "Meziantou.Framework.PublicApiGenerator/1.0.0/lib/net10.0/Meziantou.Framework.PublicApiGenerator.dll", // Optional, see "Identities" below
});

var lines = new List<string>();
foreach (var type in assembly.GetAllTypes()) // Includes nested types
{
    lines.Add($"{type.FullName} ({type.TypeKind}), declared in {type.DeclaringType?.FullName ?? type.Namespace}");
    foreach (var member in type.Members)
    {
        if (member is PublicApiMethod method)
        {
            lines.Add($"  {method.Name} returns {method.ReturnType}");
            foreach (var parameter in method.Parameters)
            {
                lines.Add($"    {parameter.RefKind} {parameter.Type} {parameter.Name} (nullable: {parameter.Type.NullableAnnotation})");
            }
        }
    }
}

Assert.Contains("Meziantou.Framework.PublicApiGenerator.PublicApi (Class), declared in Meziantou.Framework.PublicApiGenerator", lines);
Assert.Contains("  ReadAssembly returns Meziantou.Framework.PublicApiGenerator.PublicApiAssembly", lines);
Assert.Contains("    None string assemblyPath (nullable: NotAnnotated)", lines);
Assert.Contains("    None Meziantou.Framework.PublicApiGenerator.PublicApiReadOptions? options (nullable: Annotated)", lines);
```
<!-- endref -->

- `ReadAssembly` reads the metadata of a file, or of a stream that contains a PE image. The assembly, its attributes and
  its references are never loaded or executed. A stream is not closed.
- The model does not keep any reference to the file, the stream or the metadata reader: it remains usable after they are
  disposed.
- `PublicApiReadOptions` is optional. `TargetFramework` is kept as provided (for instance `net10.0` or
  `.NETCoreApp,Version=v10.0`); when it is not set, it is inferred from the `TargetFrameworkAttribute` of the assembly.
  `InputIdentity` is described in [Identifying symbols](04-IdentifyingSymbols.md). `IncludeInternalSymbols` is
  described in [Internal symbols](#internal-symbols).
- `Types` lists the top-level types; `GetAllTypes()` includes the nested types, and `GetAllSymbols()` returns every type
  and member.

## What the model contains

- The types and members that are visible outside the assembly (`public`, `protected`, `protected internal`), and the
  explicit implementations of interfaces that are visible outside the assembly. Members with names that cannot be
  written in C# (such as `<Clone>$`), static constructors and compiler-generated types are excluded. Other
  compiler-generated members, such as the members of records, are included: `IsCompilerGenerated` identifies them.
- Accessors, available from `PublicApiProperty.GetMethod`, `SetMethod`, `PublicApiEvent.AddMethod`... Accessors that are
  not visible outside the assembly (such as a `private set`) are not exposed.
- The signature of delegates, through `PublicApiType.DelegateInvokeMethod`. The other members of delegates are not
  exposed.
- C# 14 extension blocks, through the static methods the compiler generates for them (such as `get_Length(string)`).
- All the custom attributes of each symbol, parameter, return value and generic parameter, including the ones that are
  not written in the API files (such as `EditorBrowsableAttribute` or `NullableAttribute`), with their decoded
  arguments.
- Type references (`PublicApiTypeReference`) that preserve nested types, type arguments, arrays, pointers, function
  pointers, ref kinds, nullable annotations, tuple element names and the referenced assembly.
- The `new` modifier of the members and nested types that hide an inherited member (`PublicApiMember.IsNew` and
  `PublicApiType.IsNew`).

Members are in the same order as in the API files: see [Order of the declarations](../03-ApiFiles.md#order-of-the-declarations).

## Internal symbols

Set `PublicApiReadOptions.IncludeInternalSymbols` to `true` to also read the `internal` types and members. Use it when
the internal symbols of an assembly are an API, for instance to document an assembly compiled from the C# files of a
source package (`contentFiles/cs/{tfm}/`): these files are compiled into the project that references the package, so
their `internal` types are part of its API.

- The model also contains the `internal` types, members and accessors, the types nested in `internal` types, the
  `internal` interfaces a type implements and the explicit implementations of their members.
- `Accessibility` reports the declared accessibility (such as `PublicApiAccessibility.Internal`), so
  `PublicApiFormatter` writes `internal` (such as `public int Value { get; internal set; }`).
- A member that hides an `internal` member inherited from a type declared in the same assembly is reported as `new`.
- `private` and `private protected` symbols are still excluded, and so are the `internal` types generated by the
  compiler (such as the embedded `NullableAttribute`, `<PrivateImplementationDetails>` or the types declared with the
  `file` modifier). The `internal` members of the types declared in other assemblies are ignored, even when they are
  visible through `InternalsVisibleToAttribute`.

## The `new` modifier

The `new` modifier is not stored in metadata, so it is inferred using the rules of the C# compiler: a member is reported
as `new` when a base class, or a base interface for the members of an interface, declares a member with the same name,
or with the same signature for methods and indexers.

<!-- ref:../../../tests/Meziantou.Framework.PublicApiGenerator.Tests/PublicApiGeneratorTests.cs;region=PublicApi_NewModifier;format=md-fenced-code-block;dedent=true -->
```csharp
public class Base
{
    public void Method() { }
}

public class Derived : Base
{
    public new int Method() => 0;     // Hides Base.Method()
    public void Method(int value) { } // Another overload, it does not hide anything
}
```
<!-- endref -->

Only the inherited members that are visible outside their assembly (`public`, `protected` and `protected internal`) are
considered. A member that hides an `internal` member is not reported as `new`, as the hidden member is not part of the
public API, unless `PublicApiReadOptions.IncludeInternalSymbols` is set and the hidden member is declared in the same
assembly.

When a base type is declared in another assembly, the metadata of that assembly is read to find the inherited members.
The assembly is looked up in the directory of the assembly being read, then in the assemblies of the runtime that runs
the generator. It is never loaded nor executed. When the assembly cannot be found, the members of that base type and of
its own base types are ignored, so the members that hide them are not reported as `new`.

## Memory safety rules

`PublicApiAssembly.UsesUpdatedMemorySafetyRules` tells whether the assembly was compiled with the updated memory safety
rules. For these assemblies only, `PublicApiMember.RequiresUnsafe` reports the members the compiler marked as requiring
an unsafe context. The API files apply the same rules to the `unsafe` modifier: see
[Compiling the files](../03-ApiFiles.md#compiling-the-files).
