using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Meziantou.Framework.WebPlatform.Dom.FileApi;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://w3c.github.io/clipboard-apis/#dictdef-clipboardunsanitizedformats</summary>
[IdlInterface("ClipboardUnsanitizedFormats", "https://w3c.github.io/clipboard-apis/#dictdef-clipboardunsanitizedformats")]
public sealed class ClipboardUnsanitizedFormats
{
    /// <summary>The MIME types whose data must not be sanitized (only <c>text/html</c> is allowed), or null when the member is not present.</summary>
    [IdlMember("unsanitized", "https://w3c.github.io/clipboard-apis/#dom-clipboardunsanitizedformats-unsanitized")]
    public IReadOnlyList<string>? Unsanitized { get; init; }
}

/// <summary>
/// The asynchronous clipboard API (https://w3c.github.io/clipboard-apis/#clipboard-interface) of a window
/// (<c>navigator.clipboard</c>): reads and writes the <see cref="SystemClipboard"/> of the browsing context
/// (<see cref="BrowsingContextOptions.Clipboard"/>), and fires <c>clipboardchange</c> when it changes.
/// </summary>
/// <remarks>
/// The clipboard is only available to a fully active document of a secure context that has the focus, and when
/// <see cref="BrowsingContextOptions.ClipboardPermission"/> allows the access; the promises are rejected with a
/// <c>NotAllowedError</c> <see cref="DomException"/> otherwise. There is no user activation: the access does not require a
/// user gesture. The HTML read from the clipboard is sanitized by the HTML Sanitizer, unless it is requested unsanitized.
/// The <c>clipboardchange</c> events are fired at this object (as in the browsers, and in the examples of the
/// specification), for the documents allowed to read the clipboard.
/// </remarks>
[IdlInterface("Clipboard", "https://w3c.github.io/clipboard-apis/#clipboard")]
public sealed class Clipboard : EventTarget
{
    private bool _clipboardChangePending;

    internal Clipboard(Window window)
    {
        Window = window;
        SystemClipboard = window.Options.EffectiveClipboard;
        SystemClipboard.Register(this);
    }

    internal Window Window { get; }

    internal SystemClipboard SystemClipboard { get; }

    internal override Window? RelevantGlobal => Window;

    /// <summary>
    /// Reads the items of the system clipboard (https://w3c.github.io/clipboard-apis/#dom-clipboard-read): their types are
    /// known, their data is read by <see cref="ClipboardItem.GetType(string)"/>.
    /// </summary>
    [IdlMember("read", "https://w3c.github.io/clipboard-apis/#dom-clipboard-read")]
    public Task<IReadOnlyList<ClipboardItem>> Read(ClipboardUnsanitizedFormats? formats = null)
    {
        var unsanitized = formats?.Unsanitized;
        if (unsanitized is not null)
        {
            foreach (var format in unsanitized)
            {
                if (!ClipboardFormats.IsOptionalUnsanitizedDataType(format))
                    return Task.FromException<IReadOnlyList<ClipboardItem>>(DomException.NotAllowed($"'{format}' cannot be read unsanitized"));
            }
        }

        if (!ClipboardPermissions.CheckAsyncClipboardAccess(Window, ClipboardAccess.Read))
            return Task.FromException<IReadOnlyList<ClipboardItem>>(DomException.NotAllowed("The document is not allowed to read the clipboard"));

        // Only the types are read now: the data is read by getType()
        var data = SystemClipboard.Items;
        var snapshotChangeCount = SystemClipboard.ChangeCount;
        var customFormatsItem = data.FirstOrDefault(item => item.GetRepresentation(SystemClipboard.WebCustomFormatMapName) is not null);
        var items = new List<ClipboardItem>();
        foreach (var systemItem in data)
        {
            var item = ClipboardItem.FromSystemClipboard(this, systemItem, items.Count == 0 ? customFormatsItem : null, snapshotChangeCount, unsanitized);
            if (item is not null)
            {
                items.Add(item);
            }
        }

        if (items.Count == 0 && customFormatsItem is not null && ClipboardItem.FromSystemClipboard(this, item: null, customFormatsItem, snapshotChangeCount, unsanitized) is { } customItem)
        {
            items.Add(customItem);
        }

        var completion = new TaskCompletionSource<IReadOnlyList<ClipboardItem>>();
        Window.Document.QueueGlobalTask(() => completion.TrySetResult(items.AsReadOnly()));
        return completion.Task;
    }

