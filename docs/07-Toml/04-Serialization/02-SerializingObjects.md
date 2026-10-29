---
title: Serializing objects
description: The types the serializer reads and writes, how collections, dictionaries, dates and times map to TOML, and the input and output types.
---

## Objects

A class, a record or a struct is written as a table, with a key for each public property. To read it, the serializer
sets the properties that have a setter (`set` or `init`), or passes the values to a constructor whose parameters match
the keys by name:

<!-- ref:../../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlSerializer_SerializeAndDeserialize;format=md-fenced-code-block;dedent=true -->
```csharp
var toml = TomlSerializer.Serialize(new Person("Ada", 37));
Assert.Equal("""
    Name = "Ada"
    Age = 37

    """, toml);

var person = TomlSerializer.Deserialize<Person>(toml);
Assert.Equal(new Person("Ada", 37), person);
```
<!-- endref -->

Public fields are skipped unless you set `IncludeFields` or mark them with `[TomlInclude]`. See
[Mapping members](04-MappingMembers.md) to rename, ignore or require members and to choose the constructor.

A TOML document is a table, so the value you serialize must be an object or a dictionary. To write a single value or a
list at the root, set `RootValueHandling = TomlRootValueHandling.WrapInRootKey`: the value is written under the
`RootValueKeyName` key (`value` by default).

## Supported types

| Category | Types |
| --- | --- |
| Boolean | `bool` |
| Numbers | `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `nint`, `nuint`, `float`, `double`, `decimal`, `Half`, `Int128`, `UInt128` |
| Text | `char`, `string` |
| Dates and times | `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TomlDateTime` |
| Other | `Guid`, `TimeSpan`, `Uri`, `Version`, enums |
| Collections | `T[]`, `List<T>`, `IList<T>`, `IReadOnlyList<T>`, `HashSet<T>`, `SortedSet<T>`, `ISet<T>`, `IReadOnlySet<T>`, `ImmutableArray<T>`, `ImmutableList<T>`, `ImmutableHashSet<T>` |
| Dictionaries | `Dictionary<string, T>`, `IDictionary<string, T>`, `IReadOnlyDictionary<string, T>`, `SortedDictionary<string, T>`; `ImmutableDictionary<string, T>` is written but cannot be read |
| Document model | `TomlTable`, `TomlArray`, `TomlTableArray`, `TomlObject`, `object` |
| Objects | Classes, records and structs |

Nullable value types, such as `int?`, are supported. For any other type, such as `Queue<T>` or `ConcurrentBag<T>`,
write a [converter](05-Converters.md): the serializer rejects it with a `TomlException` (or the `MFTOML003` error with
the source generator).

Enums are written as numbers. To write their names, use `TomlStringEnumConverter`; see
[Enums as strings](05-Converters.md#enums-as-strings).

### Numbers

TOML integers are signed 64-bit values. A `ulong`, `nuint`, `Int128` or `UInt128` value larger than `long.MaxValue`
throws a `TomlException` when it is written. Floating-point values are written so that they read back to the same value.

### No null

TOML has no null value. By default, a `null` member is not written (`DefaultIgnoreCondition` is `WhenWritingNull`), and
a missing key leaves the member to its initial value. A `null` that must be written throws a `TomlException`. See
[Options](03-Options.md#choose-what-is-written).

## Collections and arrays of tables

A collection of values is written as an array. A collection of objects is written as an array of tables, with a
`[[name]]` header for each element:

<!-- ref:../../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlSerializer_ArrayOfTables;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class Config
{
    public Package[] Package { get; set; } = [];
}

public sealed class Package
{
    public string Name { get; set; } = "";
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlSerializer_WriteArrayOfTables;format=md-fenced-code-block;dedent=true -->
```csharp
var toml = TomlSerializer.Serialize(new Config { Package = [new Package { Name = "core" }, new Package { Name = "tools" }] });

Assert.Equal("""
    [[Package]]
    Name = "core"

    [[Package]]
    Name = "tools"

    """, toml);
