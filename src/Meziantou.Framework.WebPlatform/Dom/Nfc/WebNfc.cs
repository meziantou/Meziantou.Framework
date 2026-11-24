using System.ComponentModel;
using System.Text;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The initialization dictionary of an <see cref="NdefMessage"/> (https://w3c-cg.github.io/web-nfc/#dom-ndefmessageinit).</summary>
/// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefmessageinit"/>
[IdlInterface("NDEFMessageInit", "https://w3c-cg.github.io/web-nfc/#dom-ndefmessageinit")]
public sealed class NdefMessageInit
{
    /// <summary>Gets the records of the message. It must not be empty.</summary>
    /// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefmessageinit-records"/>
    [IdlMember("records", "https://w3c-cg.github.io/web-nfc/#dom-ndefmessageinit-records")]
    public required IReadOnlyList<NdefRecordInit> Records { get; init; }
}

/// <summary>The initialization dictionary of an <see cref="NdefRecord"/> (https://w3c-cg.github.io/web-nfc/#dom-ndefrecordinit).</summary>
/// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefrecordinit"/>
[IdlInterface("NDEFRecordInit", "https://w3c-cg.github.io/web-nfc/#dom-ndefrecordinit")]
public sealed class NdefRecordInit
{
    /// <summary>
    /// Gets the type of the record: <c>empty</c>, <c>text</c>, <c>url</c>, <c>absolute-url</c>, <c>mime</c>, <c>unknown</c>,
    /// <c>smart-poster</c>, an external type (a domain name, a colon and a type name, e.g. <c>example.com:type</c>), or a local
    /// type (a colon followed by a name that starts with a lowercase letter or a digit, e.g. <c>:act</c>) in the message of
    /// another record.
    /// </summary>
    /// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefrecordinit-recordtype"/>
    [IdlMember("recordType", "https://w3c-cg.github.io/web-nfc/#dom-ndefrecordinit-recordtype")]
    public required string RecordType { get; init; }

    /// <summary>Gets the MIME type of the payload of a <c>mime</c> record, or null.</summary>
    /// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefrecordinit-mediatype"/>
    [IdlMember("mediaType", "https://w3c-cg.github.io/web-nfc/#dom-ndefrecordinit-mediatype")]
    public string? MediaType { get; init; }

    /// <summary>Gets the identifier of the record, or null.</summary>
    /// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefrecordinit-id"/>
    [IdlMember("id", "https://w3c-cg.github.io/web-nfc/#dom-ndefrecordinit-id")]
    public string? Id { get; init; }

    /// <summary>Gets the encoding of the bytes of a <c>text</c> record (<c>utf-8</c>, <c>utf-16</c>, <c>utf-16be</c> or <c>utf-16le</c>), or null for UTF-8.</summary>
    /// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefrecordinit-encoding"/>
    [IdlMember("encoding", "https://w3c-cg.github.io/web-nfc/#dom-ndefrecordinit-encoding")]
    public string? Encoding { get; init; }

    /// <summary>Gets the language tag of a <c>text</c> record, or null for the language of the document (<c>en</c> when it has none).</summary>
    /// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefrecordinit-lang"/>
    [IdlMember("lang", "https://w3c-cg.github.io/web-nfc/#dom-ndefrecordinit-lang")]
    public string? Lang { get; init; }

    /// <summary>
    /// Gets the payload of the record: a string (<c>text</c>, <c>url</c> and <c>absolute-url</c> records), bytes (a
    /// <see cref="byte"/> array, a <see cref="ReadOnlyMemory{T}"/> of bytes or a buffer source of the scripts: <c>text</c>,
    /// <c>mime</c>, <c>unknown</c>, external and local type records), an <see cref="NdefMessageInit"/> (<c>smart-poster</c>,
    /// external and local type records), or null (<c>empty</c> records).
    /// </summary>
    /// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefrecordinit-data"/>
    [IdlMember("data", "https://w3c-cg.github.io/web-nfc/#dom-ndefrecordinit-data")]
    public object? Data { get; init; }
}

/// <summary>An NDEF message: a list of NDEF records (https://w3c-cg.github.io/web-nfc/#the-ndefmessage-interface).</summary>
/// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefmessage"/>
[IdlInterface("NDEFMessage", "https://w3c-cg.github.io/web-nfc/#dom-ndefmessage")]
public sealed class NdefMessage
{
    /// <summary>Creates a message from its records (https://w3c-cg.github.io/web-nfc/#create-web-nfc-message).</summary>
    /// <param name="messageInit">The records of the message.</param>
    /// <exception cref="TypeErrorException">The message has no records, a record is invalid, or the messages are nested more than 32 times.</exception>
    /// <exception cref="DomException">A <c>SyntaxError</c>: the URL of a record is invalid, or the language of a <c>text</c> record is longer than 63 characters.</exception>
    /// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefmessage-constructor"/>
    [IdlMember("constructor", "https://w3c-cg.github.io/web-nfc/#dom-ndefmessage-constructor")]
    public NdefMessage(NdefMessageInit messageInit)
    {
        ArgumentNullException.ThrowIfNull(messageInit);
        Records = NdefRecordFactory.ForCurrentGlobal().CreateMessage(messageInit, NdefRecordFactory.NoContext, recordsDepth: 0);
    }

    /// <summary>Gets the records of the message.</summary>
    /// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefmessage-records"/>
    [IdlMember("records", "https://w3c-cg.github.io/web-nfc/#dom-ndefmessage-records")]
    public IReadOnlyList<NdefRecord> Records { get; }
}

/// <summary>
/// An NDEF record (https://w3c-cg.github.io/web-nfc/#the-ndefrecord-interface): its type, identifier and payload. The payload of
/// a record created from an <see cref="NdefMessageInit"/> (<c>smart-poster</c>, external and local type records) is its records
/// (<see cref="ToRecords"/>): its <see cref="Data"/> is null, as the library does not serialize the NDEF messages.
/// </summary>
/// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefrecord"/>
[IdlInterface("NDEFRecord", "https://w3c-cg.github.io/web-nfc/#dom-ndefrecord")]
public sealed class NdefRecord
{
    private readonly byte[]? _data;
    private readonly IReadOnlyList<NdefRecord>? _records;

    /// <summary>Creates a record (https://w3c-cg.github.io/web-nfc/#create-web-nfc-message, the steps that create an NDEF record).</summary>
    /// <param name="recordInit">The type, the identifier and the payload of the record.</param>
    /// <exception cref="TypeErrorException">The type is unknown or invalid, the payload does not match the type, or the messages are nested more than 32 times.</exception>
    /// <exception cref="DomException">A <c>SyntaxError</c>: the URL of a <c>url</c> or <c>absolute-url</c> record is invalid, or the language of a <c>text</c> record is longer than 63 characters.</exception>
    /// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefrecord-constructor"/>
    [IdlMember("constructor", "https://w3c-cg.github.io/web-nfc/#dom-ndefrecord-constructor")]
    public NdefRecord(NdefRecordInit recordInit)
        : this(NdefRecordFactory.ForCurrentGlobal().CreateRecord(recordInit ?? throw new ArgumentNullException(nameof(recordInit)), NdefRecordFactory.NoContext, recordsDepth: 0))
    {
    }

    private NdefRecord(NdefRecord record)
    {
        RecordType = record.RecordType;
        MediaType = record.MediaType;
        Id = record.Id;
        Encoding = record.Encoding;
        Lang = record.Lang;
        _data = record._data;
        _records = record._records;
    }

    internal NdefRecord(string recordType, string? id, byte[]? data, IReadOnlyList<NdefRecord>? records = null, string? mediaType = null, string? encoding = null, string? lang = null)
    {
        RecordType = recordType;
        MediaType = mediaType;
        Id = id;
        Encoding = encoding;
        Lang = lang;
        _data = data;
        _records = records;
    }

    /// <summary>Gets the type of the record (e.g. <c>text</c>, <c>mime</c>, <c>example.com:type</c>).</summary>
    /// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefrecord-recordtype"/>
    [IdlMember("recordType", "https://w3c-cg.github.io/web-nfc/#dom-ndefrecord-recordtype")]
    public string RecordType { get; }

    /// <summary>Gets the MIME type of the payload of a <c>mime</c> record, or null.</summary>
    /// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefrecord-mediatype"/>
    [IdlMember("mediaType", "https://w3c-cg.github.io/web-nfc/#dom-ndefrecord-mediatype")]
    public string? MediaType { get; }

    /// <summary>Gets the identifier of the record, or null.</summary>
    /// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefrecord-id"/>
    [IdlMember("id", "https://w3c-cg.github.io/web-nfc/#dom-ndefrecord-id")]
    public string? Id { get; }

    /// <summary>Gets the encoding of the text of a <c>text</c> record, or null.</summary>
    /// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefrecord-encoding"/>
    [IdlMember("encoding", "https://w3c-cg.github.io/web-nfc/#dom-ndefrecord-encoding")]
    public string? Encoding { get; }

    /// <summary>Gets the language tag of the text of a <c>text</c> record, or null.</summary>
    /// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefrecord-lang"/>
    [IdlMember("lang", "https://w3c-cg.github.io/web-nfc/#dom-ndefrecord-lang")]
    public string? Lang { get; }

    /// <summary>Gets the bytes of the payload of the record (the encoded text of a <c>text</c> record, the URL of a <c>url</c> record), or null when the record has no payload or its payload is an NDEF message.</summary>
    [NotInIdl("The bytes for the .NET code: the data attribute is DataView")]
    public ReadOnlyMemory<byte>? Data => _data is null ? (ReadOnlyMemory<byte>?)null : _data;

    /// <summary>Gets a new <c>DataView</c> of a copy of the payload for the scripts: use <see cref="Data"/> in .NET code.</summary>
    /// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefrecord-data"/>
    [IdlMember("data", "https://w3c-cg.github.io/web-nfc/#dom-ndefrecord-data")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public object? DataView => _data is null ? null : new JsBufferSource(JsBufferKind.DataView, _data.ToArray());

    /// <summary>
    /// Returns the records of the NDEF message that is the payload of a <c>smart-poster</c>, external type or local type record,
    /// or null when its payload is bytes.
    /// </summary>
    /// <exception cref="DomException">A <c>NotSupportedError</c>: the record cannot contain records.</exception>
    /// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefrecord-torecords"/>
    [IdlMember("toRecords", "https://w3c-cg.github.io/web-nfc/#dom-ndefrecord-torecords")]
    public IReadOnlyList<NdefRecord>? ToRecords()
    {
        if (RecordType is not NdefRecordFactory.SmartPosterType && !NdefRecordFactory.IsLocalType(RecordType) && !NdefRecordFactory.ValidateExternalType(RecordType))
            throw DomException.NotSupported("Only the smart-poster, external type and local type records contain records");

        return _records;
    }
}

/// <summary>The initialization dictionary of an <see cref="NdefReadingEvent"/> (https://w3c-cg.github.io/web-nfc/#dom-ndefreadingeventinit).</summary>
/// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefreadingeventinit"/>
[IdlInterface("NDEFReadingEventInit", "https://w3c-cg.github.io/web-nfc/#dom-ndefreadingeventinit")]
public sealed class NdefReadingEventInit : EventInit
{
    /// <summary>Gets the serial number of the tag (the empty string by default, and for null).</summary>
    /// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefreadingeventinit-serialnumber"/>
    [IdlMember("serialNumber", "https://w3c-cg.github.io/web-nfc/#dom-ndefreadingeventinit-serialnumber")]
    public string? SerialNumber { get; init; } = "";

    /// <summary>Gets the message read from the tag.</summary>
    /// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefreadingeventinit-message"/>
    [IdlMember("message", "https://w3c-cg.github.io/web-nfc/#dom-ndefreadingeventinit-message")]
    public required NdefMessageInit Message { get; init; }
}

/// <summary>The event fired at an <see cref="NdefReader"/> when a tag is read (<c>reading</c>, https://w3c-cg.github.io/web-nfc/#the-ndefreadingevent-interface).</summary>
/// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefreadingevent"/>
[IdlInterface("NDEFReadingEvent", "https://w3c-cg.github.io/web-nfc/#dom-ndefreadingevent")]
public sealed class NdefReadingEvent : Event
{
    /// <summary>Creates an event.</summary>
    /// <param name="type">The type of the event.</param>
    /// <param name="readingEventInitDict">The initialization dictionary.</param>
    /// <exception cref="TypeErrorException">The message is invalid (see <see cref="NdefMessage(NdefMessageInit)"/>).</exception>
    /// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefreadingevent-constructor"/>
    [IdlMember("constructor", "https://w3c-cg.github.io/web-nfc/#dom-ndefreadingevent-constructor")]
    public NdefReadingEvent(string type, NdefReadingEventInit readingEventInitDict)
        : base(type, readingEventInitDict)
    {
        ArgumentNullException.ThrowIfNull(readingEventInitDict);
        SerialNumber = readingEventInitDict.SerialNumber ?? "";
        Message = new NdefMessage(readingEventInitDict.Message);
    }

    /// <summary>Gets the serial number of the tag, or the empty string.</summary>
    /// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefreadingevent-serialnumber"/>
    [IdlMember("serialNumber", "https://w3c-cg.github.io/web-nfc/#dom-ndefreadingevent-serialnumber")]
    public string SerialNumber { get; }

    /// <summary>Gets the message read from the tag.</summary>
    /// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefreadingevent-message"/>
    [IdlMember("message", "https://w3c-cg.github.io/web-nfc/#dom-ndefreadingevent-message")]
    public NdefMessage Message { get; }
}

/// <summary>The options of <see cref="NdefReader.Scan"/> (https://w3c-cg.github.io/web-nfc/#dom-ndefscanoptions).</summary>
/// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefscanoptions"/>
[IdlInterface("NDEFScanOptions", "https://w3c-cg.github.io/web-nfc/#dom-ndefscanoptions")]
public sealed class NdefScanOptions
{
    /// <summary>Gets the signal that stops the scan, or null.</summary>
    /// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefscanoptions-signal"/>
    [IdlMember("signal", "https://w3c-cg.github.io/web-nfc/#dom-ndefscanoptions-signal")]
    public AbortSignal? Signal { get; init; }
}

/// <summary>The options of <see cref="NdefReader.Write"/> (https://w3c-cg.github.io/web-nfc/#dom-ndefwriteoptions).</summary>
/// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefwriteoptions"/>
[IdlInterface("NDEFWriteOptions", "https://w3c-cg.github.io/web-nfc/#dom-ndefwriteoptions")]
public sealed class NdefWriteOptions
{
    /// <summary>Gets whether the message replaces the message of a tag that has one (true by default).</summary>
    /// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefwriteoptions-overwrite"/>
    [IdlMember("overwrite", "https://w3c-cg.github.io/web-nfc/#dom-ndefwriteoptions-overwrite")]
    public bool Overwrite { get; init; } = true;

    /// <summary>Gets the signal that aborts the write, or null.</summary>
    /// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefwriteoptions-signal"/>
    [IdlMember("signal", "https://w3c-cg.github.io/web-nfc/#dom-ndefwriteoptions-signal")]
    public AbortSignal? Signal { get; init; }
}

/// <summary>The options of <see cref="NdefReader.MakeReadOnly"/> (https://w3c-cg.github.io/web-nfc/#dom-ndefmakereadonlyoptions).</summary>
/// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefmakereadonlyoptions"/>
[IdlInterface("NDEFMakeReadOnlyOptions", "https://w3c-cg.github.io/web-nfc/#dom-ndefmakereadonlyoptions")]
public sealed class NdefMakeReadOnlyOptions
{
    /// <summary>Gets the signal that aborts the operation, or null.</summary>
    /// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefmakereadonlyoptions-signal"/>
    [IdlMember("signal", "https://w3c-cg.github.io/web-nfc/#dom-ndefmakereadonlyoptions-signal")]
    public AbortSignal? Signal { get; init; }
}

/// <summary>
/// Reads and writes the NFC tags of a window (https://w3c-cg.github.io/web-nfc/#the-ndefreader-object). The library has no NFC
/// adapter: as on a device without NFC, <see cref="Scan"/>, <see cref="Write"/> and <see cref="MakeReadOnly"/> fail with a
/// <c>NotSupportedError</c> once the window is allowed to use NFC.
/// </summary>
/// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefreader"/>
[IdlInterface("NDEFReader", "https://w3c-cg.github.io/web-nfc/#dom-ndefreader")]
public sealed class NdefReader : EventTarget
{
    private const string PermissionName = "nfc";

    private readonly Window _window;

    /// <summary>Creates a reader for the current window.</summary>
    /// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefreader-constructor"/>
    [IdlMember("constructor", "https://w3c-cg.github.io/web-nfc/#dom-ndefreader-constructor")]
    public NdefReader()
        : this(GlobalScope.Current as Window ?? throw new InvalidOperationException("An NFC reader can only be created by a window"))
    {
    }

    /// <summary>Creates a reader for a window.</summary>
    /// <param name="window">The window whose reader it is.</param>
    [NotInIdl("The .NET constructor whose window is explicit")]
    public NdefReader(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
    }

    /// <summary>Gets or sets the event handler of the <c>reading</c> event (<see cref="NdefReadingEvent"/>).</summary>
    /// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefreader-onreading"/>
    [IdlMember("onreading", "https://w3c-cg.github.io/web-nfc/#dom-ndefreader-onreading")]
    public Action<Event>? OnReading
    {
        get => GetEventHandler("reading");
        set => SetEventHandler("reading", value);
    }

    /// <summary>Gets or sets the event handler of the <c>readingerror</c> event.</summary>
    /// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefreader-onreadingerror"/>
    [IdlMember("onreadingerror", "https://w3c-cg.github.io/web-nfc/#dom-ndefreader-onreadingerror")]
    public Action<Event>? OnReadingError
    {
        get => GetEventHandler("readingerror");
        set => SetEventHandler("readingerror", value);
    }

    /// <summary>
    /// Starts listening to the NFC tags (https://w3c-cg.github.io/web-nfc/#the-scan-method). The task fails with an
    /// <c>InvalidStateError</c> <see cref="DomException"/> when the window is not the window of a top-level browsing context,
    /// with the abort reason of the signal when it is aborted, with a <c>NotAllowedError</c> when the <c>nfc</c> permission is
    /// not granted, and else with a <c>NotSupportedError</c>: the library has no NFC adapter.
    /// </summary>
    /// <param name="options">The signal that stops the scan.</param>
    /// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefreader-scan"/>
    [IdlMember("scan", "https://w3c-cg.github.io/web-nfc/#dom-ndefreader-scan")]
    public Task Scan(NdefScanOptions? options = null) => Start(options?.Signal);

    /// <summary>
    /// Writes a message to an NFC tag (https://w3c-cg.github.io/web-nfc/#the-write-method). The task fails as the task of
    /// <see cref="Scan"/>: the library has no NFC adapter.
    /// </summary>
    /// <param name="message">The message: a string (a <c>text</c> record), bytes (a <c>mime</c> record), or an <see cref="NdefMessageInit"/>.</param>
    /// <param name="options">Whether the message of the tag is replaced, and the signal that aborts the write.</param>
    /// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefreader-write"/>
    [IdlMember("write", "https://w3c-cg.github.io/web-nfc/#dom-ndefreader-write")]
    public Task Write(object? message, NdefWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Start(options?.Signal);
    }

    /// <summary>
    /// Makes an NFC tag permanently read-only (https://w3c-cg.github.io/web-nfc/#the-makereadonly-method). The task fails as the
    /// task of <see cref="Scan"/>: the library has no NFC adapter.
    /// </summary>
    /// <param name="options">The signal that aborts the operation.</param>
    /// <seealso href="https://w3c-cg.github.io/web-nfc/#dom-ndefreader-makereadonly"/>
    [IdlMember("makeReadOnly", "https://w3c-cg.github.io/web-nfc/#dom-ndefreader-makereadonly")]
    public Task MakeReadOnly(NdefMakeReadOnlyOptions? options = null) => Start(options?.Signal);

    // The first steps of the scan, write and make read-only algorithms, up to the NFC adapter, which the library does not have
    private Task Start(AbortSignal? signal)
    {
        var document = _window.Document;
        if (!document.IsFullyActive || document.BrowsingContext is not { Parent: null })
            return Task.FromException(DomException.InvalidState("NFC can only be used by the active document of a top-level browsing context"));

        if (signal is { Aborted: true })
            return Task.FromException(signal.AbortReason as Exception ?? new AbortSignalException(signal.AbortReason));

        var result = new TaskCompletionSource();
        signal?.AddAbortAlgorithm(() => result.TrySetException(signal.AbortReason as Exception ?? new AbortSignalException(signal.AbortReason)));

        // In parallel: obtain the permission (https://w3c-cg.github.io/web-nfc/#dfn-obtain-permission), then the NFC adapter
        _window.QueueGlobalTask(() =>
        {
            if (Permissions.RequestPermissionToUse(_window, PermissionName) != PermissionState.Granted)
            {
                result.TrySetException(DomException.NotAllowed("The nfc permission is not granted"));
                return;
            }

            result.TrySetException(DomException.NotSupported("There is no NFC adapter"));
        });
        return result.Task;
    }
}

/// <summary>The steps that create the NDEF records and messages of the scripts (https://w3c-cg.github.io/web-nfc/#create-web-nfc-message).</summary>
internal sealed class NdefRecordFactory(IScriptValueOperations operations, string documentLanguage)
{
    /// <summary>The context of a record that is not the payload of another record.</summary>
    public const string NoContext = "";

    public const string SmartPosterType = "smart-poster";

    private const string ExternalContext = "external";
    private const string LocalContext = "local";
    private const string SmartPosterContext = "smart-poster";

    // https://w3c-cg.github.io/web-nfc/#create-web-nfc-message: the limit of the depth of the nested messages
    private const int MaxRecordsDepth = 32;

    private const int MaxTypeLength = 255;
    private const int MaxLanguageLength = 63;

    /// <summary>Returns the factory of the current global object: its conversions of the script values, and the language of its document.</summary>
    public static NdefRecordFactory ForCurrentGlobal()
    {
        var global = GlobalScope.Current;
        var operations = global?.ScriptEngine?.GetScriptValueOperations(global) ?? DotNetScriptValueOperations.Instance;
        var language = (global as Window)?.Document.DocumentElement?.GetAttributeValue("lang");
        return new NdefRecordFactory(operations, string.IsNullOrEmpty(language) ? "en" : language);
    }

    /// <summary>The steps that create an NDEF message, given an NDEFMessageInit.</summary>
    public IReadOnlyList<NdefRecord> CreateMessage(NdefMessageInit source, string context, int recordsDepth)
    {
        if (source.Records.Count == 0)
            throw new TypeErrorException("The records of an NDEF message must not be empty");

        recordsDepth++;
        if (recordsDepth > MaxRecordsDepth)
            throw new TypeErrorException("The NDEF messages cannot be nested more than 32 times");

        var output = new List<NdefRecord>(source.Records.Count);
        foreach (var record in source.Records)
        {
            output.Add(CreateRecord(record, context, recordsDepth));
        }

        CheckCreatedRecords(output, context);
        return output;
    }

    /// <summary>The steps that create an NDEF record.</summary>
    public NdefRecord CreateRecord(NdefRecordInit record, string context, int recordsDepth)
    {
        switch (record.RecordType)
        {
            case "empty":
                if (record.MediaType is not null)
                    throw new TypeErrorException("An empty record has no media type");

                if (record.Id is not null)
                    throw new TypeErrorException("An empty record has no identifier");

                return new NdefRecord("empty", id: null, data: null);

            case "text":
                return MapText(record);

            case "url":
                return MapUrl(record);

            case "mime":
                return MapBinaryData(record);

            case SmartPosterType:
                return MapSmartPoster(record, recordsDepth);

            case "absolute-url":
                if (context == SmartPosterContext)
                    throw new TypeErrorException("A smart poster cannot contain an absolute-url record");

                return MapUrl(record);

            case "unknown":
                // Not in the steps of the specification, which defines the record type: as in Chromium, the payload is bytes
                CheckNoMediaType(record);
                return new NdefRecord("unknown", record.Id, GetBytes(record.Data) ?? throw new TypeErrorException("The data of an unknown record must be a BufferSource"));
        }

        if (IsLocalType(record.RecordType))
        {
            if (context.Length == 0)
                throw new TypeErrorException("A local type record must be in the message of another record");

            if (!ValidateLocalType(record.RecordType))
                throw new TypeErrorException($"The local type '{record.RecordType}' is invalid");

            return MapLocalType(record, context, recordsDepth);
        }

        if (ValidateExternalType(record.RecordType))
            return MapExternalData(record, recordsDepth);

        throw new TypeErrorException($"The record type '{record.RecordType}' is invalid");
    }

    public static bool IsLocalType(string recordType) => recordType.StartsWith(':', StringComparison.Ordinal);

    /// <summary>https://w3c-cg.github.io/web-nfc/#validate-external-type</summary>
    public static bool ValidateExternalType(string input) => SplitExternalType(input, out var domain, out var type) && IsValidExternalTypeName(type) && HostParser.ParseDomain(domain, beStrict: false) is not null;

    /// <summary>https://w3c-cg.github.io/web-nfc/#validate-local-type</summary>
    /// <remarks>
    /// As in Chromium, the name must be ASCII (the NFC Forum record type names are ASCII). The name is not compared with the types
    /// of the other records of the message.
    /// </remarks>
    public static bool ValidateLocalType(string input)
    {
        var localTypeName = input.AsSpan(input.IndexOf(':', StringComparison.Ordinal) + 1);
        if (localTypeName.IsEmpty || localTypeName.Length > MaxTypeLength || !Ascii.IsValid(localTypeName))
            return false;

        return char.IsAsciiLetterLower(localTypeName[0]) || char.IsAsciiDigit(localTypeName[0]);
    }

    /// <summary>https://w3c-cg.github.io/web-nfc/#split-external-type</summary>
    private static bool SplitExternalType(string input, out string domain, out string type)
    {
        var index = input.IndexOf(':', StringComparison.Ordinal);
        if (index < 0)
        {
            domain = "";
            type = "";
            return false;
        }

        domain = input[..index];
        type = input[(index + 1)..];
        return true;
    }

    // The type name of an external type: at least one character (NFC Forum RTD), each an ASCII alphanumeric or one of $'()*+,-.;=@_
    private static bool IsValidExternalTypeName(string type)
    {
        if (type.Length == 0)
            return false;

        foreach (var c in type)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('$' or '\'' or '(' or ')' or '*' or '+' or ',' or '-' or '.' or ';' or '=' or '@' or '_'))
                return false;
        }

        return true;
    }

    // https://w3c-cg.github.io/web-nfc/#create-web-nfc-message: the steps that check the created records
    private static void CheckCreatedRecords(List<NdefRecord> records, string context)
    {
        if (context != SmartPosterContext)
            return;

        if (records.Count(record => record.RecordType == "url") != 1)
            throw new TypeErrorException("A smart poster must contain exactly one url record");

        foreach (var localType in (ReadOnlySpan<string>)[":t", ":s", ":act"])
        {
            if (records.Count(record => record.RecordType == localType) > 1)
                throw new TypeErrorException($"A smart poster cannot contain more than one '{localType}' record");
        }

        // The URI record is the first record of the smart poster
        var uriIndex = records.FindIndex(record => record.RecordType == "url");
        var uriRecord = records[uriIndex];
        records.RemoveAt(uriIndex);
        records.Insert(0, uriRecord);
    }

    // Mapping string to NDEF
    private NdefRecord MapText(NdefRecordInit record)
    {
        CheckNoMediaType(record);
        string encoding;
        byte[] data;
        if (operations.IsString(record.Data))
        {
            if (record.Encoding is not (null or "utf-8"))
                throw new TypeErrorException("The encoding of a text record whose data is a string must be utf-8");

            encoding = "utf-8";
            data = Encoding.UTF8.GetBytes(operations.ToDomString(record.Data));
        }
        else
        {
            data = GetBytes(record.Data) ?? throw new TypeErrorException("The data of a text record must be a string or a BufferSource");
            encoding = record.Encoding ?? "utf-8";
            if (encoding is not ("utf-8" or "utf-16" or "utf-16le" or "utf-16be"))
                throw new TypeErrorException($"The encoding '{encoding}' of the text record is not utf-8, utf-16, utf-16le or utf-16be");
        }

        var language = record.Lang ?? documentLanguage;
        if (language.Length > MaxLanguageLength)
            throw DomException.Syntax("The language of a text record cannot be longer than 63 characters");

        return new NdefRecord("text", record.Id, data, encoding: encoding, lang: language);
    }

    // Mapping URL to NDEF and Mapping absolute-URL to NDEF: the payload is the serialized URL
    private NdefRecord MapUrl(NdefRecordInit record)
    {
        CheckNoMediaType(record);
        if (!operations.IsString(record.Data))
            throw new TypeErrorException($"The data of a {record.RecordType} record must be a string");

        var url = UrlParser.Parse(operations.ToDomString(record.Data), baseUrl: null) ?? throw DomException.Syntax($"The data of the {record.RecordType} record is not a valid URL");
        return new NdefRecord(record.RecordType, record.Id, Encoding.UTF8.GetBytes(url.Serialize()));
    }

    // Mapping binary data to NDEF
    private NdefRecord MapBinaryData(NdefRecordInit record)
    {
        var data = GetBytes(record.Data) ?? throw new TypeErrorException("The data of a mime record must be a BufferSource");
        var mimeType = record.MediaType is { } mediaType ? MimeType.Parse(mediaType) : null;
        return new NdefRecord("mime", record.Id, data, mediaType: mimeType?.ToString() ?? "application/octet-stream");
    }

    // Mapping external data to NDEF
    private NdefRecord MapExternalData(NdefRecordInit record, int recordsDepth)
    {
        CheckNoMediaType(record);
        SplitExternalType(record.RecordType, out var domain, out var type);
        var asciiDomain = HostParser.ParseDomain(domain, beStrict: false)!;
        if (asciiDomain.Length + 1 + type.Length > MaxTypeLength)
            throw new TypeErrorException("The external type cannot be longer than 255 bytes");

        return CreatePayload(record, ExternalContext, recordsDepth, "The data of the external type record must be a BufferSource or an NDEFMessageInit");
    }

    // Mapping local type to NDEF
    private NdefRecord MapLocalType(NdefRecordInit record, string context, int recordsDepth)
    {
        CheckNoMediaType(record);
        var payload = CreatePayload(record, LocalContext, recordsDepth, "The data of a local type record must be a BufferSource or an NDEFMessageInit");
        if (context == SmartPosterContext)
        {
            // The size record of a smart poster is a 32-bit unsigned integer, and its action record is a byte (NFC Forum Smart
            // Poster RTD): as in Chromium, the size must be 4 bytes (the specification only rejects more than 4 bytes)
            var localTypeName = record.RecordType[1..];
            if (localTypeName == "s" && payload.Data is not { Length: 4 })
                throw new TypeErrorException("The size record of a smart poster must be a 4-byte BufferSource");

            if (localTypeName == "act" && payload.Data is not { Length: 1 })
                throw new TypeErrorException("The action record of a smart poster must be a 1-byte BufferSource");
        }

        return payload;
    }

    // Mapping smart poster to NDEF
    private NdefRecord MapSmartPoster(NdefRecordInit record, int recordsDepth)
    {
        CheckNoMediaType(record);
        if (operations.IsString(record.Data) || GetBytes(record.Data) is not null)
            throw new TypeErrorException("The data of a smart-poster record must be an NDEFMessageInit");

        var records = CreateMessage(operations.ToNdefMessageInit(record.Data), SmartPosterContext, recordsDepth);
        return new NdefRecord(SmartPosterType, record.Id, data: null, records);
    }

    // The payload of an external or local type record: bytes, or the records of an NDEF message
    private NdefRecord CreatePayload(NdefRecordInit record, string context, int recordsDepth, string error)
    {
        if (GetBytes(record.Data) is { } data)
            return new NdefRecord(record.RecordType, record.Id, data);

        if (operations.IsString(record.Data))
            throw new TypeErrorException(error);

        var records = CreateMessage(operations.ToNdefMessageInit(record.Data), context, recordsDepth);
        return new NdefRecord(record.RecordType, record.Id, data: null, records);
    }

    private byte[]? GetBytes(object? data) => operations.GetBufferSourceCopy(data);

    private static void CheckNoMediaType(NdefRecordInit record)
    {
        if (record.MediaType is not null)
            throw new TypeErrorException($"A {record.RecordType} record has no media type");
    }
}
