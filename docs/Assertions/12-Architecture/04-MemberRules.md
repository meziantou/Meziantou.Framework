---
title: Member rules
description: Select methods, properties, fields and events with LINQ, then assert their shape and the types they use.
---

A member rule selects members of types, such as the public methods of the controllers, then asserts something about
them with [Assert.Architecture](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureAssert): their shape, or the
types they use.

## Select the members

Each [TypeElement](cref:Meziantou.Framework.Assertions.Architecture.TypeElement) lists the members it declares:
[Methods](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.Methods),
[Constructors](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.Constructors),
[Properties](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.Properties),
[Fields](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.Fields),
[Events](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.Events), and all of them in
[Members](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.Members). Select the types first, then their
members with `SelectMany`: `controllers.SelectMany(t => t.Methods).Where(method => method.IsPublic)`.

- [Methods](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.Methods) does not contain the constructors and
  the accessors of the properties and events: they are in
  [Constructors](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.Constructors), and in the
  [PropertyElement](cref:Meziantou.Framework.Assertions.Architecture.PropertyElement) and
  [EventElement](cref:Meziantou.Framework.Assertions.Architecture.EventElement) they belong to.
- The members are the ones the type declares: the inherited members are in the base type.
- The members the compiler generates without a name you wrote, such as the fields of the auto-properties and of the
  parameters of a primary constructor, are not listed. The members it generates for a record, such as `Equals`, are.

[MemberElement](cref:Meziantou.Framework.Assertions.Architecture.MemberElement) has the members that are common to all
kinds, such as [Name](cref:Meziantou.Framework.Assertions.Architecture.MemberElement.Name),
[Accessibility](cref:Meziantou.Framework.Assertions.Architecture.MemberElement.Accessibility),
[IsPublic](cref:Meziantou.Framework.Assertions.Architecture.MemberElement.IsPublic),
[IsStatic](cref:Meziantou.Framework.Assertions.Architecture.MemberElement.IsStatic) and
[HasAttribute](cref:Meziantou.Framework.Assertions.Architecture.MemberElement.HasAttribute). Each kind adds its own, such as
[IsAsync](cref:Meziantou.Framework.Assertions.Architecture.MethodElement.IsAsync) and
[ReturnType](cref:Meziantou.Framework.Assertions.Architecture.MethodElement.ReturnType) for a method,
[IsInitOnly](cref:Meziantou.Framework.Assertions.Architecture.PropertyElement.IsInitOnly) for a property, or
[IsReadOnly](cref:Meziantou.Framework.Assertions.Architecture.FieldElement.IsReadOnly) for a field.

## Check the shape

[AllMatch](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureAssert.AllMatch) checks a predicate on each member,
and lists every member that does not match it, with its location:

<!-- ref:../../../tests/Meziantou.Framework.Assertions.Tests/Architecture/DocumentationTests.cs;region=Architecture_MemberRules;format=md-fenced-code-block;dedent=true -->
```csharp
var architecture = ArchitectureModel.FromAssemblyContaining<Order>();
var handlers = architecture.Types.Where(t => t.IsInNamespace("SampleApp.Application") && t.Name.EndsWith("Handler", StringComparison.Ordinal));
var controllers = architecture.Types.Where(t => t.IsInNamespace("SampleApp.Web") && t.Name.EndsWith("Controller", StringComparison.Ordinal));

Assert.Architecture.AllMatch(handlers.SelectMany(t => t.Methods).Where(method => method.IsPublic), method => method.IsAsync);
var domain = architecture.Types.Where(t => t.IsInNamespaceOrChildren("SampleApp.Domain"));
Assert.Architecture.AllMatch(domain.SelectMany(t => t.Properties), property => property.Setter is null || property.IsInitOnly, "the domain is immutable");

// OrdersController.CreateRepository is a public method that does not return a Task
var actions = controllers.SelectMany(t => t.Methods).Where(method => method.IsPublic);
var exception = Assert.Throws<AssertionException>(() => Assert.Architecture.AllMatch(actions, method => method.ReturnType.Is(typeof(Task<>))));
Assert.StartsWith("""
    Assert.Architecture.AllMatch() assertion failed: 1 member does not match the predicate.
    Members expression:   actions
    Predicate expression: method => method.ReturnType.Is(typeof(Task<>))
    - SampleApp.Web.OrdersController.CreateRepository()
    """.ReplaceLineEndings("\n"), exception.Message);
```
<!-- endref -->

