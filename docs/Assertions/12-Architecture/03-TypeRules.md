---
title: Type rules
description: Select types with LINQ, then assert the types they use, the members they access, their names and their shape.
---

A type rule selects types, then asserts something about them with
[Assert.Architecture](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureAssert): the types they use, or their names and shape.

## Select the types

[ArchitectureModel.Types](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureModel.Types) is a list of
[TypeElement](cref:Meziantou.Framework.Assertions.Architecture.TypeElement): select the types with LINQ. The failure
messages show the expression you pass to the assertion, so a variable with a clear name, such as `domain`, gives a
clear message.

These members of [TypeElement](cref:Meziantou.Framework.Assertions.Architecture.TypeElement) cover the common filters:

| Member | Is `true` when the type |
|---|---|
| [IsInNamespace](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.IsInNamespace) | is in the namespace, not in one of its child namespaces |
| [IsInNamespaceOrChildren](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.IsInNamespaceOrChildren) | is in the namespace or one of its child namespaces: `MyApp.Domain` matches `MyApp.Domain.Events`, not `MyApp.DomainEvents` |
| [IsClass](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.IsClass), [IsInterface](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.IsInterface), [IsStruct](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.IsStruct), [IsEnum](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.IsEnum), [IsDelegate](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.IsDelegate), [IsRecord](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.IsRecord) | is of a kind |
| [IsAbstract](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.IsAbstract), [IsSealed](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.IsSealed), [IsStatic](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.IsStatic), [IsPublic](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.IsPublic), [IsNested](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.IsNested) | has a modifier |
| [InheritsFrom](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.InheritsFrom), [Implements](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.Implements), [IsAssignableTo](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.IsAssignableTo) | inherits from a class or implements an interface, directly or not |
| [HasAttribute](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.HasAttribute) | has an attribute |

[Name](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.Name) is the name without the namespace and the generic
arity (`Repository` for `Repository<T>`), so `t.Name.EndsWith("Repository", StringComparison.Ordinal)` filters by name.

The members that take a type, such as `Implements<T>()`, compare full names. Pass a generic type definition, such as
`typeof(Repository<>)`, to match all its instantiations. The overloads that take a string expect the name as written in
metadata: ``MyApp.Repository`1``, `MyApp.Outer+Inner`.

Combine sets with LINQ (`Except`, `Concat`) or with a collection expression: `[.. application, .. web]`.

## Check the dependencies

| Assertion | Fails when a type |
|---|---|
| [DoesNotDependOn(types, forbiddenTypes)](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureAssert.DoesNotDependOn) | uses one of the forbidden types. The dependencies between the checked types are ignored |
| [DoesNotDependOn(types, predicate)](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureAssert.DoesNotDependOn) | uses a type that matches the predicate, including a type of .NET or of a package |
| [DoesNotDependOn\<T\>(types)](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureAssert.DoesNotDependOn) | uses `T`. A generic type definition, such as `typeof(List<>)`, forbids all its instantiations |
| [DependsOnlyOn(types, allowedTypes)](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureAssert.DependsOnlyOn) | uses a type of the loaded assemblies that is neither allowed nor checked. The types of .NET and of the packages are not checked |
| [DependsOnlyOn(types, predicate)](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureAssert.DependsOnlyOn) | uses a type that is not checked and does not match the predicate. Add `System` to allow the types of .NET |
| [DoesNotAccessMember\<T\>(types, memberName)](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureAssert.DoesNotAccessMember) | calls a method or a constructor of `T`, or reads or writes a field, a property or an event |

The predicates take a [TypeReference](cref:Meziantou.Framework.Assertions.Architecture.TypeReference), which has the same
[IsInNamespace](cref:Meziantou.Framework.Assertions.Architecture.TypeReference.IsInNamespace) and
[IsInNamespaceOrChildren](cref:Meziantou.Framework.Assertions.Architecture.TypeReference.IsInNamespaceOrChildren) members
as [TypeElement](cref:Meziantou.Framework.Assertions.Architecture.TypeElement):

<!-- ref:../../../tests/Meziantou.Framework.Assertions.Tests/Architecture/DocumentationTests.cs;region=Architecture_DependsOnlyOn;format=md-fenced-code-block;dedent=true -->
```csharp
var architecture = ArchitectureModel.FromAssemblyContaining<Order>();
var domain = architecture.Types.Where(t => t.IsInNamespaceOrChildren("SampleApp.Domain"));

// The types of the domain may use each other and the types of .NET, nothing else
Assert.Architecture.DependsOnlyOn(domain, t => t.IsInNamespaceOrChildren("System"));
```
<!-- endref -->

[DoesNotAccessMember](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureAssert.DoesNotAccessMember) bans a member of a type that is otherwise allowed, such as the
current time:

<!-- ref:../../../tests/Meziantou.Framework.Assertions.Tests/Architecture/DocumentationTests.cs;region=Architecture_DoesNotAccessMember;format=md-fenced-code-block;dedent=true -->
```csharp
var architecture = ArchitectureModel.FromAssemblyContaining<Order>();
var types = architecture.Types.Where(t => t.IsInNamespaceOrChildren("SampleApp"));

Assert.Architecture.DoesNotAccessMember<DateTime>(types, "Now", "the current time comes from TimeProvider");
Assert.Architecture.DoesNotAccessMember<DateTime>(types, nameof(DateTime.UtcNow), "the current time comes from TimeProvider");
```
<!-- endref -->

[The model](06-Model.md#dependencies) explains what counts as a dependency.

## Check the names and the shape

[AllMatch](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureAssert.AllMatch) checks a predicate on each type, with
the members of [TypeElement](cref:Meziantou.Framework.Assertions.Architecture.TypeElement):

<!-- ref:../../../tests/Meziantou.Framework.Assertions.Tests/Architecture/DocumentationTests.cs;region=Architecture_ShapeRules;format=md-fenced-code-block;dedent=true -->
```csharp
var architecture = ArchitectureModel.FromAssemblyContaining<Order>();
var repositories = architecture.Types.Where(t => t.IsClass && t.Implements<IOrderRepository>());

Assert.Architecture.AllMatch(repositories, t => t.Name.EndsWith("Repository", StringComparison.Ordinal));
Assert.Architecture.AllMatch(repositories, t => t.IsSealed);
Assert.Architecture.AllMatch(architecture.Types.Where(t => t.IsInNamespaceOrChildren("SampleApp.Domain") && !t.IsInterface), t => t.IsImmutable);
```
<!-- endref -->

- Unlike `Assert.All`, the message lists every type that does not match the predicate, with its location, and the
  assertion fails when there is no type to check.
- [IsImmutable](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.IsImmutable) is `true` when the instance
  fields are `readonly` or constant, and the instance properties have no setter other than `init`.

To check methods, properties or fields, see [Member rules](04-MemberRules.md).