    /// <summary>Reads the text of the system clipboard (https://w3c.github.io/clipboard-apis/#dom-clipboard-readtext): the task fails with a <c>NotFoundError</c> when there is no text.</summary>
    [IdlMember("readText", "https://w3c.github.io/clipboard-apis/#dom-clipboard-readtext")]
    public Task<string> ReadText()
    {
        if (!ClipboardPermissions.CheckAsyncClipboardAccess(Window, ClipboardAccess.Read))
            return Task.FromException<string>(DomException.NotAllowed("The document is not allowed to read the clipboard"));

        var data = SystemClipboard.Items;
        var completion = new TaskCompletionSource<string>();
        Window.Document.QueueGlobalTask(() =>
        {
            foreach (var item in data)
            {
                foreach (var representation in item.Representations)
                {
                    if (ClipboardFormats.GetWellKnownMimeType(representation.Name) is { Essence: "text/plain" })
                    {
                        completion.TrySetResult(Encoding.UTF8.GetString(representation.Data.Span));
                        return;
                    }
                }
            }

            completion.TrySetException(DomException.NotFound("The clipboard has no text"));
        });
        return completion.Task;
    }

    /// <summary>
    /// Writes items to the system clipboard (https://w3c.github.io/clipboard-apis/#dom-clipboard-write), once the data of
    /// their representations is available. The task fails with a <c>NotAllowedError</c> when the data of a representation
    /// is not available, or when its type is not a mandatory or an optional data type (or not the type of the representation).
    /// </summary>
    [IdlMember("write", "https://w3c.github.io/clipboard-apis/#dom-clipboard-write")]
    public Task Write(IReadOnlyList<ClipboardItem> data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!ClipboardPermissions.CheckAsyncClipboardAccess(Window, ClipboardAccess.Write))
            return Task.FromException(DomException.NotAllowed("The document is not allowed to write the clipboard"));

        var items = data.ToArray();
        var completion = new TaskCompletionSource();
        Window.Document.QueueGlobalTask(() =>
        {
            // React to the promises of the data of the representations
            var itemsData = items.Select(item => (Item: item, Data: item.Representations.Select(r => (Representation: r, Data: item.GetData(r))).ToArray())).ToArray();
            _ = Task.WhenAll(itemsData.SelectMany(item => item.Data).Select(r => r.Data)).ContinueWith(task =>
            {
                if (!task.IsCompletedSuccessfully)
                {
                    completion.TrySetException(DomException.NotAllowed("The data of a representation of the clipboard item is not available"));
                    return;
                }

                var systemItems = new List<SystemClipboardItem>();
                foreach (var (item, itemData) in itemsData)
                {
                    if (CreateSystemClipboardItem(item, itemData) is not { } systemItem)
                    {
                        completion.TrySetException(DomException.NotAllowed("The type of a representation of the clipboard item is not supported"));
                        return;
                    }

                    systemItems.Add(systemItem);
                }

                SystemClipboard.Write(systemItems);
                completion.TrySetResult();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        });
        return completion.Task;
    }

    /// <summary>Writes text to the system clipboard (https://w3c.github.io/clipboard-apis/#dom-clipboard-writetext).</summary>
    [IdlMember("writeText", "https://w3c.github.io/clipboard-apis/#dom-clipboard-writetext")]
    public Task WriteText(string data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!ClipboardPermissions.CheckAsyncClipboardAccess(Window, ClipboardAccess.Write))
            return Task.FromException(DomException.NotAllowed("The document is not allowed to write the clipboard"));

        var completion = new TaskCompletionSource();
        Window.Document.QueueGlobalTask(() =>
        {
            SystemClipboard.WriteText(InfraString.ToScalarValueString(data));
            completion.TrySetResult();
        });
        return completion.Task;
    }

