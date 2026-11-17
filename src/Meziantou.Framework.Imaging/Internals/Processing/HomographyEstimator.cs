namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Estimates the projective transformation (homography) that maps points onto others: the normalized direct linear
/// transformation (Hartley, "In Defense of the Eight-Point Algorithm", 1997; Hartley and Zisserman, "Multiple View Geometry
/// in Computer Vision", algorithm 4.2), and RANSAC (Fischler and Bolles, "Random Sample Consensus", 1981) for outliers.
/// </summary>
/// <remarks>
/// <para>
/// Each set of points is moved so that its centroid is the origin and scaled so that the mean distance to it is √2. Each
/// correspondence gives two rows of the 2n x 9 matrix <c>A</c> of the equations <c>u (h31 x + h32 y + h33) = h11 x + h12 y
/// + h13</c> and likewise for <c>v</c>; the homography is the unit vector <c>h</c> that minimizes <c>|A h|</c>, the
/// eigenvector of the smallest eigenvalue of <c>AᵀA</c>, found with the cyclic Jacobi method (rotations computed with
/// square roots only, in a fixed order, so the result does not depend on the platform). The points determine no
/// homography when the second smallest eigenvalue is not larger than 10⁻¹² times the largest one (three collinear points
/// of four, all the points on a line), or when the result is singular. The scale of the result makes <c>W = 1</c> at the
/// centroid of the source points, so that they are in front of the viewer.
/// </para>
/// <para>
/// RANSAC draws samples of four distinct correspondences with a xorshift generator seeded by the options, keeps the
/// homography of the sample that has the most inliers (the correspondences whose source point lands within the threshold
/// of its destination; ties go to the smaller sum of squared errors, then to the earlier sample), refits it on all its
/// inliers, and keeps the refit when it has at least as many inliers.
/// </para>
/// </remarks>
internal static class HomographyEstimator
{
    private const int MaxJacobiSweeps = 100;

    /// <summary>Fits the homography of all the correspondences by least squares, or returns <see langword="false"/> when they determine none.</summary>
    public static bool TryFit(ReadOnlySpan<PointD> source, ReadOnlySpan<PointD> destination, ReadOnlySpan<int> indices, out ProjectiveTransform transform)
    {
        transform = default;
        if (indices.Length < 4)
            return false;

        var (sourceCenter, sourceScale) = GetNormalization(source, indices);
        var (destinationCenter, destinationScale) = GetNormalization(destination, indices);
        if (sourceScale == 0 || destinationScale == 0)
            return false;

        // AᵀA, accumulated row by row
        Span<double> matrix = stackalloc double[81];
        matrix.Clear();
        Span<double> row = stackalloc double[9];
        foreach (var index in indices)
        {
            var x = (source[index].X - sourceCenter.X) * sourceScale;
            var y = (source[index].Y - sourceCenter.Y) * sourceScale;
            var u = (destination[index].X - destinationCenter.X) * destinationScale;
            var v = (destination[index].Y - destinationCenter.Y) * destinationScale;

            row[0] = -x;
            row[1] = -y;
            row[2] = -1;
            row[3] = 0;
            row[4] = 0;
            row[5] = 0;
            row[6] = u * x;
            row[7] = u * y;
            row[8] = u;
            AddOuterProduct(matrix, row);

            row[0] = 0;
            row[1] = 0;
            row[2] = 0;
            row[3] = -x;
            row[4] = -y;
            row[5] = -1;
            row[6] = v * x;
            row[7] = v * y;
            row[8] = v;
            AddOuterProduct(matrix, row);
        }

        Span<double> eigenvalues = stackalloc double[9];
        Span<double> eigenvectors = stackalloc double[81];
        DiagonalizeSymmetric(matrix, eigenvalues, eigenvectors);

        // The smallest eigenvalue, and the second smallest that must be clearly larger
        var smallest = 0;
        for (var i = 1; i < 9; i++)
        {
            if (eigenvalues[i] < eigenvalues[smallest])
            {
                smallest = i;
            }
        }

        var second = double.PositiveInfinity;
        var largest = 0.0;
        for (var i = 0; i < 9; i++)
        {
            largest = Math.Max(largest, eigenvalues[i]);
            if (i != smallest)
            {
                second = Math.Min(second, eigenvalues[i]);
            }
        }

        if (!(second > 1e-12 * largest))
            return false;

        // h in the normalized coordinates: u = (h0 x + h1 y + h2) / (h6 x + h7 y + h8)
        Span<double> h = stackalloc double[9];
        for (var i = 0; i < 9; i++)
        {
            h[i] = eigenvectors[(i * 9) + smallest];
        }

        // Denormalize: H = T_destination⁻¹ * Hn * T_source, with T(p) = s (p - c)
        var a = sourceScale;
        var cx = sourceCenter.X;
        var cy = sourceCenter.Y;
        var b = destinationScale;
        var dx = destinationCenter.X;
        var dy = destinationCenter.Y;

        // Hn * T_source
        var m00 = h[0] * a;
        var m01 = h[1] * a;
        var m02 = h[2] - (h[0] * a * cx) - (h[1] * a * cy);
        var m10 = h[3] * a;
        var m11 = h[4] * a;
        var m12 = h[5] - (h[3] * a * cx) - (h[4] * a * cy);
        var m20 = h[6] * a;
        var m21 = h[7] * a;
        var m22 = h[8] - (h[6] * a * cx) - (h[7] * a * cy);

        // T_destination⁻¹ * M: p = q / b + d
        var h11 = (m00 / b) + (dx * m20);
        var h12 = (m01 / b) + (dx * m21);
        var h13 = (m02 / b) + (dx * m22);
        var h21 = (m10 / b) + (dy * m20);
        var h22 = (m11 / b) + (dy * m21);
        var h23 = (m12 / b) + (dy * m22);
        var h31 = m20;
        var h32 = m21;
        var h33 = m22;

        // W = 1 at the centroid of the source points
        var w = (h31 * cx) + (h32 * cy) + h33;
        if (!(Math.Abs(w) > 0) || !double.IsFinite(w))
            return false;

        var candidate = new ProjectiveTransform(h11 / w, h21 / w, h31 / w, h12 / w, h22 / w, h32 / w, h13 / w, h23 / w, h33 / w);
        if (!(Math.Abs(candidate.Determinant) > 1e-300) || !candidate.TryInvert(out _))
            return false;

        transform = candidate;
        return true;
    }

