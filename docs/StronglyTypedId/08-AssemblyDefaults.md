---
title: Assembly-wide defaults
description: Set the default options of every ID of an assembly once, and override them on a type.
---

## Set the defaults

When most IDs of a project need the same options, set them once with `[assembly: StronglyTypedIdDefaults]`, in any file
of the project:

<!-- ref:../../samples/Meziantou.Framework.StronglyTypedId.AssemblyDefaults.Samples/AssemblyDefaults.cs;region=StronglyTypedId_AssemblyDefaults;format=md-fenced-code-block;dedent=true -->
```csharp
[assembly: StronglyTypedIdDefaults(GuidGenerationStrategy = GuidGenerationStrategy.Version7,
                                   GenerateNewtonsoftJsonConverter = false,
                                   GenerateMongoDBBsonSerialization = false,
                                   GenerateSystemComponentModelTypeConverter = false,
                                   GenerateSystemTextJsonConverter = true,
                                   AddCodeGeneratedAttribute = true,
                                   StringComparison = StringComparison.Ordinal,
                                   GenerateToStringAsRecord = true)]

namespace Meziantou.Framework.StronglyTypedId.AssemblyDefaults.Samples;

[StronglyTypedId<Guid>]
public partial struct ProjectId { } // New() uses Guid.CreateVersion7()

[StronglyTypedId<Guid>(GuidGenerationStrategy = GuidGenerationStrategy.Version4)]
public partial struct CustomerId { } // New() uses Guid.NewGuid()
```
<!-- endref -->

`StronglyTypedIdDefaults` has a property for each [option](07-Options.md), with the same name in PascalCase:
`GenerateSystemTextJsonConverter`, `GenerateNewtonsoftJsonConverter`, `GenerateSystemComponentModelTypeConverter`,
`GenerateMongoDBBsonSerialization`, `AddCodeGeneratedAttribute`, `StringComparison`, `GenerateToStringAsRecord` and
`GuidGenerationStrategy`. A property you do not set keeps the default value of the option.

## Override a default on a type

An option written on `[StronglyTypedId]` takes precedence over the assembly-wide default. In the example above,
`ProjectId` uses the default `Version7` strategy, and `CustomerId` overrides it with `Version4`.

Only the options that you write count. A constructor argument written by name (`generateNewtonsoftJsonConverter: true`)
or by position (`[StronglyTypedId(typeof(int), true)]`) overrides the default; an optional argument that you leave out
uses the assembly-wide default, not `true`.

## Scope

- The defaults apply to the IDs declared in the assembly that has the attribute, not to other projects of the solution.
  Use a shared file, such as one included by a `Directory.Build.props` file, to give several projects the same defaults.
- An assembly has one `StronglyTypedIdDefaults` attribute at most.
- `GuidGenerationStrategy = GuidGenerationStrategy.Version7` is an error on a framework without
  `Guid.CreateVersion7()`, as on a type
  ([MFSTID0002](11-Diagnostics.md#mfstid0002-guid-generation-strategy-is-not-supported-by-the-target-framework)). A
  `GuidGenerationStrategy` default has no effect on the IDs that do not wrap a `Guid`, without a warning.
