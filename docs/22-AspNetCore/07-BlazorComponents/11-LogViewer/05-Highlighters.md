---
title: Highlighters
description: Turn URLs into links, highlight quoted text, and write a highlighter for your own patterns in the log viewer.
---

A highlighter finds ranges of a string message to mark up: a highlighted span, or a link. Pass the highlighters to
`LogHighlighters`:

<!-- ref:../../../../samples/BlazorAppSample/ReadmeExamples/LogViewerAdvancedExample.razor;format=md-fenced-code-block -->
```razor
@page "/logs/advanced"
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
<!-- endref -->

## Built-in highlighters

| Highlighter | Finds |
| --- | --- |
| `UrlLogHighlighter` | `http` and `https` URLs, rendered as links that open in a new tab. Trailing punctuation such as `.` or `)` is not part of the link |
| `QuoteLogHighlighter` | Text between single or double quotes, highlighted without the quotes |

By default, `LogHighlighters` contains a `UrlLogHighlighter`. Passing a collection replaces the default: include a
`UrlLogHighlighter` in it to keep the links, or pass an empty collection to turn highlighting off.

## Write a highlighter

Implement `ILogHighlighter`. `Process` receives the text of the message and returns a `LogHighlighterResult` for each
range to mark up:

<!-- ref:../../../../tests/Meziantou.AspNetCore.Components.LogViewer.Tests/LogHighlighterTests.cs;region=LogHighlighter_Custom;format=md-fenced-code-block;dedent=true -->
```csharp
// Turns "#42" into a link to issue 42
public sealed class IssueLogHighlighter : ILogHighlighter
{
    public IEnumerable<LogHighlighterResult> Process(string text)
    {
        var index = text.IndexOf('#', StringComparison.Ordinal);
        while (index >= 0)
        {
            var end = index + 1;
            while (end < text.Length && char.IsAsciiDigit(text[end]))
            {
                end++;
            }

            if (end > index + 1)
            {
                var number = text[(index + 1)..end];
                yield return new LogHighlighterResult(index, end - index, Priority: 1)
                {
                    Link = "https://example.com/issues/" + number,
                    Title = "Issue " + number,
                };
            }

            index = text.IndexOf('#', end, StringComparison.Ordinal);
        }
    }
}
```
<!-- endref -->

| Member | Description |
| --- | --- |
| `Index`, `Length` | The range in the text. Ranges outside the text, or empty, are ignored |
| `Priority` | Decides between overlapping ranges |
| `Link` | Makes the range a link |
| `Title` | The tooltip of the range |
| `ReplacementText` | Text displayed instead of the range |

Use it with the built-in highlighters, for instance
`LogHighlighters="@(new ILogHighlighter[] { new IssueLogHighlighter(), new UrlLogHighlighter() })"`: with the message
`Fixed in #42`, `#42` becomes a link to `https://example.com/issues/42` with the tooltip `Issue 42`.

## Overlapping ranges

When two ranges overlap, only one is rendered: the one with the higher `Priority`, even when the other starts earlier.
Between equal priorities, the one that starts first wins, then the longest one. The built-in highlighters use priority
`0`; the example above uses `1`, so an issue number wins over a quote or a URL that contains it.

## Links are restricted

Only `http` and `https` links are rendered as links. A `Link` with any other scheme, such as `javascript:`, falls back to
a plain highlight, so a highlighter cannot inject a script into the page from untrusted log text. The text and the
`Title` are HTML-encoded.
