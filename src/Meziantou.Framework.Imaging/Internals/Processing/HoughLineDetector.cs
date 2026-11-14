using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The Hough transform of lines (Duda and Hart, "Use of the Hough Transformation to Detect Lines and Curves in Pictures",
/// 1972): every edge pixel of the Canny detector votes for the lines through it, <c>ρ = x cos θ + y sin θ</c>, in an
/// accumulator of angles and distances, and the cells with the most votes are the lines.
/// </summary>
/// <remarks>
/// <para>
/// An edge pixel only votes for the angles within 30 degrees of the middle of the sector of its gradient (one of the four
/// of the Canny detector, each 45 degrees wide), the angles of the lines it can belong to (O'Gorman and Clowes, "Finding
/// Picture Edges Through Collinearity of Feature Points", 1976): the pixels where a Canny edge turns a corner then do not
/// make a line a few degrees away from the edge stronger than the edge, and the voting is about three times faster.
/// </para>
/// <para>
/// The angles are the multiples of <c>180 / n</c> degrees, their sines and cosines from <c>DeterministicAngle</c>. A pixel
/// votes at its index <c>(x, y)</c>, so that the pixels of an edge along an axis vote for one distance exactly (with the
/// centers, at half pixels, they would fall on the ties of the rounding); the distance is rounded to the nearest multiple of
/// the distance step, ties away from zero, and the distance of a line is moved by <c>(cos θ + sin θ) / 2</c> to the
/// coordinates of the centers. The votes are integers, so the
/// result does not depend on the platform.
/// </para>
/// <para>
/// A cell is a peak when it has at least the minimum of votes and is a local maximum of its 8 neighbors (the angles wrap:
/// the line of angle 180 - step is next to the one of angle 0 with the opposite distance). A segment of length L gets the
/// same votes over the angles within about 1 / L radian of its own, so equal votes are compared by the sum of the
/// distances of the votes to the middle of their cell (each one rounded to 1/65,536 of a step and added as an integer):
/// the angle where the pixels line up best wins. Then the cell that comes first (the smallest angle) wins, so that a
/// plateau gives one peak. The peaks are sorted in the same order, and kept unless an already kept line is closer than
/// both separations.
/// </para>
/// </remarks>
internal static class HoughLineDetector
{
    /// <summary>The half width, in degrees, of the angles an edge pixel votes for around the direction of its gradient.</summary>
    private const double VoteWindow = 30;

    /// <summary>The unit of the rounding residuals, in distance steps: they are added as integers.</summary>
    private const double ResidualScale = 65536;

    public static IReadOnlyList<HoughLine> Detect(AllocationScope scope, PixelFormat format, scoped in PixelLease lease, HoughLineOptions options, CancellationToken cancellationToken)
    {
        var width = lease.Width;
        var height = lease.Height;
        var angleCount = Math.Max(1, (int)Math.Round(180 / options.AngleStep, MidpointRounding.AwayFromZero));
        var angleStep = 180.0 / angleCount;
        var distanceStep = options.DistanceStep;
        var diagonal = Math.Sqrt(((double)width * width) + ((double)height * height));
        var offset = (int)Math.Ceiling(diagonal / distanceStep) + 1;
        var distanceCount = (2 * offset) + 1;
        var minVotes = options.MinVotes ?? Math.Max(2, Math.Min(width, height) / 4);

        using var detector = new CannyEdgeDetector(scope, format, new Size(width, height), options.EdgeOptions);
        using var accumulatorBuffer = ResampleWeights.Rent<int>(scope, (long)angleCount * distanceCount);
        using var residualBuffer = ResampleWeights.Rent<long>(scope, (long)angleCount * distanceCount);
        using var trigonometry = ResampleWeights.Rent<double>(scope, 2L * angleCount);
        detector.Detect(lease, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var accumulator = unsafe(MemoryMarshal.Cast<byte, int>(accumulatorBuffer.Span))[..(angleCount * distanceCount)];
        var residuals = unsafe(MemoryMarshal.Cast<byte, long>(residualBuffer.Span))[..(angleCount * distanceCount)];
        var cosines = unsafe(MemoryMarshal.Cast<byte, double>(trigonometry.Span))[..angleCount];
        var sines = unsafe(MemoryMarshal.Cast<byte, double>(trigonometry.Span)).Slice(angleCount, angleCount);
        for (var k = 0; k < angleCount; k++)
        {
            DeterministicAngle.SinCos(k * angleStep / 180, out var sin, out var cos);
            sines[k] = sin;
            cosines[k] = cos;
        }

        // The angles each of the four sectors of the gradient votes for: within VoteWindow degrees of the middle of the sector
        var sectorAngles = new List<int>[4];
        for (var sector = 0; sector < 4; sector++)
        {
            sectorAngles[sector] = [];
            for (var k = 0; k < angleCount; k++)
            {
                var difference = Math.Abs((k * angleStep) - (45 * sector));
                if (Math.Min(difference, 180 - difference) <= VoteWindow)
                {
                    sectorAngles[sector].Add(k);
                }
            }
        }

        for (var y = 0; y < height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            for (var x = 0; x < width; x++)
            {
                if (!detector.IsEdge(x, y))
                    continue;

                foreach (var k in sectorAngles[detector.GetGradientSectorAngle(x, y) / 45])
                {
                    var scaled = ((x * cosines[k]) + (y * sines[k])) / distanceStep;
                    var rounded = Math.Round(scaled, MidpointRounding.AwayFromZero);
                    var cell = (k * distanceCount) + (int)rounded + offset;
                    accumulator[cell]++;
                    residuals[cell] += (long)Math.Round(Math.Abs(scaled - rounded) * ResidualScale, MidpointRounding.AwayFromZero);
                }
            }
        }

        // The peaks, as cell indices
        var peaks = new List<(int Votes, long Residual, int Cell)>();
        for (var k = 0; k < angleCount; k++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var j = 0; j < distanceCount; j++)
            {
                var votes = accumulator[(k * distanceCount) + j];
                if (votes >= minVotes && IsPeak(accumulator, residuals, angleCount, distanceCount, k, j))
                {
                    peaks.Add((votes, residuals[(k * distanceCount) + j], (k * distanceCount) + j));
                }
            }
        }

        peaks.Sort(static (a, b) =>
        {
            var byVotes = b.Votes.CompareTo(a.Votes);
            if (byVotes != 0)
                return byVotes;

            var byResidual = a.Residual.CompareTo(b.Residual);
            return byResidual != 0 ? byResidual : a.Cell.CompareTo(b.Cell);
        });

        var lines = new List<HoughLine>();
        foreach (var (votes, _, cell) in peaks)
        {
            // The votes use the corners of the pixels: the line through the centers is half a pixel further along both axes
            var k = cell / distanceCount;
            var angle = k * angleStep;
            var distance = ((cell % distanceCount - offset) * distanceStep) + ((cosines[k] + sines[k]) / 2);
            if (lines.Exists(line => IsNear(line, angle, distance, options)))
                continue;

            lines.Add(new HoughLine(angle, distance, votes));
            if (lines.Count == options.MaxLines)
                break;
        }

        return lines;
    }

    /// <summary>
    /// Gets whether a cell is a local maximum: more votes than its neighbors, the smaller sum of rounding residuals among equal
    /// votes, and the cell that comes first among equal sums.
    /// </summary>
    private static bool IsPeak(ReadOnlySpan<int> accumulator, ReadOnlySpan<long> residuals, int angleCount, int distanceCount, int k, int j)
    {
        var cell = (k * distanceCount) + j;
        var votes = accumulator[cell];
        var residual = residuals[cell];
        for (var dk = -1; dk <= 1; dk++)
        {
            for (var dj = -1; dj <= 1; dj++)
            {
                if (dk == 0 && dj == 0)
                    continue;

                // Beyond the last angle comes the first one with the opposite distance, and conversely
                var nk = k + dk;
                var nj = j + dj;
                if (nk < 0 || nk >= angleCount)
                {
                    if (angleCount == 1)
                        continue;

                    nk = (nk + angleCount) % angleCount;
                    nj = distanceCount - 1 - nj;
                }

                if ((uint)nj >= (uint)distanceCount)
                    continue;

                var neighborCell = (nk * distanceCount) + nj;
                var neighborVotes = accumulator[neighborCell];
                if (neighborVotes != votes)
                {
                    if (neighborVotes > votes)
                        return false;

                    continue;
                }

                var neighborResidual = residuals[neighborCell];
                if (neighborResidual < residual || (neighborResidual == residual && neighborCell < cell))
                    return false;
            }
        }

        return true;
    }

    /// <summary>Gets whether a line is within both separations of another one, an angle near 180 being near 0 with the opposite distance.</summary>
    private static bool IsNear(HoughLine line, double angle, double distance, HoughLineOptions options)
    {
        var angleDifference = Math.Abs(line.Angle - angle);
        if (angleDifference <= options.MinAngleSeparation && Math.Abs(line.Distance - distance) <= options.MinDistanceSeparation)
            return true;

        return 180 - angleDifference <= options.MinAngleSeparation && Math.Abs(line.Distance + distance) <= options.MinDistanceSeparation;
    }
}
