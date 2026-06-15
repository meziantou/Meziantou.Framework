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

Built-in converters support strings, characters, booleans, all integral and floating-point types, decimals, enums, GUIDs, dates, times, time spans, and URIs. Temporal and floating-point values use round-trip formats.

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

Attributes can also be added using serializer options. Option-provided attributes override attributes declared on the member.

```csharp
var options = new YamlishSerializerOptions();
options.AddAttribute(typeof(Product), nameof(Product.IsAvailable), new YamlishIgnoreAttribute());
options.AddAttribute<Product>(product => product.Price, new YamlishPropertyNameAttribute("cost"));
options.AddPropertyAttribute(property => property.PropertyType == typeof(Uri), new YamlishIgnoreAttribute());
```

Serializer options become read-only after their first use. Resolved member metadata is then cached by type and reused by subsequent serialization and deserialization operations.

## Converters

Custom converters can read and write any `YamlishNode` shape. Converters are checked in collection order and resolved converters are cached after the options become read-only.

```csharp
var options = new YamlishSerializerOptions();
options.Converters.Add(new TemperatureConverter());

public sealed class TemperatureConverter : YamlishConverter<Temperature>
{
    public override Temperature Read(YamlishNode node, YamlishSerializerOptions options)
        => new(int.Parse(((YamlishScalar)node).Value, CultureInfo.InvariantCulture));

    public override YamlishNode Write(Temperature value, YamlishSerializerOptions options)
        => new YamlishScalar(value.Value.ToString(CultureInfo.InvariantCulture));
}
```

Use `YamlishConverterFactory` to create converters for open generic types or families of related types.
