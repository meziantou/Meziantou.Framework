using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.SevenZip;

/// <summary>The content of an entry: a window of the output of a folder.</summary>
internal sealed class SevenZipEntryReadStream : ReadOnlyStreamBase
{
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Disposed only when owned")]
    private readonly SevenZipFolderCursor _cursor;
    private readonly bool _ownsCursor;
    private readonly bool _encrypted;
    private long _remaining;
    private bool _disposed;

    public SevenZipEntryReadStream(SevenZipFolderCursor cursor, long length, bool ownsCursor, bool encrypted)
    {
        _cursor = cursor;
        _remaining = length;
        _ownsCursor = ownsCursor;
        _encrypted = encrypted;
    }

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_remaining <= 0 || buffer.IsEmpty)
            return 0;

        if (buffer.Length > _remaining)
        {
            buffer = buffer[..(int)_remaining];
        }

        int read;
        try
        {
            read = _cursor.Read(buffer);
        }
        catch (InvalidDataException ex) when (_encrypted)
        {
            throw new ArchivePasswordException("The content of the encrypted entry is invalid. The password may be wrong.", ex);
        }

        _remaining -= read;
        return read;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            if (_ownsCursor)
            {
                _cursor.Dispose();
            }
            else
            {
                _cursor.InUse = false;
            }
        }

        base.Dispose(disposing);
    }
}