- The predicate takes the kind of the members you pass: a
  [MethodElement](cref:Meziantou.Framework.Assertions.Architecture.MethodElement) for `t.Methods`, a
  [MemberElement](cref:Meziantou.Framework.Assertions.Architecture.MemberElement) for `t.Members`.
- A rule that forbids something, such as "no public fields", is a predicate that negates it: `field => !field.IsPublic`.
- The assertion fails when there is no member to check, so that a typo in a filter does not make it pass silently.

## Check the dependencies

The dependency assertions of [Type rules](03-TypeRules.md#check-the-dependencies) also take members. They check the
declaration of each member (its type, parameters, return type and attributes) and its code, including the code of its
lambdas and local functions:

| Assertion | Fails when a member |
|---|---|
| [DoesNotDependOn(members, forbiddenTypes)](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureAssert.DoesNotDependOn) | uses one of the forbidden types |
| [DoesNotDependOn(members, predicate)](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureAssert.DoesNotDependOn) | uses a type that matches the predicate, including a type of .NET or of a package |
| [DoesNotDependOn\<T\>(members)](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureAssert.DoesNotDependOn) | uses `T`. A generic type definition, such as `typeof(List<>)`, forbids all its instantiations |
| [DependsOnlyOn(members, predicate)](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureAssert.DependsOnlyOn) | uses a type that does not match the predicate. The types that declare the checked members are allowed |
| [DoesNotAccessMember\<T\>(members, memberName)](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureAssert.DoesNotAccessMember) | calls a method or a constructor of `T`, or reads or writes a field, a property or an event |

<!-- ref:../../../tests/Meziantou.Framework.Assertions.Tests/Architecture/DocumentationTests.cs;region=Architecture_MemberDependencies;format=md-fenced-code-block;dedent=true -->
```csharp
var architecture = ArchitectureModel.FromAssemblyContaining<Order>();
var publicMembers = architecture.Types.Where(t => t.IsInNamespaceOrChildren("SampleApp") && t.IsPublic).SelectMany(t => t.Members).Where(member => member.IsPublic);
var infrastructure = architecture.Types.Where(t => t.IsInNamespaceOrChildren("SampleApp.Infrastructure"));

// The signatures and the code of the public members do not use Entity Framework Core
Assert.Architecture.DoesNotDependOn(publicMembers, t => t.IsInNamespaceOrChildren("Microsoft.EntityFrameworkCore"));

// The controllers get the infrastructure by injection: OrdersController.CreateRepository breaks the rule
var controllerMethods = architecture.Types.Where(t => t.IsInNamespace("SampleApp.Web")).SelectMany(t => t.Methods);
var exception = Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotDependOn(controllerMethods, infrastructure));
Assert.StartsWith("""
    Assert.Architecture.DoesNotDependOn() assertion failed: 1 member depends on a forbidden type.
    Members expression:   controllerMethods
    Forbidden expression: infrastructure
    - SampleApp.Web.OrdersController.CreateRepository() depends on SampleApp.Infrastructure.SqlOrderRepository
        SampleApp.Web.OrdersController.CreateRepository creates SampleApp.Infrastructure.SqlOrderRepository..ctor
    """.ReplaceLineEndings("\n"), exception.Message);
```
<!-- endref -->

A dependency belongs to the member whose declaration or code uses the type. A dependency of the type declaration, such
as its base type, belongs to no member: check it with the type rules.
