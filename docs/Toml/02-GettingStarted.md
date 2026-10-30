---
title: Getting started
description: Install the package, write an object as TOML, read it back, and read a configuration file.
---

## Install the package

Add the `Meziantou.Framework.Toml` package to a project:

```bash
dotnet add package Meziantou.Framework.Toml
```

The package contains the source generator too: there is no other package to add. The examples of this documentation
omit the `using` directives; see [Namespaces](01-Introduction.md#namespaces) for where each type lives.

## Write and read an object

Declare the type that holds your data. A record, a class with properties, or a struct works:

<!-- ref:../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlSerializer_Person;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed record Person(string Name, int Age);
```
<!-- endref -->

`TomlSerializer.Serialize` writes it as TOML, and `TomlSerializer.Deserialize` reads it back:

<!-- ref:../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlSerializer_SerializeAndDeserialize;format=md-fenced-code-block;dedent=true -->
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

- Each property is a key of the document. The keys keep the C# names (`Name`, `Age`) unless you set a naming policy;
  see [Options](04-Serialization/03-Options.md).
- The record has no parameterless constructor: the values are passed to its constructor, matched by name.

## Read a configuration file

`Deserialize` and `Serialize` also take a `Stream` (UTF-8), a `TextReader` or a `TextWriter`:

<!-- ref:../../samples/Meziantou.Framework.Toml.Samples/ReadmeSnippets.cs;region=TomlSerializer_Files;format=md-fenced-code-block;dedent=true -->
```csharp
using var stream = File.OpenRead("config.toml");
var config = TomlSerializer.Deserialize<ServerConfig>(stream);

using var writer = new StreamWriter("output.toml");
TomlSerializer.Serialize(writer, config);
```
<!-- endref -->

Nested objects become tables, and collections of objects become arrays of tables. See
[Serializing objects](04-Serialization/02-SerializingObjects.md) for the types you can use.

## Handle invalid input

A document that is not valid TOML, or does not match your type, throws a `TomlException` that gives the line and the
column of the error. When invalid input is expected, for example a file written by a user, `TryDeserialize` returns
`false` instead:

<!-- ref:../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlSerializer_TryDeserialize;format=md-fenced-code-block;dedent=true -->
```csharp
var toml = "Port = \"not a number\"";

var success = TomlSerializer.TryDeserialize<ServerConfig>(toml, out var config);
Assert.False(success); // Invalid input
Assert.Null(config);
```
<!-- endref -->

See [Handling errors](04-Serialization/07-ErrorHandling.md).

## Next steps

- [Serialization](04-Serialization/01-Overview.md): map your types, configure the output, write converters.
- [Source generation](05-SourceGeneration.md): generate the serialization code for NativeAOT and trimming.
- [Document model](07-DocumentModel.md): read a document without declaring a type.
