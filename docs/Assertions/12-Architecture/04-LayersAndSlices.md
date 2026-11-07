---
title: Layers and slices
description: Declare the layers of an application and the dependencies they may have, and forbid cycles between modules.
---

## Layers

A layer is a set of types. Select each layer with LINQ, then forbid the dependencies between them with
[DoesNotDependOn](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureAssert.DoesNotDependOn). A collection expression combines several layers:

<!-- ref:../../../tests/Meziantou.Framework.Assertions.Tests/Architecture/DocumentationTests.cs;region=Architecture_Layers;format=md-fenced-code-block;dedent=true -->
```csharp
var architecture = ArchitectureModel.FromAssemblyContaining<Order>();
var web = architecture.Types.Where(t => t.IsInNamespaceOrChildren("SampleApp.Web"));
var application = architecture.Types.Where(t => t.IsInNamespaceOrChildren("SampleApp.Application"));
var domain = architecture.Types.Where(t => t.IsInNamespaceOrChildren("SampleApp.Domain"));
var infrastructure = architecture.Types.Where(t => t.IsInNamespaceOrChildren("SampleApp.Infrastructure"));

Assert.Architecture.DoesNotDependOn(domain, [.. application, .. web, .. infrastructure]);
Assert.Architecture.DoesNotDependOn(application, [.. web, .. infrastructure]);
Assert.Architecture.DoesNotDependOn(infrastructure, [.. application, .. web]);

// OrdersController creates the repository itself
var exception = Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotDependOn(web, infrastructure, "the web layer gets the repository by injection"));
Assert.StartsWith("""
    Assert.Architecture.DoesNotDependOn() assertion failed: 1 type depends on a forbidden type.
    Message: the web layer gets the repository by injection
    Types expression:     web
    Forbidden expression: infrastructure
    - SampleApp.Web.OrdersController depends on SampleApp.Infrastructure.SqlOrderRepository
        SampleApp.Web.OrdersController.CreateRepository creates SampleApp.Infrastructure.SqlOrderRepository..ctor
    """.ReplaceLineEndings("\n"), exception.Message);
```
<!-- endref -->

- The dependencies inside a layer are always allowed.
- The types that are in no layer are not checked, and the dependencies on them are allowed. To forbid the use of a
  library, pass a predicate: `Assert.Architecture.DoesNotDependOn(domain, t => t.IsInNamespaceOrChildren("Microsoft.EntityFrameworkCore"))`.
- A layer that contains no type makes the assertion fail, to catch a typo in a namespace.

## Slices

[SliceByNamespace](cref:Meziantou.Framework.Assertions.Architecture.TypeElementExtensions.SliceByNamespace) groups types
into slices, such as the modules of an application. The pattern has one `{slice}` segment whose value names the slice:
`SampleApp.Modules.{slice}` puts `SampleApp.Modules.Billing` and `SampleApp.Modules.Billing.Data` in the slice
`Billing`. A `*` segment matches any segment, and the types whose namespace does not match are in no slice.

<!-- ref:../../../tests/Meziantou.Framework.Assertions.Tests/Architecture/DocumentationTests.cs;region=Architecture_Slices;format=md-fenced-code-block;dedent=true -->
```csharp
var architecture = ArchitectureModel.FromAssemblyContaining<Order>();
var modules = architecture.Types.SliceByNamespace("SampleApp.Modules.{slice}");

var exception = Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotHaveCycles(modules));
Assert.StartsWith("""
    Assert.Architecture.DoesNotHaveCycles() assertion failed: 1 cycle between slices.
    Slices expression: modules
    - Billing -> Shipping -> Billing
        SampleApp.Modules.Billing.Invoice.Shipment has a property of type SampleApp.Modules.Shipping.Shipment
    """.ReplaceLineEndings("\n"), exception.Message);
```
<!-- endref -->

- [DoesNotHaveCycles](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureAssert.DoesNotHaveCycles) allows a slice to use another one, but not in a cycle. Each group of
  slices that depend on each other is reported once, with one of the shortest cycles and the dependencies that make it.
- [DoesNotDependOnEachOther](cref:Meziantou.Framework.Assertions.Architecture.ArchitectureAssert.DoesNotDependOnEachOther) forbids any dependency between two slices: each type
  that uses another slice is reported.
- Both take any `IGrouping<string, TypeElement>` collection, so `GroupBy` slices the types too:
  `architecture.Types.GroupBy(t => t.Assembly.Name)`. A type must not be in several slices.
