---
title: Clipboard
description: Copy text to the clipboard and read it from a Blazor component, and handle the browser refusing access.
---

`ClipboardService` reads and writes text with the clipboard API of the browser. Register it with
`builder.Services.AddClipboard()`, then inject it:

<!-- ref:../../../samples/BlazorAppSample/Examples/ClipboardServiceExample.razor;format=md-fenced-code-block -->
```razor
@inject ClipboardService ClipboardService

<button @onclick="CopyToClipboard">Copy</button>

@code {
    private async Task CopyToClipboard()
    {
        await ClipboardService.WriteTextAsync("Hello, World!");
    }
}
```
<!-- endref -->

- `WriteTextAsync(text)` copies the text.
- `ReadTextAsync()` returns the text of the clipboard.

## When the browser refuses

The browser decides whether a page can use the clipboard. `WriteTextAsync` throws a `JSException` when it refuses:
the page is not in a secure context (HTTPS or `localhost`), the clipboard permission is denied, or the call does not
come from a user gesture such as a click. Catch it to tell the user, instead of reporting a copy that did not happen:

<!-- ref:../../../samples/BlazorAppSample/Documentation/ClipboardErrorHandlingExample.razor;format=md-fenced-code-block -->
```razor
@inject ClipboardService ClipboardService

<button @onclick="CopyAsync">Copy the invitation link</button>
<span>@status</span>

@code {
    private string? status;

    private async Task CopyAsync()
    {
        try
        {
            await ClipboardService.WriteTextAsync("https://example.com/invite/1234");
            status = "Copied";
        }
        catch (JSException)
        {
            // Not a secure (HTTPS) context, permission denied, or not called from a user gesture
            status = "Copy the link manually: https://example.com/invite/1234";
        }
    }
}
```
<!-- endref -->

Call the service from an event handler, such as `@onclick`: browsers only allow the clipboard during a user gesture,
and JavaScript calls are not possible during prerendering.
