---
title: Generated members
description: The resource class and the names class generated for a .resx file, and how strings, files and objects are exposed.
---

For each `.resx` file, the generator writes two classes in the same namespace: the resource class, named after the
file (`Sample` for `Sample.resx`), and the names class (`SampleNames`). The localized files of a resource
(`Sample.fr.resx`, `Sample.fr-CA.resx`...) are grouped with the neutral one: they share the classes.

## One member per resource

| Resource | Generated members |
| --- | --- |
| A string | A `static string?` property that returns the string for the current culture. |
| A string with placeholders (`{0}`, `{1:N2}`...) | The property, and two `Format<Name>` methods: one with the arguments, one with a `CultureInfo? provider` first. |
| A file (`ResXFileRef`) | A property of the type of the file: `string?` for a text file, `byte[]?` for a binary file, or the declared type. |
| Another typed value | A property of the declared type, read with `GetObject`. |

The names of the resources are converted to C# identifiers: invalid characters are replaced by `_` (`Error.Title`
gives `Error_Title`), and a name that starts with a digit gets a `_` prefix. When two resources would get the same member name, or a member name used by the class, the
later ones get a number suffix (`Name1`...). A resource whose type cannot be determined gets no property.

The `Format` methods take one argument per placeholder, up to the highest index: `Hello {0}, {2}` has three arguments.
They are named `arg0`, `arg1`... and typed `object?`, unless the `.resx` file [describes them](04-FormatParameters.md).
The version with `provider` uses that culture both to look up the string and to format the arguments:

<!-- ref:../../tests/Meziantou.Framework.ResxSourceGenerator.GeneratorTests/ResxGeneratorTests.cs;region=ResxSourceGenerator_Culture;format=md-fenced-code-block;dedent=true -->
```csharp
var french = CultureInfo.GetCultureInfo("fr");

// Resource1.resx and Resource1.fr.resx
Assert.Equal("valeur", Resource1.GetString(french, "Sample"));
Assert.Equal("Bonjour le monde!", Resource1.FormatHello(french, "le monde"));
```
<!-- endref -->

Files referenced by the `.resx` file get a property of their type, read from the embedded resource:

<!-- ref:../../tests/Meziantou.Framework.ResxSourceGenerator.GeneratorTests/ResxGeneratorTests.cs;region=ResxSourceGenerator_FileResources;format=md-fenced-code-block;dedent=true -->
```csharp
// Resources\Image1.png;System.Byte[]
byte[]? image = Resource1.Image1;
Assert.Equal([0x89, 0x50, 0x4E, 0x47], image!.AsSpan()[..4]);

// Resources\TextFile1.txt;System.String
string? text = Resource1.TextFile1;
Assert.Equal("test", text);
```
<!-- endref -->

## Members of the resource class

| Member | Description |
| --- | --- |
| `ResourceManager` | The `ResourceManager` that reads the resources of the file, created on first use. |
| `Culture` | A `CultureInfo?` that replaces the current UI culture for every lookup of this class, when it is set. |
| `GetString(name)`, `GetString(culture, name)` | The string of a resource, or `null` when it does not exist. |
| `GetString(name, defaultValue)` | The string, or `defaultValue` when the resource does not exist. |
| `GetString(name, params args)`, `GetString(culture, name, params args)` | The string formatted with the arguments. |
| `GetStringWithDefault(...)` | The same, with a default value and arguments. |
| `GetObject(...)` | The object of a resource, with an optional culture and default value. |
| `GetStream(name)`, `GetStream(culture, name)` | The content of a resource as a `Stream`. |

These methods are useful when the name of the resource is only known at run time:

<!-- ref:../../tests/Meziantou.Framework.ResxSourceGenerator.GeneratorTests/ResxGeneratorTests.cs;region=ResxSourceGenerator_GetString;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.Equal("Hello {0}!", Sample.GetString("Hello"));

// Unknown resources
Assert.Null(Sample.GetString("Unknown"));
Assert.Equal("Default value", Sample.GetString("Unknown", defaultValue: "Default value"));
```
<!-- endref -->

When a culture is `null`, the `Culture` property is used, then the current UI culture.

## The names class

The names class has a `const string` field per resource, whose value is the name of the resource. Use it instead of a
string literal when you pass a name to `GetString`, to an attribute or to another API:

<!-- ref:../../tests/Meziantou.Framework.ResxSourceGenerator.GeneratorTests/ResxGeneratorTests.cs;region=ResxSourceGenerator_KeyNames;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.Equal("Hello", SampleNames.Hello);
Assert.Equal("Hello {0}!", Sample.GetString(SampleNames.Hello));
```
<!-- endref -->

Each class can be disabled for a file: see [Configuration](05-Configuration.md#per-file-metadata).

## Documentation comments

The string properties and the `Format` methods have a `<summary>` that contains the name of the resource and its value,
so the IDE shows the text when you hover a member.
