---
title: Strict mode
description: Also report tagged values mixed with untagged values, so every value that meets a tagged value is tagged too.
---

By default, untagged values are never reported, which lets you adopt the package one declaration at a time. Once the
important ids are tagged, strict mode makes sure the tags do not get lost: it also reports a tagged value mixed with an
untagged value ([MFTV0009](09-Rules/10-MFTV0009.md)).

## Enable strict mode

Enable it in the `.editorconfig` file of your solution:

```editorconfig
[*.cs]
taggedvalues.strict = true
```

The analyzer then reports the comparisons and the flows between tagged and untagged values:

<!-- ref:../../samples/Meziantou.Framework.TaggedValues.Samples/Strict/StrictMode.cs;region=TaggedValues_StrictMode;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class Order
{
    [ValueTag("OrderId")] public Guid Id { get; set; } = Guid.NewGuid(); // ok, a new value
}

public sealed class OrderService
{
    private readonly Guid _other = Guid.NewGuid();

    public void M(Order order)
    {
        _ = order.Id == _other;     // MFTV0009, compared with an untagged value
        order.Id = _other;          // MFTV0009, an untagged value flows to a tagged declaration
        Load(order.Id);             // MFTV0009, a tagged value flows to an untagged declaration
        _ = order.Id == Guid.Empty; // ok
    }

    private static void Load(Guid id) { }
}
```
<!-- endref -->

## Fix the reports

Tag the untagged declarations. The code fix adds the tag to the untagged declaration:

<!-- ref:../../samples/Meziantou.Framework.TaggedValues.Samples/Strict/Fixed/StrictMode.cs;region=TaggedValues_StrictModeFixed;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class OrderService
{
    [ValueTag("OrderId")] private readonly Guid _other = Guid.NewGuid();

    public void M(Order order)
    {
        _ = order.Id == _other;
        order.Id = _other;
        Load(order.Id);
    }

    private static void Load([ValueTag("OrderId")] Guid id) { }
}
```
<!-- endref -->

## What is allowed

- Default values, `null`, constants, `Guid.Empty`, `string.Empty`, and `new Guid()` are always allowed.
- New values, such as `Guid.NewGuid()`, `new Guid(bytes)`, or `Guid.Parse(text)`, can flow to a tagged declaration.
  Values read from an untagged field, property, parameter, local, or array element, or returned by an untagged method of
  your code, cannot.
- A `?:`, `??`, or switch expression is a new value only when each of its branches is a new value or an allowed value.
- A tagged value can flow to a declaration that cannot be tagged: a declaration of a referenced assembly or project, or
  a declaration typed `object`, `dynamic`, a type parameter, or a collection of them such as `params object[]`.
- A local initialized with a tagged value takes its tag, so it is not reported.
- The elements of a tuple cannot be tagged, so an untagged element of a tuple is not reported.
