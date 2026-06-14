# Meziantou.Framework.Yamlish

`Meziantou.Framework.Yamlish` parses and writes a small, deliberate subset of YAML:

- Mappings using `property: value`
- Nested mappings and block sequences using indentation
- Scalar-only inline sequences using `[]`
- Plain, single-quoted, double-quoted, and literal block (`|`) strings

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
