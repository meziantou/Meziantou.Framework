namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The hash of the sample arrays of a decoded picture (Rec. ITU-T H.265, clauses D.2.20 "Decoded picture hash SEI message
/// syntax" and D.3.20 "Decoded picture hash SEI message semantics"): one value per colour component.
/// </summary>
internal sealed class HevcDecodedPictureHash
{
    public const int Md5 = 0;
    public const int Crc = 1;
    public const int Checksum = 2;

    private readonly byte[] _md5;
    private readonly uint[] _values;

    public HevcDecodedPictureHash(int hashType, int componentCount, byte[] md5, uint[] values)
    {
        HashType = hashType;
        ComponentCount = componentCount;
        _md5 = md5;
        _values = values;
    }

    /// <summary>Gets <c>hash_type</c> (Table D.10): <see cref="Md5"/>, <see cref="Crc"/> or <see cref="Checksum"/>.</summary>
    public int HashType { get; }

    /// <summary>Gets the number of colour components that have a value: 1 for a monochrome picture, otherwise 3.</summary>
    public int ComponentCount { get; }

    /// <summary>Gets <c>picture_md5[ cIdx ]</c>: 16 bytes.</summary>
    public ReadOnlySpan<byte> GetMd5(int cIdx) => _md5.AsSpan(cIdx * 16, 16);

    /// <summary>Gets <c>picture_crc[ cIdx ]</c> or <c>picture_checksum[ cIdx ]</c>.</summary>
    public uint GetValue(int cIdx) => _values[cIdx];
}
