// A port of the LZMA encoder of the LZMA SDK by Igor Pavlov, which is in the public domain.
using System.Buffers.Binary;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Lzma;

/// <summary>
/// Encodes a raw LZMA stream (the 7z and zip LZMA methods, the data of the .lzma and .lz formats). Disposing the stream
/// encodes the rest of the input, writes the end marker when requested, and flushes the range encoder.
/// </summary>
internal sealed class LzmaEncoderStream : WriteOnlyStreamBase
{
    private readonly Stream _output;
    private readonly bool _leaveOpen;
    private readonly bool _writeEndMarker;
    private readonly LzmaEncoder _encoder;
    private bool _started;
    private bool _disposed;

    /// <param name="output">The stream that receives the compressed data.</param>
    /// <param name="settings">The settings of the encoder.</param>
    /// <param name="writeEndMarker">Whether to write the end marker after the data (needed when the decoder does not know the length).</param>
    /// <param name="leaveOpen">Whether to leave <paramref name="output"/> open when the stream is disposed.</param>
    public LzmaEncoderStream(Stream output, LzmaEncoderSettings settings, bool writeEndMarker, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(settings);

        _encoder = new LzmaEncoder(settings);
        _output = output;
        _leaveOpen = leaveOpen;
        _writeEndMarker = writeEndMarker;
        Properties = new byte[5];
        Properties[0] = LzmaConstants.EncodeProperties(settings.LiteralContextBits, settings.LiteralPositionBits, settings.PositionBits);
        BinaryPrimitives.WriteUInt32LittleEndian(Properties.AsSpan(1), (uint)settings.DictionarySize);
    }

    /// <summary>Gets the 5-byte LZMA properties: the lc/lp/pb byte, then the dictionary size (little-endian).</summary>
    public byte[] Properties { get; }

    /// <summary>Gets the number of compressed bytes written to the output.</summary>
    internal long CompressedLength { get; private set; }

    /// <summary>Gets the number of uncompressed bytes written to the stream.</summary>
    internal long UncompressedLength { get; private set; }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        UncompressedLength += buffer.Length;
        while (!buffer.IsEmpty)
        {
            buffer = buffer[_encoder.Append(buffer)..];
            Encode(finishing: false);
        }
    }

    private void Encode(bool finishing)
    {
        if (!_started)
        {
            if (!finishing && !_encoder.HasMatchFinder)
                return;

            _encoder.BeginBlock();
            _started = true;
        }

        var rc = _encoder.RangeEncoder;
        _encoder.CodeOneBlock(finishing, useLimits: false, 0, 0);
        if (rc.Length >= 1 << 16 || finishing)
        {
            _output.Write(rc.Buffer, 0, rc.Length);
            CompressedLength += rc.Length;
            rc.Clear();
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
                if (_writeEndMarker)
                {
                    _encoder.WriteEndMarker();
                }

                var rc = _encoder.RangeEncoder;
                rc.Flush();
                _output.Write(rc.Buffer, 0, rc.Length);
                CompressedLength += rc.Length;
                rc.Clear();
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
