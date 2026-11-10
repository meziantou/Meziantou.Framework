---
title: The model
description: Query the types, members, attributes and dependencies of the assemblies with LINQ, and learn what counts as a dependency.
---

The assertions are built on a model of the assemblies that you can query directly, to write checks the assertions do not cover:

<!-- ref:../../../tests/Meziantou.Framework.Assertions.Tests/Architecture/DocumentationTests.cs;region=Architecture_Model;format=md-fenced-code-block;dedent=true -->
```csharp
var architecture = ArchitectureModel.FromAssemblyContaining<Order>();
var controller = architecture.GetTypeElement(typeof(OrdersController));

var usedTypes = controller.Dependencies
    .Select(dependency => dependency.Target.FullName)
    .Where(name => name.StartsWith("SampleApp.", StringComparison.Ordinal))
    .Distinct()
    .Order(StringComparer.Ordinal)
    .ToList();

string[] expected = ["SampleApp.Application.PlaceOrderHandler", "SampleApp.Domain.IOrderRepository", "SampleApp.Domain.OrderLine", "SampleApp.Infrastructure.SqlOrderRepository"];
Assert.Equal(expected, usedTypes);
```
<!-- endref -->

## Types and members

- An [AssemblyElement](cref:Meziantou.Framework.Assertions.Architecture.AssemblyElement) has its name, version, attributes,
  references and types.
- A [TypeElement](cref:Meziantou.Framework.Assertions.Architecture.TypeElement) has its names, kind, accessibility,
  modifiers, base type, interfaces, attributes, nested types and members.
  [InheritsFrom](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.InheritsFrom) and
  [Implements](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.Implements) follow the hierarchy across
  assemblies, including the types of .NET.
- The members are [MethodElement](cref:Meziantou.Framework.Assertions.Architecture.MethodElement) (constructors in
  [Constructors](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.Constructors)),
  [PropertyElement](cref:Meziantou.Framework.Assertions.Architecture.PropertyElement),
  [FieldElement](cref:Meziantou.Framework.Assertions.Architecture.FieldElement) and
  [EventElement](cref:Meziantou.Framework.Assertions.Architecture.EventElement).
- A type used in a signature or by the code is a
  [TypeReference](cref:Meziantou.Framework.Assertions.Architecture.TypeReference): it may be defined outside the
  architecture. [Definition](cref:Meziantou.Framework.Assertions.Architecture.TypeReference.Definition) returns the
  [TypeElement](cref:Meziantou.Framework.Assertions.Architecture.TypeElement) when one of the loaded assemblies defines it.

[FullName](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.FullName) is the name as written in metadata, which
is also the value of `Type.FullName`: ``MyApp.Repository`1``, `MyApp.Outer+Inner`. Types are compared by full name,
without their assembly: the types of .NET are often referenced from a facade assembly such as `System.Runtime` and
defined in another one.

## Dependencies

[Dependencies](cref:Meziantou.Framework.Assertions.Architecture.TypeElement.Dependencies) lists the types a type uses. Each
[Dependency](cref:Meziantou.Framework.Assertions.Architecture.Dependency) has the member that uses the type, the
[DependencyKind](cref:Meziantou.Framework.Assertions.Architecture.DependencyKind), the method or field used, and the source
location. A type depends on the types it uses in:

- its declaration: base type, interfaces, attributes and their `typeof` arguments, generic constraints;
- the declarations of its members: the types of the fields, properties and events, the parameters and return types;
- the code of its methods: the methods it calls, the objects it creates, the fields it accesses, the types of its local
  variables, the types it casts to, tests with `is`, or uses with `typeof`, and the exceptions it catches.

A generic type gives a dependency on each of its parts: `List<Order>` gives a dependency on `List<T>` and one on
`Order`.

The code the compiler generates belongs to the member that was written: the code of a lambda, a local function, an
iterator or an async method is a dependency of the method that declares it, and the variables they capture are
[LocalVariable](cref:Meziantou.Framework.Assertions.Architecture.DependencyKind.LocalVariable) dependencies. The types the
compiler generates (closures, state machines, anonymous types) are not part of
[Types](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureModel.Types).

A type does not depend on itself, on `System.Void`, or on the attributes the compiler adds on its own, such as
`NullableAttribute` or `CompilerGeneratedAttribute`. The dependencies reflect the compiled code, so they include the
types the compiler uses to implement a feature, such as `AsyncTaskMethodBuilder` for an async method.
