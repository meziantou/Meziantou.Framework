---
title: Equatable collections
description: Immutable arrays, sets and dictionaries that are equal when their content is equal, for records and caches.
---

The immutable collections of .NET compare by reference: two `ImmutableArray<T>` with the same items are not equal. A
record that contains one loses its value equality, which breaks caches keyed by the record, and the incremental
pipelines of source generators.

[ImmutableEquatableArray\<T\>](cref:Meziantou.Framework.Collections.ImmutableEquatableArray{T}), [ImmutableEquatableSet\<T\>](cref:Meziantou.Framework.Collections.ImmutableEquatableSet{T}) and [ImmutableEquatableDictionary\<TKey, TValue\>](cref:Meziantou.Framework.Collections.ImmutableEquatableDictionary{TKey,TValue}) are immutable
collections that are equal when their items are equal:

<!-- ref:../../../tests/Meziantou.Framework.Tests/Collections/ImmutableEquatableArrayTests.cs;region=ImmutableEquatableArray_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var first = new[] { "a", "b" }.ToImmutableEquatableArray();
var second = new List<string> { "a", "b" }.ToImmutableEquatableArray();

// Two arrays with the same items are equal and have the same hash code
Assert.Equal(first, second);
Assert.True(first == second);
Assert.Equal(first.GetHashCode(), second.GetHashCode());

// A record that contains the array gets value equality too
Assert.Equal(new Model("name", first), new Model("name", second));
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Tests/Collections/ImmutableEquatableArrayTests.cs;region=ImmutableEquatableArray_Record;format=md-fenced-code-block;dedent=true -->
```csharp
private sealed record Model(string Name, ImmutableEquatableArray<string> Items);
```
<!-- endref -->

## Create them

| Collection | Created with |
| --- | --- |
| [ImmutableEquatableArray\<T\>](cref:Meziantou.Framework.Collections.ImmutableEquatableArray{T}) | [ToImmutableEquatableArray()](cref:Meziantou.Framework.Collections.ImmutableEquatableArray.ToImmutableEquatableArray), [ImmutableEquatableArray.Create](cref:Meziantou.Framework.Collections.ImmutableEquatableArray.Create), [ImmutableEquatableArray\<T\>.Empty](cref:Meziantou.Framework.Collections.ImmutableEquatableArray{T}.Empty) |
| [ImmutableEquatableSet\<T\>](cref:Meziantou.Framework.Collections.ImmutableEquatableSet{T}) | [ToImmutableEquatableSet()](cref:Meziantou.Framework.Collections.ImmutableEquatableSet.ToImmutableEquatableSet), [ImmutableEquatableSet.Create](cref:Meziantou.Framework.Collections.ImmutableEquatableSet.Create), [ImmutableEquatableSet\<T\>.Empty](cref:Meziantou.Framework.Collections.ImmutableEquatableSet{T}.Empty) |
| [ImmutableEquatableDictionary\<TKey, TValue\>](cref:Meziantou.Framework.Collections.ImmutableEquatableDictionary{TKey,TValue}) | [ToImmutableEquatableDictionary()](cref:Meziantou.Framework.Collections.ImmutableEquatableDictionary.ToImmutableEquatableDictionary) from pairs, tuples or with key and value selectors, [ImmutableEquatableDictionary.Empty\<TKey, TValue\>()](cref:Meziantou.Framework.Collections.ImmutableEquatableDictionary.Empty) |

The items, and the keys and values of the dictionary, must implement `IEquatable<T>`.
Creating a collection from an empty collection returns the shared `Empty` instance.

## Equality

- The array is equal to another array with the same items in the same order.
- The set and the dictionary are equal to another set or dictionary with the same items, whatever their order.
- A `null` value of the dictionary is equal to another `null` value.
- `==`, `!=`, `Equals` and `GetHashCode` all follow these rules.

They implement the read-only collection interfaces (`IReadOnlyList<T>`, `ISet<T>`, `IReadOnlyDictionary<TKey, TValue>`
and the others). The methods of the mutable interfaces throw an `InvalidOperationException`.
