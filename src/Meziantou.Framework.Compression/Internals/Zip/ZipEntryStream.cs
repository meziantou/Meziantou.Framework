using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Zip;

/// <summary>
/// The decoded content of a zip entry. When the content ends, it runs the end action (which checks the authentication code
/// of an AES entry, and reads the data descriptor of a streamed entry, whose length and CRC-32 then replace the expected
/// ones), then checks the length and the CRC-32. It differs from <see cref="ChecksumVerifyingStream"/> by this action, which
/// a streamed entry needs to know what to check.
/// </summary>
internal sealed class ZipEntryStream : ReadOnlyStreamBase
{
    private readonly Stream _inner;
    private readonly bool _passwordMayBeWrong;
    private readonly Func<(long Length, uint? Crc)?>? _onEnd;
    private readonly Func<bool>? _isValid;
    private readonly System.IO.Hashing.Crc32 _crc = new();
    private long _expectedLength;
    private uint? _expectedCrc;
    private long _length;
    private bool _completed;

    /// <param name="inner">The decoded content.</param>
    /// <param name="expectedLength">The length of the content, or -1 to read it to its end.</param>
    /// <param name="expectedCrc">The CRC-32 of the content, or <see langword="null"/> when it is not checked.</param>
    /// <param name="passwordMayBeWrong">Whether a mismatch may come from a wrong password (ZipCrypto): it is then reported with an <see cref="ArchivePasswordException"/>.</param>
    /// <param name="onEnd">The action run when the content ends; it may return the length and the CRC-32 to check instead of the expected ones.</param>
    /// <param name="isValid">A function that returns <see langword="false"/> when the stream can no longer be read (the forward-only reader moved to the next entry).</param>
    public ZipEntryStream(Stream inner, long expectedLength, uint? expectedCrc, bool passwordMayBeWrong, Func<(long Length, uint? Crc)?>? onEnd, Func<bool>? isValid)
    {
        _inner = inner;
        _expectedLength = expectedLength;
        _expectedCrc = expectedCrc;
        _passwordMayBeWrong = passwordMayBeWrong;
        _onEnd = onEnd;
        _isValid = isValid;
    }

    public override int Read(Span<byte> buffer)
    {
        if (_isValid is not null && !_isValid())
            throw new InvalidOperationException("The reader moved past this entry: its content can no longer be read.");

        if (_completed)
            return 0;

        if (_expectedLength >= 0)
        {
            var remaining = _expectedLength - _length;
            if (remaining <= 0)
            {
                Complete();
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
            Complete();
            return 0;
        }

        _crc.Append(buffer[..read]);
        _length += read;
        if (_expectedLength >= 0 && _length == _expectedLength)
        {
            Complete();
        }

        return read;
    }

    private void Complete()
    {
        if (_completed)
            return;

        _completed = true;
        if (_onEnd?.Invoke() is { } end)
        {
            _expectedLength = end.Length;
            _expectedCrc = end.Crc;
        }

        if (_expectedLength >= 0 && _length != _expectedLength)
            throw Fail($"The content of the zip entry ends after {_length} bytes instead of {_expectedLength}.");

        if (_expectedCrc is { } expected && _crc.GetCurrentHashAsUInt32() != expected)
            throw Fail("The CRC-32 of the content of the zip entry does not match the CRC-32 of the archive.");
    }

    private Exception Fail(string message) => _passwordMayBeWrong ? new ArchivePasswordException(message + " The password may be wrong.") : new InvalidDataException(message);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
