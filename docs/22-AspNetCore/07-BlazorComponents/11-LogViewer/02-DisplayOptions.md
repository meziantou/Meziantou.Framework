---
title: Display options
description: Show absolute or relative timestamps, choose their formats, and hide line numbers in the log viewer.
---

<!-- ref:../../../../samples/BlazorAppSample/Documentation/LogViewerDisplayOptionsExample.razor;format=md-fenced-code-block -->
```razor
<LogViewer Entries="@entries"
           TimestampDisplayFormat="TimestampDisplayFormat.RelativeTimeStartingAtZero"
           TimeSpanStringFormat="mm\:ss\.fff"
           ShowLineNumbers="false"
           LogHighlighters="@(Array.Empty<ILogHighlighter>())" />

@code {
    private static readonly DateTimeOffset Start = new(2025, 1, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly List<LogEntry> entries =
    [
        new LogEntry { Timestamp = Start, LogLevel = LogLevel.Information, Message = "Build started" },
        new LogEntry { Timestamp = Start.AddSeconds(12.5), LogLevel = LogLevel.Warning, Message = "Package 'Contoso.Core' is deprecated" },
        new LogEntry { Timestamp = Start.AddSeconds(40), LogLevel = LogLevel.Information, Message = "Build succeeded" },
    ];
}
```
<!-- endref -->

## Timestamps

`TimestampDisplayFormat` decides what is shown before each message:

| Value | Display |
| --- | --- |
| `FullDateTime` (default) | The date and time of each entry |
| `DateTimeThenRelativeTime` | A first line with the date and time of the earliest entry, then the time elapsed since it on each entry |
| `RelativeTimeStartingAtZero` | The time elapsed since the earliest entry, so the earliest one shows zero |
| `Hidden` | No timestamp |

The relative times are computed from the earliest timestamp of all the entries, children included.

Relative times are formatted with `TimeSpanStringFormat` (`G` by default), and dates with `DateTimeStringFormat` (`R`
by default, such as `Mon, 01 Jan 2024 00:00:00 GMT`). Both use the invariant culture, so the display does not depend
on the server or the browser.

## Line numbers

`ShowLineNumbers` (`true` by default) shows a number before each entry. The numbers count every entry of the tree,
collapsed children included, so a line keeps its number when you expand or collapse another one. The line numbers are
also how lines are selected; see [Selecting lines](01-Overview.md#selecting-lines).

## Highlighting

`LogHighlighters` sets the highlighters of the string messages. The example above passes an empty array to turn off
the default link detection. See [Highlighters](05-Highlighters.md).
