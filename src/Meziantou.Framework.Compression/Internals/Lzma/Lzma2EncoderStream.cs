// A port of the LZMA2 encoder of the LZMA SDK (Lzma2Enc.c) by Igor Pavlov, which is in the public domain.
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Lzma;

/// <summary>
/// Encodes an LZMA2 stream: LZMA chunks of at most 2 MiB of data and 64 KiB of compressed data, or uncompressed chunks
/// when the data does not compress. Disposing the stream encodes the rest of the input and writes the end of the stream.
/// </summary>
internal sealed class Lzma2EncoderStream : WriteOnlyStreamBase
{
    private const int PackSizeMax = 1 << 16;
    private const int UnpackSizeMax = 1 << 21;
    private const int CopyChunkSize = 1 << 16;

    private readonly Stream _output;
    private readonly bool _leaveOpen;
    private readonly LzmaEncoder _encoder;
    private readonly byte _propertiesByte;
    private readonly byte[] _header = new byte[6];
    private bool _needInitState = true;
    private bool _needInitProperties = true;
    private long _sourcePosition;
    private bool _chunkStarted;
    private bool _disposed;

    /// <param name="output">The stream that receives the compressed data.</param>
    /// <param name="settings">The settings of the encoder: LZMA2 requires lc + lp &lt;= 4.</param>
    /// <param name="leaveOpen">Whether to leave <paramref name="output"/> open when the stream is disposed.</param>
    public Lzma2EncoderStream(Stream output, LzmaEncoderSettings settings, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.LiteralContextBits + settings.LiteralPositionBits > 4)
            throw new ArgumentOutOfRangeException(nameof(settings), "LZMA2 requires lc + lp <= 4.");

        _encoder = new LzmaEncoder(settings, keepBefore: UnpackSizeMax);
        _output = output;
        _leaveOpen = leaveOpen;
        _propertiesByte = LzmaConstants.EncodeProperties(settings.LiteralContextBits, settings.LiteralPositionBits, settings.PositionBits);
        DictionaryProperty = LzmaConstants.GetLzma2DictionaryProperty((uint)settings.DictionarySize);
    }

    /// <summary>Gets the LZMA2 dictionary property (the byte of the properties of the XZ and 7z LZMA2 filters).</summary>
    public byte DictionaryProperty { get; }

    /// <summary>Gets the number of compressed bytes written to the output.</summary>
    internal long CompressedLength { get; private set; }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (!buffer.IsEmpty)
        {
            buffer = buffer[_encoder.Append(buffer)..];
            if (_encoder.HasMatchFinder)
            {
                Encode(finishing: false);
            }
        }
    }

    /// <summary>Encodes the buffered input and ends the current chunk, so that the data written so far can be decoded.</summary>
    public void FlushChunk()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _encoder.EnsureMatchFinder();
        Encode(finishing: true);
    }

    private void Encode(bool finishing)
    {
        while (true)
        {
            if (!_chunkStarted)
            {
                _encoder.SaveState();
                if (_needInitState)
                {
                    _encoder.ResetState();
                }

                _encoder.RangeEncoder.Init();
                _encoder.BeginBlock();
                _chunkStarted = true;
            }

            if (!_encoder.CodeOneBlock(finishing, useLimits: true, PackSizeMax, UnpackSizeMax))
                return;

            var rc = _encoder.RangeEncoder;
            rc.Flush();
            _chunkStarted = false;
            var unpackSize = (int)(_encoder.Position - _sourcePosition);
            if (unpackSize == 0)
            {
                // Nothing was encoded: the input ended
                _encoder.RestoreState();
                return;
            }

            var packSize = rc.Length;
            if (packSize + 2 >= unpackSize || packSize > PackSizeMax)
            {
                WriteUncompressedChunks(unpackSize);
                _encoder.RestoreState();
            }
            else
            {
                var u = unpackSize - 1;
                var pm = packSize - 1;
                var mode = _sourcePosition == 0 ? 3 : _needInitState ? (_needInitProperties ? 2 : 1) : 0;
                var headerLength = 0;
                _header[headerLength++] = (byte)(0x80 | (mode << 5) | ((u >> 16) & 0x1F));
                _header[headerLength++] = (byte)(u >> 8);
                _header[headerLength++] = (byte)u;
                _header[headerLength++] = (byte)(pm >> 8);
                _header[headerLength++] = (byte)pm;
                if (_needInitProperties)
                {
                    _header[headerLength++] = _propertiesByte;
                }

                _needInitProperties = false;
                _needInitState = false;
                _output.Write(_header, 0, headerLength);
                _output.Write(rc.Buffer, 0, packSize);
                CompressedLength += headerLength + packSize;
                _sourcePosition += unpackSize;
            }

            rc.Clear();
        }
    }

    private void WriteUncompressedChunks(int unpackSize)
    {
        var data = _encoder.InputBuffer.AsSpan(_encoder.InputPosition - unpackSize, unpackSize);
        while (!data.IsEmpty)
        {
            var count = Math.Min(data.Length, CopyChunkSize);
            _header[0] = (byte)(_sourcePosition == 0 ? 1 : 2);
            _header[1] = (byte)((count - 1) >> 8);
            _header[2] = (byte)(count - 1);
            _output.Write(_header, 0, 3);
            _output.Write(data[..count]);
            CompressedLength += 3 + count;
            _sourcePosition += count;
            data = data[count..];
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            try
            {
                _encoder.EndInput();
                Encode(finishing: true);
                _output.WriteByte(0);
                CompressedLength++;
                _output.Flush();
            }
            finally
            {
                _encoder.Dispose();
                if (!_leaveOpen)
                {
                    _output.Dispose();
                }
            }
        }

        base.Dispose(disposing);
    }
}
