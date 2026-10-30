---
title: Serializers
description: Configure how values are written in inline snapshots, use JSON, or write your own serializer.
---

The serializer converts the value under test to the text of the snapshot. By default, `InlineSnapshot` uses the
human-readable serializer
([`Meziantou.Framework.HumanReadableSerializer`](https://www.nuget.org/packages/Meziantou.Framework.HumanReadableSerializer)),
which writes objects as readable, YAML-like text and suits most cases.

By default, properties with a default value (`null`, `0`, `false`) are omitted and obsolete members are skipped. JSON
and XML values are indented, the attributes of XML elements are ordered, and the converters for HTTP messages and their
headers are registered.

## Configure the human-readable serializer

`WithSerializer` takes a function that changes the options of the serializer. The options are a copy, so the change only
applies to this assertion:

<!-- ref:../../tests/Meziantou.Framework.InlineSnapshotTesting.Tests/InlineSnapshotTests.cs;region=InlineSnapshot_ConfigureSerializer;format=md-fenced-code-block;dedent=true -->
```csharp
var data = new { LastName = "Barré", FirstName = "Gérald" };

// Configure the HumanReadableSerializer
InlineSnapshot
    .WithSerializer(options => options.PropertyOrder = StringComparer.Ordinal)
    .Validate(data, """
        FirstName: Gérald
        LastName: Barré
        """);
```
<!-- endref -->

To configure every test, call `UseHumanReadableSerializer(options => ...)` on the settings in your
[module initializer](03-Settings.md#configure-every-test). The options of the human-readable serializer that is already
set are kept and changed.

### Order properties and dictionary keys

Recent versions of .NET return the properties of a type in a deterministic order, but .NET Framework does not. Sort the
properties and the dictionary keys to get the same snapshot everywhere:

<!-- ref:../../tests/Meziantou.Framework.InlineSnapshotTesting.Tests/InlineSnapshotTests.cs;region=InlineSnapshot_PropertyOrder;format=md-fenced-code-block;dedent=true -->
```csharp
var data = new
{
    Name = "meziantou",
    Age = 42,
    Links = new Dictionary<string, string> { ["web"] = "meziantou.net", ["code"] = "github.com/meziantou" },
};

InlineSnapshot
    .WithSerializer(options =>
    {
        options.PropertyOrder = StringComparer.Ordinal;
        options.DictionaryKeyOrder = StringComparer.Ordinal;
    })
    .Validate(data, """
        Age: 42
        Links:
          code: github.com/meziantou
          web: meziantou.net
        Name: meziantou
        """);
```
<!-- endref -->

### Format JSON, XML and form content

Formatters rewrite values in a readable form. Their options are in the [Meziantou.Framework.HumanReadable.ValueFormatters](cref:Meziantou.Framework.HumanReadable.ValueFormatters)
namespace:

<!-- ref:../../tests/Meziantou.Framework.InlineSnapshotTesting.Tests/InlineSnapshotTests.cs;region=InlineSnapshot_Formatters;format=md-fenced-code-block;dedent=true -->
```csharp
using var json = new StringContent("""{"b":1,"a":2}""", Encoding.UTF8, "application/json");
using var xml = new StringContent("""<root><item b="1" a="2" /></root>""", Encoding.UTF8, "application/xml");
using var form = new FormUrlEncodedContent([new("b", "1 2"), new("a", "3")]);

InlineSnapshot
    .WithSerializer(options =>
    {
        options.AddJsonFormatter(new JsonFormatterOptions
        {
            OrderProperties = true,
            WriteIndented = true,
            FormatAsStandardObject = false,
        });

        options.AddXmlFormatter(new XmlFormatterOptions
        {
            OrderAttributes = true,
            WriteIndented = true,
        });

        options.AddUrlEncodedFormFormatter(new UrlEncodedFormFormatterOptions
        {
            OrderProperties = true,
            UnescapeValues = true,
            PrettyFormat = true,
        });
    })
    .Validate(new { Json = json, Xml = xml, Form = form }, """
        Json:
          Headers:
            Content-Type: application/json; charset=utf-8
          Value:
            {
              "a": 2,
              "b": 1
            }
        Xml:
          Headers:
            Content-Type: application/xml; charset=utf-8
          Value:
            <root>
              <item a="2" b="1" />
            </root>
        Form:
          Headers:
            Content-Type: application/x-www-form-urlencoded
          Value:
            a: 3
            b: 1 2
        """);
```
<!-- endref -->

HTML values are not formatted by default. `AddHtmlFormatter`, from the
[`Meziantou.Framework.HumanReadableSerializer.Html`](https://www.nuget.org/packages/Meziantou.Framework.HumanReadableSerializer.Html)
package, parses the HTML and serializes it back as defined by the HTML specification:

<!-- ref:../../samples/Meziantou.Framework.InlineSnapshotTesting.Samples/ReadmeSnippets.cs;region=InlineSnapshot_HtmlFormatter;format=md-fenced-code-block;dedent=true -->
```csharp
// Requires the Meziantou.Framework.HumanReadableSerializer.Html package
InlineSnapshot
    .WithSerializer(options =>
    {
        options.AddHtmlFormatter(new HtmlFormatterOptions
        {
            OrderAttributes = true,
            AttributeQuote = HtmlAttributeQuote.DoubleQuote,
            RedactContentSecurityPolicyNonce = true,
        });
    })
    .Validate(response);
```
<!-- endref -->

### Ignore members

Ignore members by name, by type, or when reading them throws. For instance, with this type:

<!-- ref:../../tests/Meziantou.Framework.InlineSnapshotTesting.Tests/InlineSnapshotTests.cs;region=InlineSnapshot_IgnoreMembersType;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class Person
{
    public string FirstName { get; set; } = "Gérald";
    public string LastName { get; set; } = "Barré";
    public string? Email { get; set; } = "gerald@example.com";
    public string? Phone { get; set; } = "555-0100";
    public int Age { get; set; } = 42;
    public Person Manager => throw new InvalidOperationException($"The manager of {FirstName} is not loaded");
}
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.InlineSnapshotTesting.Tests/InlineSnapshotTests.cs;region=InlineSnapshot_IgnoreMembers;format=md-fenced-code-block;dedent=true -->
```csharp
InlineSnapshot
    .WithSerializer(options =>
    {
        options.IgnoreMember<Person>(x => x.LastName);
        options.IgnoreMember<Person>(x => new { x.Email, x.Phone });
        options.IgnoreMembersWithType<int>(); // ignore all properties of type int

        // ignore properties that throw an exception when accessed
        options.IgnoreMembersThatThrow();
        options.IgnoreMembersThatThrow<InvalidOperationException>();
    })
    .Validate(new Person(), "FirstName: Gérald");
```
<!-- endref -->

### Write or skip default values

`DefaultIgnoreCondition` decides which values are written. `WhenWritingDefault`, the default of the snapshot serializer,
skips `null` and default values; `Never` writes every value:

<!-- ref:../../tests/Meziantou.Framework.InlineSnapshotTesting.Tests/InlineSnapshotTests.cs;region=InlineSnapshot_IgnoreDefaultValues;format=md-fenced-code-block;dedent=true -->
```csharp
var data = new { Name = "meziantou", Email = (string?)null, Age = 0 };

InlineSnapshot
    .WithSerializer(options => options.DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingDefault)
    .Validate(data, "Name: meziantou");
```
<!-- endref -->

## Use System.Text.Json

`JsonSnapshotSerializer` writes the value as indented JSON with `System.Text.Json`:

<!-- ref:../../tests/Meziantou.Framework.InlineSnapshotTesting.Tests/InlineSnapshotTests.cs;region=InlineSnapshot_JsonSerializer;format=md-fenced-code-block;dedent=true -->
```csharp
var data = new { FirstName = "Gérald", LastName = "Barré" };

// Use System.Text.Json
InlineSnapshot
    .WithSerializer(new JsonSnapshotSerializer())
    .Validate(data, """
        {
          "FirstName": "Gérald",
          "LastName": "Barré"
        }
        """);
```
<!-- endref -->

Pass `JsonSerializerOptions` to its constructor to change the output. The default options write indented JSON, do not
escape non-ASCII characters, and show `null` values even where the nullable annotations rule them out.

## Write your own serializer

Derive from `SnapshotSerializer` and implement `Serialize`:

<!-- ref:../../tests/Meziantou.Framework.InlineSnapshotTesting.Tests/InlineSnapshotTests.cs;region=InlineSnapshot_CustomSerializer;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class MySnapshotSerializer : SnapshotSerializer
{
    public override string? Serialize(object? value)
    {
        return value switch
        {
            null => "<null>",
            _ => value.ToString(),
        };
    }
}
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.InlineSnapshotTesting.Tests/InlineSnapshotTests.cs;region=InlineSnapshot_CustomSerializerUsage;format=md-fenced-code-block;dedent=true -->
```csharp
var data = new { FirstName = "Gérald", LastName = "Barré" };

InlineSnapshot
    .WithSerializer(new MySnapshotSerializer())
    .Validate(data, "{ FirstName = Gérald, LastName = Barré }");
```
<!-- endref -->

Set `SnapshotSerializer` in the settings to use it for every test.
