---
title: ANSI colors
description: Display the colors and styles of console output, written as ANSI escape sequences, in the log viewer.
---

Console tools color their output with ANSI escape sequences. The log viewer parses the SGR sequences of string
messages and renders their colors and styles, instead of showing the escape characters:

<!-- ref:../../../../tests/Meziantou.AspNetCore.Components.LogViewer.Tests/LogViewerComponentTests.cs;region=LogEntry_AnsiMessage;format=md-fenced-code-block;dedent=true -->
```csharp
var entry = new LogEntry
{
    Timestamp = DateTimeOffset.UtcNow,
    LogLevel = LogLevel.Information,
    Message = "\u001b[1;33mwarning\u001b[0m \u001b[38;2;100;200;50mcustom color\u001b[0m",
};
```
<!-- endref -->

Supported colors are the 16 standard colors, the 256-color palette and truecolor (24-bit) sequences, for the
foreground and the background, and the bold, italic and underline styles.

Highlighters run on the visible text, without the escape sequences, so a URL in a colored message is still a link and
keeps its color. You can pass the output of a process to the log viewer as it is.
