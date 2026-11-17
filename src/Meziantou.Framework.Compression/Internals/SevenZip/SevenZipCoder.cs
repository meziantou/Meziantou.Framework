namespace Meziantou.Framework.Compression.Internals.SevenZip;

/// <summary>A coder of a folder: a compression method, a filter, or an encryption.</summary>
internal sealed class SevenZipCoder
{
    public SevenZipCoder(ulong methodId, byte[] properties, int inStreamCount = 1, int outStreamCount = 1)
    {
        MethodId = methodId;
        Properties = properties;
        InStreamCount = inStreamCount;
        OutStreamCount = outStreamCount;
    }

    public ulong MethodId { get; }

    public byte[] Properties { get; }

    /// <summary>Gets the number of packed-side streams (the inputs of the decoder).</summary>
    public int InStreamCount { get; }

    /// <summary>Gets the number of unpacked-side streams (the outputs of the decoder).</summary>
    public int OutStreamCount { get; }
}
