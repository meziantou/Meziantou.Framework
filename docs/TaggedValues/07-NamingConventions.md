---
title: Naming conventions
description: Let the analyzer infer the tags of Id, xxxId and id members from their names, so most ids need no attribute.
---

## Enable the conventions

The conventions are opt-in. Enable them in the `.editorconfig` file of your solution:

```editorconfig
[*.cs]
taggedvalues.infer_tags_from_names = true
```

Most ids then need no attribute:

<!-- ref:../../samples/Meziantou.Framework.TaggedValues.Samples/NamingConventions/NamingConventions.cs;region=TaggedValues_NamingConventions;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class Customer
{
    public Guid Id { get; set; }        // a CustomerId, from the name of the type
    public Guid AccountId { get; set; } // an AccountId, from the name of the property
}

public sealed class CustomerService
{
    public void Delete(Guid customerId) { } // a CustomerId, from the name of the parameter

    public void Archive(Customer id) { }    // a CustomerId, from the type of the parameter

    public void M(Customer customer)
    {
        Delete(customer.Id);        // ok
        Delete(customer.AccountId); // MFTV0002
    }
}
```
<!-- endref -->

## The conventions

- **A property or a field named `Id`** takes the name of its declaring type: `Id` on `Order` is an `"OrderId"`, and
  `Id` on the interface `IEntity` is an `"EntityId"`. When the member is inherited, it is also an id of every type
  between the receiver and the declaring type. When it overrides or implements an `Id`, it is also an id of the base
  type or of the interface.
- **A property, a field, or a parameter named `xxxId`** takes its name: `projectId` is a `"projectId"`, which is the
  same tag as `"ProjectId"`, as tags are compared ignoring case. The `s_`, `t_`, and `m_` prefixes and the leading
  underscores of fields are ignored: `s_projectId` and `_projectId` are both `"projectId"`.
- **A parameter named `id`** takes the name of its type: `void Load(Order id)` is an `"OrderId"`, and
  `void Load(OrderId id)` is an `"OrderId"` too. A parameter named `id` whose type is a primitive, a `string`, a `Guid`,
  an enum, or another type of the `System` namespace such as `Int128` or `DateTimeOffset` is not tagged, as its type
  does not say what it identifies.

## Limits

- Explicit tags always win over the conventions.
- Collections and indexers are not tagged by convention.
- The conventions only apply to the declarations of the project, not to the declarations of referenced projects or
  assemblies. Tag the members a referenced project exposes explicitly when its consumers must see the tags.

## Rules specific to the conventions

- Two types with the same name in different namespaces give their `Id` the same tag, for instance `Sales.Order.Id` and
  `Billing.Order.Id`. The analyzer reports it ([MFTV0006](09-Rules/07-MFTV0006.md)): add an explicit tag to at least
  one of them.
- A [\[ValueTag\]](cref:Meziantou.Framework.TaggedValues.ValueTagAttribute) attribute that declares the tag the convention already infers is redundant
  ([MFTV0007](09-Rules/08-MFTV0007.md)): the code fix removes it.
