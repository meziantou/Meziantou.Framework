# Meziantou.AspNetCore.Components.LogViewer

A Blazor component for displaying and analyzing log entries with support for filtering, highlighting, and interactive exploration.

## Features

- **Display log entries** with customizable formatting
- **Syntax highlighting** for URLs, quoted text, and custom patterns
- **Flexible timestamp formats** (full datetime, relative time, hidden)
- **Interactive log details** with table and JSON views
- **Line numbers** for easy reference
- **Log level styling** with distinct visual indicators
- **Clickable URLs** in log messages
- **Multi-line log support** with expandable details

## Usage

### Basic Example

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

### Advanced Example with Configuration

```razor
@page "/logs"
@using Meziantou.AspNetCore.Components

<LogViewer
    Entries="@logEntries"
    TimestampDisplayFormat="TimestampDisplayFormat.DateTimeThenRelativeTime"
    ShowLineNumbers="true"
    LogHighlighters="@highlighters" />

@code {
    private ILogHighlighter[] highlighters = new ILogHighlighter[]
    {
        new UrlLogHighlighter(),
        new QuoteLogHighlighter()
    };

    private List<LogEntry> logEntries = new()
    {
        new LogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Message = "Check out https://example.com for more info",
            LogLevel = LogLevel.Information
        },
        new LogEntry
        {
            Timestamp = DateTimeOffset.UtcNow.AddSeconds(5),
            Message = "User 'admin' logged in",
            LogLevel = LogLevel.Information
        },
    };
}
```

### Log Entries with Structured Messages

```razor
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

When `Message` is a string, it is displayed as highlighted text. For non-string `Message` values, the structured payload is rendered inline and can be viewed as either a table or JSON.

### Hierarchical / Nested Logs

A `LogEntry` can contain child entries via the `Children` property, with an unlimited number of levels.
Each entry is independently collapsible. Set `Expanded = true` on an entry to make it start expanded
(the default is collapsed). The toggle controls nested children.

```razor
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

Line numbers count every entry in the tree (including collapsed children), so the numbers stay stable
when you expand or collapse nodes.
