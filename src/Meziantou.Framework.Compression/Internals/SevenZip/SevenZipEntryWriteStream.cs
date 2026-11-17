using System.IO.Compression;
using System.Security.Cryptography;
using Meziantou.Framework.Compression.Internals.Crypto;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.SevenZip;

/// <summary>The content of a new entry: it computes the CRC and the length, and writes to the folder writer.</summary>
internal sealed class SevenZipEntryWriteStream : WriteOnlyStreamBase
{
    private readonly SevenZipFolderWriter _writer;
    private readonly SevenZipFileItem _item;
    private readonly System.IO.Hashing.Crc32 _crc = new();
    private Stream? _content;
    private long _length;
    private bool _disposed;

    public SevenZipEntryWriteStream(SevenZipFolderWriter writer, SevenZipFileItem item)
    {
        _writer = writer;
        _item = item;
    }

    /// <summary>Gets or sets an action called once the entry is complete.</summary>
    public Action? Completed { get; set; }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.IsEmpty)
            return;

        _content ??= _writer.BeginContent(_item);
        _content.Write(buffer);
        _crc.Append(buffer);
        _length += buffer.Length;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _writer.EndEntry(_item, _length, _crc.GetCurrentHashAsUInt32());
            Completed?.Invoke();
        }

        base.Dispose(disposing);
    }
}