    /// <summary>Gets the reprojection error of a correspondence, or infinity when its source point has no image.</summary>
    public static double GetError(ProjectiveTransform transform, PointD source, PointD destination)
        => transform.TryTransform(source, out var projected) ? projected.DistanceTo(destination) : double.PositiveInfinity;

    /// <summary>Runs RANSAC; returns <see langword="false"/> when no sample determines a homography.</summary>
    public static bool TryFitRobust(ReadOnlySpan<PointD> source, ReadOnlySpan<PointD> destination, HomographyRansacOptions options, out ProjectiveTransform transform, out bool[] inliers)
    {
        var count = source.Length;
        var all = new int[count];
        for (var i = 0; i < count; i++)
        {
            all[i] = i;
        }

        transform = default;
        inliers = new bool[count];
        var best = -1;
        var bestError = double.PositiveInfinity;
        var found = false;
        var state = options.Seed == 0 ? 0x9E3779B97F4A7C15UL : (ulong)options.Seed * 0x9E3779B97F4A7C15UL;
        Span<int> sample = stackalloc int[4];
        var candidateInliers = new bool[count];
        for (var iteration = 0; iteration < options.Iterations; iteration++)
        {
            // Four distinct indices
            for (var i = 0; i < 4; i++)
            {
                int index;
                do
                {
                    state ^= state << 13;
                    state ^= state >> 7;
                    state ^= state << 17;
                    index = (int)(state % (ulong)count);
                }
                while (sample[..i].Contains(index));

                sample[i] = index;
            }

            if (!TryFit(source, destination, sample, out var candidate))
                continue;

            var inlierCount = CountInliers(candidate, source, destination, options.Threshold, candidateInliers, out var error);
            if (inlierCount > best || (inlierCount == best && error < bestError))
            {
                best = inlierCount;
                bestError = error;
                transform = candidate;
                candidateInliers.CopyTo(inliers, 0);
                found = true;
                if (best == count)
                    break;
            }
        }

        if (!found)
            return false;

        // Refit on the inliers
        var selected = new List<int>(best);
        for (var i = 0; i < count; i++)
        {
            if (inliers[i])
            {
                selected.Add(i);
            }
        }

        if (TryFit(source, destination, selected.ToArray(), out var refit))
        {
            var refitCount = CountInliers(refit, source, destination, options.Threshold, candidateInliers, out _);
            if (refitCount >= best)
            {
                transform = refit;
                candidateInliers.CopyTo(inliers, 0);
            }
        }

        return true;
    }

