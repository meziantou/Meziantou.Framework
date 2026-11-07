---
title: Getting started
description: Install the package, serialize an object, and pass options.
---

## Install the package

Add the `Meziantou.Framework.HumanReadableSerializer` package to your project:

<InstallNuGetPackage Id="Meziantou.Framework.HumanReadableSerializer" />

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework.HumanReadable;`.
They check their results with assertions such as `Assert.Equal`, which show the text the serializer writes.

## Serialize an object

[HumanReadableSerializer.Serialize](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializer.Serialize) returns the text of any value:

<!-- ref:../../tests/Meziantou.Framework.HumanReadableSerializer.Tests/HumanReadableSerializerOptionsTests.cs;region=HumanReadableSerializer_GettingStarted;format=md-fenced-code-block;dedent=true -->
```csharp
var order = new
{
    Id = 42,
    Customer = new { Name = "Ada Lovelace", Email = "ada@example.com" },
    Lines = new[]
    {
        new { Product = "Keyboard", Quantity = 1 },
        new { Product = "Mouse", Quantity = 2 },
    },
    Notes = "Leave at the door\nRing twice",
};

var text = HumanReadableSerializer.Serialize(order);

Assert.Equal("""
    Id: 42
    Customer:
      Name: Ada Lovelace
      Email: ada@example.com
    Lines:
      - Product: Keyboard
        Quantity: 1
      - Product: Mouse
        Quantity: 2
    Notes:
      Leave at the door
      Ring twice
    """, text);
```
<!-- endref -->

- Each property is written on its own line as `Name: value`, in the order of the type's declaration.
- A nested object is written below its property name, indented by two spaces.
- Each item of a collection starts with `- `.
- A string with several lines is written below its property name, one line per line.

[Output format](03-OutputFormat.md) shows how each kind of value is written.

## Pass options

[HumanReadableSerializerOptions](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions) changes what is written, for instance to skip the `null` values:

<!-- ref:../../tests/Meziantou.Framework.HumanReadableSerializer.Tests/SerializerTests.cs;region=HumanReadableSerializerOptions_Serialize;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new HumanReadableSerializerOptions
{
    IncludeFields = true,
    DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingNull,
};
var obj = new { Name = "John Doe", Nickname = (string?)null };
var output = HumanReadableSerializer.Serialize(obj, options);
Assert.Equal("Name: John Doe", output);
```
<!-- endref -->

An options instance becomes read-only the first time it is used. Create it once, configure it, and reuse it: it caches
what it learns about each type. See [Options](08-Options.md).

## Next steps

- [Output format](03-OutputFormat.md): how values, collections and dictionaries are written.
- [Members](04-Members.md): choose, rename and order the members.
- [Snapshot testing](09-SnapshotTesting.md): configure the serializer of snapshot tests.
