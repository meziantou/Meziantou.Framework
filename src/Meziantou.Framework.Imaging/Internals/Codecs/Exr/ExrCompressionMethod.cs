namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The value of the <c>compression</c> attribute.</summary>
internal enum ExrCompressionMethod
{
    None = 0,
    Rle = 1,
    Zips = 2,
    Zip = 3,
    Piz = 4,
    Pxr24 = 5,
    B44 = 6,
    B44A = 7,
    Dwaa = 8,
    Dwab = 9,
    Htj2k256 = 10,
    Htj2k32 = 11,
    Lj2k = 12,
    Zstd = 13,
}
