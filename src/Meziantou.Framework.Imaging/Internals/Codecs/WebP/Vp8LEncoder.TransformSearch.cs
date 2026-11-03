namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The choices of the transforms of a main image by the cost of their output: subtract green when it makes the residuals
/// cheaper, the predictor mode and the color transform multipliers of each tile by the cost of their residuals under the
/// statistics of the tiles chosen before (<see cref="Vp8LPredictorSearch.AllModesByCost"/>, <see cref="Vp8LColorSearch.Cost"/>).
/// </summary>
/// <remarks>
/// The statistics of the tiles chosen before are what the prefix codes will see: a tile prefers the residuals that are
/// already frequent, which a sum of magnitudes or the entropy of the tile alone ignores. The statistics start from a prior
/// peaked at zero (a two-sided geometric distribution of about 250 samples) and the bit costs, in fixed point, are refreshed
/// once per row of tiles. Measured on photographs with tiles of 8 pixels: the predictor modes are 0.2 to 1.9 % smaller than
/// by the sum of magnitudes, and the color transform is 0.1 to 0.3 % smaller and several times faster than a search of each
/// multiplier by the entropy of the tile.
/// </remarks>
internal sealed partial class Vp8LEncoder
{
    /// <summary>The fixed-point scale of the bit costs: integer sums do not depend on the order of the additions.</summary>
    private const int CostScale = 1 << 10;

    /// <summary>The bits a tile saves (scaled) when it keeps the mode of the tile on its left: the mode image compresses better.</summary>
    private const int LeftModeBonus = 4 * CostScale;

    /// <summary>
    /// Whether subtracting green makes the red and blue residuals cheaper: the entropy of the residuals of the gradient
    /// predictor (L + T - TL) with and without it, on rows sampled to about 65,536 pixels.
    /// </summary>
    internal static bool IsSubtractGreenCheaper(ReadOnlySpan<uint> argb, int width)
    {
        var height = argb.Length / width;
        Span<int> histograms = stackalloc int[4 * 256];
        histograms.Clear();
        var red = histograms[..256];
        var blue = histograms.Slice(256, 256);
        var redMinusGreen = histograms.Slice(512, 256);
        var blueMinusGreen = histograms.Slice(768, 256);
        var step = (int)((long)argb.Length / 65536 / width) + 1;
        var count = 0;
        for (var y = 1; y < height; y += step)
        {
            var row = argb.Slice(y * width, width);
            var above = argb.Slice((y - 1) * width, width);
            for (var x = 1; x < width; x++)
            {
                uint current = row[x], left = row[x - 1], top = above[x], topLeft = above[x - 1];
                var r = (int)(((current >> 16) - (left >> 16) - (top >> 16) + (topLeft >> 16)) & 0xFF);
                var g = (int)(((current >> 8) - (left >> 8) - (top >> 8) + (topLeft >> 8)) & 0xFF);
                var b = (int)((current - left - top + topLeft) & 0xFF);
                red[r]++;
                blue[b]++;
                redMinusGreen[(r - g) & 0xFF]++;
                blueMinusGreen[(b - g) & 0xFF]++;
            }

            count += width - 1;
        }

        return count == 0 || Entropy(redMinusGreen, count) + Entropy(blueMinusGreen, count) < Entropy(red, count) + Entropy(blue, count);
    }

