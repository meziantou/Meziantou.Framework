using Meziantou.Framework.WebPlatform.Dom.FileApi;
using File = Meziantou.Framework.WebPlatform.Dom.FileApi.File;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The kind of a drag data item (https://html.spec.whatwg.org/multipage/dnd.html#the-drag-data-item-kind).</summary>
internal enum DragDataItemKind
{
    Text,
    File,
}

/// <summary>The mode of a drag data store (https://html.spec.whatwg.org/multipage/dnd.html#drag-data-store-mode).</summary>
internal enum DragDataStoreMode
{
    ReadWrite,
    ReadOnly,
    Protected,
}

/// <summary>An item of the drag data store item list: its kind, its type string and its data (a string or a <see cref="File"/>).</summary>
internal sealed class DragDataItem(DragDataItemKind kind, string type, object data)
{
    public DragDataItemKind Kind { get; } = kind;

    public string Type { get; } = type;

    public object Data { get; } = data;
}

/// <summary>The data that underlies a drag-and-drop operation (https://html.spec.whatwg.org/multipage/dnd.html#drag-data-store).</summary>
internal sealed class DragDataStore
{
    public List<DragDataItem> Items { get; } = [];

    public DragDataStoreMode Mode { get; set; } = DragDataStoreMode.Protected;

    public string AllowedEffectsState { get; set; } = "uninitialized";

    /// <summary>The element and the hot spot coordinate of setDragImage() (the drag data store bitmap).</summary>
    public (Element Image, int X, int Y)? Bitmap { get; set; }

    /// <summary>The clear-was-called flag and the types-to-clear list of a clipboard event (https://w3c.github.io/clipboard-apis/#fire-a-clipboard-event), null for a drag-and-drop operation.</summary>
    public ClipboardClearState? ClipboardClear { get; init; }

    /// <summary>Records a call to clearData() or clear() during a clipboard event: <paramref name="format"/> is null when all the data is cleared.</summary>
    public void OnCleared(string? format)
    {
        if (ClipboardClear is not { } state || Mode != DragDataStoreMode.ReadWrite)
            return;

        state.ClearWasCalled = true;
        if (format is not null)
        {
            state.TypesToClear.Add(format);
        }
    }

    /// <summary>Records a call to setData() or items.add() during a clipboard event.</summary>
    public void OnDataSet(string type)
    {
        if (ClipboardClear is not { ClearWasCalled: true } state)
            return;

        if (state.TypesToClear.Count == 0 || (state.TypesToClear.Remove(type) && state.TypesToClear.Count == 0))
        {
            state.ClearWasCalled = false;
        }
    }
}

/// <summary>The clear-was-called flag and the types-to-clear list of a clipboard event (https://w3c.github.io/clipboard-apis/#fire-a-clipboard-event).</summary>
internal sealed class ClipboardClearState
{
    public bool ClearWasCalled { get; set; }

    public List<string> TypesToClear { get; } = [];
}

/// <summary>https://html.spec.whatwg.org/multipage/dnd.html#functionstringcallback</summary>
[IdlInterface("FunctionStringCallback", "https://html.spec.whatwg.org/multipage/dnd.html#functionstringcallback")]
public delegate void FunctionStringCallback(string data);

/// <summary>
/// Exposes the drag data store of a drag-and-drop operation (https://html.spec.whatwg.org/multipage/dnd.html#the-datatransfer-interface).
/// The objects created for the drag-and-drop events are only associated with the drag data store while the event is dispatched.
/// </summary>
[IdlInterface("DataTransfer", "https://html.spec.whatwg.org/multipage/dnd.html#datatransfer")]
public sealed class DataTransfer
{
    private readonly List<File> _files = [];
    private string _dropEffect;
    private string _effectAllowed;
    private IReadOnlyList<string> _types = [];
    private DataTransferItemList? _items;
    private FileList? _fileList;

    /// <summary>https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransfer</summary>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransfer")]
    public DataTransfer()
        : this(new DragDataStore { Mode = DragDataStoreMode.ReadWrite }, dropEffect: "none", effectAllowed: "none")
    {
    }

    internal DataTransfer(DragDataStore store, string dropEffect, string effectAllowed)
    {
        Store = store;
        _dropEffect = dropEffect;
        _effectAllowed = effectAllowed;
        UpdateTypes();
    }

    /// <summary>The associated drag data store, or null when the association is broken.</summary>
    internal DragDataStore? Store { get; private set; }

    /// <summary>https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransfer-dropeffect</summary>
    [IdlMember("dropEffect", "https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransfer-dropeffect")]
    public string DropEffect
    {
        get => _dropEffect;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value is "none" or "copy" or "link" or "move")
            {
                _dropEffect = value;
            }
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransfer-effectallowed</summary>
    [IdlMember("effectAllowed", "https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransfer-effectallowed")]
    public string EffectAllowed
    {
        get => _effectAllowed;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (Store is { Mode: DragDataStoreMode.ReadWrite } && value is "none" or "copy" or "copyLink" or "copyMove" or "link" or "linkMove" or "move" or "all" or "uninitialized")
            {
                _effectAllowed = value;
            }
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransfer-items</summary>
    [IdlMember("items", "https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransfer-items")]
    public DataTransferItemList Items => _items ??= new DataTransferItemList(this);

    /// <summary>https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransfer-setdragimage</summary>
    [IdlMember("setDragImage", "https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransfer-setdragimage")]
    public void SetDragImage(Element image, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (Store is not { Mode: DragDataStoreMode.ReadWrite } store)
            return;

        store.Bitmap = (image, x, y);
    }

    /// <summary>The types array (https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransfer-types): the same array until the drag data store item list changes.</summary>
    [IdlMember("types", "https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransfer-types")]
    public IReadOnlyList<string> Types => _types;

    /// <summary>https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransfer-getdata</summary>
    [IdlMember("getData", "https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransfer-getdata")]
    public string GetData(string format)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (Store is not { } store || store.Mode == DragDataStoreMode.Protected)
            return "";

        format = NormalizeFormat(format, out var convertToUrl);
        var item = store.Items.Find(i => i.Kind == DragDataItemKind.Text && i.Type == format);
        if (item is null)
            return "";

        var result = (string)item.Data;
        return convertToUrl ? GetFirstUrl(result) : result;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransfer-setdata</summary>
    [IdlMember("setData", "https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransfer-setdata")]
    public void SetData(string format, string data)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(data);
        if (Store is not { Mode: DragDataStoreMode.ReadWrite } store)
            return;

        format = NormalizeFormat(format, out _);
        store.Items.RemoveAll(i => i.Kind == DragDataItemKind.Text && i.Type == format);
        store.Items.Add(new DragDataItem(DragDataItemKind.Text, format, data));
        store.OnDataSet(format);
        UpdateTypes();
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransfer-cleardata</summary>
    [IdlMember("clearData", "https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransfer-cleardata")]
    public void ClearData(string? format = null)
    {
        if (Store is not { Mode: DragDataStoreMode.ReadWrite } store)
            return;

        int removed;
        if (format is null)
        {
            removed = store.Items.RemoveAll(i => i.Kind == DragDataItemKind.Text);
        }
        else
        {
            format = NormalizeFormat(format, out _);
            removed = store.Items.RemoveAll(i => i.Kind == DragDataItemKind.Text && i.Type == format);
        }

        store.OnCleared(format);

        if (removed > 0)
        {
            UpdateTypes();
        }
    }

    /// <summary>The files of the drag data store (https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransfer-files): a live list, empty in the protected mode.</summary>
    [IdlMember("files", "https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransfer-files")]
    public FileList Files
    {
        get
        {
            UpdateFiles();
            return _fileList ??= new FileList(_files);
        }
    }

    /// <summary>Breaks the association between the object and its drag data store (at the end of the dispatch of a drag-and-drop event).</summary>
    internal void Disassociate()
    {
        Store = null;
        UpdateTypes();
        UpdateFiles();
    }

    /// <summary>Updates the types array when the drag data store item list changes (https://html.spec.whatwg.org/multipage/dnd.html#concept-datatransfer-types).</summary>
    internal void UpdateTypes()
    {
        var types = new List<string>();
        if (Store is { } store)
        {
            foreach (var item in store.Items)
            {
                if (item.Kind == DragDataItemKind.Text)
                {
                    types.Add(item.Type);
                }
            }

            if (store.Items.Exists(i => i.Kind == DragDataItemKind.File))
            {
                types.Add("Files");
            }
        }

        _types = types.AsReadOnly();
        UpdateFiles();
    }

    private void UpdateFiles()
    {
        _files.Clear();
        if (Store is { } store && store.Mode != DragDataStoreMode.Protected)
        {
            foreach (var item in store.Items)
            {
                if (item.Kind == DragDataItemKind.File)
                {
                    _files.Add((File)item.Data);
                }
            }
        }
    }

    /// <summary>
    /// The format of getData(), setData() and clearData(): ASCII lowercase, "text" is "text/plain" and "url" is
    /// "text/uri-list". As in the browsers, the leading and trailing whitespace is ignored, and so are the parameters of
    /// the text/plain and text/uri-list types.
    /// </summary>
    private static string NormalizeFormat(string format, out bool convertToUrl)
    {
        format = InfraAscii.ToAsciiLowercase(InfraAscii.StripLeadingAndTrailingAsciiWhitespace(format).ToString());
        convertToUrl = false;
        if (format == "text" || format.StartsWith("text/plain;", StringComparison.Ordinal))
            return "text/plain";

        if (format == "url")
        {
            convertToUrl = true;
            return "text/uri-list";
        }

        if (format.StartsWith("text/uri-list;", StringComparison.Ordinal))
            return "text/uri-list";

        return format;
    }

    /// <summary>The first URL of text/uri-list data (RFC 2483): the first line that is neither blank nor a comment.</summary>
    private static string GetFirstUrl(string uriList)
    {
        foreach (var line in uriList.Split('\n'))
        {
            var url = line.TrimEnd('\r');
            if (url.Length > 0 && url[0] != '#')
                return url;
        }

        return "";
    }
}

/// <summary>The list of the items of a drag data store (https://html.spec.whatwg.org/multipage/dnd.html#the-datatransferitemlist-interface).</summary>
[IdlInterface("DataTransferItemList", "https://html.spec.whatwg.org/multipage/dnd.html#datatransferitemlist")]
public sealed class DataTransferItemList : IReadOnlyList<DataTransferItem>
{
    private readonly DataTransfer _dataTransfer;
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<DragDataItem, DataTransferItem> _objects = [];

    internal DataTransferItemList(DataTransfer dataTransfer)
    {
        _dataTransfer = dataTransfer;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransferitemlist-length</summary>
    [IdlMember("length", "https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransferitemlist-length")]
    public uint Length => (uint)(_dataTransfer.Store?.Items.Count ?? 0);

    /// <summary>The indexed property getter (https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransferitemlist-item): the same object each time for an item.</summary>
    [IdlMember("getter", "https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransferitemlist-item")]
    public DataTransferItem? this[uint index]
    {
        get
        {
            if (_dataTransfer.Store is not { } store || index >= (uint)store.Items.Count)
                return null;

            return _objects.GetValue(store.Items[(int)index], item => new DataTransferItem(_dataTransfer, item));
        }
    }

    /// <summary>Gets the number of items in the list (the .NET counterpart of <c>length</c>).</summary>
    [NotInIdl]
    public int Count => (int)Length;

    DataTransferItem IReadOnlyList<DataTransferItem>.this[int index] => this[(uint)index] ?? throw new ArgumentOutOfRangeException(nameof(index));

    /// <summary>Adds a text item (https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransferitemlist-add).</summary>
    [IdlMember("add", "https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransferitemlist-add")]
    public DataTransferItem? Add(string data, string type)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(type);
        if (_dataTransfer.Store is not { Mode: DragDataStoreMode.ReadWrite } store)
            return null;

        type = InfraAscii.ToAsciiLowercase(type);
        if (store.Items.Exists(i => i.Kind == DragDataItemKind.Text && i.Type == type))
            throw DomException.NotSupported($"The drag data store already has an item of type '{type}'");

        return Add(store, new DragDataItem(DragDataItemKind.Text, type, data));
    }

    /// <summary>Adds a file item (https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransferitemlist-add).</summary>
    [IdlMember("add", "https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransferitemlist-add")]
    public DataTransferItem? Add(File data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (_dataTransfer.Store is not { Mode: DragDataStoreMode.ReadWrite } store)
            return null;

        return Add(store, new DragDataItem(DragDataItemKind.File, InfraAscii.ToAsciiLowercase(data.Type), data));
    }

    private DataTransferItem? Add(DragDataStore store, DragDataItem item)
    {
        store.Items.Add(item);
        store.OnDataSet(item.Type);
        _dataTransfer.UpdateTypes();
        return this[(uint)(store.Items.Count - 1)];
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransferitemlist-remove</summary>
    [IdlMember("remove", "https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransferitemlist-remove")]
    public void Remove(uint index)
    {
        if (_dataTransfer.Store is not { Mode: DragDataStoreMode.ReadWrite } store)
            throw DomException.InvalidState("The drag data store is not in the read/write mode");

        if (index >= (uint)store.Items.Count)
            return;

        store.Items.RemoveAt((int)index);
        _dataTransfer.UpdateTypes();
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransferitemlist-clear</summary>
    [IdlMember("clear", "https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransferitemlist-clear")]
    public void Clear()
    {
        if (_dataTransfer.Store is not { Mode: DragDataStoreMode.ReadWrite } store)
            return;

        store.OnCleared(format: null);
        if (store.Items.Count == 0)
            return;

        store.Items.Clear();
        _dataTransfer.UpdateTypes();
    }

    /// <inheritdoc/>
    [NotInIdl("IEnumerable implementation")]
    public IEnumerator<DataTransferItem> GetEnumerator()
    {
        for (uint i = 0; i < Length; i++)
        {
            yield return this[i]!;
        }
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>An item of a drag data store (https://html.spec.whatwg.org/multipage/dnd.html#the-datatransferitem-interface).</summary>
[IdlInterface("DataTransferItem", "https://html.spec.whatwg.org/multipage/dnd.html#datatransferitem")]
public sealed class DataTransferItem
{
    private readonly DataTransfer _dataTransfer;
    private readonly DragDataItem _item;

    internal DataTransferItem(DataTransfer dataTransfer, DragDataItem item)
    {
        _dataTransfer = dataTransfer;
        _item = item;
    }

    /// <summary>The mode of the item: the mode of the drag data store, or null (the disabled mode) when the item was removed or the association is broken.</summary>
    private DragDataStoreMode? Mode => _dataTransfer.Store is { } store && store.Items.Contains(_item) ? store.Mode : null;

    /// <summary>https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransferitem-kind</summary>
    [IdlMember("kind", "https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransferitem-kind")]
    public string Kind => Mode is null ? "" : _item.Kind == DragDataItemKind.Text ? "string" : "file";

    /// <summary>https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransferitem-type</summary>
    [IdlMember("type", "https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransferitem-type")]
    public string Type => Mode is null ? "" : _item.Type;

    /// <summary>https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransferitem-getasstring: the callback is invoked from a task.</summary>
    [IdlMember("getAsString", "https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransferitem-getasstring")]
    public void GetAsString(FunctionStringCallback? callback)
    {
        if (callback is null || Mode is not (DragDataStoreMode.ReadWrite or DragDataStoreMode.ReadOnly) || _item.Kind != DragDataItemKind.Text)
            return;

        var data = (string)_item.Data;
        if (Document.CurrentGlobalDocumentOrNull is { } document)
        {
            document.QueueGlobalTask(() => callback(data));
        }
        else
        {
            callback(data);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransferitem-getasfile</summary>
    [IdlMember("getAsFile", "https://html.spec.whatwg.org/multipage/dnd.html#dom-datatransferitem-getasfile")]
    public File? GetAsFile()
    {
        if (Mode is not (DragDataStoreMode.ReadWrite or DragDataStoreMode.ReadOnly) || _item.Kind != DragDataItemKind.File)
            return null;

        return (File)_item.Data;
    }
}
