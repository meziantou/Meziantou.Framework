using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Hevc;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>Decodes whole HEVC byte streams with <see cref="HevcDecoder"/>, one access unit at a time, for the tests.</summary>
internal static class HevcTestDecoder
{
    /// <summary>A picture that the decoder output, copied out of its buffers.</summary>
    /// <param name="PicOrderCntVal">The picture order count of the picture.</param>
    /// <param name="AccessUnitIndex">The index of the access unit of the picture.</param>
    /// <param name="Sps">The sequence parameter set that was active for the picture.</param>
    /// <param name="Planes">The samples of each plane: its rows, without padding.</param>
    /// <param name="Hash">The hash that the bitstream carries for the picture, or <see langword="null"/>.</param>
    internal sealed record DecodedPicture(int PicOrderCntVal, int AccessUnitIndex, HevcSequenceParameterSet Sps, ushort[][] Planes, HevcDecodedPictureHash? Hash)
    {
        public int GetWidth(int plane) => plane is 0 ? Sps.PicWidthInLumaSamples : Sps.PicWidthInLumaSamples / Sps.SubWidthC;

        public int GetHeight(int plane) => plane is 0 ? Sps.PicHeightInLumaSamples : Sps.PicHeightInLumaSamples / Sps.SubHeightC;
    }

    /// <summary>Decodes every picture of a byte stream (Annex B), in output order.</summary>
    public static List<DecodedPicture> Decode(ReadOnlyMemory<byte> annexB, AllocationScope scope, out HevcTools tools, HevcInLoopFilters inLoopFilters = HevcInLoopFilters.All, int nalLengthSize = 4)
    {
        var accessUnits = ReferenceHevcStream.GroupAccessUnits(ReferenceHevcStream.SplitAnnexB(annexB));
        var pictures = new List<DecodedPicture>();
        using var decoder = new HevcDecoder(scope) { InLoopFilters = inLoopFilters };
        foreach (var accessUnit in accessUnits)
        {
            var data = ReferenceHevcStream.ToLengthPrefixed(accessUnit, nalLengthSize);
            decoder.SetInput(data, 0, data.Length, nalLengthSize);
            Drain(decoder, pictures);
        }

        decoder.EndOfSequence();
        Drain(decoder, pictures);
        tools = decoder.Tools;
        return pictures;
    }

    /// <summary>
    /// Checks the samples of a picture against the hash of its decoded picture hash SEI message (Rec. ITU-T H.265, clause
    /// D.3.20), computed by the reference of the test harness.
    /// </summary>
    public static void AssertMatchesHash(DecodedPicture picture, string name)
    {
        var hash = picture.Hash;
        Assert.NotNull(hash, $"{name}: the picture has no decoded picture hash SEI message");
        Assert.Equal(picture.Planes.Length, hash.ComponentCount);
        for (var cIdx = 0; cIdx < picture.Planes.Length; cIdx++)
        {
            var width = picture.GetWidth(cIdx);
            var height = picture.GetHeight(cIdx);
            var samples = picture.Planes[cIdx];
            var message = $"{name}: the plane {cIdx} of the picture {picture.PicOrderCntVal} of the access unit {picture.AccessUnitIndex} does not have the hash of the bitstream";
            switch (hash.HashType)
            {
                case HevcDecodedPictureHash.Md5:
                    Assert.Equal(Convert.ToHexString(hash.GetMd5(cIdx)), Convert.ToHexString(ReferenceHevcPictureHash.ComputeMd5(samples, width, height, width, picture.Sps.BitDepth)), message);
                    break;

                case HevcDecodedPictureHash.Crc:
                    Assert.Equal(hash.GetValue(cIdx), ReferenceHevcPictureHash.ComputeCrc(samples, width, height, width, picture.Sps.BitDepth), message);
                    break;

                default:
                    Assert.Equal(hash.GetValue(cIdx), ReferenceHevcPictureHash.ComputeChecksum(samples, width, height, width, picture.Sps.BitDepth), message);
                    break;
            }
        }
    }

    private static void Drain(HevcDecoder decoder, List<DecodedPicture> pictures)
    {
        while (decoder.DecodeNextOutputPicture(XunitCancellationToken) is { } picture)
        {
            try
            {
                var planes = new ushort[picture.PlaneCount][];
                for (var i = 0; i < planes.Length; i++)
                {
                    var plane = picture.GetPlane(i);
                    planes[i] = new ushort[plane.Width * plane.Height];
                    for (var y = 0; y < plane.Height; y++)
                    {
                        plane.GetRow(y).CopyTo(planes[i].AsSpan(y * plane.Width));
                    }
                }

                pictures.Add(new DecodedPicture(picture.PicOrderCntVal, picture.AccessUnitIndex, picture.Sps, planes, picture.DecodedPictureHash));
            }
            finally
            {
                picture.Release();
            }
        }
    }
}
