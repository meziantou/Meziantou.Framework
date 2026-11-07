---
title: Format parameters
description: Name, type and document the arguments of the generated Format methods in the .resx file.
---

By default, the arguments of a `Format` method are named `arg0`, `arg1`... and typed `object?`. To generate meaningful
names, types and documentation, describe the parameters in the `.resx` file with `parameter` elements in the
`https://meziantou.net/meziantou.framework/resxgenerator` namespace:

```xml
<root xmlns:mfrg="https://meziantou.net/meziantou.framework/resxgenerator">
  <data name="Location" xml:space="preserve">
    <value>I live in country {0}, city {1}</value>
    <mfrg:parameter name="country" comment="Country name." />
    <mfrg:parameter name="city" typename="global::System.String" comment="City name." />
  </data>
</root>
```

The generator then writes `FormatLocation(object? country, global::System.String city)` and the overload with a
`CultureInfo? provider`, with a `<param>` documentation comment for each parameter. The arguments can be passed by name:

<!-- ref:../../tests/Meziantou.Framework.ResxSourceGenerator.GeneratorTests/ResxGeneratorTests.cs;region=ResxSourceGenerator_FormatParameters;format=md-fenced-code-block;dedent=true -->
```csharp
// <mfrg:parameter name="name" typename="global::System.String" comment="Name to greet." />
string? message = Resource1.FormatHello(name: "world");
Assert.NotNull(message);
```
<!-- endref -->

## Attributes

| Attribute | Effect |
| --- | --- |
| `name` | Name of the parameter, converted to a C# identifier. |
| `typename` | Type of the parameter, as written in C#. Use a fully qualified name with `global::`, as the generated file has no `using` directive. Default: `object?`. |
| `comment` | Text of the `<param>` documentation comment. |

## Rules

- The parameters are matched to the placeholders by their order in the `.resx` file: the first element describes
  `{0}`, the second `{1}`... The element does not say which placeholder it describes.
- A placeholder without an element keeps its default name and type. Extra elements are ignored.
- A name that is empty, or already used by another parameter, falls back to the default name.
- The elements can be in the neutral file or in a localized one: the generator uses the first entry of the resource that
  has elements. Keep them in the neutral file.

The build and the `ResourceManager` ignore these elements: the resources are read as usual.
