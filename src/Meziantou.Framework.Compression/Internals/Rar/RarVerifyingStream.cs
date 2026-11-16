using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Rar;

/// <summary>
/// Reads the content of a RAR entry and checks, at its end, its length and its CRC-32 or BLAKE2sp hash (converted with
/// the hash key when RAR5 makes them depend on the encryption key). A mismatch throws an <see cref="InvalidDataException"/>,
/// or an <see cref="ArchivePasswordException"/> for an encrypted entry, where a wrong password produces garbage.
/// </summary>
internal sealed class RarVerifyingStream : ReadOnlyStreamBase
{
    private readonly Stream _inner;
    private readonly long _expectedLength;
    private readonly Func<(uint? Crc, byte[]? Blake2)> _expectedHashes;
    private readonly byte[]? _hashKey;
    private readonly bool _encrypted;
    private readonly bool _passwordChecked;
    private readonly string _name;
    private readonly System.IO.Hashing.Crc32 _crc = new();
    private readonly Blake2sp? _blake2;
    private long _length;
    private bool _checked;

    /// <param name="inner">The decoded content, disposed with the stream.</param>
    /// <param name="name">The name of the entry.</param>
    /// <param name="expectedLength">The length of the content, or -1 when it is unknown.</param>
    /// <param name="expectedHashes">Returns the CRC-32 and the hash of the archive, read when the content ends (the last part of an entry split between volumes has them).</param>
    /// <param name="computeBlake2">Whether the archive stores a BLAKE2sp hash.</param>
    /// <param name="hashKey">The key that converts the CRC and the hash, or <see langword="null"/>.</param>
    /// <param name="encrypted">Whether the content is decrypted with a password.</param>
    /// <param name="passwordChecked">Whether the password was checked with a check value: corrupted data are then not a password error.</param>
    public RarVerifyingStream(Stream inner, string name, long expectedLength, Func<(uint? Crc, byte[]? Blake2)> expectedHashes, bool computeBlake2, byte[]? hashKey, bool encrypted, bool passwordChecked)
    {
        _inner = inner;
        _name = name;
        _expectedLength = expectedLength;
        _expectedHashes = expectedHashes;
        _hashKey = hashKey;
        _encrypted = encrypted;
        _passwordChecked = passwordChecked;
        _blake2 = computeBlake2 ? new Blake2sp() : null;
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

        int read;
        try
        {
            read = _inner.Read(buffer);
        }
        catch (InvalidDataException ex) when (_encrypted && !_passwordChecked)
        {
            throw new ArchivePasswordException($"The content of the entry '{_name}' is corrupted. The password may be wrong.", ex);
        }

        if (read is 0)
        {
            Check();
            return 0;
        }

        _crc.Append(buffer[..read]);
        _blake2?.Append(buffer[..read]);
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
            throw Fail($"The content of the entry '{_name}' ends after {_length} bytes instead of {_expectedLength}.");

        var (expectedCrc, expectedBlake2) = _expectedHashes();
        if (expectedBlake2 is not null && _blake2 is not null)
        {
            var hash = _blake2.GetHash();
            if (_hashKey is not null)
            {
                hash = RarKeyStore.ConvertBlake2(hash, _hashKey);
            }

            if (!hash.AsSpan().SequenceEqual(expectedBlake2))
                throw Fail($"The BLAKE2sp hash of the content of the entry '{_name}' does not match the hash of the archive.");
        }

        if (expectedCrc is { } crc)
        {
            var actual = _crc.GetCurrentHashAsUInt32();
            if (_hashKey is not null)
            {
                actual = RarKeyStore.ConvertCrc(actual, _hashKey);
            }

            if (actual != crc)
                throw Fail($"The CRC-32 of the content of the entry '{_name}' does not match the CRC-32 of the archive.");
        }
    }

    private Exception Fail(string message) => _encrypted && !_passwordChecked ? new ArchivePasswordException(message + " The password may be wrong.") : new InvalidDataException(message);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
