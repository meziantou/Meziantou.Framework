using System.Text;
using System.Text.Json;
using Meziantou.Framework.WebPlatform.Dom.FileApi;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://w3c.github.io/clipboard-apis/#enumdef-presentationstyle</summary>
/// <seealso href="https://w3c.github.io/clipboard-apis/#enumdef-presentationstyle"/>
[IdlInterface("PresentationStyle", "https://w3c.github.io/clipboard-apis/#enumdef-presentationstyle")]
public enum PresentationStyle
{
    /// <summary>The presentation style is not specified.</summary>
    /// <seealso href="https://w3c.github.io/clipboard-apis/#dom-presentationstyle-unspecified"/>
    [IdlMember("unspecified", "https://w3c.github.io/clipboard-apis/#dom-presentationstyle-unspecified")]
    Unspecified,

    /// <summary>The item is meant to be inserted inline in the document when it is pasted.</summary>
    /// <seealso href="https://w3c.github.io/clipboard-apis/#dom-presentationstyle-inline"/>
    [IdlMember("inline", "https://w3c.github.io/clipboard-apis/#dom-presentationstyle-inline")]
    Inline,

    /// <summary>The item is meant to be inserted as an attachment when it is pasted.</summary>
    /// <seealso href="https://w3c.github.io/clipboard-apis/#dom-presentationstyle-attachment"/>
    [IdlMember("attachment", "https://w3c.github.io/clipboard-apis/#dom-presentationstyle-attachment")]
    Attachment,
}

/// <summary>https://w3c.github.io/clipboard-apis/#dictdef-clipboarditemoptions</summary>
/// <seealso href="https://w3c.github.io/clipboard-apis/#dictdef-clipboarditemoptions"/>
[IdlInterface("ClipboardItemOptions", "https://w3c.github.io/clipboard-apis/#dictdef-clipboarditemoptions")]
public sealed class ClipboardItemOptions
{
    /// <summary>Gets the presentation style of the clipboard item.</summary>
    /// <seealso href="https://w3c.github.io/clipboard-apis/#dom-clipboarditemoptions-presentationstyle"/>
    [IdlMember("presentationStyle", "https://w3c.github.io/clipboard-apis/#dom-clipboarditemoptions-presentationstyle")]
    public PresentationStyle PresentationStyle { get; init; } = PresentationStyle.Unspecified;
}

