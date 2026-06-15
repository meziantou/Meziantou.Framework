# Meziantou.Framework.Yamlish

`Meziantou.Framework.Yamlish` parses and writes a small, deliberate subset of YAML:

- Mappings using `property: value`
- Nested mappings and block sequences using indentation
- Scalar-only inline sequences using `[]`
- Plain, single-quoted, double-quoted, literal block (`|`, `|-`, `|+`), and folded block (`>`, `>-`, `>+`) strings
- Full-line and trailing comments using `#`

It intentionally does not support the complete YAML specification.

## Parse a document

```csharp
using Meziantou.Framework.Yamlish;

var document = YamlishDocument.Parse("""
    id: abc
    name: sample product
    description: |
        This is a sample product.
        Line 2
    """);

var product = (YamlishMapping)document.Root;
var id = ((YamlishScalar)product["id"]).Value;
```

## Serialize and deserialize objects

```csharp
using Meziantou.Framework.Yamlish;

var options = new YamlishSerializerOptions
{
    PropertyNamingPolicy = YamlishNamingPolicy.SnakeCaseLower,
};

var content = YamlishSerializer.Serialize(new Product
{
    Id = "abc",
    IsAvailable = true,
}, options);

var product = YamlishSerializer.Deserialize<Product>(content, options);
```

Plain scalar values remain strings in the document model. During typed deserialization, they are converted to the requested .NET property type using invariant culture.

Serialized documents do not end with a trailing newline. Block scalar chomping and folding follow YAML semantics.

Properties and fields can be conditionally omitted using `YamlishIgnoreAttribute`. The global default is configured using `YamlishSerializerOptions.DefaultIgnoreCondition`.

```csharp
var options = new YamlishSerializerOptions
{
    DefaultIgnoreCondition = YamlishIgnoreCondition.WhenWritingNull,
};

public sealed class Product
{
    [YamlishIgnore(Condition = YamlishIgnoreCondition.WhenWritingDefault)]
    public decimal Price { get; set; }
}
```
