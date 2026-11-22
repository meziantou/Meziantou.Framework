using System.IO.Compression;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Zstandard;

/// <summary>
/// Creates the decoder of Zstandard data: <c>ZstandardStream</c> from .NET 11, and before it or on the browser (WebAssembly)
/// the managed decoder of Meziantou.Framework.Imaging, which decodes the whole data in memory.
/// </summary>
internal static class ZstandardDecoderFactory
{
    /// <param name="input">The compressed data.</param>
    /// <param name="outputLength">The length of the decoded data, or -1 when it is not known.</param>
    /// <param name="maxMemoryUsage">The maximum length of the compressed and of the decoded data, when the managed decoder is used.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave <paramref name="input"/> open when the stream is disposed.</param>
    public static Stream Create(Stream input, long outputLength, long maxMemoryUsage, bool leaveOpen)
    {
#if NET11_0_OR_GREATER
        if (!OperatingSystem.IsBrowser())
            return new ZstandardStream(input, CompressionMode.Decompress, leaveOpen);
#endif

        return new BufferedZstandardDecoderStream(input, outputLength, maxMemoryUsage, leaveOpen);
    }

    /// <summary>
    /// Reads all the compressed data, then decodes them in memory with <see cref="ZstdDecoder"/>, whose frames need the
    /// whole output to resolve their matches. The data are limited to what an array holds, and to the memory limit.
    /// </summary>
    private sealed class BufferedZstandardDecoderStream : ReadOnlyStreamBase
    {
        private readonly Stream _input;
        private readonly long _outputLength;
        private readonly long _maxMemoryUsage;
        private readonly bool _leaveOpen;
        private byte[]? _output;
        private int _position;

        public BufferedZstandardDecoderStream(Stream input, long outputLength, long maxMemoryUsage, bool leaveOpen)
        {
            _input = input;
            _outputLength = outputLength;
            _maxMemoryUsage = maxMemoryUsage;
            _leaveOpen = leaveOpen;
        }

        public override int Read(Span<byte> buffer)
        {
            var output = _output ??= Decode();
            var count = Math.Min(buffer.Length, output.Length - _position);
            output.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        private byte[] Decode()
        {
            var limit = (int)Math.Min(_maxMemoryUsage, Array.MaxLength);
            if (_outputLength > limit)
                throw new InvalidDataException($"The Zstandard data decode to {_outputLength} bytes, more than the memory limit ({limit} bytes): before .NET 11, they are decoded in memory.");

            using var compressed = new MemoryStream();
            var chunk = new byte[81920];
            while (true)
            {
                var read = _input.Read(chunk);
                if (read is 0)
                    break;

                if (compressed.Length + read > limit)
                    throw new InvalidDataException($"The Zstandard data are larger than the memory limit ({limit} bytes): before .NET 11, they are decoded in memory.");

                compressed.Write(chunk, 0, read);
            }

            var source = compressed.GetBuffer().AsSpan(0, (int)compressed.Length);
            var decoder = new ZstdDecoder();
            if (_outputLength >= 0)
            {
                var output = new byte[_outputLength];
                decoder.Decompress(source, output);
                return output;
            }

            return decoder.DecompressWithRawDictionary(source, [], limit);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_leaveOpen)
            {
                _input.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
