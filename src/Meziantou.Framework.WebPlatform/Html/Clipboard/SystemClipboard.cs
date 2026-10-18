using System.Text;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The system clipboard (https://w3c.github.io/clipboard-apis/#system-clipboard) of the browsing contexts that share a
/// <see cref="BrowsingContextOptions"/>: the data of the copy and cut actions, of <c>navigator.clipboard.write()</c> and
/// <c>writeText()</c>, read by the paste action, <c>read()</c> and <c>readText()</c>. The default implementation keeps the
/// data in memory: the application can read and replace it to observe or simulate what the user copies. Override
/// <see cref="Items"/>, <see cref="ChangeCount"/> and <see cref="Write"/> to use another clipboard (e.g. the clipboard of
/// the operating system), and call <see cref="OnChanged"/> when it is changed by another application.
/// </summary>
/// <remarks>
/// The names of the representations are the formats of the clipboard of Linux, ChromeOS and Android
/// (https://w3c.github.io/clipboard-apis/#os-specific-well-known-format): the MIME types <c>text/plain</c>,
/// <c>text/html</c>, <c>image/png</c> and <c>image/svg+xml</c>, and for the web custom formats,
/// <c>application/web;type="custom/formatmap"</c> (a JSON object mapping each MIME type to its format) and
/// <c>application/web;type="custom/format0"</c>, <c>application/web;type="custom/format1"</c>... The data written by the
/// <c>DataTransfer</c> of a copy event with other types is stored under these types.
/// </remarks>
public class SystemClipboard
{
    /// <summary>The name of the representation of the web custom format map (https://w3c.github.io/clipboard-apis/#os-specific-custom-map-name).</summary>
    public const string WebCustomFormatMapName = "application/web;type=\"custom/formatmap\"";

    /// <summary>The prefix of the names of the representations of the web custom formats (https://w3c.github.io/clipboard-apis/#os-specific-custom-name).</summary>
    public const string WebCustomFormatName = "application/web;type=\"custom/format";

    private readonly List<WeakReference<Clipboard>> _clipboards = [];
    private IReadOnlyList<SystemClipboardItem> _items = [];
    private long _changeCount;

    /// <summary>Raised when the data of the clipboard changes (<see cref="OnChanged"/>), e.g. to fire the <c>clipboardchange</c> events.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the system clipboard items (https://w3c.github.io/clipboard-apis/#system-clipboard-data).</summary>
    public virtual IReadOnlyList<SystemClipboardItem> Items => _items;

    /// <summary>Gets the clipboard change count (https://w3c.github.io/clipboard-apis/#clipboard-change-count): a value that changes each time the data is modified.</summary>
    public virtual long ChangeCount => _changeCount;

    /// <summary>Replaces the data of the clipboard, then raises <see cref="Changed"/>. An override must call <see cref="OnChanged"/> when the data changed.</summary>
    public virtual void Write(IReadOnlyList<SystemClipboardItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items.ToArray();
        _changeCount++;
        OnChanged();
    }

    /// <summary>Clears the clipboard.</summary>
    public void Clear() => Write([]);

    /// <summary>Replaces the data of the clipboard with a plain text representation (<c>text/plain</c>, UTF-8 encoded).</summary>
    public void WriteText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Write([new SystemClipboardItem([new SystemClipboardRepresentation("text/plain", Encoding.UTF8.GetBytes(text))])]);
    }

    /// <summary>Returns the plain text of the clipboard (the first <c>text/plain</c> representation, decoded as UTF-8), or null when there is none.</summary>
    public string? ReadText()
    {
        foreach (var item in Items)
        {
            if (item.GetRepresentation("text/plain") is { } representation)
                return Encoding.UTF8.GetString(representation.Data.Span);
        }

        return null;
    }

    /// <summary>
    /// Raises <see cref="Changed"/>, then fires the <c>clipboardchange</c> events of the documents. It must be called on the
    /// thread of the event loop of the browsing contexts.
    /// </summary>
    protected void OnChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        foreach (var clipboard in GetClipboards())
        {
            clipboard.OnSystemClipboardChanged();
        }
    }

    /// <summary>Registers the <c>navigator.clipboard</c> object of a window, to fire its <c>clipboardchange</c> events.</summary>
    internal void Register(Clipboard clipboard)
    {
        lock (_clipboards)
        {
            _clipboards.RemoveAll(reference => !reference.TryGetTarget(out _));
            _clipboards.Add(new WeakReference<Clipboard>(clipboard));
        }
    }

    internal List<Clipboard> GetClipboards()
    {
        var result = new List<Clipboard>();
        lock (_clipboards)
        {
            foreach (var reference in _clipboards)
            {
                if (reference.TryGetTarget(out var clipboard))
                {
                    result.Add(clipboard);
                }
            }
        }

        return result;
    }
}

/// <summary>A system clipboard item (https://w3c.github.io/clipboard-apis/#system-clipboard-item): the representations of the same data.</summary>
public sealed class SystemClipboardItem
{
    /// <summary>Creates a system clipboard item with the given representations and presentation style.</summary>
    public SystemClipboardItem(IReadOnlyList<SystemClipboardRepresentation> representations, PresentationStyle presentationStyle = PresentationStyle.Unspecified)
    {
        ArgumentNullException.ThrowIfNull(representations);
        Representations = representations.ToArray();
        PresentationStyle = presentationStyle;
    }

    /// <summary>Gets the representations of the item.</summary>
    public IReadOnlyList<SystemClipboardRepresentation> Representations { get; }

    /// <summary>Gets whether the item should be inserted inline or as an attachment when it is pasted.</summary>
    public PresentationStyle PresentationStyle { get; }

    /// <summary>Returns the representation with the name, or null.</summary>
    public SystemClipboardRepresentation? GetRepresentation(string name)
    {
        foreach (var representation in Representations)
        {
            if (representation.Name == name)
                return representation;
        }

        return null;
    }
}

/// <summary>A system clipboard representation (https://w3c.github.io/clipboard-apis/#system-clipboard-representation): a format and its bytes.</summary>
public sealed class SystemClipboardRepresentation
{
    /// <summary>Creates a representation with the given format name and data.</summary>
    public SystemClipboardRepresentation(string name, ReadOnlyMemory<byte> data)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        Data = data;
    }

    /// <summary>Gets the name of the format (see <see cref="SystemClipboard"/>).</summary>
    public string Name { get; }

    /// <summary>Gets the data.</summary>
    public ReadOnlyMemory<byte> Data { get; }
}

/// <summary>Whether the user agent requests access to the system clipboard to read it or to write it (see <see cref="BrowsingContextOptions.ClipboardPermission"/>).</summary>
public enum ClipboardAccess
{
    /// <summary>Read the clipboard: <c>navigator.clipboard.read()</c> and <c>readText()</c>, the paste command of <c>document.execCommand()</c>.</summary>
    Read,

    /// <summary>Write the clipboard: <c>navigator.clipboard.write()</c> and <c>writeText()</c>, the copy and cut commands of <c>document.execCommand()</c>.</summary>
    Write,
}