```
<!-- endref -->

An empty collection of objects is written as an empty array (`Package = []`): TOML has no header for an empty array of
tables. To write arrays of tables as inline arrays (`Package = [{Name = "core"}]`), set `TableArrayStyle`; see
[Options](03-Options.md#layout-of-the-output).

A collection member requires an array when it is read. To accept a single value too, see
[Single value or array](04-MappingMembers.md#accept-a-single-value-or-an-array).

## Dictionaries

A dictionary is written as a table: its keys are the keys of the table, so they must be strings. A dictionary with
another key type is rejected (`MFTOML004` with the source generator). `DictionaryKeyPolicy` converts the keys when they
are written.

A member typed as `TomlTable`, `TomlObject` or `object` receives the values without conversion, as described in
[Document model](../07-DocumentModel.md).

## Dates and times

TOML has four kinds of date and time: offset date-times, local date-times, local dates and local times. The .NET types
map to them:

| .NET type | TOML value |
| --- | --- |
| `DateTimeOffset` | Offset date-time: `2026-10-01T08:00:00+02:00` |
| `DateTime` with `DateTimeKind.Utc` | Offset date-time with `Z`: `2026-10-01T08:00:00Z` |
| `DateTime` with `DateTimeKind.Local` | Offset date-time with the offset of the machine |
| `DateTime` with `DateTimeKind.Unspecified` | Local date-time: `2026-10-01T08:00:00` |
| `DateOnly` | Local date: `2026-10-01` |
| `TimeOnly` | Local time: `08:00:00` |
| `TomlDateTime` | Any kind, with the precision of the seconds |

<!-- ref:../../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlSerializer_Meeting;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed record Meeting(DateOnly Day, TimeOnly Start, DateTime CreatedAt);
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlSerializer_DatesAndTimes;format=md-fenced-code-block;dedent=true -->
```csharp
var meeting = new Meeting(new DateOnly(2026, 10, 29), new TimeOnly(9, 30), new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc));

var toml = TomlSerializer.Serialize(meeting);
Assert.Equal("""
    Day = 2026-10-29
    Start = 09:30:00
    CreatedAt = 2026-10-01T08:00:00Z

    """, toml);

Assert.Equal(meeting, TomlSerializer.Deserialize<Meeting>(toml));
```
<!-- endref -->

A `DateTime` is read back with the kind it was written with, like `System.Text.Json`. An offset other than `Z` is
converted to local time.

A `TimeSpan` is written as a local time, such as `07:30:00`: it is a time of day, not a duration. A negative value, or a
value of 24 hours or more, throws a `TomlException` when it is written. Store a duration in an integer or a string
member, or write a [converter](05-Converters.md).

## Strings, streams, readers and writers

`Serialize` returns a string or writes to a `TextWriter`; `Deserialize` reads a string, a `TextReader` or a UTF-8
`Stream`:

<!-- ref:../../../samples/Meziantou.Framework.Toml.Samples/ReadmeSnippets.cs;region=TomlSerializer_Files;format=md-fenced-code-block;dedent=true -->
```csharp
using var stream = File.OpenRead("config.toml");
var config = TomlSerializer.Deserialize<ServerConfig>(stream);

using var writer = new StreamWriter("output.toml");
TomlSerializer.Serialize(writer, config);
```
<!-- endref -->

`DeserializeAsync` and `SerializeAsync` read and write a `Stream` asynchronously, for example the body of an ASP.NET Core
request or response, which rejects synchronous I/O:

<!-- ref:../../../samples/Meziantou.Framework.Toml.Samples/ReadmeSnippets.cs;region=TomlSerializer_Async;format=md-fenced-code-block;dedent=true -->
```csharp
var config = await TomlSerializer.DeserializeAsync<ServerConfig>(request.Body, cancellationToken: cancellationToken);
await TomlSerializer.SerializeAsync(response.Body, config, cancellationToken: cancellationToken);
```
<!-- endref -->

The asynchronous methods only make the I/O asynchronous: the whole document is still parsed and formatted in memory. Set
`MaxInputLength` to reject documents that are too long before they are loaded; see
[Handling errors](07-ErrorHandling.md#read-untrusted-input).
