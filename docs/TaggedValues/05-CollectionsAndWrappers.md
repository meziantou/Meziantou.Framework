---
title: Collections, dictionaries and wrappers
description: Tag the elements of a collection, the keys and values of a dictionary, and the result of a task.
---

## Tag the elements of a collection

On a collection, a `Nullable<T>`, a `Task<T>`, a `ValueTask<T>`, a `Lazy<T>`, a span, or a memory, the tag describes the
element or the value, not the container:

<!-- ref:../../samples/Meziantou.Framework.TaggedValues.Samples/OrderCollections.cs;region=TaggedValues_Collections;format=md-fenced-code-block;dedent=true -->
```csharp
[ValueTag("OrderId")] public List<Guid> OrderIds { get; } = [];

public void M(Order order)
{
    OrderIds.Add(order.ProjectId);                        // MFTV0002
    _ = OrderIds.Where(id => id == order.ProjectId);      // MFTV0001
    Load(OrderIds.Where(id => id != Guid.Empty).First()); // ok
}
```
<!-- endref -->

The tag flows through indexers, `foreach`, LINQ, lambda parameters (including `IQueryable` expression trees),
`ConfigureAwait`, `WithCancellation`, and any generic method whose signature preserves the element type.

A string is not a collection: the tag of a string describes the string, not its characters.

## Tag the keys and values of a dictionary

Use `Key` and `Value` for dictionaries:

<!-- ref:../../samples/Meziantou.Framework.TaggedValues.Samples/OrderCollections.cs;region=TaggedValues_Dictionaries;format=md-fenced-code-block;dedent=true -->
```csharp
[ValueTag(Key = "OrderId", Value = "ProjectId")]
public Dictionary<Guid, Guid> ProjectIdByOrderId { get; } = [];
```
<!-- endref -->

- Either one can be omitted, to tag only the keys or only the values.
- A dictionary declaration tagged with `[ValueTag("Tag")]` instead of `Key` and `Value` is reported, and so are `Key`
  and `Value` on a type that is not a dictionary ([MFTV0005](09-Rules/06-MFTV0005.md)). An attribute cannot combine tags
  with `Key` and `Value`.
- For a local variable, use the comment `// ValueTag Key=OrderId Value=ProjectId`, or tag the type arguments. See
  [Tagging local variables](04-LocalVariables.md#tag-type-arguments).

## Keep the tags of projections

Anonymous type properties take the tag of their initializer, so projections keep their tags. A collection initializer
takes the tags of the elements it adds:

<!-- ref:../../samples/Meziantou.Framework.TaggedValues.Samples/Projections.cs;region=TaggedValues_Projections;format=md-fenced-code-block;dedent=true -->
```csharp
var rows = orders.Select(order => new { order.Id, order.ProjectId });
foreach (var row in rows)
{
    Load(row.Id);        // ok
    Load(row.ProjectId); // MFTV0002
}

var ids = new List<Guid> { orders[0].Id }; // a list of OrderIds
Load(ids[0]);                              // ok
```
<!-- endref -->

In the same way, `new Dictionary<Guid, Guid> { [order.Id] = order.ProjectId }` maps OrderIds to ProjectIds.

## Elements that are checked

Adding a value to a tagged collection is a flow: the value must have the tag of the elements
([MFTV0002](09-Rules/03-MFTV0002.md)). The elements of an array, a collection expression, or a collection initializer must
share a tag ([MFTV0003](09-Rules/04-MFTV0003.md)). The arguments of `params` and the elements of a collection of `object`
are not combined, as they hold values of different kinds, such as the arguments of a log message.
