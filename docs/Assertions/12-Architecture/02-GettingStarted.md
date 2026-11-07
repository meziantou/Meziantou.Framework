---
title: Getting started
description: Load the assemblies of the application and write a first assertion.
---

The architecture assertions are part of the `Meziantou.Framework.Assertions` package. See
[Getting started](../02-GettingStarted.md) to install it. Add `using Meziantou.Framework.Assertions.Architecture;` to
use the model.

## Write an assertion

Load the assemblies to check, select the types with LINQ, and call an assertion of `Assert.Architecture`:

<!-- ref:../../../tests/Meziantou.Framework.Assertions.Tests/Architecture/DocumentationTests.cs;region=Architecture_DoesNotDependOn;format=md-fenced-code-block;dedent=true -->
```csharp
var architecture = ArchitectureModel.FromAssemblyContaining<Order>();
var domain = architecture.Types.Where(t => t.IsInNamespaceOrChildren("SampleApp.Domain"));
var infrastructure = architecture.Types.Where(t => t.IsInNamespaceOrChildren("SampleApp.Infrastructure"));

Assert.Architecture.DoesNotDependOn(domain, infrastructure, "the domain stays persistence-ignorant");
Assert.Architecture.DoesNotDependOn(domain, t => t.IsInNamespaceOrChildren("Microsoft.EntityFrameworkCore"));
Assert.Architecture.DoesNotDependOn<HttpClient>(domain);
```
<!-- endref -->

- [Types](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureModel.Types) contains all the types of the assemblies,
  including the nested types. Filter them with `Where` and the members of
  [TypeElement](cref:Meziantou.Framework.Assertions.Architecture.TypeElement).
- [Assert.Architecture](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureAssert) groups the assertions on dependencies. Each one takes the types to check first,
  and an optional `message` last, like the other assertions.

When your test framework also has an `Assert` class, add the global alias described in
[Use it as the Assert class](../02-GettingStarted.md#use-it-as-the-assert-class).

When the assertion fails, its message shows the expressions you passed, then each type that breaks it, followed by its
dependencies with the member and the statement that uses the forbidden type:

```text
Assert.Architecture.DoesNotDependOn() assertion failed: 1 type depends on a forbidden type.
Message: the domain stays persistence-ignorant
Types expression:     domain
Forbidden expression: infrastructure
- SampleApp.Domain.Order depends on SampleApp.Infrastructure.Database
    SampleApp.Domain.Order.Save calls SampleApp.Infrastructure.Database.Execute (/src/Domain/Order.cs:12:9)
```

An assertion whose types are empty fails too, so that a typo in a namespace does not make it pass silently.

## Load the assemblies

[ArchitectureModel](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureModel) loads assemblies in several ways:

- [FromAssemblyContaining\<T\>](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureModel.FromAssemblyContaining) and
  [FromAssemblies](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureModel.FromAssemblies) read the files the
  assemblies were loaded from. Reference the projects to check from the test project.
- [FromFiles](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureModel.FromFiles) reads assemblies from their paths.
- [FromBytes](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureModel.FromBytes) reads assemblies from memory, such as
  assemblies compiled at run time or the assemblies of a single-file application, which have no file.
- [Load](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureModel.Load) takes an
  [ArchitectureLoadOptions](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureLoadOptions), which also sets the
  folders where the referenced assemblies are searched, and whether the PDBs are read.

Loading reads every file into memory: no file stays open, and the model is immutable and safe to use from several
threads. Load the architecture once and share it between the tests:

<!-- ref:../../../tests/Meziantou.Framework.Assertions.Tests/Architecture/SharedArchitectureTests.cs;region=Architecture_SharedArchitecture;format=md-fenced-code-block;dedent=true -->
```csharp
// Loading reads the assemblies: load them once and share the model between the tests
private static readonly ArchitectureModel Architecture = ArchitectureModel.FromAssemblyContaining<Order>();

[Fact]
public void DomainDoesNotDependOnInfrastructure() =>
    Assert.Architecture.DoesNotDependOn(
        Architecture.Types.Where(t => t.IsInNamespaceOrChildren("SampleApp.Domain")),
        t => t.IsInNamespaceOrChildren("SampleApp.Infrastructure"));

[Fact]
public void RepositoriesAreSealed() =>
    Assert.All(Architecture.Types.Where(t => t.IsClass && t.Implements<IOrderRepository>()), t => t.IsSealed);
```
<!-- endref -->

## Source locations

The messages give the file, line and column of the statements that break an assertion when the portable PDB of the assembly
is available: a `.pdb` file next to the assembly, or a PDB embedded in it. This is the default for SDK-style projects.
The declarations that have no code, such as the type of a field, have no location.
