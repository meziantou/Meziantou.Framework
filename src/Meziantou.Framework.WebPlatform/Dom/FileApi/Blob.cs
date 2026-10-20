using System.Text;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom.FileApi;

/// <summary>
/// An immutable sequence of bytes with a type (https://w3c.github.io/FileAPI/#blob-section). The parts of the constructor
/// are strings (encoded as UTF-8), blobs, byte arrays and <see cref="ReadOnlyMemory{T}"/> of bytes. A blob can be
/// referenced by a <c>blob:</c> URL (<see cref="Url.CreateObjectUrl(Blob)"/>). <c>stream()</c> and <c>textStream()</c> are
/// defined by the WebIDL bindings, with the streams implemented in JavaScript (docs/scripting.md#streams).
/// </summary>
/// <seealso href="https://w3c.github.io/FileAPI/#blob-section"/>
[IdlInterface("Blob", "https://w3c.github.io/FileAPI/#blob-section")]
public class Blob : Scripting.IStructuredSerializable
{
    private protected readonly byte[] Content;

    /// <summary>Creates a blob from the given parts (strings, blobs or bytes) and options.</summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#constructorBlob"/>
    [IdlMember("constructor", "https://w3c.github.io/FileAPI/#constructorBlob")]
    public Blob(IReadOnlyList<object>? blobParts = null, BlobPropertyBag? options = null)
    {
        Content = ProcessParts(blobParts, options?.Endings);
        Type = NormalizeType(options?.Type ?? "");
    }

    private protected Blob(byte[] bytes, string type)
    {
        Content = bytes;
        Type = type;
    }

    /// <summary>Creates a blob with the given bytes (not copied) and type (e.g. a blob deserialized from a storage).</summary>
    internal static Blob Create(byte[] bytes, string type) => new(bytes, type);

    /// <summary>The serialization steps of Blob (https://w3c.github.io/FileAPI/#blob-section): the bytes are immutable, the clone shares them.</summary>
    object Scripting.IStructuredSerializable.CloneForStructuredSerialization() => CloneForStructuredSerialization();

    private protected virtual Blob CloneForStructuredSerialization() => new(Content, Type);

    /// <summary>Gets the bytes of the blob.</summary>
    internal ReadOnlyMemory<byte> Data => Content;

    /// <summary>https://w3c.github.io/FileAPI/#process-blob-parts</summary>
    private protected static byte[] ProcessParts(IReadOnlyList<object>? parts, string? endings)
    {
        if (parts is null)
            return [];

        var bytes = new List<byte>();
        foreach (var part in parts)
        {
            switch (part)
            {
                case string text:
                    // https://w3c.github.io/FileAPI/#convert-line-endings-to-native
                    var value = endings == "native" ? ConvertLineEndingsToNative(text) : text;
                    bytes.AddRange(Encoding.UTF8.GetBytes(InfraString.ToScalarValueString(value)));
                    break;
                case Blob blob:
                    bytes.AddRange(blob.Content);
                    break;
                case byte[] buffer:
                    bytes.AddRange(buffer);
                    break;
                case ReadOnlyMemory<byte> memory:
                    bytes.AddRange(memory.Span);
                    break;
                case Scripting.JsBufferSource bufferSource:
                    bytes.AddRange(bufferSource.Bytes.Span);
                    break;
                default:
                    bytes.AddRange(Encoding.UTF8.GetBytes(part?.ToString() ?? ""));
                    break;
            }
        }

        return [.. bytes];
    }

    /// <summary>
    /// https://w3c.github.io/FileAPI/#convert-line-endings-to-native: CR LF, CR and LF are replaced with the native line ending.
    /// The native line ending is the one of the platform that the document sees, not the one of the host: the documents do
    /// not know the operating system (<c>navigator.platform</c> is empty), so it is LF on every host, as a browser that does
    /// not run on Windows.
    /// </summary>
    private static string ConvertLineEndingsToNative(string text)
    {
        const char NativeLineEnding = '\n';
        var result = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r')
            {
                result.Append(NativeLineEnding);
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }
            }
            else if (c == '\n')
            {
                result.Append(NativeLineEnding);
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    // The type is lowercased; a type with characters outside U+0020 to U+007E is the empty string
    private protected static string NormalizeType(string type)
    {
        foreach (var c in type)
        {
            if (c is < ' ' or > '~')
                return "";
        }

        return InfraAscii.ToAsciiLowercase(type);
    }

    /// <summary>Gets the size of the blob, in bytes.</summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#dfn-size"/>
    [IdlMember("size", "https://w3c.github.io/FileAPI/#dfn-size")]
    public ulong Size => (ulong)Content.LongLength;

    /// <summary>Gets the media type of the blob, or an empty string when it is unknown.</summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#dfn-type"/>
    [IdlMember("type", "https://w3c.github.io/FileAPI/#dfn-type")]
    public string Type { get; }

    /// <summary>https://w3c.github.io/FileAPI/#dfn-slice</summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#dfn-slice"/>
    [IdlMember("slice", "https://w3c.github.io/FileAPI/#dfn-slice")]
    public Blob Slice(long? start = null, long? end = null, string? contentType = null)
    {
        var size = Content.LongLength;
        var relativeStart = start is null ? 0 : start < 0 ? Math.Max(size + start.Value, 0) : Math.Min(start.Value, size);
        var relativeEnd = end is null ? size : end < 0 ? Math.Max(size + end.Value, 0) : Math.Min(end.Value, size);
        var span = Math.Max(relativeEnd - relativeStart, 0);
        return new Blob(Content.AsSpan((int)relativeStart, (int)span).ToArray(), contentType is null ? "" : NormalizeType(contentType));
    }

    /// <summary>Returns the content decoded as UTF-8, without the byte order mark (https://w3c.github.io/FileAPI/#text-method-algo).</summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#text-method-algo"/>
    [IdlMember("text", "https://w3c.github.io/FileAPI/#text-method-algo")]
    public Task<string> Text()
    {
        var content = Content.AsSpan();
        if (content.StartsWith("\U0000FEFF"u8))
        {
            content = content[3..];
        }

        return Task.FromResult(Encoding.UTF8.GetString(content));
    }

    /// <summary>Returns a copy of the content (an <c>ArrayBuffer</c> in JavaScript, https://w3c.github.io/FileAPI/#arraybuffer-method-algo).</summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#arraybuffer-method-algo"/>
    [IdlMember("arrayBuffer", "https://w3c.github.io/FileAPI/#arraybuffer-method-algo")]
    public Task<byte[]> ArrayBuffer() => Task.FromResult(Content.ToArray());

    /// <summary>Returns a copy of the content (a <c>Uint8Array</c> in JavaScript, https://w3c.github.io/FileAPI/#bytes-method-algo).</summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#bytes-method-algo"/>
    [IdlMember("bytes", "https://w3c.github.io/FileAPI/#bytes-method-algo")]
    public Task<byte[]> Bytes() => Task.FromResult(Content.ToArray());
}
