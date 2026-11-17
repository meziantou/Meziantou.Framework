using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.BZip2;

/// <summary>
/// Encodes bzip2 data, as bzip2 does: the runs of 4 to 255 equal bytes are shortened, the blocks are compressed by
/// <see cref="BZip2BlockCompressor"/>, and the stream ends with its combined CRC when the encoder is disposed.
/// </summary>
internal sealed class BZip2EncoderStream : WriteOnlyStreamBase
{
    private readonly Stream _output;
    private readonly bool _leaveOpen;
    private readonly int _blockSize100k;
    private readonly bool _randomize;
    private readonly byte[] _block;
    private readonly int _maxBlockLength;
    private readonly BZip2BitWriter _writer = new();
    private readonly BZip2BlockCompressor _compressor = new();
    private int _blockLength;
    private uint _blockCrc = uint.MaxValue;
    private uint _streamCrc;
    private int _runByte = -1;
    private int _runLength;
    private bool _headerWritten;
    private bool _disposed;

    /// <param name="output">The stream the compressed data are written to.</param>
    /// <param name="blockSize100k">The size of the blocks, in hundreds of kB (1 to 9).</param>
    /// <param name="leaveOpen">Whether <paramref name="output"/> stays open when the encoder is disposed.</param>
    /// <param name="randomize">Whether the blocks are randomized as bzip2 0.9.0 did (to test the decoders).</param>
    public BZip2EncoderStream(Stream output, int blockSize100k, bool leaveOpen, bool randomize = false)
    {
        _output = output;
        _leaveOpen = leaveOpen;
        _blockSize100k = blockSize100k;
        _randomize = randomize;
        _block = new byte[blockSize100k * BZip2Constants.BlockSizeUnit];

        // As bzip2, the block ends when it has fewer than 19 free bytes: a run takes at most 5 bytes
        _maxBlockLength = _block.Length - 19;
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var runByte = _runByte;
        var runLength = _runLength;
        foreach (var value in buffer)
        {
            if (value == runByte && runLength < 255)
            {
                runLength++;
                continue;
            }

            if (runByte >= 0)
            {
                AddRun((byte)runByte, runLength);
                if (_blockLength >= _maxBlockLength)
                {
                    CompressBlock();
                }
            }

            runByte = value;
            runLength = 1;
        }

        _runByte = runByte;
        _runLength = runLength;
    }

    private void AddRun(byte value, int length)
    {
        _blockCrc = length is 1 ? BZip2Constants.UpdateCrc(_blockCrc, [value]) : BZip2Constants.UpdateCrc(_blockCrc, value, length);
        var block = _block;
        if (length < 4)
        {
            block.AsSpan(_blockLength, length).Fill(value);
            _blockLength += length;
            return;
        }

        block.AsSpan(_blockLength, 4).Fill(value);
        block[_blockLength + 4] = (byte)(length - 4);
        _blockLength += 5;
    }

    private void CompressBlock()
    {
        WriteHeader();
        if (_blockLength is 0)
            return;

        var crc = ~_blockCrc;
        _streamCrc = ((_streamCrc << 1) | (_streamCrc >> 31)) ^ crc;
        if (_randomize)
        {
            Randomize();
        }

        _compressor.Compress(_block, _blockLength, crc, _randomize, _writer);
        _writer.WriteTo(_output);
        _blockLength = 0;
        _blockCrc = uint.MaxValue;
    }

    private void Randomize()
    {
        var countdown = 0;
        var index = 0;
        for (var i = 0; i < _blockLength; i++)
        {
            if (countdown is 0)
            {
                countdown = BZip2Constants.RandomNumbers[index];
                index = (index + 1) & 511;
            }

            countdown--;
            if (countdown is 1)
            {
                _block[i] ^= 1;
            }
        }
    }

    private void WriteHeader()
    {
        if (_headerWritten)
            return;

        _headerWritten = true;
        _writer.Write(8, 'B');
        _writer.Write(8, 'Z');
        _writer.Write(8, 'h');
        _writer.Write(8, (uint)('0' + _blockSize100k));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            try
            {
                if (_runByte >= 0)
                {
                    AddRun((byte)_runByte, _runLength);
                    _runByte = -1;
                }

                CompressBlock();
                _writer.Write(24, (uint)(BZip2Constants.EndOfStreamMagic >> 24));
                _writer.Write(24, (uint)(BZip2Constants.EndOfStreamMagic & 0xFFFFFF));
                _writer.Write(32, _streamCrc);
                _writer.WriteFinalTo(_output);
                _output.Flush();
            }
            finally
            {
                if (!_leaveOpen)
                {
                    _output.Dispose();
                }
            }
        }

        _disposed = true;
        base.Dispose(disposing);
    }
}
