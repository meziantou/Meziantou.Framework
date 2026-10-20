using System.Text;
using Meziantou.Framework.WebPlatform.Encodings;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom.FileApi;

/// <summary>
/// Reads the content of a <see cref="Blob"/> asynchronously (https://w3c.github.io/FileAPI/#APIASynch). The events
/// (<c>loadstart</c>, <c>progress</c>, <c>load</c> or <c>error</c>, <c>loadend</c>, <c>abort</c>) are fired by the tasks of
/// the event loop of the relevant global object of the reader (the window of the current global object when it is
/// created); a reader created without current global object (e.g. from .NET without script engine) reads the blob and
/// fires the events synchronously.
/// </summary>
/// <seealso href="https://w3c.github.io/FileAPI/#dfn-filereader"/>
[IdlInterface("FileReader", "https://w3c.github.io/FileAPI/#dfn-filereader")]
public sealed class FileReader : EventTarget
{
    /// <summary>The state of a reader that has no read in progress and no result (<c>EMPTY</c>).</summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#dom-filereader-empty"/>
    [IdlMember("EMPTY", "https://w3c.github.io/FileAPI/#dom-filereader-empty")]
    public const ushort Empty = 0;

    /// <summary>The state of a reader that is reading a blob (<c>LOADING</c>).</summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#dom-filereader-loading"/>
    [IdlMember("LOADING", "https://w3c.github.io/FileAPI/#dom-filereader-loading")]
    public const ushort Loading = 1;

    /// <summary>The state of a reader whose read has completed (<c>DONE</c>).</summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#dom-filereader-done"/>
    [IdlMember("DONE", "https://w3c.github.io/FileAPI/#dom-filereader-done")]
    public const ushort Done = 2;

    // Incremented when a read starts or is aborted: the tasks of the previous read are removed from the task queue
    private int _readId;

    /// <summary>Creates a reader in the <see cref="Empty"/> state.</summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#filereaderConstrctr"/>
    [IdlMember("constructor", "https://w3c.github.io/FileAPI/#filereaderConstrctr")]
    public FileReader()
    {
    }

    /// <summary>Gets the state of the reader: <see cref="Empty"/>, <see cref="Loading"/> or <see cref="Done"/>.</summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#dom-filereader-readystate"/>
    [IdlMember("readyState", "https://w3c.github.io/FileAPI/#dom-filereader-readystate")]
    public ushort ReadyState { get; private set; }

    /// <summary>Gets the result of the read: a <see cref="string"/>, a <see cref="byte"/> array (an <c>ArrayBuffer</c> in JavaScript), or null.</summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#dom-filereader-result"/>
    [IdlMember("result", "https://w3c.github.io/FileAPI/#dom-filereader-result")]
    public FileReaderResult? Result { get; private set; }

    /// <summary>Gets the error of the read, or null.</summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#dom-filereader-error"/>
    [IdlMember("error", "https://w3c.github.io/FileAPI/#dom-filereader-error")]
    public DomException? Error { get; private set; }

    /// <summary>Reads the blob as bytes (https://w3c.github.io/FileAPI/#dfn-readAsArrayBuffer).</summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#dfn-readAsArrayBuffer"/>
    [IdlMember("readAsArrayBuffer", "https://w3c.github.io/FileAPI/#dfn-readAsArrayBuffer")]
    public void ReadAsArrayBuffer(Blob blob) => Read(blob, static (bytes, _) => FileReaderResult.FromBytes(bytes.ToArray()));

    /// <summary>Reads the blob as a binary string: each byte is a code unit (https://w3c.github.io/FileAPI/#readAsBinaryString).</summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#readAsBinaryString"/>
    [IdlMember("readAsBinaryString", "https://w3c.github.io/FileAPI/#readAsBinaryString")]
    public void ReadAsBinaryString(Blob blob) => Read(blob, static (bytes, _) => FileReaderResult.FromString(Encoding.Latin1.GetString(bytes)));

    /// <summary>
    /// Reads the blob as text (https://w3c.github.io/FileAPI/#readAsDataText), decoded with the byte order mark, else the
    /// <paramref name="encoding"/> label, else the <c>charset</c> of the type of the blob, else UTF-8.
    /// </summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#readAsDataText"/>
    [IdlMember("readAsText", "https://w3c.github.io/FileAPI/#readAsDataText")]
    public void ReadAsText(Blob blob, string? encoding = null) => Read(blob, (bytes, type) => FileReaderResult.FromString(DecodeText(bytes, type, encoding)));