    /// <summary>Chooses the mode of each tile by the cost of its residuals under the statistics of the tiles chosen before.</summary>
    private void ChoosePredictorsByCost(ReadOnlySpan<uint> argb, int width, int height, int bits, int tilesX, int tilesY, Span<uint> modes)
    {
        var statistics = new ResidualStatistics(channels: 4);
        var tileSize = 1 << (2 * bits);
        var candidate = new uint[tileSize];
        var best = new uint[tileSize];
        for (var ty = 0; ty < tilesY; ty++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            statistics.Refresh();
            var y0 = ty << bits;
            var y1 = Math.Min(height, y0 + (1 << bits));
            for (var tx = 0; tx < tilesX; tx++)
            {
                var x0 = tx << bits;
                var x1 = Math.Min(width, x0 + (1 << bits));
                var tileWidth = x1 - x0;
                var count = tileWidth * (y1 - y0);
                var leftMode = tx > 0 ? (int)((modes[(ty * tilesX) + tx - 1] >> 8) & 0xF) : -1;
                var bestMode = 0;
                var bestCost = long.MaxValue;
                for (var mode = 0; mode < 14; mode++)
                {
                    for (var y = y0; y < y1; y++)
                    {
                        PredictionResiduals(argb, width, y, x0, x1, mode, candidate.AsSpan((y - y0) * tileWidth, tileWidth));
                    }

                    var cost = statistics.Cost(candidate.AsSpan(0, count)) - (mode == leftMode ? LeftModeBonus : 0);
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestMode = mode;
                        (candidate, best) = (best, candidate);
                    }
                }

                statistics.Add(best.AsSpan(0, count));
                modes[(ty * tilesX) + tx] = 0xFF000000u | ((uint)bestMode << 8);
            }
        }
    }

    /// <summary>
    /// Applies the color transform with the multipliers of each tile chosen by the cost of the transformed red and blue under
    /// the statistics of the tiles chosen before, among their least-squares estimates (and their neighbors), zero and the
    /// multipliers of the left and top tiles.
    /// </summary>
    private void ApplyColorTransformByCost(Span<uint> argb, int width, int height, int bits)
    {
        var tilesX = Vp8LDecoder.DivRoundUp(width, bits);
        var tilesY = Vp8LDecoder.DivRoundUp(height, bits);
        var elements = RentPixels((long)tilesX * tilesY);
        var statistics = new ResidualStatistics(channels: 2);
        Span<int> greenToRedCandidates = stackalloc int[6];
        Span<(int GreenToBlue, int RedToBlue)> blueCandidates = stackalloc (int, int)[9];
        for (var ty = 0; ty < tilesY; ty++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            statistics.Refresh();
            var y0 = ty << bits;
            var y1 = Math.Min(height, y0 + (1 << bits));
            for (var tx = 0; tx < tilesX; tx++)
            {
                var x0 = tx << bits;
                var x1 = Math.Min(width, x0 + (1 << bits));
                var left = tx > 0 ? elements[(ty * tilesX) + tx - 1] : 0xFF000000u;
                var top = ty > 0 ? elements[((ty - 1) * tilesX) + tx] : 0xFF000000u;

                // Least squares: red ~ greenToRed * green / 32, blue ~ (greenToBlue * green + redToBlue * red) / 32
                double gg = 0, gr = 0, rr = 0, gb = 0, rb = 0;
                for (var y = y0; y < y1; y++)
                {
                    foreach (var pixel in argb.Slice((y * width) + x0, x1 - x0))
                    {
                        int g = (sbyte)(pixel >> 8), r = (sbyte)(pixel >> 16), b = (sbyte)pixel;
                        gg += g * g;
                        gr += g * r;
                        rr += r * r;
                        gb += g * b;
                        rb += r * b;
                    }
                }

                var estimate = gg > 0 ? ToMultiplier(32 * gr / gg) : 0;
                var redCount = 0;
                AddMultiplier(greenToRedCandidates, ref redCount, estimate);
                AddMultiplier(greenToRedCandidates, ref redCount, estimate - 1);
                AddMultiplier(greenToRedCandidates, ref redCount, estimate + 1);
                AddMultiplier(greenToRedCandidates, ref redCount, 0);
                AddMultiplier(greenToRedCandidates, ref redCount, (sbyte)left);
                AddMultiplier(greenToRedCandidates, ref redCount, (sbyte)top);
                var greenToRed = 0;
                var bestCost = long.MaxValue;
                foreach (var multiplier in greenToRedCandidates[..redCount])
                {
                    long cost = 0;
                    for (var y = y0; y < y1; y++)
                    {
                        foreach (var pixel in argb.Slice((y * width) + x0, x1 - x0))
                        {
                            cost += statistics.Bits(0, (int)(pixel >> 16) - Vp8LTransforms.ColorTransformDelta((sbyte)multiplier, (sbyte)(pixel >> 8)));
                        }
                    }

                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        greenToRed = multiplier;
                    }
                }

                var determinant = (gg * rr) - (gr * gr);
                int greenToBlueEstimate = 0, redToBlueEstimate = 0;
                if (Math.Abs(determinant) > 1e-9)
                {
                    greenToBlueEstimate = ToMultiplier(32 * ((gb * rr) - (rb * gr)) / determinant);
                    redToBlueEstimate = ToMultiplier(32 * ((rb * gg) - (gb * gr)) / determinant);
                }
                else if (gg > 0)
                {
                    greenToBlueEstimate = ToMultiplier(32 * gb / gg);
                }

                var blueCount = 0;
                blueCandidates[blueCount++] = (greenToBlueEstimate, redToBlueEstimate);
                blueCandidates[blueCount++] = (greenToBlueEstimate - 1, redToBlueEstimate);
                blueCandidates[blueCount++] = (greenToBlueEstimate + 1, redToBlueEstimate);
                blueCandidates[blueCount++] = (greenToBlueEstimate, redToBlueEstimate - 1);
                blueCandidates[blueCount++] = (greenToBlueEstimate, redToBlueEstimate + 1);
                blueCandidates[blueCount++] = (gg > 0 ? ToMultiplier(32 * gb / gg) : 0, 0);
                blueCandidates[blueCount++] = (0, 0);
                blueCandidates[blueCount++] = ((sbyte)(left >> 8), (sbyte)(left >> 16));
                blueCandidates[blueCount++] = ((sbyte)(top >> 8), (sbyte)(top >> 16));
                int greenToBlue = 0, redToBlue = 0;
                bestCost = long.MaxValue;
                foreach (var (greenCandidate, redCandidate) in blueCandidates[..blueCount])
                {
                    var greenMultiplier = (sbyte)Math.Clamp(greenCandidate, -128, 127);
                    var redMultiplier = (sbyte)Math.Clamp(redCandidate, -128, 127);
                    long cost = 0;
                    for (var y = y0; y < y1; y++)
                    {
                        foreach (var pixel in argb.Slice((y * width) + x0, x1 - x0))
                        {
                            cost += statistics.Bits(1, (int)pixel - Vp8LTransforms.ColorTransformDelta(greenMultiplier, (sbyte)(pixel >> 8)) - Vp8LTransforms.ColorTransformDelta(redMultiplier, (sbyte)(pixel >> 16)));
                        }
                    }

                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        greenToBlue = greenMultiplier;
                        redToBlue = redMultiplier;
                    }
                }

                elements[(ty * tilesX) + tx] = 0xFF000000u | ((uint)(byte)redToBlue << 16) | ((uint)(byte)greenToBlue << 8) | (byte)greenToRed;
                for (var y = y0; y < y1; y++)
                {
                    var row = argb.Slice((y * width) + x0, x1 - x0);
                    for (var i = 0; i < row.Length; i++)
                    {
                        var transformed = Vp8LTransforms.ForwardColorTransform(row[i], (sbyte)greenToRed, (sbyte)greenToBlue, (sbyte)redToBlue);
                        row[i] = transformed;
                        statistics.Add(0, (int)(transformed >> 16));
                        statistics.Add(1, (int)transformed);
                    }
                }
            }
        }

        _writer.WriteBits(1, 1);
        _writer.WriteBits(1, 2);
        _writer.WriteBits((uint)(bits - 2), 3);
        EncodeSpatialImage(elements, tilesX, tilesY, isMainImage: false);

        static int ToMultiplier(double value) => (int)Math.Clamp(Math.Round(value, MidpointRounding.ToEven), -128, 127);

        static void AddMultiplier(Span<int> candidates, ref int count, int value)
        {
            value = Math.Clamp(value, -128, 127);
            if (!candidates[..count].Contains(value))
            {
                candidates[count++] = value;
            }
        }
    }

    /// <summary>
    /// The counts of the byte values of the channels of the residuals chosen so far, and the bit cost of each value
    /// (<c>log2(total / count)</c> scaled by <see cref="CostScale"/>, as of the last <see cref="Refresh"/>).
    /// </summary>
    private sealed class ResidualStatistics
    {
        private readonly int[] _counts;
        private readonly long[] _totals;
        private readonly int[] _bits;

        public ResidualStatistics(int channels)
        {
            _counts = new int[channels * 256];
            _totals = new long[channels];
            _bits = new int[channels * 256];
            for (var channel = 0; channel < channels; channel++)
            {
                for (var value = 0; value < 256; value++)
                {
                    var magnitude = Math.Min(value, 256 - value);
                    var prior = Math.Max(1, (int)(64 * Math.Pow(0.75, magnitude)));
                    _counts[(channel * 256) + value] = prior;
                    _totals[channel] += prior;
                }
            }

            Refresh();
        }

        /// <summary>Recomputes the bit costs from the counts.</summary>
        public void Refresh()
        {
            for (var channel = 0; channel < _totals.Length; channel++)
            {
                var log2Total = Log2Count(_totals[channel]);
                for (var value = 0; value < 256; value++)
                {
                    _bits[(channel * 256) + value] = (int)Math.Round((log2Total - Log2Count(_counts[(channel * 256) + value])) * CostScale, MidpointRounding.ToEven);
                }
            }
        }

        /// <summary>Gets the bit cost (scaled) of the low byte of <paramref name="value"/> in a channel.</summary>
        public int Bits(int channel, int value) => _bits[(channel * 256) + (value & 0xFF)];

        public void Add(int channel, int value)
        {
            _counts[(channel * 256) + (value & 0xFF)]++;
            _totals[channel]++;
        }

        /// <summary>Gets the bit cost (scaled) of residual pixels (4 channels: blue, green, red, alpha).</summary>
        public long Cost(ReadOnlySpan<uint> residuals)
        {
            var bits = _bits.AsSpan(0, 4 * 256);
            long cost = 0;
            foreach (var residual in residuals)
            {
                cost += bits[(int)(residual & 0xFF)] + bits[256 + (int)((residual >> 8) & 0xFF)] + bits[512 + (int)((residual >> 16) & 0xFF)] + bits[768 + (int)(residual >> 24)];
            }

            return cost;
        }

        /// <summary>Adds residual pixels (4 channels).</summary>
        public void Add(ReadOnlySpan<uint> residuals)
        {
            var counts = _counts.AsSpan(0, 4 * 256);
            foreach (var residual in residuals)
            {
                counts[(int)(residual & 0xFF)]++;
                counts[256 + (int)((residual >> 8) & 0xFF)]++;
                counts[512 + (int)((residual >> 16) & 0xFF)]++;
                counts[768 + (int)(residual >> 24)]++;
            }

            for (var channel = 0; channel < 4; channel++)
            {
                _totals[channel] += residuals.Length;
            }
        }
    }
}
