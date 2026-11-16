namespace Meziantou.Framework.Compression.Internals.IO;

/// <summary>
/// Reads the content of an entry and checks, when it ends, that its length and its CRC-32 are the ones of the archive.
/// It throws an <see cref="InvalidDataException"/> at the end of a content that does not match (or a <see cref="ArchivePasswordException"/>
/// when the entry is encrypted: a wrong password produces garbage that fails the check).
/// </summary>
internal sealed class ChecksumVerifyingStream : ReadOnlyStreamBase
{
    private readonly Stream _inner;
    private readonly long _expectedLength;
    private readonly uint? _expectedCrc;
    private readonly bool _encrypted;
    private readonly IDisposable? _owner;
    private readonly System.IO.Hashing.Crc32 _crc = new();
    private long _length;
    private bool _checked;

    /// <param name="inner">The decoded content.</param>
    /// <param name="expectedLength">The length of the content, or -1 to read it to its end.</param>
    /// <param name="expectedCrc">The CRC-32 of the content, or <see langword="null"/> when there is none.</param>
    /// <param name="encrypted">Whether the content is decrypted with a password.</param>
    /// <param name="owner">An object disposed with the stream, in addition to <paramref name="inner"/>.</param>
    public ChecksumVerifyingStream(Stream inner, long expectedLength, uint? expectedCrc, bool encrypted, IDisposable? owner = null)
    {
        _inner = inner;
        _expectedLength = expectedLength;
        _expectedCrc = expectedCrc;
        _encrypted = encrypted;
        _owner = owner;
    }

    public override bool CanSeek => false;

    public override long Length => _expectedLength >= 0 ? _expectedLength : throw new NotSupportedException();

    public override int Read(Span<byte> buffer)
    {
        if (_expectedLength >= 0)
        {
            var remaining = _expectedLength - _length;
            if (remaining <= 0)
            {
                Check();
                return 0;
            }

            if (buffer.Length > remaining)
            {
                buffer = buffer[..(int)remaining];
            }
        }

        if (buffer.IsEmpty)
            return 0;

        var read = _inner.Read(buffer);
        if (read is 0)
        {
            Check();
            return 0;
        }

        _crc.Append(buffer[..read]);
        _length += read;
        if (_expectedLength >= 0 && _length == _expectedLength)
        {
            Check();
        }

        return read;
    }

    private void Check()
    {
        if (_checked)
            return;

        _checked = true;
        if (_expectedLength >= 0 && _length != _expectedLength)
            throw Fail($"The content of the entry ends after {_length} bytes instead of {_expectedLength}.");

        if (_expectedCrc is { } expected && _crc.GetCurrentHashAsUInt32() != expected)
            throw Fail("The CRC-32 of the content of the entry does not match the CRC-32 of the archive.");
    }

    private Exception Fail(string message) => _encrypted ? new ArchivePasswordException(message + " The password may be wrong.") : new InvalidDataException(message);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
            _owner?.Dispose();
        }

        base.Dispose(disposing);
    }
}
