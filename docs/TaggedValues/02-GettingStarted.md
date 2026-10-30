---
title: Getting started
description: Install the package, tag your first ids, and fix the warnings the analyzer reports.
---

## Install the package

Add the `Meziantou.Framework.TaggedValues` package to your project:

```bash
dotnet add package Meziantou.Framework.TaggedValues
```

The package contains the `[ValueTag]` attribute, and a Roslyn analyzer with its code fixes for C#. The analyzer runs
during the build and in the IDE: there is nothing to register.

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework.TaggedValues;`.

## Tag your ids

Add `[ValueTag]` to the properties, fields and parameters that hold ids. The analyzer then reports the places where a
value with one tag meets a value with another tag:

<!-- ref:../../samples/Meziantou.Framework.TaggedValues.Samples/Order.cs;region=TaggedValues_Overview;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class Order
{
    [ValueTag("OrderId")] public Guid Id { get; set; }
    [ValueTag("ProjectId")] public Guid ProjectId { get; set; }
}

public sealed class OrderService
{
    public Order Load([ValueTag("OrderId")] Guid orderId) => new() { Id = orderId };

    public void M(Order order)
    {
        _ = order.Id == order.ProjectId; // MFTV0001: values with different tags are compared
        Load(order.ProjectId);           // MFTV0002: a ProjectId flows to a parameter that expects an OrderId
    }
}
```
<!-- endref -->

- `Id` is tagged `"OrderId"`, and `ProjectId` is tagged `"ProjectId"`. Both stay `Guid` properties.
- Comparing an order id with a project id is reported by [MFTV0001](09-Rules/02-MFTV0001.md).
- Passing a project id to a parameter that expects an order id is reported by [MFTV0002](09-Rules/03-MFTV0002.md).

Values that are not tagged are never reported, so tagging one property or one parameter is enough to start.

## Fix the warnings

The fix is usually to use the right value:

<!-- ref:../../samples/Meziantou.Framework.TaggedValues.Samples/GettingStarted.cs;region=TaggedValues_GettingStartedFixed;format=md-fenced-code-block;dedent=true -->
```csharp
_ = order.ProjectId == other.ProjectId; // ok, both are ProjectIds
Load(order.Id);                         // ok, an OrderId flows to an OrderId
```
<!-- endref -->

Sometimes the value is right and a declaration is tagged incorrectly. The code fix of the warning then changes the tag
of the declaration the value flows to. Every message names both declarations and their tags, for instance:

```text
property 'Order.ProjectId' is [ValueTag("ProjectId")] and flows to parameter 'orderId' of 'OrderService.Load' (line 12), which is [ValueTag("OrderId")]
```

## Let the tags flow

You do not need to tag every declaration. A local variable takes the tag of the value it is initialized with, and the
tag follows the value through collections, LINQ, `await` and arithmetic:

<!-- ref:../../samples/Meziantou.Framework.TaggedValues.Samples/LocalVariables.cs;region=TaggedValues_LocalInference;format=md-fenced-code-block;dedent=true -->
```csharp
var id = order.Id;    // id is an OrderId
Load(id);             // ok
id = order.ProjectId; // MFTV0002

var pair = (order.Id, order.ProjectId);
Load(pair.Item1);     // ok
Load(pair.Item2);     // MFTV0002
```
<!-- endref -->

When a method returns tagged values but its return value is not tagged, the callers lose the tag. The analyzer
suggests the missing attribute ([MFTV0008](09-Rules/09-MFTV0008.md)).

## Next steps

- [Tagging values](03-TaggingValues.md): every place a tag can be declared.
- [Naming conventions](07-NamingConventions.md): let the analyzer infer the tags of `Id`, `OrderId` and `projectId`
  from their names.
- [Strict mode](08-StrictMode.md): also report tagged values mixed with untagged values.
- [Analyzer rules](09-Rules/01-Overview.md): what each rule reports and how to configure it.
