using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.SevenZip;

/// <summary>A coder to encode with: its ID, and a function that creates the encoder over its output and returns its properties.</summary>
internal sealed class SevenZipEncoderSpec
{
    public SevenZipEncoderSpec(ulong methodId, Func<Stream, (Stream Encoder, Func<byte[]> Properties)> create)
    {
        MethodId = methodId;
        Create = create;
    }

    public ulong MethodId { get; }

    /// <summary>Gets the function that creates the encoder. The encoder owns its output.</summary>
    public Func<Stream, (Stream Encoder, Func<byte[]> Properties)> Create { get; }

    public static SevenZipEncoderSpec Copy { get; } = new(SevenZipMethodId.Copy, output => (output, () => []));
}