    /// <summary>Reads the blob as a <c>data:</c> URL whose body is base64-encoded (https://w3c.github.io/FileAPI/#dfn-readAsDataURL).</summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#dfn-readAsDataURL"/>
    [IdlMember("readAsDataURL", "https://w3c.github.io/FileAPI/#dfn-readAsDataURL")]
    public void ReadAsDataUrl(Blob blob) => Read(blob, static (bytes, type) => FileReaderResult.FromString("data:" + (type.Length > 0 ? type : "application/octet-stream") + ";base64," + Convert.ToBase64String(bytes)));

    /// <summary>Aborts the read (https://w3c.github.io/FileAPI/#dfn-abort).</summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#dfn-abort"/>
    [IdlMember("abort", "https://w3c.github.io/FileAPI/#dfn-abort")]
    public void Abort()
    {
        if (ReadyState is Empty or Done)
        {
            Result = null;
            return;
        }

        ReadyState = Done;
        Result = null;
        _readId++;
        FireProgressEvent("abort", loaded: 0, total: 0);
        if (ReadyState != Loading)
        {
            FireProgressEvent("loadend", loaded: 0, total: 0);
        }
    }

    /// <summary>Gets or sets the event handler of the <c>loadstart</c> event.</summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#dfn-onloadstart"/>
    [IdlMember("onloadstart", "https://w3c.github.io/FileAPI/#dfn-onloadstart")]
    public Action<Event>? OnLoadStart
    {
        get => GetEventHandler("loadstart");
        set => SetEventHandler("loadstart", value);
    }

    /// <summary>Gets or sets the event handler of the <c>progress</c> event.</summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#dfn-onprogress"/>
    [IdlMember("onprogress", "https://w3c.github.io/FileAPI/#dfn-onprogress")]
    public Action<Event>? OnProgress
    {
        get => GetEventHandler("progress");
        set => SetEventHandler("progress", value);
    }

    /// <summary>Gets or sets the event handler of the <c>load</c> event.</summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#dfn-onload"/>
    [IdlMember("onload", "https://w3c.github.io/FileAPI/#dfn-onload")]
    public Action<Event>? OnLoad
    {
        get => GetEventHandler("load");
        set => SetEventHandler("load", value);
    }

    /// <summary>Gets or sets the event handler of the <c>abort</c> event.</summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#dfn-onabort"/>
    [IdlMember("onabort", "https://w3c.github.io/FileAPI/#dfn-onabort")]
    public Action<Event>? OnAbort
    {
        get => GetEventHandler("abort");
        set => SetEventHandler("abort", value);
    }

    /// <summary>Gets or sets the event handler of the <c>error</c> event.</summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#dfn-onerror"/>
    [IdlMember("onerror", "https://w3c.github.io/FileAPI/#dfn-onerror")]
    public Action<Event>? OnError
    {
        get => GetEventHandler("error");
        set => SetEventHandler("error", value);
    }

    /// <summary>Gets or sets the event handler of the <c>loadend</c> event.</summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#dfn-onloadend"/>
    [IdlMember("onloadend", "https://w3c.github.io/FileAPI/#dfn-onloadend")]
    public Action<Event>? OnLoadEnd
    {
        get => GetEventHandler("loadend");
        set => SetEventHandler("loadend", value);
    }

    /// <summary>
    /// The read operation (https://w3c.github.io/FileAPI/#readOperation). The blob is read as a single chunk: the tasks fire
    /// <c>loadstart</c>, then <c>progress</c> (when the blob is not empty), then <c>load</c> and <c>loadend</c>.
    /// </summary>
    private void Read(Blob blob, Func<ReadOnlySpan<byte>, string, FileReaderResult> packageData)
    {
        ArgumentNullException.ThrowIfNull(blob);
        if (ReadyState == Loading)
            throw new DomException("The reader is already loading a blob", "InvalidStateError");

        ReadyState = Loading;
        Result = null;
        Error = null;

        var readId = ++_readId;
        var bytes = blob.Data;
        var total = bytes.Length;
        QueueTask(readId, () => FireProgressEvent("loadstart", loaded: 0, total));
        if (total > 0)
        {
            QueueTask(readId, () => FireProgressEvent("progress", loaded: total, total));
        }

        QueueTask(readId, () =>
        {
            ReadyState = Done;
            Result = packageData(bytes.Span, blob.Type);
            FireProgressEvent("load", loaded: total, total);
            if (ReadyState != Loading)
            {
                FireProgressEvent("loadend", loaded: total, total);
            }
        });
    }

