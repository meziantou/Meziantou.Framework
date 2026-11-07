---
title: Nested entries
description: Group log entries in a tree, such as the steps of a request, and choose which ones start expanded.
---

An entry can contain other entries in [Children](cref:Meziantou.AspNetCore.Components.LogEntry.Children), to any depth. An entry with children gets a button that expands or
collapses them:

<!-- ref:../../../../samples/BlazorAppSample/ReadmeExamples/LogViewerNestedLogsExample.razor;format=md-fenced-code-block -->
```razor
<LogViewer Entries="@logEntries" />

@code {
    private List<LogEntry> logEntries = new()
    {
        new LogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Message = "Handling request",
            LogLevel = LogLevel.Information,
            Expanded = true,
            Children = new List<LogEntry>
            {
                new LogEntry
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Message = "Querying database",
                    LogLevel = LogLevel.Debug,
                    Children = new List<LogEntry>
                    {
                        new LogEntry
                        {
                            Timestamp = DateTimeOffset.UtcNow,
                            Message = "Executing query",
                            LogLevel = LogLevel.Trace,
                        },
                    },
                },
            },
        },
    };
}
```
<!-- endref -->

- Entries are collapsed by default. Set `Expanded = true` on the entries that start expanded; here, the request is
  open and the database query is closed.
- Each entry is expanded or collapsed on its own: collapsing a parent hides its children, and expanding it again shows
  them as they were.
- What the user expands or collapses is kept when the parent component renders again with the same collection, and
  entries added later still start in their own [Expanded](cref:Meziantou.AspNetCore.Components.LogEntry.Expanded) state.
- Line numbers count every entry, collapsed ones included, so they do not change when the user expands an entry.
