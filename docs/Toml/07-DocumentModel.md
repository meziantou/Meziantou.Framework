---
title: Document model
description: Read a TOML document without declaring a type, build one by code, and edit a file while keeping its comments.
---

The document model represents a TOML document with tables and arrays, without a type of your own. Use it to read a
document whose shape you don't know, to build one by code, or to change a few values of a file. Its types are in the
[Meziantou.Framework.Toml.Model](cref:Meziantou.Framework.Toml.Model) namespace.

## Read a document

Deserialize to `TomlTable`, then read the values by key:

<!-- ref:../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlTable_Read;format=md-fenced-code-block;dedent=true -->
```csharp
var table = TomlSerializer.Deserialize<TomlTable>("""
    title = "My App"

    [database]
    host = "localhost"
    ports = [8000, 8001]
    """)!;

var title = (string)table["title"];
var database = (TomlTable)table["database"];
var firstPort = (long)((TomlArray)database["ports"])[0]!;

Assert.Equal("My App", title);
Assert.Equal(8000L, firstPort);
```
<!-- endref -->

| TOML | .NET |
| --- | --- |
| Table, inline table | `TomlTable`, which implements `IDictionary<string, object>` |
| Array | `TomlArray`, which implements `IList<object?>` |
| Array of tables | `TomlTableArray`, a list of `TomlTable` |
| String | `string` |
| Integer | `long` |
| Float | `double` |
| Boolean | `bool` |
| Date, time, date-time | `TomlDateTime` |

`TomlTable`, `TomlArray` and `TomlTableArray` derive from `TomlObject`. A member of your own types typed as one of them,
or as `object`, receives this model; see [Serializing objects](04-Serialization/02-SerializingObjects.md#dictionaries).

## Build a document

Create the tables and the arrays, then serialize the root table:

<!-- ref:../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlTable_Build;format=md-fenced-code-block;dedent=true -->
```csharp
var document = new TomlTable
{
    ["title"] = "My App",
    ["owner"] = new TomlTable(inline: true) { ["name"] = "Ada" },
    ["database"] = new TomlTable
    {
        ["ports"] = new TomlArray { 8000L, 8001L },
    },
    ["servers"] = new TomlTableArray
    {
        new TomlTable { ["name"] = "alpha" },
        new TomlTable { ["name"] = "beta" },
    },
};

var toml = TomlSerializer.Serialize(document);
Assert.Equal("""
    title = "My App"
    owner = {name = "Ada"}
    [database]
    ports = [8000, 8001]
    [[servers]]
    name = "alpha"

    [[servers]]
    name = "beta"

    """, toml);
```
<!-- endref -->

- `new TomlTable(inline: true)` is written as an inline table (`{name = "Ada"}`); other tables get a `[header]`.
- The keys of a `TomlTable` are written as they are: a key that contains a dot is quoted, never expanded into nested
  tables.

## Dates and times

`TomlDateTime` keeps what the document says: the kind of value (`OffsetDateTimeByZ`, `OffsetDateTimeByNumber`,
`LocalDateTime`, `LocalDate` or `LocalTime`) and the number of digits of the fractional seconds:

<!-- ref:../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlDateTime_Kind;format=md-fenced-code-block;dedent=true -->
```csharp
var table = TomlSerializer.Deserialize<TomlTable>("""
    released = 1979-05-27
    updated = 1979-05-27T07:32:00.50-07:00
    """)!;

var released = (TomlDateTime)table["released"];
Assert.Equal(TomlDateTimeKind.LocalDate, released.Kind);
Assert.Equal("1979-05-27", released.ToString());

var updated = (TomlDateTime)table["updated"];
Assert.Equal(TomlDateTimeKind.OffsetDateTimeByNumber, updated.Kind);
Assert.Equal(TimeSpan.FromHours(-7), updated.DateTime.Offset);
Assert.Equal("1979-05-27T07:32:00.50-07:00", updated.ToString()); // The precision of the seconds is kept
```
<!-- endref -->

`DateTime` returns the value as a `DateTimeOffset`. A local value has no offset: two `TomlDateTime` values are equal when
they have the same kind and the same date, time or instant, whatever their precision.

## Edit a file and keep its comments

Reading a document into the model drops its comments and its layout. To keep them, read and write with the same
`TomlMetadataStore`: it records the comments and the style of each value, and the writer puts them back:

<!-- ref:../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlTable_EditAndKeepComments;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new TomlSerializerOptions { MetadataStore = new TomlMetadataStore() };
var document = TomlSerializer.Deserialize<TomlTable>("""
    # Server settings
    [server]
    host = "localhost" # the public name
    port = 8080
    """, options)!;

var server = (TomlTable)document["server"];
server["port"] = 443L;

var toml = TomlSerializer.Serialize(document, options);
Assert.Equal("""
    # Server settings
    [server]
    host = "localhost" # the public name
    port = 443

    """, toml);
```
<!-- endref -->

The store also works with your own types. When the output must keep every character of the input, including spacing,
work on the [syntax tree](08-SyntaxTreeAndParser.md) instead.

## Comments and source locations

The store keeps the comments, the source location and the way each value was written. Read them with
`TryGetProperties`:

<!-- ref:../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlMetadataStore_Read;format=md-fenced-code-block;dedent=true -->
```csharp
var toml = """
    # The name of the application
    title = 'My App' # shown in the title bar
    """;

var store = new TomlMetadataStore();
var options = new TomlSerializerOptions { MetadataStore = store };
var model = TomlSerializer.Deserialize<TomlTable>(toml, options)!;

Assert.True(store.TryGetProperties(model, out var metadata));
Assert.True(metadata.TryGetProperty("title", out var property));
Assert.Equal("# The name of the application", property.LeadingTrivia![0].Text);
Assert.Equal("# shown in the title bar", property.TrailingTrivia![1].Text);
Assert.Equal(TomlPropertyDisplayKind.StringLiteral, property.DisplayKind);
```
<!-- endref -->

`DisplayKind` says how the value was written, for example a literal string or a hexadecimal integer: the writer uses it
to write the value the same way.
