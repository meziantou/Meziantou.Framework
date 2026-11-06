using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.Imaging.InteropTests.JpegXlConformance;

/// <summary>A test case of the conformance corpus (<see cref="JpegXlConformanceCorpus"/>): a codestream and what its <c>test.json</c> describes.</summary>
internal sealed partial class JpegXlConformanceCase
{
    private readonly IReadOnlyDictionary<string, Func<CancellationToken, Task<FullPath>>> _files;

    public JpegXlConformanceCase(string name, FullPath inputPath, IReadOnlyList<JpegXlConformanceTolerance> frames, IReadOnlyDictionary<string, Func<CancellationToken, Task<FullPath>>> files)
    {
        Name = name;
        InputPath = inputPath;
        Frames = frames;
        _files = files;
    }

    public string Name { get; }

    /// <summary>Gets the path of the codestream (<c>input.jxl</c>).</summary>
    public FullPath InputPath { get; }

    /// <summary>Gets the tolerance of each displayed frame.</summary>
    public IReadOnlyList<JpegXlConformanceTolerance> Frames { get; }

    /// <summary>Gets whether the case has a JPEG file the codestream rebuilds (<c>reconstructed.jpg</c>).</summary>
    public bool HasReconstructedJpeg => _files.ContainsKey("reconstructed.jpg");

    /// <summary>Gets whether the case has the profile of the original image (<c>original.icc</c>).</summary>
    public bool HasOriginalIcc => _files.ContainsKey("original.icc");

    /// <summary>Downloads the reference decoding (<c>reference_image.npy</c>): float32 samples of shape (frames, height, width, channels).</summary>
    public Task<FullPath> GetReferenceImageAsync(CancellationToken cancellationToken) => _files["reference_image.npy"](cancellationToken);

    /// <summary>Downloads the profile of the reference decoding (<c>reference.icc</c>).</summary>
    public Task<FullPath> GetReferenceIccAsync(CancellationToken cancellationToken) => _files["reference.icc"](cancellationToken);

    /// <summary>Downloads the profile of the original image (<c>original.icc</c>).</summary>
    public Task<FullPath> GetOriginalIccAsync(CancellationToken cancellationToken) => _files["original.icc"](cancellationToken);

    /// <summary>Downloads the JPEG file the codestream rebuilds (<c>reconstructed.jpg</c>).</summary>
    public Task<FullPath> GetReconstructedJpegAsync(CancellationToken cancellationToken) => _files["reconstructed.jpg"](cancellationToken);

    public override string ToString() => Name;

    /// <summary>
    /// Reads a reference decoding one frame at a time: a version 1 or 2 <c>.npy</c> file of little-endian float32
    /// samples in C order, of shape (frames, height, width, channels).
    /// </summary>
    internal sealed partial class NpyFrameReader : IDisposable
    {
        private readonly FileStream _stream;
        private readonly byte[] _buffer;

        public NpyFrameReader(FullPath path)
        {
            _stream = File.OpenRead(path);
            Span<byte> prefix = stackalloc byte[12];
            _stream.ReadExactly(prefix[..8]);
            if (prefix[0] != 0x93 || !prefix[1..6].SequenceEqual("NUMPY"u8))
                throw new InvalidDataException($"'{path}' is not a .npy file.");

            int headerLength;
            if (prefix[6] == 1)
            {
                _stream.ReadExactly(prefix.Slice(8, 2));
                headerLength = BinaryPrimitives.ReadUInt16LittleEndian(prefix[8..]);
            }
            else
            {
                _stream.ReadExactly(prefix.Slice(8, 4));
                headerLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(prefix[8..]));
            }

            var header = new byte[headerLength];
            _stream.ReadExactly(header);
            var text = Encoding.ASCII.GetString(header);
            var match = HeaderRegex().Match(text);
            if (!match.Success)
                throw new InvalidDataException($"'{path}' does not hold a four-dimensional array of little-endian float32 in C order: {text.Trim()}");

            FrameCount = int.Parse(match.Groups["frames"].Value, CultureInfo.InvariantCulture);
            Height = int.Parse(match.Groups["height"].Value, CultureInfo.InvariantCulture);
            Width = int.Parse(match.Groups["width"].Value, CultureInfo.InvariantCulture);
            ChannelCount = int.Parse(match.Groups["channels"].Value, CultureInfo.InvariantCulture);
            _buffer = new byte[checked(Height * Width * ChannelCount * sizeof(float))];
        }

        public int FrameCount { get; }

        public int Height { get; }

        public int Width { get; }

        public int ChannelCount { get; }

        /// <summary>Reads the next frame.</summary>
        /// <param name="samples">Receives the samples of the frame, <see cref="ChannelCount"/> per pixel, row by row.</param>
        public void ReadFrame(float[] samples)
        {
            _stream.ReadExactly(_buffer);
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = BinaryPrimitives.ReadSingleLittleEndian(_buffer.AsSpan(i * sizeof(float)));
            }
        }

        public void Dispose() => _stream.Dispose();

        [GeneratedRegex(@"'descr':\s*'<f4',\s*'fortran_order':\s*False,\s*'shape':\s*\((?<frames>\d+),\s*(?<height>\d+),\s*(?<width>\d+),\s*(?<channels>\d+),?\)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
        private static partial Regex HeaderRegex();
    }
}
