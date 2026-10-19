# Meziantou.Framework.HumanReadableSerializer.Html

`Meziantou.Framework.HumanReadableSerializer.Html` formats the HTML values (`text/html`) serialized by [`Meziantou.Framework.HumanReadableSerializer`](https://www.nuget.org/packages/Meziantou.Framework.HumanReadableSerializer), such as the content of an `HttpResponseMessage`. The value is parsed by the spec-compliant HTML parser of [`Meziantou.Framework.WebPlatform`](https://www.nuget.org/packages/Meziantou.Framework.WebPlatform) and serialized back as defined by the HTML specification, so the output is normalized: the attribute values are quoted, the character references are decoded and re-escaped, and `<br/>` becomes `<br>`.

A value that contains a doctype or an `html`, `head` or `body` tag is parsed as a document. Any other value is parsed as the content of a `body` element, so it is not wrapped in `<html><head></head><body>…</body></html>`. A value that cannot be formatted is written as is.

## Options

| Option | Description |
| ------ | ----------- |
| `OrderAttributes` | Sorts the attributes of each element by namespace and name |
| `AttributeQuote` | `DoubleQuote` (the default) or `SimpleQuote` |
| `RedactContentSecurityPolicyNonce` | Replaces the value of the `nonce` attributes with `[redacted]` |

## Usage

```csharp
using Meziantou.Framework.HumanReadable;
using Meziantou.Framework.HumanReadable.ValueFormatters;

var options = new HumanReadableSerializerOptions()
    .AddHtmlFormatter(new HtmlFormatterOptions
    {
        OrderAttributes = true,
        RedactContentSecurityPolicyNonce = true,
    });

var text = HumanReadableSerializer.Serialize(httpResponseMessage, options);
```

### Meziantou.Framework.SnapshotTesting

```csharp
using Meziantou.Framework.HumanReadable.ValueFormatters;
using Meziantou.Framework.SnapshotTesting;

internal static class SnapshotConfiguration
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        SnapshotSettings.Default.ConfigureHumanReadableSerializer(options => options.AddHtmlFormatter(new HtmlFormatterOptions
        {
            OrderAttributes = true,
        }));
    }
}
```

### Meziantou.Framework.InlineSnapshotTesting

```csharp
using Meziantou.Framework.HumanReadable.ValueFormatters;
using Meziantou.Framework.InlineSnapshotTesting;

internal static class SnapshotConfiguration
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var settings = InlineSnapshotSettings.Default with { };
        settings.UseHumanReadableSerializer(options => options.AddHtmlFormatter(new HtmlFormatterOptions
        {
            OrderAttributes = true,
        }));
        InlineSnapshotSettings.Default = settings;
    }
}
```
