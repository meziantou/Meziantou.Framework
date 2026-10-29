---
title: Structured messages
description: Log objects instead of strings, display them as a table or as JSON, and use LogEntryDetails on its own.
---

`Message` accepts any object. A message that is not a `string` is rendered as a structured payload, which the user can
view as a table or as JSON:

<!-- ref:../../../samples/BlazorAppSample/ReadmeExamples/LogViewerStructuredMessageExample.razor;format=md-fenced-code-block -->
```razor
<LogViewer Entries="@logEntries" />

@code {
    private List<LogEntry> logEntries = new()
    {
        new LogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            LogLevel = LogLevel.Information,
            Message = new
            {
                Text = "Request completed with details",
                UserId = 123,
                Endpoint = "/api/users",
                Duration = TimeSpan.FromMilliseconds(245),
                Headers = new Dictionary<string, string>
                {
                    { "User-Agent", "Mozilla/5.0" },
                    { "Content-Type", "application/json" }
                }
            }
        },
    };
}
```
<!-- endref -->

## The table view

The table walks the object with reflection:

- dictionaries are rendered as key and value rows,
- other sequences as indexed rows,
- other objects by their public instance properties.

Indexers are skipped. A property getter that throws is rendered as an error instead of failing the render. Nesting is
limited by a maximum depth (32 levels), which also stops cyclic object graphs.

## The JSON view

The JSON view serializes the object with `System.Text.Json`, with enums as strings. Cycles are ignored instead of
failing the serialization.

## Display a payload on its own

The details are rendered by the `LogEntryDetails` component, which you can use without a log viewer:

<!-- ref:../../../samples/BlazorAppSample/Documentation/LogEntryDetailsExample.razor;format=md-fenced-code-block -->
```razor
<LogEntryDetails Data="@request" Format="LogDetailsDisplayFormat.Json" CanChangeDisplayFormat="false" MaxDepth="3" />

@code {
    private readonly Dictionary<string, object> request = new(StringComparer.Ordinal)
    {
        ["Method"] = "GET",
        ["Path"] = "/api/orders",
        ["StatusCode"] = 200,
        ["Headers"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Accept"] = "application/json",
        },
    };
}
```
<!-- endref -->

| Parameter | Default | Description |
| --- | --- | --- |
| `Data` | `null` | The object to display |
| `Format` | `Table` | The initial view, `LogDetailsDisplayFormat.Table` or `LogDetailsDisplayFormat.Json` |
| `CanChangeDisplayFormat` | `true` | Lets the user switch between the table and the JSON view. Their choice is kept until `Format` changes |
| `MaxDepth` | `32` | How many levels of nested objects are rendered. Deeper values are elided |

## Trimming and Native AOT

Both views use reflection over arbitrary objects, so `LogEntryDetails` is not compatible with trimming or Native AOT.
In a trimmed Blazor WebAssembly application, the properties of logged types may be removed and silently left out of
the output. Keep the types you log rooted, or log a `Dictionary<string, object>` instead of an arbitrary object.
