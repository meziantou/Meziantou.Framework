---
title: Overview
description: Install the LogViewer Blazor component, display log entries, and select lines.
---

`Meziantou.AspNetCore.Components.LogViewer` is a Blazor component that displays log entries: the output of a build,
of a job, or of a request, with highlighting and expandable details.

<InstallNuGetPackage Id="Meziantou.AspNetCore.Components.LogViewer" />

The types are in the [Meziantou.AspNetCore.Components](cref:Meziantou.AspNetCore.Components.LogViewer!N:Meziantou.AspNetCore.Components) namespace, like the other
[Blazor components](../01-Overview.md).

## Display entries

<!-- ref:../../../../samples/BlazorAppSample/ReadmeExamples/LogViewerBasicExample.razor;format=md-fenced-code-block -->
```razor
@page "/logs"
@using Meziantou.AspNetCore.Components

<LogViewer Entries="@logEntries" />

@code {
    private List<LogEntry> logEntries = new()
    {
        new LogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Message = "Application started",
            LogLevel = LogLevel.Information
        },
        new LogEntry
        {
            Timestamp = DateTimeOffset.UtcNow.AddSeconds(1),
            Message = "Processing request",
            LogLevel = LogLevel.Debug
        },
        new LogEntry
        {
            Timestamp = DateTimeOffset.UtcNow.AddSeconds(2),
            Message = "An error occurred",
            LogLevel = LogLevel.Error
        },
    };
}
```
<!-- endref -->

[LogLevel](cref:Meziantou.AspNetCore.Components.LogLevel) here is the [Meziantou.AspNetCore.Components.LogLevel](cref:Meziantou.AspNetCore.Components.LogLevel) enum of the package ([Trace](cref:Meziantou.AspNetCore.Components.LogLevel.Trace), [Debug](cref:Meziantou.AspNetCore.Components.LogLevel.Debug),
[Information](cref:Meziantou.AspNetCore.Components.LogLevel.Information), [Warning](cref:Meziantou.AspNetCore.Components.LogLevel.Warning), [Error](cref:Meziantou.AspNetCore.Components.LogLevel.Error), [Critical](cref:Meziantou.AspNetCore.Components.LogLevel.Critical)), not the one of `Microsoft.Extensions.Logging`. Each level has its own
style.

## LogEntry

| Property | Description |
| --- | --- |
| [Timestamp](cref:Meziantou.AspNetCore.Components.LogEntry.Timestamp) | The date and time of the entry, a `DateTimeOffset` |
| [LogLevel](cref:Meziantou.AspNetCore.Components.LogLevel) | The severity |
| [Message](cref:Meziantou.AspNetCore.Components.LogEntry.Message) | A `string`, displayed as text with [highlighting](05-Highlighters.md) and [ANSI colors](04-AnsiColors.md), or any other object, displayed as a [structured payload](03-StructuredMessages.md) |
| [Children](cref:Meziantou.AspNetCore.Components.LogEntry.Children) | Child entries, shown when the entry is expanded; see [Nested entries](06-NestedEntries.md) |
| [Expanded](cref:Meziantou.AspNetCore.Components.LogEntry.Expanded) | Whether the children are visible at first; `false` by default |

## Parameters

| Parameter | Default | Description |
| --- | --- | --- |
| [Entries](cref:Meziantou.AspNetCore.Components.LogViewer.Entries) | `null` | The entries. Nothing is rendered while it is `null` |
| [TimestampDisplayFormat](cref:Meziantou.AspNetCore.Components.TimestampDisplayFormat) | [FullDateTime](cref:Meziantou.AspNetCore.Components.TimestampDisplayFormat.FullDateTime) | How timestamps are shown; see [Display options](02-DisplayOptions.md) |
| [DateTimeStringFormat](cref:Meziantou.AspNetCore.Components.LogViewer.DateTimeStringFormat) | `R` | The format of dates and times |
| [TimeSpanStringFormat](cref:Meziantou.AspNetCore.Components.LogViewer.TimeSpanStringFormat) | `G` | The format of relative times |
| [ShowLineNumbers](cref:Meziantou.AspNetCore.Components.LogViewer.ShowLineNumbers) | `true` | Shows a line number before each entry |
| [LogHighlighters](cref:Meziantou.AspNetCore.Components.LogViewer.LogHighlighters) | A [UrlLogHighlighter](cref:Meziantou.AspNetCore.Components.UrlLogHighlighter) | The highlighters applied to string messages |

## Selecting lines

When line numbers are shown, clicking a line number selects the line; Ctrl+click (Cmd+click on macOS) adds a line to
the selection or removes it. Selected lines get the `selected` CSS class.

## Updating the entries

The viewer takes a snapshot of [Entries](cref:Meziantou.AspNetCore.Components.LogViewer.Entries) each time its parameters are set. To add entries, add them to the collection
and render the parent component again (`StateHasChanged`): the expanded lines and the selection are kept. Assigning
another collection resets them.

The entries are tracked by reference: the same [LogEntry](cref:Meziantou.AspNetCore.Components.LogEntry) instance must not appear twice in the tree.
