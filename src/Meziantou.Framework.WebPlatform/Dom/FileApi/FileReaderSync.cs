using System.Text;

namespace Meziantou.Framework.WebPlatform.Dom.FileApi;

/// <summary>
/// Reads the content of a blob synchronously (https://w3c.github.io/FileAPI/#FileReaderSync), in the dedicated and shared
/// workers.
/// </summary>
/// <seealso href="https://w3c.github.io/FileAPI/#FileReaderSync"/>
[IdlInterface("FileReaderSync", "https://w3c.github.io/FileAPI/#FileReaderSync")]
public sealed class FileReaderSync
{
    /// <summary>https://w3c.github.io/FileAPI/#filereadersyncConstrctr</summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#filereadersyncConstrctr"/>
    [IdlMember("constructor", "https://w3c.github.io/FileAPI/#filereadersyncConstrctr")]
    public FileReaderSync()
    {
    }

    /// <summary>Returns a copy of the content of the blob (an <c>ArrayBuffer</c> in JavaScript, https://w3c.github.io/FileAPI/#readAsArrayBufferSyncSection).</summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#readAsArrayBufferSyncSection"/>
    [IdlMember("readAsArrayBuffer", "https://w3c.github.io/FileAPI/#readAsArrayBufferSyncSection")]
    public byte[] ReadAsArrayBuffer(Blob blob)
    {
        ArgumentNullException.ThrowIfNull(blob);
        return blob.Data.ToArray();
    }

    /// <summary>Returns the content of the blob as a binary string: each byte is a code unit (https://w3c.github.io/FileAPI/#readAsBinaryStringSyncSection).</summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#readAsBinaryStringSyncSection"/>
    [IdlMember("readAsBinaryString", "https://w3c.github.io/FileAPI/#readAsBinaryStringSyncSection")]
    public string ReadAsBinaryString(Blob blob)
    {
        ArgumentNullException.ThrowIfNull(blob);
        return Encoding.Latin1.GetString(blob.Data.Span);
    }

    /// <summary>
    /// Returns the content of the blob as text (https://w3c.github.io/FileAPI/#readAsTextSync), decoded with the byte order
    /// mark, else the <paramref name="encoding"/> label, else the <c>charset</c> of the type of the blob, else UTF-8.
    /// </summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#readAsTextSync"/>
    [IdlMember("readAsText", "https://w3c.github.io/FileAPI/#readAsTextSync")]
    public string ReadAsText(Blob blob, string? encoding = null)
    {
        ArgumentNullException.ThrowIfNull(blob);
        return FileReader.DecodeText(blob.Data.Span, blob.Type, encoding);
    }

    /// <summary>Returns the content of the blob as a <c>data:</c> URL whose body is base64-encoded (https://w3c.github.io/FileAPI/#readAsDataURLSync-section).</summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#readAsDataURLSync-section"/>
    [IdlMember("readAsDataURL", "https://w3c.github.io/FileAPI/#readAsDataURLSync-section")]
    public string ReadAsDataUrl(Blob blob)
    {
        ArgumentNullException.ThrowIfNull(blob);
        return "data:" + (blob.Type.Length > 0 ? blob.Type : "application/octet-stream") + ";base64," + Convert.ToBase64String(blob.Data.Span);
    }
}
