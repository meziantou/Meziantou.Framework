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

The types are in the `Meziantou.Framework.HumanReadable` and `Meziantou.Framework.HumanReadable.ValueFormatters` namespaces.

<!-- ref:../../tests/Meziantou.Framework.HumanReadableSerializer.Html.Tests/HttpHtmlSerializerTests.cs;region=HtmlFormatter_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new HumanReadableSerializerOptions()
    .AddHtmlFormatter(new HtmlFormatterOptions
    {
        OrderAttributes = true,
        RedactContentSecurityPolicyNonce = true,
    });

var text = HumanReadableSerializer.Serialize(httpResponseMessage, options);
```
<!-- endref -->

### Meziantou.Framework.SnapshotTesting

<!-- ref:../../samples/Meziantou.Framework.HumanReadableSerializer.Html.Samples/SnapshotTesting/SnapshotConfiguration.cs;format=md-fenced-code-block -->
```csharp
using System.Runtime.CompilerServices;
using Meziantou.Framework.HumanReadable.ValueFormatters;
using Meziantou.Framework.SnapshotTesting;

namespace SnapshotTestingSample;

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
<!-- endref -->

### Meziantou.Framework.InlineSnapshotTesting

<!-- ref:../../samples/Meziantou.Framework.HumanReadableSerializer.Html.Samples/InlineSnapshotTesting/SnapshotConfiguration.cs;format=md-fenced-code-block -->
```csharp
using System.Runtime.CompilerServices;
using Meziantou.Framework.HumanReadable.ValueFormatters;
using Meziantou.Framework.InlineSnapshotTesting;

namespace InlineSnapshotTestingSample;

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
<!-- endref -->