    /// <summary>Queues a task on the file reading task source, which does not run if the read is aborted or another read starts.</summary>
    private void QueueTask(int readId, Action action)
    {
        void Run()
        {
            if (readId == _readId)
            {
                action();
            }
        }

        if (RelevantGlobal is { } global)
        {
            global.QueueGlobalTask(Run);
        }
        else
        {
            Run();
        }
    }

    private void FireProgressEvent(string type, int loaded, int total)
        => FireEvent(new ProgressEvent(type, new ProgressEventInit { LengthComputable = true, Loaded = loaded, Total = total }));

    /// <summary>The package data algorithm for the Text type (https://w3c.github.io/FileAPI/#packaging-data).</summary>
    internal static string DecodeText(ReadOnlySpan<byte> bytes, string type, string? encodingName)
    {
        // The encoding label, else the charset parameter of the type, else UTF-8 (the replacement encoding is a failure)
        if (encodingName is null || !WebEncodingRegistry.TryGetEncoding(encodingName, out var encoding) || encoding == WebEncodingId.Replacement)
        {
            encoding = MimeType.Parse(type)?.GetParameter("charset") is { } charset && WebEncodingRegistry.TryGetEncoding(charset, out var charsetEncoding) && charsetEncoding != WebEncodingId.Replacement
                ? charsetEncoding
                : WebEncodingId.Utf8;
        }

        // https://encoding.spec.whatwg.org/#decode: the byte order mark overrides the encoding
        var bomLength = WebEncodingRegistry.SniffBom(bytes, out var bomEncoding);
        if (bomLength > 0)
        {
            encoding = bomEncoding;
            bytes = bytes[bomLength..];
        }

        return WebEncodingRegistry.CreateDecoder(encoding).DecodeToString(bytes);
    }
}

/// <summary>The result of a <see cref="FileReader"/>: a string or a byte array (an <c>ArrayBuffer</c> in JavaScript).</summary>
/// <seealso href="https://w3c.github.io/FileAPI/#dom-filereader-result"/>
[IdlInterface("(DOMString or ArrayBuffer)", "https://w3c.github.io/FileAPI/#dom-filereader-result")]
public readonly struct FileReaderResult : IUnionValue, IEquatable<FileReaderResult>
{
    private FileReaderResult(object value) => Value = value;

    /// <summary>Gets the value: a <see cref="string"/> or a <see cref="byte"/> array.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Gets the value as a string, or null when the result is a byte array.</summary>
    [NotInIdl]
    public string? StringValue => Value as string;

    /// <summary>Gets the value as bytes, or null when the result is a string.</summary>
    [NotInIdl]
    public ReadOnlyMemory<byte>? BytesValue => Value is byte[] bytes ? bytes : null;

    /// <summary>Converts a string to a result.</summary>
    [NotInIdl]
    public static implicit operator FileReaderResult(string value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts a byte array to a result.</summary>
    [NotInIdl]
    public static implicit operator FileReaderResult(byte[] value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates a result from a string.</summary>
    [NotInIdl]
    public static FileReaderResult FromString(string value) => value;

    /// <summary>Creates a result from a byte array.</summary>
    [NotInIdl]
    public static FileReaderResult FromBytes(byte[] value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(FileReaderResult other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is FileReaderResult other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <inheritdoc/>
    [NotInIdl]
    public override string? ToString() => Value?.ToString();

    /// <summary>Returns whether two results have the same value.</summary>
    [NotInIdl]
    public static bool operator ==(FileReaderResult left, FileReaderResult right) => left.Equals(right);

    /// <summary>Returns whether two results have different values.</summary>
    [NotInIdl]
    public static bool operator !=(FileReaderResult left, FileReaderResult right) => !left.Equals(right);
}
