---
title: Tagging values
description: Tag fields, properties, parameters and return values, declare unions, tag members you do not own, and inherit tags.
---

## Tag a declaration

`[ValueTag]` applies to fields, properties, parameters, and return values (`[return: ValueTag("OrderId")]`):

<!-- ref:../../samples/Meziantou.Framework.TaggedValues.Samples/Tagging/Declarations.cs;region=TaggedValues_TagDeclarations;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class Shipment([ValueTag("OrderId")] Guid orderId) // a parameter
{
    [ValueTag("OrderId")] private readonly Guid _orderId = orderId; // a field

    [ValueTag("ShipmentId")] public Guid Id { get; } = Guid.NewGuid(); // a property

    [return: ValueTag("OrderId")] // a return value
    public Guid GetOrderId() => _orderId;

    // A union: the parameter accepts an OrderId or a ProjectId
    public bool Concerns([ValueTag("OrderId", "ProjectId")] Guid id) => id == _orderId;
}
```
<!-- endref -->

- A tag is a string you choose. Name it after what the value identifies or measures: `"OrderId"`, `"Meters"`,
  `"CacheKey"`.
- Casing does not matter: tags are compared ignoring case, so `"OrderId"` and `"orderId"` are the same tag.
- An empty tag is reported ([MFTV0005](09-Rules/06-MFTV0005.md)).
- Local variables cannot have attributes: tag them with a comment, see [Tagging local variables](04-LocalVariables.md).

## Accept several tags

`[ValueTag("OrderId", "ProjectId")]` declares a union: the declaration accepts both. Two values are compatible when
they share at least one tag, so the `Concerns` method above accepts an order id and a project id, but not a customer id.

## Tag async methods and iterators

On an async method or an iterator, the tag of the return value describes the result of the task or the elements:

<!-- ref:../../samples/Meziantou.Framework.TaggedValues.Samples/Tagging/Declarations.cs;region=TaggedValues_AsyncAndIterators;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class ShipmentFactory
{
    [return: ValueTag("ShipmentId")] // the result of the task is a ShipmentId
    public async Task<Guid> CreateAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(10, cancellationToken);
        return Guid.NewGuid();
    }

    [return: ValueTag("ShipmentId")] // every element is a ShipmentId
    public IEnumerable<Guid> CreateMany(int count)
    {
        for (var i = 0; i < count; i++)
            yield return Guid.NewGuid();
    }
}
```
<!-- endref -->

The same goes for any collection, `Task<T>` or `Nullable<T>`: the tag describes the element or the value. See
[Collections, dictionaries and wrappers](05-CollectionsAndWrappers.md).

## Tag members of types you do not own

To tag a property or a field of a type you cannot edit, apply the attribute to the assembly, with the type and the name
of the member:

<!-- ref:../../samples/Meziantou.Framework.TaggedValues.Samples/Tagging/ExternalMembers.cs;region=TaggedValues_AssemblyAttribute;format=md-fenced-code-block;dedent=true -->
```csharp
[assembly: ValueTag(typeof(Process), nameof(Process.Id), "ProcessId")]
```
<!-- endref -->

The tag then applies wherever the member is read:

<!-- ref:../../samples/Meziantou.Framework.TaggedValues.Samples/Tagging/ExternalMembers.cs;region=TaggedValues_AssemblyAttributeUsage;format=md-fenced-code-block;dedent=true -->
```csharp
public static void Stop([ValueTag("ProcessId")] int processId) { }

public static void M(Process process, [ValueTag("ThreadId")] int threadId)
{
    Stop(process.Id); // ok, Process.Id is a ProcessId
    Stop(threadId);   // MFTV0002
}
```
<!-- endref -->

- Use `nameof` to name the member. A name that matches no property or field of the type or its base types is reported
  ([MFTV0005](09-Rules/06-MFTV0005.md)).
- The tags also apply when the member is accessed through a derived type.
- Assembly attributes are read from referenced assemblies too, so a shared project can declare them once for the whole
  solution.
- On a generic type, `typeof(Box<>)` tags the member of every `Box<T>`, while `typeof(Box<int>)` only tags the member of
  `Box<int>`.
- `Key` and `Value` are not supported on an assembly attribute.

## Inherited tags

You do not need to repeat a tag on an override or an interface implementation. Tags are inherited from:

- overridden members,
- implemented interface members, including static abstract members, and members that a base type implements for a
  derived type,
- the parameters of a record primary constructor, which tag the properties the record generates.

<!-- ref:../../samples/Meziantou.Framework.TaggedValues.Samples/Tagging/Inheritance.cs;region=TaggedValues_Inheritance;format=md-fenced-code-block;dedent=true -->
```csharp
public interface IParcelStore
{
    void Delete([ValueTag("ParcelId")] Guid id);
}

public sealed class ParcelStore : IParcelStore
{
    public void Delete(Guid id) { } // id is a ParcelId: the tag comes from the interface
}

public sealed record Parcel([ValueTag("ParcelId")] Guid Id, [ValueTag("ShipmentId")] Guid ShipmentId);

public static class ParcelCleanup
{
    public static void M(ParcelStore store, Parcel parcel)
    {
        store.Delete(parcel.Id);         // ok, the property Id is a ParcelId
        store.Delete(parcel.ShipmentId); // MFTV0002
    }
}
```
<!-- endref -->

An override or an implementation that declares other tags than its base member is reported
([MFTV0004](09-Rules/05-MFTV0004.md)).

## Drop a tag on purpose

An explicit cast through `object` drops the tag: `(Guid)(object)value` is not tagged. Use it for the rare places where
a value legitimately changes meaning.
