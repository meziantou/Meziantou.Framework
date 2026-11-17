using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.SevenZip;

/// <summary>
/// The decoder of a folder and its position, kept between the entries of a solid folder so that reading the entries in
/// order decodes the folder once. It checks the CRC of the folder, when the archive stores one, at the end of the folder.
/// </summary>
internal sealed class SevenZipFolderCursor : IDisposable
{
    private readonly Stream _decoder;
    private readonly System.IO.Hashing.Crc32 _crc = new();
    private byte[]? _skipBuffer;

    public SevenZipFolderCursor(Stream source, SevenZipFolder folder, SevenZipDecoderContext context)
    {
        Source = source;
        Folder = folder;
        _decoder = SevenZipFolderDecoder.Open(source, folder, context);
    }

    public Stream Source { get; }

    public SevenZipFolder Folder { get; }

    public long Position { get; private set; }

    /// <summary>Gets or sets a value indicating whether a stream of an entry reads from the cursor.</summary>
    public bool InUse { get; set; }

    public int Read(Span<byte> buffer)
    {
        var read = _decoder.Read(buffer);
        if (read > 0)
        {
            _crc.Append(buffer[..read]);
            Position += read;
            if (Position == Folder.UnpackSize && Folder.UnpackCrc is { } expected && _crc.GetCurrentHashAsUInt32() != expected)
                throw new InvalidDataException("The CRC-32 of a folder of the 7z archive does not match.");
        }

        return read;
    }

    /// <summary>Decodes and discards the content up to <paramref name="position"/>.</summary>
    public void SkipTo(long position)
    {
        while (Position < position)
        {
            _skipBuffer ??= new byte[(int)Math.Min(81920, Math.Max(1, position - Position))];
            var count = (int)Math.Min(_skipBuffer.Length, position - Position);
            if (Read(_skipBuffer.AsSpan(0, count)) is 0)
                throw new InvalidDataException("The content of a folder of the 7z archive is truncated.");
        }
    }

    public void Dispose() => _decoder.Dispose();
}
