using System.Text;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing;

/// <summary>
/// A system clipboard for the tests: <see cref="ProvideContent"/> supplies what the pages paste or read, and
/// <see cref="ContentChanged"/> is called with what they copy or write. Set it in
/// <see cref="BrowsingContextOptions.Clipboard"/> with <see cref="TestPageSettings.ConfigureBrowsingContext"/>; the pages
/// that use the same instance share it. Without it, each page has a clipboard of its own.
/// </summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.AspNetCore.Testing.Tests/WebAppBrowserTests.cs" ref:region="FakeClipboard_Usage" ref:dedent="true">
/// var copied = new List&lt;string?&gt;();
/// var clipboard = new FakeClipboard
/// {
///     // What the pages paste or read
///     ProvideContent = () =&gt; [FakeClipboard.CreateTextItem("Pasted text")],
///
///     // Called with what the pages copy or write
///     ContentChanged = items =&gt; copied.Add(FakeClipboard.GetText(items)),
/// };
///
/// await using var factory = new WebApplicationFactory&lt;Program&gt;();
/// await using var browser = WebAppBrowser.Create(factory.Server, new WebAppBrowserOptions
/// {
///     // The pages that use the same clipboard share it
///     ConfigureBrowsingContext = options =&gt; options with { Clipboard = clipboard },
/// });
/// var page = await browser.OpenAsync("/secure");
///
/// await page.Find("#copy").ClickAsync();
/// await page.Find("#paste").ClickAsync();
///
/// Assert.Equal(["Copied text"], copied);
/// Assert.Equal("Pasted text", page.Find("output").TextContent);
/// </code>
/// </example>
public sealed class FakeClipboard : SystemClipboard
{
    private IReadOnlyList<SystemClipboardItem> _items = [];
    private long _changeCount;

    /// <summary>
    /// Gets a function that returns the content of the clipboard each time it is read (the paste action,
    /// <c>navigator.clipboard.read()</c> and <c>readText()</c>), or null to read the content last written.
    /// </summary>
    public Func<IReadOnlyList<SystemClipboardItem>>? ProvideContent { get; init; }

    /// <summary>
    /// Gets a function called with the new content each time the clipboard is written (the copy and cut actions,
    /// <c>navigator.clipboard.write()</c> and <c>writeText()</c>). It is called before <see cref="SystemClipboard.Changed"/>
    /// and the <c>clipboardchange</c> events, so their listeners read the new content.
    /// </summary>
    public Action<IReadOnlyList<SystemClipboardItem>>? ContentChanged { get; init; }

    /// <inheritdoc />
    public override IReadOnlyList<SystemClipboardItem> Items => ProvideContent?.Invoke() ?? Volatile.Read(ref _items);

    /// <inheritdoc />
    public override long ChangeCount => Interlocked.Read(ref _changeCount);

    /// <inheritdoc />
    public override void Write(IReadOnlyList<SystemClipboardItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        IReadOnlyList<SystemClipboardItem> content = [.. items];
        Volatile.Write(ref _items, content);
        Interlocked.Increment(ref _changeCount);
        ContentChanged?.Invoke(content);
        OnChanged();
    }

    /// <summary>
    /// Notifies the pages that the content returned by <see cref="ProvideContent"/> changed, as when another application
    /// writes to the clipboard: it fires the <c>clipboardchange</c> events. Call it on the thread of the event loop of the pages.
    /// </summary>
    public void NotifyContentChanged()
    {
        Interlocked.Increment(ref _changeCount);
        OnChanged();
    }

    /// <summary>Creates a clipboard item with a plain text representation (<c>text/plain</c>, UTF-8 encoded).</summary>
    /// <param name="text">The text.</param>
    /// <returns>The item.</returns>
    public static SystemClipboardItem CreateTextItem(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new SystemClipboardItem([new SystemClipboardRepresentation("text/plain", Encoding.UTF8.GetBytes(text))]);
    }

    /// <summary>Returns the plain text of clipboard items (the first <c>text/plain</c> representation, decoded as UTF-8), or null when there is none.</summary>
    /// <param name="items">The items, for instance the ones passed to <see cref="ContentChanged"/>.</param>
    /// <returns>The text, or null.</returns>
    public static string? GetText(IReadOnlyList<SystemClipboardItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (var item in items)
        {
            if (item.GetRepresentation("text/plain") is { } representation)
                return Encoding.UTF8.GetString(representation.Data.Span);
        }

        return null;
    }
}