    /// <summary>Flags the correspondences within the threshold and returns their number and the sum of their squared errors.</summary>
    private static int CountInliers(ProjectiveTransform transform, ReadOnlySpan<PointD> source, ReadOnlySpan<PointD> destination, double threshold, bool[] inliers, out double squaredErrors)
    {
        var count = 0;
        squaredErrors = 0;
        for (var i = 0; i < source.Length; i++)
        {
            var error = GetError(transform, source[i], destination[i]);
            inliers[i] = error <= threshold;
            if (inliers[i])
            {
                count++;
                squaredErrors += error * error;
            }
        }

        return count;
    }

    /// <summary>Gets the centroid of points and the scale that makes their mean distance to it √2 (0 when they are all equal).</summary>
    private static (PointD Center, double Scale) GetNormalization(ReadOnlySpan<PointD> points, ReadOnlySpan<int> indices)
    {
        var sumX = 0.0;
        var sumY = 0.0;
        foreach (var index in indices)
        {
            sumX += points[index].X;
            sumY += points[index].Y;
        }

        var center = new PointD(sumX / indices.Length, sumY / indices.Length);
        var distance = 0.0;
        foreach (var index in indices)
        {
            distance += points[index].DistanceTo(center);
        }

        distance /= indices.Length;
        return (center, distance > 0 && double.IsFinite(distance) ? Math.Sqrt(2) / distance : 0);
    }

    private static void AddOuterProduct(Span<double> matrix, ReadOnlySpan<double> row)
    {
        for (var i = 0; i < 9; i++)
        {
            for (var j = 0; j < 9; j++)
            {
                matrix[(i * 9) + j] += row[i] * row[j];
            }
        }
    }

    /// <summary>
    /// Diagonalizes a symmetric 9 x 9 matrix with the cyclic Jacobi method: <paramref name="eigenvalues"/> receives its
    /// eigenvalues and the columns of <paramref name="eigenvectors"/> the unit eigenvectors. The matrix is destroyed.
    /// </summary>
    private static void DiagonalizeSymmetric(Span<double> matrix, Span<double> eigenvalues, Span<double> eigenvectors)
    {
        const int N = 9;
        eigenvectors.Clear();
        for (var i = 0; i < N; i++)
        {
            eigenvectors[(i * N) + i] = 1;
        }

        for (var sweep = 0; sweep < MaxJacobiSweeps; sweep++)
        {
            var off = 0.0;
            var diagonal = 0.0;
            for (var p = 0; p < N; p++)
            {
                diagonal += matrix[(p * N) + p] * matrix[(p * N) + p];
                for (var q = p + 1; q < N; q++)
                {
                    off += matrix[(p * N) + q] * matrix[(p * N) + q];
                }
            }

            if (off <= 1e-30 * diagonal || off == 0)
                break;

            for (var p = 0; p < N - 1; p++)
            {
                for (var q = p + 1; q < N; q++)
                {
                    var apq = matrix[(p * N) + q];
                    if (apq == 0)
                        continue;

                    var app = matrix[(p * N) + p];
                    var aqq = matrix[(q * N) + q];
                    var tau = (aqq - app) / (2 * apq);
                    var t = (tau >= 0 ? 1 : -1) / (Math.Abs(tau) + Math.Sqrt(1 + (tau * tau)));
                    var c = 1 / Math.Sqrt(1 + (t * t));
                    var s = t * c;

                    for (var k = 0; k < N; k++)
                    {
                        var akp = matrix[(k * N) + p];
                        var akq = matrix[(k * N) + q];
                        matrix[(k * N) + p] = (c * akp) - (s * akq);
                        matrix[(k * N) + q] = (s * akp) + (c * akq);
                    }

                    for (var k = 0; k < N; k++)
                    {
                        var apk = matrix[(p * N) + k];
                        var aqk = matrix[(q * N) + k];
                        matrix[(p * N) + k] = (c * apk) - (s * aqk);
                        matrix[(q * N) + k] = (s * apk) + (c * aqk);
                    }

                    for (var k = 0; k < N; k++)
                    {
                        var vkp = eigenvectors[(k * N) + p];
                        var vkq = eigenvectors[(k * N) + q];
                        eigenvectors[(k * N) + p] = (c * vkp) - (s * vkq);
                        eigenvectors[(k * N) + q] = (s * vkp) + (c * vkq);
                    }
                }
            }
        }

        for (var i = 0; i < N; i++)
        {
            eigenvalues[i] = matrix[(i * N) + i];
        }
    }
}