    /// <summary>
    /// The blobs of the representations of an item, written to the system clipboard (the steps of write() and
    /// https://w3c.github.io/clipboard-apis/#write-blobs-and-option-to-the-clipboard), or null when a type is not supported.
    /// </summary>
    private static SystemClipboardItem? CreateSystemClipboardItem(ClipboardItem item, (ClipboardItemRepresentation Representation, Task<StringOrBlob> Data)[] itemData)
    {
        var representations = new List<SystemClipboardRepresentation>();
        var webCustomFormats = new List<(string MimeType, byte[] Data)>();
        foreach (var (representation, data) in itemData)
        {
            var value = data.Result;
            var type = representation.IsCustom ? ClipboardFormats.WebPrefix + representation.Essence : representation.Essence;
            byte[] bytes;
            if (value.Blob is { } blob)
            {
                bytes = blob.Data.ToArray();

                // As in the browsers, the type of the blob is the type of the representation (the "web " prefix of a web
                // custom format may be omitted), or empty
                var blobType = blob.Type;
                if (representation.IsCustom)
                {
                    ClipboardFormats.RemoveWebPrefix(ref blobType);
                }

                if (blobType.Length > 0 && MimeType.Parse(blobType)?.Essence != representation.Essence)
                    return null;
            }
            else
            {
                bytes = Encoding.UTF8.GetBytes(InfraString.ToScalarValueString(value.StringValue ?? ""));
            }

            if (!ClipboardFormats.IsSupportedType(type))
                return null;

            if (representation.IsCustom)
            {
                webCustomFormats.Add((representation.MimeType, bytes));
            }
            else
            {
                representations.Add(new SystemClipboardRepresentation(ClipboardFormats.GetWellKnownFormat(representation.Essence) ?? representation.Essence, bytes));
            }
        }

        ClipboardFormats.AddWebCustomFormats(representations, webCustomFormats);
        return new SystemClipboardItem(representations, item.PresentationStyle);
    }

    /// <summary>
    /// Fires a clipboardchange event (https://w3c.github.io/clipboard-apis/#fire-a-clipboardchange-event) when the system
    /// clipboard changed: the event is fired when the document has the focus, else when it gains the focus.
    /// </summary>
    internal void OnSystemClipboardChanged()
    {
        var document = Window.Document;
        if (!ReferenceEquals(document.BrowsingContext?.Document, document) || !document.IsFullyActive || !ClipboardPermissions.IsAllowed(Window, ClipboardAccess.Read))
            return;

        if (FocusModel.HasFocus(document))
        {
            FireClipboardChange();
        }
        else
        {
            _clipboardChangePending = true;
        }
    }

    /// <summary>The document focus steps (https://w3c.github.io/clipboard-apis/#document-focus-steps): fires the pending clipboardchange event.</summary>
    internal void OnDocumentFocused()
    {
        if (!_clipboardChangePending)
            return;

        _clipboardChangePending = false;
        FireClipboardChange();
    }

    private void FireClipboardChange()
    {
        var types = ClipboardFormats.GetAvailableMandatoryDataTypes(SystemClipboard);
        FireEvent(new ClipboardChangeEvent("clipboardchange", new ClipboardChangeEventInit { Types = types, ChangeId = GenerateChangeId() }));
    }

    /// <summary>
    /// https://w3c.github.io/clipboard-apis/#generate-a-changeid: a 128-bit integer derived from the change count of the
    /// clipboard and the origin of the document (the storage key), the same for the documents of the same origin.
    /// </summary>
    private BigInteger GenerateChangeId()
    {
        var bytes = Encoding.UTF8.GetBytes(SystemClipboard.ChangeCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + Window.Document.SerializeOrigin());
        var hash = SHA256.HashData(bytes);
        return new BigInteger(hash.AsSpan(0, 16), isUnsigned: true, isBigEndian: true);
    }
}

/// <summary>The permissions of the documents to access the system clipboard (https://w3c.github.io/clipboard-apis/#permissions).</summary>
internal static class ClipboardPermissions
{
    /// <summary>
    /// Whether the document of the window may access the clipboard: it is fully active, and
    /// <see cref="BrowsingContextOptions.ClipboardPermission"/> allows the access (by default, all the documents are allowed).
    /// </summary>
    public static bool IsAllowed(Window window, ClipboardAccess access)
    {
        var document = window.Document;
        if (!document.IsFullyActive)
            return false;

        return window.Options.ClipboardPermission?.Invoke(document, access) ?? true;
    }

    /// <summary>
    /// The check clipboard read and write permission steps of the asynchronous clipboard API
    /// (https://w3c.github.io/clipboard-apis/#check-clipboard-read-permission): the API is only available to a secure
    /// context whose document has the focus (https://w3c.github.io/clipboard-apis/#privacy-async) and is allowed to use the
    /// "clipboard-read" or "clipboard-write" policy-controlled feature (their default allowlist is 'self').
    /// </summary>
    public static bool CheckAsyncClipboardAccess(Window window, ClipboardAccess access)
        => IsAllowed(window, access)
            && window.IsSecureContext
            && PermissionsPolicyAlgorithms.IsAllowedToUse(window.Document, access == ClipboardAccess.Read ? PolicyControlledFeature.ClipboardRead : PolicyControlledFeature.ClipboardWrite)
            && FocusModel.HasFocus(window.Document);
}
