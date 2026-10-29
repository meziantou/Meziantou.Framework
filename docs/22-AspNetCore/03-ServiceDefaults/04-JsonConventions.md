---
title: JSON conventions
description: How the service defaults configure System.Text.Json for minimal APIs and controllers, and how to change it.
---

The service defaults configure the JSON serializer of minimal APIs (`Microsoft.AspNetCore.Http.Json.JsonOptions`) and of
controllers (`Microsoft.AspNetCore.Mvc.JsonOptions`) the same way. For this endpoint:

<!-- ref:../../../tests/Meziantou.AspNetCore.ServiceDefaults.Tests/ServiceDefaultTests.cs;region=ServiceDefaults_JsonConventions;format=md-fenced-code-block;dedent=true -->
```csharp
app.MapGet("/schedule", () => new
{
    Day = DayOfWeek.Monday,
    Comment = (string?)null,
    SlotsPerRoom = new Dictionary<string, int>(StringComparer.Ordinal) { ["MeetingRoom"] = 3 },
});
```
<!-- endref -->

the response is:

<!-- ref:../../../tests/Meziantou.AspNetCore.ServiceDefaults.Tests/ServiceDefaultTests.cs;region=ServiceDefaults_JsonConventionsOutput;format=md-fenced-code-block;dedent=true -->
```csharp
var json = await httpClient.GetStringAsync("/schedule");
Assert.Equal("""{"day":"monday","slotsPerRoom":{"meetingRoom":3}}""", json);
```
<!-- endref -->

## The conventions

| Setting | Value | Effect |
| --- | --- | --- |
| `PropertyNamingPolicy` | camelCase | `SlotsPerRoom` is written `slotsPerRoom` |
| `DictionaryKeyPolicy` | camelCase | The `MeetingRoom` key is written `meetingRoom` |
| `DefaultIgnoreCondition` | `WhenWritingNull` | `null` properties are not written |
| Enum converter | `JsonStringEnumConverter` with camelCase, integers allowed | Enums are written as camelCase strings; numbers are still accepted when reading |
| `RespectNullableAnnotations` | `true` | Reading `null` into a non-nullable property, or writing `null` from one, fails |
| `RespectRequiredConstructorParameters` | `true` | Reading an object without a value for a non-optional constructor parameter fails |

The last two settings make the serializer enforce the nullable annotations and constructors of your types: a request
body with a missing or `null` value where your type does not allow it is rejected instead of producing an object in an
invalid state.

## Changing the conventions

`ConfigureJsonOptions` runs after the conventions, so it can add converters or override any setting:

<!-- ref:../../../tests/Meziantou.AspNetCore.ServiceDefaults.Tests/ServiceDefaultTests.cs;region=ServiceDefaults_ConfigureOptions;format=md-fenced-code-block;dedent=true -->
```csharp
builder.UseMeziantouConventions(options =>
{
    options.OpenApi.RoutePattern = "/swagger/{documentName}.json";
    options.Https.HstsEnabled = false;
    options.AntiForgery.Enabled = false;
    options.ForwardedHeaders.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ConfigureJsonOptions = json => json.WriteIndented = true;
    options.OpenTelemetry.ConfigureTracing = tracing => tracing.AddSource("MyCompany.*");
});
```
<!-- endref -->

Dictionary keys are camelCased too: a dictionary whose keys are identifiers, such as product codes, is written with
its first letter lowercased. Set `DictionaryKeyPolicy` to `null` in `ConfigureJsonOptions` to keep the keys unchanged.
