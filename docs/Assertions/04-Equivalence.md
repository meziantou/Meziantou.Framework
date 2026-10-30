---
title: Equivalence
description: Compare two objects member by member, whatever their types, including collections, dictionaries and JSON.
---

`Equivalent` compares two objects by walking their public properties and fields, recursively. The two objects do not
need to have the same type: an anonymous object can describe the expected state of an entity, a DTO or a record.

<!-- ref:../../tests/Meziantou.Framework.Assertions.Tests/AssertEqualByStructureTests.cs;region=Assert_Equivalent;format=md-fenced-code-block;dedent=true -->
```csharp
var actual = new { Name = "Alice", Age = 42, Roles = new List<string> { "admin", "user" } };

// Another type with the same public members: values are compared member by member
var expected = new { Name = "Alice", Age = 42L, Roles = new[] { "admin", "user" } };
Assert.Equivalent(expected, actual);
```
<!-- endref -->

## How values are compared

- **Objects** are compared member by member, by name. Each member of `expected` must exist in `actual`, and each member
  of `actual` must exist in `expected`: a missing or an extra member fails the assertion. Private members are ignored.
- **Collections** are compared item by item, in order. **Dictionaries** are compared by key, and **sets** regardless of
  order. Multidimensional arrays are compared by their dimensions, then their items.
- **Scalars** are compared by value: strings, numbers (`42` is equivalent to `42L`), dates, `Uri`, `Version`,
  `IPAddress`, `CultureInfo`, and reflection objects such as `Type`. Other .NET types that define their own
  equality, such as `Encoding` or `XName`, are compared with it.
- Some .NET types are compared by content: a `StringBuilder` by its text, a `FileSystemInfo` by its full name, a
  `Regex` by its pattern and options, and an `XNode` with `XNode.DeepEquals`.
- **JSON**: `JsonElement` and `JsonNode` are compared by content: objects as dictionaries of their properties, arrays as
  collections, strings as strings.
- Your own types are always walked member by member, even when they override `Equals`: a record or an entity whose
  equality only compares an identifier is still compared on all its members.
- A property whose getter throws is compared by the type of the exception. Object graphs with cycles are supported.

<!-- ref:../../tests/Meziantou.Framework.Assertions.Tests/AssertEqualByStructureTests.cs;region=Assert_EquivalentJson;format=md-fenced-code-block;dedent=true -->
```csharp
var actual = JsonNode.Parse("""{ "name": "Alice", "roles": ["admin"] }""");

// JSON is compared by content: the order of the properties does not matter
Assert.Equivalent(JsonNode.Parse("""{ "roles": ["admin"], "name": "Alice" }"""), actual);
```
<!-- endref -->

## Read the difference

The failure message gives the path of the first difference, from the root `$`, and the reason:

<!-- ref:../../tests/Meziantou.Framework.Assertions.Tests/AssertEqualByStructureTests.cs;region=Assert_EquivalentMissingMember;format=md-fenced-code-block;dedent=true -->
```csharp
var expected = new { Name = "Alice" };
var actual = new { Name = "Alice", Age = 42 };

// Every member must have a counterpart: an extra member in actual fails the assertion
var exception = Assert.Throws<AssertionException>(() => Assert.Equivalent(expected, actual));
Assert.Equal("""
    Assert.Equivalent() assertion failed.
    Expected expression: expected
    Actual expression:   actual
    Path: $.Age
    Reason: Actual member is unexpected.
    Expected: <missing>
    Actual:   42
    """, exception.Message);
```
<!-- endref -->

## Options

`EquivalentOptions` relaxes the comparison:

<!-- ref:../../tests/Meziantou.Framework.Assertions.Tests/AssertEqualByStructureTests.cs;region=Assert_EquivalentOptions;format=md-fenced-code-block;dedent=true -->
```csharp
var expected = new { Name = "alice", Roles = new[] { "admin", "user" } };
var actual = new { name = "ALICE", roles = new[] { "user", "admin" } };

Assert.NotEquivalent(expected, actual);
Assert.Equivalent(expected, actual, new EquivalentOptions
{
    IgnoreCollectionOrder = true,
    IgnoreMemberNameCase = true,
    IgnoreStringCase = true,
});
```
<!-- endref -->

| Option | Effect |
| --- | --- |
| `IgnoreCollectionOrder` | Collections, including JSON arrays, match regardless of the order of their items |
| `IgnoreMemberNameCase` | Members whose names differ only by case are paired. Members with the same name are paired first |
| `IgnoreStringCase` | Strings are compared ignoring case, including dictionary keys, JSON strings and property names, and the content of a `StringBuilder` |

`NotEquivalent` takes the same options, and fails exactly when `Equivalent` would pass.

## Equivalent or Equal

Use `Equal` when the type defines the equality you want to test, such as a value object, a string or a collection of
numbers. Use `Equivalent` to check the state of an object whose type has no equality, or when the expected value is
easier to write as another type, such as an anonymous object. To validate a large object graph and keep the expected
state in a file, consider [snapshot testing](../SnapshotTesting/01-Introduction.md).
