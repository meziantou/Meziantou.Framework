---
title: Handling errors
description: Find where a document is invalid with TomlException, use TryDeserialize for expected errors, and set limits for untrusted input.
---

## Find the error in the document

Invalid TOML, and a document that does not match your types, throw a [TomlException](cref:Meziantou.Framework.Toml.TomlException). It gives the location of the
first error: `SourceName`, [Line](cref:Meziantou.Framework.Toml.TomlException.Line) and [Column](cref:Meziantou.Framework.Toml.TomlException.Column) (1-based), [Offset](cref:Meziantou.Framework.Toml.TomlException.Offset) (0-based) and `Span`. Set `SourceName` in the
options to put the file name in the messages:

<!-- ref:../../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlException_Location;format=md-fenced-code-block;dedent=true -->
```csharp
var toml = """
    Host = "example.com"
    Port = "https"
    """;

var options = new TomlSerializerOptions { SourceName = "server.toml" };
var exception = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<ServerConfig>(toml, options));
Assert.Equal("server.toml", exception.SourceName);
Assert.Equal(2, exception.Line);
Assert.Equal(8, exception.Column);
```
<!-- endref -->

[Diagnostics](cref:Meziantou.Framework.Toml.TomlException.Diagnostics) lists every error. Reading does not stop at the first mapping error: a value that cannot be converted, an
invalid collection element or a missing required key is recorded, and the rest of the document is read, so a user can
fix all the errors of a file at once. Reading stops after 1,000 errors.

## Expect invalid input

[TryDeserialize](cref:Meziantou.Framework.Toml.TomlSerializer.TryDeserialize) returns `false` instead of throwing when the input is not valid TOML or does not match the model. It is
available for every input type, with options or with a source-generated context:

<!-- ref:../../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlSerializer_TryDeserialize;format=md-fenced-code-block;dedent=true -->
```csharp
var toml = "Port = \"not a number\"";

var success = TomlSerializer.TryDeserialize<ServerConfig>(toml, out var config);
Assert.False(success); // Invalid input
Assert.Null(config);
```
<!-- endref -->

- It also returns `false` when the result is `null`.
- It stops at the first error, as it does not report them. Use [Deserialize](cref:Meziantou.Framework.Toml.TomlSerializer.Deserialize) and catch the [TomlException](cref:Meziantou.Framework.Toml.TomlException) to show the
  errors to the user.
- It still throws for errors of the program, such as a type that has no metadata in the context, or a converter that
  reads too much or too little.
- An exception thrown by your model, such as a setter or an [ITomlOnDeserialized](cref:Meziantou.Framework.Toml.Serialization.ITomlOnDeserialized) callback that validates a value, is
  not caught, like in `System.Text.Json`. An exception thrown by a constructor is reported as a [TomlException](cref:Meziantou.Framework.Toml.TomlException).

## Read untrusted input

A document sent by a user can be large or deeply nested. Set limits on the options you use to read it:

<!-- ref:../../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlSerializerOptions_UntrustedInput;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new TomlSerializerOptions
{
    MaxInputLength = 64 * 1024, // characters for a string, bytes for a Stream
    MaxDepth = 16,
    UnmappedMemberHandling = TomlUnmappedMemberHandling.Disallow,
};

var toml = """
    Host = "example.com"
    Timeout = 30
    """;

var success = TomlSerializer.TryDeserialize<ServerConfig>(toml, out var config, options);
Assert.False(success); // Timeout is not a member of ServerConfig
Assert.Null(config);
```
<!-- endref -->

- [MaxInputLength](cref:Meziantou.Framework.Toml.TomlSerializerOptions.MaxInputLength) rejects a document that is too long before it is loaded: a `TextReader` or a `Stream` is not read
  further. It counts characters for a string or a `TextReader`, and bytes for a `Stream`. There is no limit by default.
- [MaxDepth](cref:Meziantou.Framework.Toml.TomlSerializerOptions.MaxDepth) limits the nesting of tables and arrays, 64 by default. A document nested deeper than the stack of the
  current thread allows throws a [TomlException](cref:Meziantou.Framework.Toml.TomlException) instead of overflowing the stack.
- `UnmappedMemberHandling = TomlUnmappedMemberHandling.Disallow` rejects keys that your model does not expect.
- [TomlSerializer](cref:Meziantou.Framework.Toml.TomlSerializer) reads with less memory than the [syntax tree](../08-SyntaxTreeAndParser.md), which keeps every
  character of the input as an object: read untrusted input with the serializer or with [TomlParser](cref:Meziantou.Framework.Toml.Parsing.TomlParser).

## Errors of the values

Some values that TOML accepts cannot be held by .NET types, and some .NET values cannot be written as TOML. Each throws a
[TomlException](cref:Meziantou.Framework.Toml.TomlException):

- A float that overflows a `double`, a leap second, the year `0000`, or an offset beyond ±14:00, when reading. See
  [TOML 1.0 and 1.1](../03-TomlVersions.md#values-that-net-cannot-hold).
- A `null` member that is not skipped, an integer outside the range of `long`, or a `TimeSpan` that is negative or of
  24 hours or more, when writing. See [Serializing objects](02-SerializingObjects.md).
