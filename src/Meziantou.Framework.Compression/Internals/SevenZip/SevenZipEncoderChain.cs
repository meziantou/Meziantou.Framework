using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.SevenZip;

/// <summary>
/// Coders chained to encode one folder with one packed stream: the first coder is the one closest to the unpacked data
/// (a filter), the last one writes the packed stream (an encryption, or the compression). The chain counts the bytes that
/// enter each coder, which are the unpack sizes of the folder.
/// </summary>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Complete disposes the encoders, which dispose the counter of the packed stream")]
internal sealed class SevenZipEncoderChain
{
    private readonly SevenZipEncoderSpec[] _specs;
    private readonly CountingWriteStream[] _counters;
    private readonly Func<byte[]>[] _properties;
    private readonly CountingWriteStream _packCounter;
    private readonly long _packOffset;

    public SevenZipEncoderChain(Stream output, IReadOnlyList<SevenZipEncoderSpec> specs)
    {
        _specs = [.. specs];
        _packOffset = output.Position;
        _packCounter = new CountingWriteStream(output, leaveOpen: true);
        _counters = new CountingWriteStream[_specs.Length];
        _properties = new Func<byte[]>[_specs.Length];
        Stream current = _packCounter;
        for (var i = _specs.Length - 1; i >= 0; i--)
        {
            var (encoder, properties) = _specs[i].Create(current);
            _properties[i] = properties;
            _counters[i] = new CountingWriteStream(encoder, leaveOpen: false);
            current = _counters[i];
        }

        Input = current;
    }

    /// <summary>Gets the stream to write the unpacked data to.</summary>
    public Stream Input { get; }

    public long UnpackedLength => _counters[0].Count;

    /// <summary>Flushes the encoders and returns the folder they wrote.</summary>
    public SevenZipFolder Complete()
    {
        Input.Dispose();
        var coders = new SevenZipCoder[_specs.Length];
        var unpackSizes = new long[_specs.Length];
        var bindPairs = new SevenZipBindPair[_specs.Length - 1];
        for (var i = 0; i < _specs.Length; i++)
        {
            coders[i] = new SevenZipCoder(_specs[i].MethodId, _properties[i]());
            unpackSizes[i] = _counters[i].Count;
            if (i > 0)
            {
                bindPairs[i - 1] = new SevenZipBindPair(i - 1, i);
            }
        }

        return new SevenZipFolder
        {
            Coders = coders,
            BindPairs = bindPairs,
            PackedStreams = [_specs.Length - 1],
            UnpackSizes = unpackSizes,
            PackOffsets = [_packOffset],
            PackSizes = [_packCounter.Count],
        };
    }
}
