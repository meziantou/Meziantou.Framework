---
title: Supported types
description: The scalar types, collections and dictionaries the serializer handles, untyped values, and how objects are created.
---

## Built-in types

| Category | Types |
| --- | --- |
| Text | `string`, `char`, `Rune` |
| Boolean | `bool` |
| Integers | `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `nint`, `nuint`, `Int128`, `UInt128`, `BigInteger` |
| Floating point | `float`, `double`, `decimal`, `Half`; on .NET 11, `BFloat16`, `Decimal32`, `Decimal64`, `Decimal128` |
| Date and time | `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan` |
| Other scalars | `Guid`, `Uri`, `CultureInfo`, `Version`, enums |
| Collections | arrays, `List<>`, `HashSet<>`, `ImmutableArray<>`, `ImmutableList<>`, `ImmutableHashSet<>`, `FrozenSet<>`, `Queue<>`, `Stack<>`, `ConcurrentQueue<>`, `ConcurrentStack<>`, `ConcurrentBag<>`, `ArraySegment<>`, and the interfaces `IEnumerable<>`, `ICollection<>`, `IList<>`, `IReadOnlyCollection<>`, `IReadOnlyList<>`, `ISet<>`, `IReadOnlySet<>` |
| Dictionaries | `Dictionary<,>`, `OrderedDictionary<,>`, `FrozenDictionary<,>`, `IDictionary<,>`, `IReadOnlyDictionary<,>` |
| Pairs | `KeyValuePair<,>`, written as a mapping with a `Key` and a `Value` entry |
| Untyped | `object`, `object[]`, `List<object>`, `Dictionary<string, object>` |
| Documents | any node of the [document object model](../05-DocumentObjectModel.md) (`YamlMapping`, `YamlSequence`, `YamlValue`, …) |
| Unions | [C# unions](09-Unions.md) |

Every value type of the table also works as `Nullable<T>`. Any other type is written as a mapping of its members, as
described in [Objects](#objects).

A few details of the scalar types:

- Enums are written with the name of the member and read ignoring the case, or from their numeric value. To change the
  name of a member, see [Enum values](04-MembersAndNames.md#enum-values).
- `double`, `float` and `Half` are always written so that they are read back as floating-point numbers: a whole number
  keeps a fraction (`3.0`), and the special values are `.inf`, `-.inf` and `.nan`. `decimal` keeps the exact text of its
  value.
- `Uri` is written with its original string and read as a relative or absolute URI. `CultureInfo` is written with its
  name. `Version` and `BigInteger` are written with their text, and `Rune` as a one-character scalar.
- A string that would be read back as another type, such as `true` or `123`, is quoted: see
  [Quoting](05-OutputFormat.md#strings-that-look-like-other-values).

## Collections and dictionaries

Besides the types of the table, a type that implements a single `IDictionary<TKey, TValue>` and has a public
parameterless constructor, such as `SortedDictionary<,>` or `ConcurrentDictionary<,>`, is handled as a dictionary. A
type that implements a single `ICollection<T>` and has a public parameterless constructor is handled as a collection.

A stack is written from its top and read back in the same order.

A dictionary key is written as a scalar, so its type must be `string`, `object`, an enum, or one of the scalar types of
the table. Another key type throws a `NotSupportedException`, unless a [custom converter](07-CustomConverters.md)
handles it. A custom converter registered for a supported key type converts the values of the dictionary, not its
keys.

The other collections of .NET, such as `ArrayList`, `Hashtable` or `ReadOnlyCollection<>`, and the types that cannot
be serialized, such as `Type`, delegates and `IAsyncEnumerable<>`, throw a `NotSupportedException` instead of being
written as a mapping of their properties that could not be read back. The source generator reports them at build
time.

## Untyped values

Reading into `object` creates `null`, `bool`, `long` (or `ulong` for larger values), `double`, `string`,
`List<object?>` for a sequence, and `Dictionary<object, object?>` for a mapping:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerApiTests.cs;region=YamlSerializer_Untyped;format=md-fenced-code-block;dedent=true -->
```csharp
var value = YamlSerializer.Deserialize<object>("""
    name: api
    replicas: 3
    ports: [80, 443]
    """);

var root = Assert.IsType<Dictionary<object, object?>>(value);
Assert.Equal("api", root["name"]);
Assert.Equal(3L, root["replicas"]);
var ports = Assert.IsType<List<object?>>(root["ports"]);
Assert.Equal(443L, ports[1]);
```
<!-- endref -->

The keys of the dictionary are resolved like the values: `1` is a `long` and `'1'` is a `string`. A null key, or a key
that is a sequence or a mapping, throws a `YamlException`. When the document has no fixed shape, the
[document object model](../05-DocumentObjectModel.md) is often easier to use than untyped values.

## Objects

Any other type is written as a mapping of its public properties. The serializer creates the object with:

- a public parameterless constructor, then sets the properties, including `init` ones;
- or a constructor with parameters, such as the primary constructor of a record. Each parameter receives the value of
  the member of the same name.

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerApiTests.cs;region=YamlSerializer_Record;format=md-fenced-code-block;dedent=true -->
```csharp
internal sealed record Endpoint(string Url, int Timeout = 30);
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerApiTests.cs;region=YamlSerializer_RecordDeserialize;format=md-fenced-code-block;dedent=true -->
```csharp
var endpoint = YamlSerializer.Deserialize<Endpoint>("Url: https://example.com");
Assert.Equal(30, endpoint!.Timeout);

Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Endpoint>("Timeout: 10")); // Url is missing
```
<!-- endref -->

A parameter without a default value is required: a missing key throws a `YamlException`. Set
`RespectRequiredConstructorParameters` to `false` to accept it. When a type declares several public constructors, mark
the one to use with `[YamlConstructor]`, which also selects a non-public constructor. A struct is created without
calling a constructor, unless one has `[YamlConstructor]`.

Properties declared with the `required` keyword, and members marked with `[YamlRequired]`, must also be present in the
YAML.

A null scalar, such as `~` or `null`, sets `null` on a member whose type accepts it. A non-nullable value type, such as
`int`, rejects it with a `YamlException` instead of using the default value. A reference type declared non-nullable,
such as `string` in a nullable-enabled project, rejects it too, unless you set `RespectNullableAnnotations` to `false`.

Members are written from the base type down to the type itself, and within a type the properties before the fields,
in declaration order. To change the order, the names, or the members that are written, see
[Members and names](04-MembersAndNames.md).
