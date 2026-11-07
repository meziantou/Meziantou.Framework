---
title: Formatting declarations
description: Write a symbol, an attribute or a type reference as a C# declaration, and use its segments to link the referenced types.
---

## Format a symbol

[PublicApiFormatter.Format](cref:Meziantou.Framework.PublicApiGenerator.PublicApiFormatter.Format) writes a symbol, an attribute or a type reference with the C# syntax. The result has the
[Text](cref:Meziantou.Framework.PublicApiGenerator.PublicApiFormattedDeclaration.Text) of the declaration and its [Segments](cref:Meziantou.Framework.PublicApiGenerator.PublicApiFormattedDeclaration.Segments): each segment is a piece of the text with its kind (keyword, type name,
parameter name, punctuation...) and, for type names, the type it references. This example formats a method found in
[Identifying symbols](04-IdentifyingSymbols.md#find-a-symbol), and lists the types it references:

<!-- ref:../../../tests/Meziantou.Framework.PublicApiGenerator.Tests/PublicApiGeneratorTests.cs;region=PublicApi_Format;format=md-fenced-code-block;dedent=true -->
```csharp
var declaration = PublicApiFormatter.Format(symbol);
Assert.Equal("public System.Collections.Generic.List<string?> MyMethod(int value)", declaration.Text);

var references = new List<string>();
foreach (var segment in declaration.Segments)
{
    if (segment.Kind == PublicApiDeclarationSegmentKind.TypeName && segment.TypeReference is PublicApiNamedTypeReference typeReference)
    {
        // e.g. generate a link to the documentation of the referenced type
        references.Add($"{segment.Text} => {typeReference.DocumentationId} in {typeReference.AssemblyName}");
    }
}

Assert.Equal(
    [
        "System.Collections.Generic.List => T:System.Collections.Generic.List`1 in System.Collections",
        "string => T:System.String in System.Runtime",
        "int => T:System.Int32 in System.Runtime",
    ],
    references);
```
<!-- endref -->

The segments make it possible to color a declaration, or to link each type to its documentation, without parsing the
text.

## Options

Pass a [PublicApiFormattingOptions](cref:Meziantou.Framework.PublicApiGenerator.PublicApiFormattingOptions) to change the output:

<!-- ref:../../../tests/Meziantou.Framework.PublicApiGenerator.Tests/PublicApiGeneratorTests.cs;region=PublicApi_FormatOptions;format=md-fenced-code-block;dedent=true -->
```csharp
var shortDeclaration = PublicApiFormatter.Format(symbol, new PublicApiFormattingOptions { QualifyTypeNames = false });
Assert.Equal("public List<string?> MyMethod(int value)", shortDeclaration.Text);

var stub = PublicApiFormatter.Format(symbol, new PublicApiFormattingOptions { Style = PublicApiDeclarationStyle.Compilable });
Assert.Equal("public System.Collections.Generic.List<string?> MyMethod(int value) => throw null;", stub.Text);
```
<!-- endref -->

| Property | Default | Description |
| --- | --- | --- |
| [Style](cref:Meziantou.Framework.PublicApiGenerator.PublicApiFormattingOptions.Style) | [Declaration](cref:Meziantou.Framework.PublicApiGenerator.PublicApiDeclarationStyle.Declaration) | [Declaration](cref:Meziantou.Framework.PublicApiGenerator.PublicApiDeclarationStyle.Declaration) writes a declaration to display. [Compilable](cref:Meziantou.Framework.PublicApiGenerator.PublicApiDeclarationStyle.Compilable) writes the stubs of the API files. |
| [QualifyTypeNames](cref:Meziantou.Framework.PublicApiGenerator.PublicApiFormattingOptions.QualifyTypeNames) | `true` | Writes type names with their namespace, in the [Declaration](cref:Meziantou.Framework.PublicApiGenerator.PublicApiDeclarationStyle.Declaration) style. The segments reference the types in both cases. |
| [IncludeAttributes](cref:Meziantou.Framework.PublicApiGenerator.PublicApiFormattingOptions.IncludeAttributes) | `true` | Writes the attributes before the declaration. |
| [AttributeFilter](cref:Meziantou.Framework.PublicApiGenerator.PublicApiFormattingOptions.AttributeFilter) | `null` | Selects the attributes to write. When it is `null`, the attributes of the `System` namespaces are written, except the ones that only matter to the compiler or that the C# syntax represents (such as `NullableAttribute`, `ExtensionAttribute` or `EditorBrowsableAttribute`). |
| [NewLine](cref:Meziantou.Framework.PublicApiGenerator.PublicApiFormattingOptions.NewLine) | `Environment.NewLine` | The string that separates lines. |

## Declaration and compilable styles

- [PublicApiDeclarationStyle.Declaration](cref:Meziantou.Framework.PublicApiGenerator.PublicApiDeclarationStyle.Declaration) writes a declaration suitable for display: no member body, no constructor
  initializer, no type body. Attributes are written before the declaration, generic constraints after the signature,
  and properties list their accessors (such as `public int Value { get; protected set; }`).
- [PublicApiDeclarationStyle.Compilable](cref:Meziantou.Framework.PublicApiGenerator.PublicApiDeclarationStyle.Compilable) writes the stubs of the [API files](../03-ApiFiles.md) (such as
  `public int MyMethod(int value) => throw null;`). Formatting a type writes the type with all its members and nested
  types.
