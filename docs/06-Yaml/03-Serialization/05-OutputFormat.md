---
title: Output format
description: Control the indentation, flow collections, the layout of sequences, the style of strings and numbers, and the order of keys.
---

The serializer writes block YAML indented by two spaces. The options of this page change the layout of the text; they
never change the values, which read back the same way.

## Indentation and flow collections

`IndentSize` sets the number of spaces per level (`2` by default). Set `WriteIndented` to `false` to write the
collections in the flow style, on a single line:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerApiTests.cs;region=YamlSerializerOptions_WriteIndented;format=md-fenced-code-block;dedent=true -->
```csharp
var product = new Product { Id = 1, DisplayName = "Table", Tags = ["new", "sale"] };

var yaml = YamlSerializer.Serialize(product, new YamlSerializerOptions { WriteIndented = false });
Assert.Equal("{Id: 1, name: Table, Tags: [new, sale]}\n", yaml);
```
<!-- endref -->

YAML expresses nesting through indentation, so this is the only way to write YAML without indentation. A scalar that
contains a flow indicator, such as `,`, `:`, `[` or `{`, is quoted. `IndentSize` and the sequence layout options below
do not apply to the flow style.

## Sequence layout

By default, a sequence is indented under its key, and the first key of a mapping inside a sequence shares the line of
its dash. `IndentBlockSequences = false` keeps the dashes at the indentation of the parent key, and
`BlockSequenceMappingStyle = YamlSequenceItemStyle.Expanded` starts the mapping on the line after its dash:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerApiTests.cs;region=YamlSerializerOptions_BlockSequences;format=md-fenced-code-block;dedent=true -->
```csharp
var value = new
{
    Tags = new[] { "new", "sale" },
    Items = new[] { new { Id = 1, Name = "first" } },
};

Assert.Equal("""
    Tags:
      - new
      - sale
    Items:
      - Id: 1
        Name: first

    """, YamlSerializer.Serialize(value));

var options = new YamlSerializerOptions
{
    IndentBlockSequences = false,
    BlockSequenceMappingStyle = YamlSequenceItemStyle.Expanded,
};

Assert.Equal("""
    Tags:
    - new
    - sale
    Items:
    -
      Id: 1
      Name: first

    """, YamlSerializer.Serialize(value, options));
```
<!-- endref -->

`BlockSequenceSequenceStyle` does the same for a sequence inside a sequence; its default is `Expanded`. A sequence
nested in another sequence is always indented past the dash of its parent, because YAML has no other form for it.
`[YamlBlockSequenceItemStyle]` sets these styles for one type or one member.

## Strings that look like other values

A string that would be read back as a boolean, a number or null is quoted, so that it stays a string. Other strings
are written plain when possible:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerApiTests.cs;region=YamlSerializerOptions_AmbiguousStrings;format=md-fenced-code-block;dedent=true -->
```csharp
var yaml = YamlSerializer.Serialize(new { A = "yes", B = "123", C = "true", D = "plain" });
Assert.Equal("""
    A: yes
    B: "123"
    C: "true"
    D: plain

    """, yaml);
```
<!-- endref -->

`yes` is not quoted: under the default [schema](11-Schemas.md), it is a string.

`ScalarStylePreferences` controls quoting:

- `PreferQuotedForAmbiguousScalars` (`true` by default) quotes the ambiguous strings. Disable it only when the reader
  of the YAML expects these values unquoted.
- `PreferPlainStyle = false` double-quotes the strings and keys that would be written plain, unless `StringStyle`
  requests another style.

## Block and quoted strings

`ScalarStylePreferences.StringStyle` selects the style of every string value. `Literal` keeps the line breaks of a
multi-line string, such as a script:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerApiTests.cs;region=YamlSerializerOptions_LiteralStrings;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new YamlSerializerOptions
{
    ScalarStylePreferences = new YamlScalarStylePreferences { StringStyle = ScalarStyle.Literal },
};

var yaml = YamlSerializer.Serialize(new { Script = "echo one\necho two\n" }, options);
Assert.Equal("""
    Script: |
      echo one
      echo two

    """, yaml);
```
<!-- endref -->

The styles are:

| Style | Writes |
| --- | --- |
| `Any` (default) | plain or double-quoted, chosen by the writer |
| `Plain`, `SingleQuoted`, `DoubleQuoted` | that style |
| `Literal` | a block scalar (`\|`) that keeps the line breaks |
| `Folded` | a folded block scalar (`>`) |

A literal block scalar starts with `|-` when the value has no trailing line break, `|` when it has one, and `|+` when
it has more. `[YamlStringStyle]` sets the style of one member, and of the strings below it:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerApiTests.cs;region=YamlStringStyleAttribute_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
internal sealed class Job
{
    [YamlStringStyle(ScalarStyle.Literal)]
    public string? Script { get; set; }

    public string? Name { get; set; }
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerApiTests.cs;region=YamlStringStyleAttribute_Serialize;format=md-fenced-code-block;dedent=true -->
```csharp
var yaml = YamlSerializer.Serialize(new Job { Script = "echo one\necho two", Name = "build" });
Assert.Equal("""
    Script: |-
      echo one
      echo two
    Name: build

    """, yaml);
```
<!-- endref -->

The requested style is used only when the value reads back unchanged through it; otherwise the writer chooses. A block
scalar cannot hold an empty value, a carriage return, a control character, a line that ends with a space or a tab, or
a line that starts with a tab, and is never used inside a flow collection; a folded scalar cannot hold a line that
starts with a space either. The style applies to string values only: keys, numbers, booleans and dates keep their
representation.

## Numbers as strings

`[YamlNumberHandling(YamlNumberHandling.WriteAsString)]` writes the numbers of a member or a type as quoted strings,
for a reader that expects them so:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerApiTests.cs;region=YamlNumberHandlingAttribute_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
internal sealed class Server
{
    [YamlNumberHandling(YamlNumberHandling.WriteAsString)]
    public int Port { get; set; }
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerApiTests.cs;region=YamlNumberHandlingAttribute_Serialize;format=md-fenced-code-block;dedent=true -->
```csharp
var yaml = YamlSerializer.Serialize(new Server { Port = 8080 });
Assert.Equal("Port: \"8080\"\n", yaml);

Assert.Equal(8080, YamlSerializer.Deserialize<Server>(yaml)!.Port);
```
<!-- endref -->

`YamlNumberHandling.AllowNamedFloatingPointLiterals` writes and reads the special floating-point values as the quoted
`"NaN"`, `"Infinity"` and `"-Infinity"` instead of the YAML `.nan`, `.inf` and `-.inf`.

## Key order

Members are written in declaration order, or in the order set by
[`[YamlPropertyOrder]`](04-MembersAndNames.md#order-the-members). Set `MappingOrder` to `YamlMappingOrderPolicy.Sorted`
to sort them by their YAML key instead.
