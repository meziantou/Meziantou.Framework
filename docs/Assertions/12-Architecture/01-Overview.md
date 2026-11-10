---
title: Overview
description: Assert architecture rules in unit tests with Assert.Architecture, by reading the compiled assemblies.
---

[Assert.Architecture](cref:Meziantou.Framework.Assertions.Assert.Architecture) checks the architecture of an application
in unit tests: the domain does not use the infrastructure, the controllers only use the application layer, the modules
do not depend on each other in a cycle. When an assertion fails, its message lists the types, members and statements
that break it.

It is part of the `Meziantou.Framework.Assertions` package: no other package is needed. The model and the assertions
are in the `Meziantou.Framework.Assertions.Architecture` namespace.

## What you can do with it

- **Write a first assertion** and load the assemblies to check. See [Getting started](02-GettingStarted.md).
- **Check types**: select types with LINQ, then assert which types they may use, which members they must not access, and
  their names and shape. See [Type rules](03-TypeRules.md).
- **Check members**: select methods, properties, fields or events, then assert their shape, such as "the public methods
  of the controllers are async", and the types they use. See [Member rules](04-MemberRules.md).
- **Check layers and modules**: forbid the dependencies between layers, or split the types into slices by namespace and
  forbid cycles between them. See [Layers and slices](05-LayersAndSlices.md).
- **Query the code**: the model of the assemblies (types, members, attributes, dependencies) is a plain object model
  you can query with LINQ to write any check. See [The model](06-Model.md).
- **Know what the compiled code does not show**, such as constants and reflection. See [Limitations](07-Limitations.md).

## How it works

The library reads the compiled assemblies with `System.Reflection.Metadata`: the metadata for the declarations, the IL
of the methods for the code, and the portable PDB for the source locations. The assemblies are not loaded for execution.

The assertions of [Assert.Architecture](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureAssert), such as
[DoesNotDependOn](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureAssert.DoesNotDependOn), take the types or the members to check and throw an
[AssertionException](cref:Meziantou.Framework.Assertions.AssertionException), like the other assertions. To use the
model with another assertion library, read the dependencies of the types from
[TypeElement.Dependencies](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.Dependencies), and of the members from
[MemberElement.Dependencies](cref:Meziantou.Framework.Assertions.Architecture.MemberElement.Dependencies).

## Namespace

`Assert.Architecture` is in the [Meziantou.Framework.Assertions](cref:Meziantou.Framework.Assertions) namespace, next to
[Assert](cref:Meziantou.Framework.Assertions.Assert). The model and the assertions, such as
[ArchitectureModel](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureModel), are in the
[Meziantou.Framework.Assertions.Architecture](cref:Meziantou.Framework.Assertions.Architecture) namespace.
