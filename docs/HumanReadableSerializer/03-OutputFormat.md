---
title: Output format
description: How the serializer writes objects, collections, dictionaries, null values, strings, numbers, dates and other values.
---

## Values

<!-- ref:../../tests/Meziantou.Framework.HumanReadableSerializer.Tests/HumanReadableSerializerOptionsTests.cs;region=HumanReadableSerializer_Values;format=md-fenced-code-block;dedent=true -->
```csharp
var value = new
{
    Missing = (string?)null,
    Empty = Array.Empty<int>(),
    Matrix = new[] { new[] { 1, 2 }, new[] { 3, 4 } },
    Scores = new Dictionary<string, int>(StringComparer.Ordinal) { ["Ada"] = 12, ["Alan"] = 9 },
    Day = DayOfWeek.Friday,
    Date = new DateTimeOffset(2024, 6, 15, 12, 30, 0, TimeSpan.FromHours(2)),
    Duration = TimeSpan.FromMinutes(90),
    Price = 12.5m,
    Id = new Guid("1c7a7a4e-0f43-4a3f-9d5b-6a7cb4b1f2d3"),
    Website = new Uri("https://www.meziantou.net/"),
};

Assert.Equal("""
    Missing: <null>
    Empty: []
    Matrix:
      - - 1
        - 2
      - - 3
        - 4
    Scores:
      Ada: 12
      Alan: 9
    Day: Friday
    Date: 2024-06-15T12:30:00+02:00
    Duration: 01:30:00
    Price: 12.5
    Id: 1c7a7a4e-0f43-4a3f-9d5b-6a7cb4b1f2d3
    Website: https://www.meziantou.net/
    """, HumanReadableSerializer.Serialize(value));
```
<!-- endref -->

| Value | Output |
| --- | --- |
| `null` | `<null>` |
| Object | One `Name: value` line per member, indented below its parent. An object without members is `{}` |
| Collection | One `- item` line per item. An empty collection is `[]` |
| Dictionary | One `key: value` line per entry |
| Multi-line string | Below its name, one line per line, indented |
| Number | Invariant culture: `12.5`, never `12,5` |
| `DateTime`, `DateTimeOffset` | ISO 8601 (`2024-06-15T12:30:00+02:00`), with the fractional seconds when there are any |
| Enum | Name of the value, or the names of the flags separated by `, `. An undefined value is written as a number |
| `byte[]` | Base64 |
| `Type` | Full name of the type, followed by the name of its assembly |

Many other types have their own converter, among them `Guid`, `Uri`, `Version`, `TimeSpan`, `DateOnly`, `TimeOnly`,
`IPAddress`, `CultureInfo`, `Regex`, `StringBuilder`, `BigInteger`, `Int128`, `Half`, `Complex`, `BitArray`,
`Memory<T>`, value tuples, multi-dimensional arrays, `IAsyncEnumerable<T>`, LINQ groupings, expression trees,
reflection objects, F# options and discriminated unions, `JsonNode`, `JsonElement`, `XmlNode` and `XElement`.

A type that has a `TypeConverter` able to convert it to a string is written as that string, and a type that implements
`IConvertible` is written with `ToString(CultureInfo.InvariantCulture)`. Other types are written as objects, with their
public properties.

## Dictionaries and sets

The keys of a `Dictionary<TKey, TValue>` are written in enumeration order. The keys of the hash-based collections that
have no meaningful order, `Hashtable`, `HybridDictionary` and `StringDictionary`, are always sorted, so the output does
not change from one process to the next. Set [DictionaryKeyOrder](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions.DictionaryKeyOrder) to sort the keys of every dictionary; see
[Members](04-Members.md#order-members-and-dictionary-keys).

## Invisible characters and line endings

<!-- ref:../../tests/Meziantou.Framework.HumanReadableSerializer.Tests/HumanReadableSerializerOptionsTests.cs;region=HumanReadableSerializer_TextOptions;format=md-fenced-code-block;dedent=true -->
```csharp
var value = new { Name = "Ada ", Notes = "line1\tend\nline2" };

var options = new HumanReadableSerializerOptions
{
    ShowInvisibleCharactersInValues = true,
};

Assert.Equal("""
    Name: Ada␠
    Notes:
      line1␉end␊
      line2
    """, HumanReadableSerializer.Serialize(value, options));

// Windows line endings
var windowsOptions = new HumanReadableSerializerOptions { NewLine = "\r\n" };
Assert.Equal("Name: Ada \r\nNotes:\r\n  line1\tend\r\n  line2", HumanReadableSerializer.Serialize(value, windowsOptions));
```
<!-- endref -->

- [ShowInvisibleCharactersInValues](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions.ShowInvisibleCharactersInValues) writes the control characters as their Unicode control pictures (`␉` for a tab,
  `␊` for a line feed), and a space as `␠` at the start or the end of a line. The other invisible characters, such as a
  no-break space, a zero-width character, a soft hyphen or a bidirectional mark, are written as their code point, for
  instance `<U+00A0>`. Use it when two snapshots differ only by whitespace.
- [NewLine](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions.NewLine) is `"\n"` by default on every platform. Set it to `"\r\n"` for Windows line endings; line breaks inside
  values are rewritten too. Other values throw an `ArgumentException`.

## Depth and cycles

[MaxDepth](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions.MaxDepth) (64 by default) limits how deep objects and collections can be nested. A deeper value, or an object graph
with a cycle, throws a [HumanReadableSerializerException](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerException) instead of running out of stack.