/// <summary>The data of a representation of a <see cref="ClipboardItem"/>: a string or a <see cref="Blob"/> (the result of a <c>ClipboardItemData</c> promise).</summary>
/// <seealso href="https://w3c.github.io/clipboard-apis/#typedefdef-clipboarditemdata"/>
[IdlInterface("(DOMString or Blob)", "https://w3c.github.io/clipboard-apis/#typedefdef-clipboarditemdata")]
public readonly struct StringOrBlob : IUnionValue, IEquatable<StringOrBlob>
{
    private StringOrBlob(object value) => Value = value;

    /// <summary>Gets the value: a <see cref="string"/> or a <see cref="FileApi.Blob"/>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Gets the value when it is a string, or null.</summary>
    [NotInIdl]
    public string? StringValue => Value as string;

    /// <summary>Gets the value when it is a blob, or null.</summary>
    [NotInIdl]
    public Blob? Blob => Value as Blob;

    /// <summary>Converts a string to the union.</summary>
    [NotInIdl]
    public static implicit operator StringOrBlob(string value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts a blob to the union.</summary>
    [NotInIdl]
    public static implicit operator StringOrBlob(Blob value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates the union from a string.</summary>
    [NotInIdl]
    public static StringOrBlob FromString(string value) => value;

    /// <summary>Creates the union from a blob.</summary>
    [NotInIdl]
    public static StringOrBlob FromBlob(Blob value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(StringOrBlob other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is StringOrBlob other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <inheritdoc/>
    [NotInIdl]
    public override string? ToString() => Value?.ToString();

    /// <summary>Returns whether the two unions hold the same value.</summary>
    [NotInIdl]
    public static bool operator ==(StringOrBlob left, StringOrBlob right) => left.Equals(right);

    /// <summary>Returns whether the two unions hold different values.</summary>
    [NotInIdl]
    public static bool operator !=(StringOrBlob left, StringOrBlob right) => !left.Equals(right);
}

/// <summary>
/// The data of a clipboard item (https://w3c.github.io/clipboard-apis/#clipboarditem): representations of the same data
/// in different MIME types. An item created by <c>navigator.clipboard.read()</c> reads its data from the system clipboard
/// when <see cref="GetType(string)"/> is called, and fails when the clipboard changed since.
/// </summary>
/// <seealso href="https://w3c.github.io/clipboard-apis/#clipboarditem"/>
[IdlInterface("ClipboardItem", "https://w3c.github.io/clipboard-apis/#clipboarditem")]
public sealed class ClipboardItem
{
    private readonly List<ClipboardItemRepresentation> _representations = [];

    /// <summary>The representations with resolvers: the reads of the system clipboard of getType(), by MIME type.</summary>
    private Dictionary<string, Task<Blob>>? _representationsWithResolvers;

    /// <summary>https://w3c.github.io/clipboard-apis/#dom-clipboarditem-clipboarditem</summary>
    /// <exception cref="TypeErrorException"><paramref name="items"/> is empty, a type is not a valid MIME type, or two types are the same MIME type.</exception>
    /// <seealso href="https://w3c.github.io/clipboard-apis/#dom-clipboarditem-clipboarditem"/>
    [IdlMember("constructor", "https://w3c.github.io/clipboard-apis/#dom-clipboarditem-clipboarditem")]
    public ClipboardItem(IEnumerable<KeyValuePair<string, Task<StringOrBlob>>> items, ClipboardItemOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        PresentationStyle = options?.PresentationStyle ?? PresentationStyle.Unspecified;
        var types = new List<string>();
        foreach (var (key, value) in items)
        {
            if (key is null || value is null)
                throw new ArgumentException("The items cannot contain null keys or values", nameof(items));
            var type = key;
            var isCustom = ClipboardFormats.RemoveWebPrefix(ref type);
            var mimeType = MimeType.Parse(type) ?? throw new TypeErrorException($"'{key}' is not a valid MIME type");
            var serialized = mimeType.ToString();
            if (_representations.Exists(r => r.MimeType == serialized && r.IsCustom == isCustom))
                throw new TypeErrorException($"The clipboard item already has a representation of type '{key}'");

            _representations.Add(new ClipboardItemRepresentation(mimeType, isCustom, value));
            types.Add(isCustom ? ClipboardFormats.WebPrefix + serialized : serialized);
        }

        if (_representations.Count == 0)
            throw new TypeErrorException("The clipboard item has no representation");

        Types = types.AsReadOnly();
    }

    /// <summary>Creates a clipboard item from data available synchronously.</summary>
    [NotInIdl]
    public ClipboardItem(IEnumerable<KeyValuePair<string, StringOrBlob>> items, ClipboardItemOptions? options = null)
        : this(ToTasks(items), options)
    {
    }

    /// <summary>The steps of read(): an item whose data is read from the system clipboard by getType().</summary>
    private ClipboardItem(Clipboard clipboard, SystemClipboardItem? originatingItem, List<ClipboardItemRepresentation> representations, long changeCountAtRead, IReadOnlyList<string>? unsanitizedMimeTypes)
    {
        Clipboard = clipboard;
        OriginatingItem = originatingItem;
        ChangeCountAtRead = changeCountAtRead;
        UnsanitizedMimeTypes = unsanitizedMimeTypes ?? [];
        PresentationStyle = originatingItem?.PresentationStyle ?? PresentationStyle.Unspecified;
        _representations = representations;
        Types = representations.Select(r => r.IsCustom ? ClipboardFormats.WebPrefix + r.MimeType : r.MimeType).ToList().AsReadOnly();
    }

    /// <summary>The clipboard that read the item, or null for an item created by the constructor.</summary>
    private Clipboard? Clipboard { get; }

    /// <summary>The originating system clipboard item (https://w3c.github.io/clipboard-apis/#clipboarditem-originating-system-clipboard-item).</summary>
    private SystemClipboardItem? OriginatingItem { get; }

    /// <summary>The clipboard change count at read (https://w3c.github.io/clipboard-apis/#clipboarditem-clipboard-change-count-at-read), null for an item created by the constructor.</summary>
    private long? ChangeCountAtRead { get; }

    /// <summary>https://w3c.github.io/clipboard-apis/#clipboarditem-unsanitized-mime-types</summary>
    private IReadOnlyList<string> UnsanitizedMimeTypes { get; } = [];

    internal IReadOnlyList<ClipboardItemRepresentation> Representations => _representations;

    /// <summary>The data of a representation: the data given to the constructor, or the data read from the system clipboard by getType().</summary>
    internal Task<StringOrBlob> GetData(ClipboardItemRepresentation representation)
    {
        if (representation.Data is { } data)
            return data;

        return GetType(representation.IsCustom ? ClipboardFormats.WebPrefix + representation.MimeType : representation.MimeType)
            .ContinueWith(static task => (StringOrBlob)task.Result, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>https://w3c.github.io/clipboard-apis/#dom-clipboarditem-presentationstyle</summary>
    /// <seealso href="https://w3c.github.io/clipboard-apis/#dom-clipboarditem-presentationstyle"/>
    [IdlMember("presentationStyle", "https://w3c.github.io/clipboard-apis/#dom-clipboarditem-presentationstyle")]
    public PresentationStyle PresentationStyle { get; }

    /// <summary>Gets the MIME types of the representations (https://w3c.github.io/clipboard-apis/#dom-clipboarditem-types); the web custom formats have the <c>web </c> prefix.</summary>
    /// <seealso href="https://w3c.github.io/clipboard-apis/#dom-clipboarditem-types"/>
    [IdlMember("types", "https://w3c.github.io/clipboard-apis/#dom-clipboarditem-types")]
    public IReadOnlyList<string> Types { get; }

    /// <summary>
    /// Returns the data of the representation of a type as a blob (https://w3c.github.io/clipboard-apis/#dom-clipboarditem-gettype).
    /// The task fails with a <c>NotFoundError</c> <see cref="DomException"/> when there is no such representation, or an
    /// <c>InvalidStateError</c> when the item was read from the system clipboard and the clipboard changed since.
    /// </summary>
    /// <exception cref="TypeErrorException"><paramref name="type"/> is not a valid MIME type.</exception>
    /// <seealso href="https://w3c.github.io/clipboard-apis/#dom-clipboarditem-gettype"/>
    [IdlMember("getType", "https://w3c.github.io/clipboard-apis/#dom-clipboarditem-gettype")]
    public Task<Blob> GetType(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var isCustom = ClipboardFormats.RemoveWebPrefix(ref type);
        var mimeType = MimeType.Parse(type) ?? throw new TypeErrorException($"'{type}' is not a valid MIME type");
        var serialized = mimeType.ToString();

        // As in the browsers, the type of the blob of a web custom format has the "web " prefix (the type of the representation)
        var blobType = isCustom ? ClipboardFormats.WebPrefix + serialized : serialized;
        foreach (var representation in _representations)
        {
            if (representation.MimeType != serialized || representation.IsCustom != isCustom)
                continue;

            if (ChangeCountAtRead is { } changeCount)
            {
                // The data must correspond to the current state of the system clipboard
                if (Clipboard!.SystemClipboard.ChangeCount != changeCount)
                    return Task.FromException<Blob>(DomException.InvalidState("The clipboard changed since the item was read"));

                // The system clipboard is read once per type
                var key = isCustom ? ClipboardFormats.WebPrefix + mimeType.Essence : mimeType.Essence;
                _representationsWithResolvers ??= new(StringComparer.Ordinal);
                if (_representationsWithResolvers.TryGetValue(key, out var existing))
                    return existing;

                var result = ReadSystemClipboard(mimeType, isCustom);
                _representationsWithResolvers[key] = result;
                return result;
            }

            var completion = new TaskCompletionSource<Blob>();
            _ = representation.Data!.ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    var value = task.Result;
                    completion.TrySetResult(value.Blob ?? new Blob([Encoding.UTF8.GetBytes(InfraString.ToScalarValueString(value.StringValue ?? ""))], new BlobPropertyBag { Type = blobType }));
                }
                else
                {
                    completion.TrySetException(DomException.NotFound($"The data of the representation of type '{serialized}' is not available"));
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return completion.Task;
        }

        return Task.FromException<Blob>(DomException.NotFound($"The clipboard item has no representation of type '{serialized}'"));
    }

    /// <summary>The steps of getType() for an item read from the system clipboard: the data is read, then the promise is settled from a task.</summary>
    private Task<Blob> ReadSystemClipboard(MimeType mimeType, bool isCustom)
    {
        var completion = new TaskCompletionSource<Blob>();
        var clipboard = Clipboard!;
        void Settle(Action action)
        {
            if (clipboard.Window.Document.BrowsingContext is null)
            {
                action();
            }
            else
            {
                clipboard.Window.Document.QueueGlobalTask(action);
            }
        }

        if (OriginatingItem is not { } item)
        {
            Settle(() => completion.TrySetException(DomException.InvalidState("The clipboard item does not originate from the system clipboard")));
            return completion.Task;
        }

        string? formatName;
        if (isCustom)
        {
            formatName = ClipboardFormats.GetWebCustomFormatName(item, mimeType.ToString());
        }
        else
        {
            formatName = ClipboardFormats.GetWellKnownFormat(mimeType.Essence);
        }

        if (formatName is null || item.GetRepresentation(formatName) is not { } representation)
        {
            Settle(() => completion.TrySetException(DomException.NotFound($"The system clipboard has no data of type '{mimeType}'")));
            return completion.Task;
        }

        var data = representation.Data.ToArray();
        var unsanitized = UnsanitizedMimeTypes.Contains(mimeType.Essence, StringComparer.Ordinal) && ClipboardFormats.IsOptionalUnsanitizedDataType(mimeType.Essence);
        if (!unsanitized && !isCustom && mimeType.Essence == "text/html")
        {
            data = Encoding.UTF8.GetBytes(ClipboardFormats.SanitizeHtml(Encoding.UTF8.GetString(data)));
        }

        var blob = new Blob([data], new BlobPropertyBag { Type = isCustom ? ClipboardFormats.WebPrefix + mimeType : mimeType.ToString() });
        Settle(() => completion.TrySetResult(blob));
        return completion.Task;
    }

    /// <summary>Whether the type is a mandatory or an optional data type (https://w3c.github.io/clipboard-apis/#dom-clipboarditem-supports).</summary>
    /// <seealso href="https://w3c.github.io/clipboard-apis/#dom-clipboarditem-supports"/>
    [IdlMember("supports", "https://w3c.github.io/clipboard-apis/#dom-clipboarditem-supports")]
    public static bool Supports(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return ClipboardFormats.IsSupportedType(type);
    }

    /// <summary>The steps of read() that create a clipboard item from a system clipboard item, or from the web custom formats when <paramref name="item"/> is null.</summary>
    internal static ClipboardItem? FromSystemClipboard(Clipboard clipboard, SystemClipboardItem? item, SystemClipboardItem? customFormatsItem, long changeCount, IReadOnlyList<string>? unsanitizedMimeTypes)
    {
        var representations = new List<ClipboardItemRepresentation>();
        if (item is not null)
        {
            foreach (var systemRepresentation in item.Representations)
            {
                if (ClipboardFormats.GetWellKnownMimeType(systemRepresentation.Name) is { } mimeType && !representations.Exists(r => !r.IsCustom && r.MimeType == mimeType.ToString()))
                {
                    representations.Add(new ClipboardItemRepresentation(mimeType, isCustom: false, data: null));
                }
            }
        }

        // https://w3c.github.io/clipboard-apis/#read-web-custom-format: the web custom formats are representations of the first item
        if (customFormatsItem is not null)
        {
            foreach (var customType in ClipboardFormats.GetWebCustomFormats(customFormatsItem).Keys)
            {
                if (MimeType.Parse(customType) is { } mimeType)
                {
                    representations.Add(new ClipboardItemRepresentation(mimeType, isCustom: true, data: null));
                }
            }
        }

        if (representations.Count == 0)
            return null;

        return new ClipboardItem(clipboard, item ?? customFormatsItem, representations, changeCount, unsanitizedMimeTypes);
    }

    private static List<KeyValuePair<string, Task<StringOrBlob>>> ToTasks(IEnumerable<KeyValuePair<string, StringOrBlob>> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Select(item => new KeyValuePair<string, Task<StringOrBlob>>(item.Key, Task.FromResult(item.Value))).ToList();
    }
}

/// <summary>A representation of a clipboard item (https://w3c.github.io/clipboard-apis/#representation): its MIME type (serialized), whether it is a web custom format, and its data (null when it is read from the system clipboard).</summary>
internal sealed class ClipboardItemRepresentation(MimeType mimeType, bool isCustom, Task<StringOrBlob>? data)
{
    public string MimeType { get; } = mimeType.ToString();

    public string Essence { get; } = mimeType.Essence;

    public bool IsCustom { get; } = isCustom;

    public Task<StringOrBlob>? Data { get; } = data;
}

/// <summary>The data types of the clipboard and their formats on the system clipboard (https://w3c.github.io/clipboard-apis/#mandatory-data-types-x).</summary>
internal static class ClipboardFormats
{
    public const string WebPrefix = "web ";

    /// <summary>The maximum number of web custom formats written to the system clipboard (https://w3c.github.io/clipboard-apis/#write-web-custom-formats).</summary>
    private const int MaxWebCustomFormats = 100;

    /// <summary>https://w3c.github.io/clipboard-apis/#mandatory-data-types-x</summary>
    public static readonly string[] MandatoryDataTypes = ["text/plain", "text/html", "image/png"];

    /// <summary>https://w3c.github.io/clipboard-apis/#optional-data-types-x (without the web custom formats).</summary>
    private static readonly string[] OptionalDataTypes = ["text/uri-list", "image/svg+xml"];

    /// <summary>Removes the <c>web </c> prefix of a type. Returns whether the type had the prefix (a web custom format).</summary>
    public static bool RemoveWebPrefix(ref string type)
    {
        if (!type.StartsWith(WebPrefix, StringComparison.Ordinal))
            return false;

        type = type[WebPrefix.Length..];
        return true;
    }

    /// <summary>Whether the type is in the mandatory data types or the optional data types (https://w3c.github.io/clipboard-apis/#optional-data-types-x).</summary>
    public static bool IsSupportedType(string type)
    {
        if (MandatoryDataTypes.Contains(type, StringComparer.Ordinal) || OptionalDataTypes.Contains(type, StringComparer.Ordinal))
            return true;

        // A web custom format: "web " followed by a MIME type (without parameters, as in browsers)
        return RemoveWebPrefix(ref type) && MimeType.Parse(type) is { Parameters.Count: 0 };
    }

    /// <summary>https://w3c.github.io/clipboard-apis/#optional-unsanitized-data-types</summary>
    public static bool IsOptionalUnsanitizedDataType(string type) => type == "text/html";

    /// <summary>The format of a well-known MIME type essence on the system clipboard (https://w3c.github.io/clipboard-apis/#os-specific-well-known-format), or null.</summary>
    public static string? GetWellKnownFormat(string essence) => essence is "text/plain" or "text/html" or "image/png" or "image/svg+xml" ? essence : null;

    /// <summary>The well-known MIME type of a format of the system clipboard (https://w3c.github.io/clipboard-apis/#well-known-mime-type-from-os-specific-format), or null.</summary>
    public static MimeType? GetWellKnownMimeType(string formatName) => formatName is "text/plain" or "text/html" or "image/png" or "image/svg+xml" ? MimeType.Parse(formatName) : null;

    /// <summary>The web custom formats of a system clipboard item (its web custom format map): the format of each MIME type.</summary>
    public static Dictionary<string, string> GetWebCustomFormats(SystemClipboardItem item)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (item.GetRepresentation(SystemClipboard.WebCustomFormatMapName) is not { } map)
            return result;

        try
        {
            using var document = JsonDocument.Parse(map.Data);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return result;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    result[property.Name] = property.Value.GetString()!;
                }
            }
        }
        catch (JsonException)
        {
        }

        return result;
    }

    /// <summary>The format of the web custom format of a MIME type (serialized) on a system clipboard item, or null.</summary>
    public static string? GetWebCustomFormatName(SystemClipboardItem item, string mimeType) => GetWebCustomFormats(item).TryGetValue(mimeType, out var name) ? name : null;

    /// <summary>
    /// Adds the web custom formats to the representations of a system clipboard item, with the web custom format map
    /// (https://w3c.github.io/clipboard-apis/#write-web-custom-formats). The formats after the hundredth are ignored.
    /// </summary>
    public static void AddWebCustomFormats(List<SystemClipboardRepresentation> representations, IReadOnlyList<(string MimeType, byte[] Data)> formats)
    {
        if (formats.Count == 0)
            return;

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < formats.Count && i < MaxWebCustomFormats; i++)
        {
            var name = SystemClipboard.WebCustomFormatName + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"";
            map[formats[i].MimeType] = name;
            representations.Add(new SystemClipboardRepresentation(name, formats[i].Data));
        }

        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var (mimeType, name) in map)
            {
                writer.WriteString(mimeType, name);
            }

            writer.WriteEndObject();
        }

        representations.Add(new SystemClipboardRepresentation(SystemClipboard.WebCustomFormatMapName, buffer.WrittenMemory.ToArray()));
    }

    /// <summary>The mandatory data types available on the system clipboard, in order.</summary>
    public static List<string> GetAvailableMandatoryDataTypes(SystemClipboard clipboard)
    {
        var types = new List<string>();
        foreach (var item in clipboard.Items)
        {
            foreach (var representation in item.Representations)
            {
                if (MandatoryDataTypes.Contains(representation.Name, StringComparer.Ordinal) && !types.Contains(representation.Name, StringComparer.Ordinal))
                {
                    types.Add(representation.Name);
                }
            }
        }

        return types;
    }

    /// <summary>
    /// The sanitized copy of HTML read from the system clipboard: the scripts and the other unsafe content are removed by the
    /// HTML Sanitizer (the safe default configuration of <c>Document.parseHTML()</c>).
    /// </summary>
    public static string SanitizeHtml(string html)
    {
        var document = new HtmlDocumentParser(new HtmlParserOptions { ScriptingEnabled = false }).Parse(html);
        var sanitizer = Sanitizer.FromOptions(SanitizerPresets.Default, safe: true);
        Html.SanitizerConfiguration.Sanitize(document, sanitizer.Configuration, safe: true);
        return document.GetHtml();
    }
}
