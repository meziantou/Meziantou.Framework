using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The choice of transforms. None is always smallest: the color indexing of an image with few colors loses to the predictor
/// on smooth gradients, the predictor loses to plain pixels on screenshots (whose repetitions are exact), and small predictor
/// tiles win on photographs but cost side information on flat images. From effort 3, every candidate is encoded at a low
/// effort into a counting writer (no output is stored), and the candidates are ranked by size.
/// </summary>
internal sealed partial class Vp8LEncoder
{
    /// <summary>
    /// The effort of the encodings that rank the candidates: 4 predictor modes, no color transform and no color cache, a single
    /// parse, and no entropy image (it shrinks every candidate alike: the ranking of the test images did not change without
    /// it). Ranking at effort 3 (14 predictor modes, the color transform) made the outputs 0.1 % smaller for 15 to 25 % more
    /// time.
    /// </summary>
    private const int RankingEffort = 2;

    /// <summary>Images up to this many pixels are ranked whole; larger ones on a sample of bands of rows.</summary>
    private const int RankingSamplePixels = 1 << 18;

    /// <summary>The height of the bands of a ranking sample: the largest tile of the transforms, so that their tiles stay whole.</summary>
    private const int RankingBandHeight = 1 << TransformTileBits;

    /// <summary>Gets the candidate configurations of an image, ranked by their estimated size (smallest first).</summary>
    /// <param name="scope">The scope charged for the working memory: a copy of the pixels and the encoder state of one candidate at a time.</param>
    /// <param name="argb">The pixels (<c>0xAARRGGBB</c>), not modified.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="effort">The effort, 0 to 9.</param>
    /// <param name="cancellationToken">Checked between candidates and rows.</param>
    /// <returns>At least one configuration.</returns>
    public static Vp8LConfiguration[] RankConfigurations(AllocationScope scope, ReadOnlySpan<uint> argb, int width, int height, int effort, CancellationToken cancellationToken)
    {
        var candidates = GetCandidates(effort, CountColors(argb), IsFlat(argb, width));
        if (candidates.Length == 1)
            return candidates;

        // A large image is ranked on bands of rows spread over its height, about a quarter of it (at least 2^18 pixels)
        var bandCount = Vp8LDecoder.DivRoundUp(height, TransformTileBits);
        if ((long)width * height > RankingSamplePixels)
        {
            var sampleRows = Math.Max(height / 4, RankingSamplePixels / width);
            bandCount = Math.Min(bandCount, Math.Max(1, Vp8LDecoder.DivRoundUp(sampleRows, TransformTileBits)));
        }

        var sampleHeight = 0;
        for (var band = 0; band < bandCount; band++)
        {
            sampleHeight += GetBand(band).Rows;
        }

        var sampleLength = (long)width * sampleHeight;
        var sizes = new long[candidates.Length];
        using (var copyBuffer = scope.Rent((int)Math.Min(CheckedSizes.MaxBufferLength, sampleLength * sizeof(uint)), AllocationKind.Temporary, clear: false))
        {
            var copy = unsafe(MemoryMarshal.Cast<byte, uint>(copyBuffer.RawBuffer.AsSpan()))[..(int)sampleLength];
            for (var i = 0; i < candidates.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var offset = 0;
                for (var band = 0; band < bandCount; band++)
                {
                    var (start, rows) = GetBand(band);
                    argb.Slice(start * width, rows * width).CopyTo(copy[offset..]);
                    offset += rows * width;
                }

                using var counter = LsbBitWriter.CreateCounter(scope);
                using var encoder = new Vp8LEncoder(scope, counter, Math.Min(effort, RankingEffort), candidates[i].EntropyPredictors, cancellationToken) { Ranking = true };
                encoder.EncodeMainImage(copy, width, sampleHeight, candidates[i]);
                sizes[i] = counter.BitCount;
            }
        }

        // A stable sort: on a tie, the candidate listed first
        var order = new int[candidates.Length];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) => sizes[a] != sizes[b] ? sizes[a].CompareTo(sizes[b]) : a.CompareTo(b));
        var ranked = new Vp8LConfiguration[candidates.Length];
        for (var i = 0; i < ranked.Length; i++)
        {
            ranked[i] = candidates[order[i]];
        }

        return ranked;

        // The bands are evenly spaced; the last one ends with the image
        (int Start, int Rows) GetBand(int band)
        {
            var totalBands = Vp8LDecoder.DivRoundUp(height, TransformTileBits);
            var index = bandCount == totalBands ? band : bandCount == 1 ? totalBands / 2 : (int)((long)band * (totalBands - 1) / (bandCount - 1));
            var start = index * RankingBandHeight;
            return (start, Math.Min(RankingBandHeight, height - start));
        }
    }

    /// <summary>
    /// The candidate configurations of an effort, for an image of <paramref name="colorCount"/> colors (257 for more than 256)
    /// where <paramref name="flat"/> tells whether at least half the pixels repeat their left or top neighbor.
    /// </summary>
    /// <remarks>
    /// Measured on screenshots, icons, photographs, gradients and synthetic graphics: the pixels as they are (subtract green
    /// only) win on screenshots and flat graphics and lose by half or more on images that are not flat; the bundled color
    /// indexing of 16 colors or fewer wins below effort 7; predictor tiles of 16 or 64 pixels differ by 2 % at most.
    /// </remarks>
    internal static Vp8LConfiguration[] GetCandidates(int effort, int colorCount, bool flat)
    {
        var entropy = effort >= 6;
        var hasPalette = colorCount <= 256;
        if (effort < 3 || (colorCount <= 16 && effort < 7))
            return [hasPalette ? new(Vp8LTransformMode.Palette, 0, entropy) : new(Vp8LTransformMode.Spatial, effort < 3 ? TransformTileBits : 5, entropy)];

        var candidates = new List<Vp8LConfiguration>();
        if (hasPalette)
        {
            candidates.Add(new(Vp8LTransformMode.Palette, 0, entropy));
            if (colorCount > 16 && effort >= 5)
            {
                candidates.Add(new(Vp8LTransformMode.PredictedPalette, 5, entropy));
            }
        }

        int[] bits = effort switch
        {
            < 7 => [5],
            < 9 => [4, 6],
            _ => [3, 4, 5, 6],
        };

        foreach (var value in bits)
        {
            candidates.Add(new(Vp8LTransformMode.Spatial, value, entropy));
            if (effort >= 9)
            {
                candidates.Add(new(Vp8LTransformMode.Spatial, value, !entropy));
            }
        }

        if (flat)
        {
            candidates.Add(new(Vp8LTransformMode.SubtractGreen, 0, entropy));
        }

        return [.. candidates];
    }

    /// <summary>The number of distinct colors, up to 257 (257 means more than 256).</summary>
    private static int CountColors(ReadOnlySpan<uint> argb) => TryBuildPalette(argb, out var palette) ? palette.Length : 257;

    /// <summary>Whether at least half the pixels are equal to their left or top neighbor.</summary>
    private static bool IsFlat(ReadOnlySpan<uint> argb, int width)
    {
        var repeated = 0;
        for (var i = 1; i < argb.Length; i++)
        {
            if (argb[i] == argb[i - 1] || (i >= width && argb[i] == argb[i - width]))
            {
                repeated++;
            }
        }

        return repeated * 2L >= argb.Length;
    }
}
