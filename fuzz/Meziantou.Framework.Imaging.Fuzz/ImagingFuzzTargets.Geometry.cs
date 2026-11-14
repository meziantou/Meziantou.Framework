using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Imaging.Fuzz;

public static partial class ImagingFuzzTargets
{
    /// <summary>
    /// <see cref="ProjectiveTransform.EstimateHomography(ReadOnlySpan{PointD}, ReadOnlySpan{PointD})"/> and
    /// <see cref="ProjectiveTransform.EstimateHomographyRobust(ReadOnlySpan{PointD}, ReadOnlySpan{PointD}, HomographyRansacOptions?)"/>
    /// on correspondences read from the input: a byte of mode, then pairs of points of signed bytes. In the first mode, the
    /// destination points are the images of the source points by a homography read from the input, and the estimate of four
    /// or more such pairs maps them back within a tolerance. Otherwise the points are arbitrary: both estimates throw an
    /// <see cref="ArgumentException"/> or return a finite, invertible homography, and the robust one reports inliers that are
    /// within its threshold and the same result on a second run.
    /// </summary>
    private static void HomographyTarget(ReadOnlySpan<byte> data)
    {
        var mode = FuzzInput.ReadByte(ref data);
        var source = new List<PointD>();
        var destination = new List<PointD>();
        if ((mode & 1) == 0)
        {
            ProjectiveTransform truth;
            try
            {
                truth = ProjectiveTransform.CreateFromQuadrilateral(new RectangleD(0, 0, 100, 100), ReadPoint(ref data, 1), ReadPoint(ref data, 1) + new PointD(100, 0), ReadPoint(ref data, 1) + new PointD(100, 100), ReadPoint(ref data, 1) + new PointD(0, 100));
            }
            catch (ArgumentException)
            {
                return;
            }

            while (data.Length >= 2 && source.Count < 64)
            {
                var point = ReadPoint(ref data, 0.5) + new PointD(50, 50);
                if (truth.TryTransform(point, out var image))
                {
                    source.Add(point);
                    destination.Add(image);
                }
            }
        }
        else
        {
            while (data.Length >= 4 && source.Count < 64)
            {
                source.Add(ReadPoint(ref data, 1));
                destination.Add(ReadPoint(ref data, 1));
            }
        }

        ProjectiveTransform estimate;
        try
        {
            estimate = ProjectiveTransform.EstimateHomography([.. source], [.. destination]);
        }
        catch (ArgumentException)
        {
            estimate = default;
            FuzzAssert.True(source.Count < 4 || (mode & 1) != 0 || HasCollinearTriple(source), "The exact correspondences of a homography were rejected");
        }

        CheckTransform(estimate);
        if ((mode & 1) == 0 && source.Count >= 4 && !estimate.IsIdentity)
        {
            for (var i = 0; i < source.Count; i++)
            {
                FuzzAssert.True(estimate.TryTransform(source[i], out var image) && image.DistanceTo(destination[i]) <= 1e-3 * (1 + Math.Abs(destination[i].X) + Math.Abs(destination[i].Y)), $"The estimate does not map the point {i} onto its destination");
            }
        }

        var options = new HomographyRansacOptions { Iterations = 1 + (mode >> 1), Seed = mode, Threshold = 1 + (mode % 5) };
        HomographyEstimate robust;
        try
        {
            robust = ProjectiveTransform.EstimateHomographyRobust([.. source], [.. destination], options);
        }
        catch (ArgumentException)
        {
            return;
        }

        CheckTransform(robust.Transform);
        FuzzAssert.Equal(source.Count, robust.Inliers.Count, "There is not one inlier flag per correspondence");
        FuzzAssert.Equal(robust.Inliers.Count(inlier => inlier), robust.InlierCount, "The inlier count does not match the flags");
        for (var i = 0; i < source.Count; i++)
        {
            if (robust.Inliers[i])
            {
                FuzzAssert.True(robust.Transform.TryTransform(source[i], out var image) && image.DistanceTo(destination[i]) <= options.Threshold, $"The inlier {i} is beyond the threshold");
            }
        }

        var again = ProjectiveTransform.EstimateHomographyRobust([.. source], [.. destination], options);
        FuzzAssert.Equal(robust.Transform, again.Transform, "Two robust estimates of the same points differ");
    }

    /// <summary>
    /// <see cref="ImageProcessingExtensions.EstimateSkew(ImageFrame, SkewEstimationOptions?, CancellationToken)"/> on the
    /// first frame of an image (<see cref="CreateImage"/>) preceded by bytes of largest angle and precision: the frame is
    /// unchanged, the angle is within the largest one, and a second run gives the same angle.
    /// </summary>
    private static void SkewTarget(ReadOnlySpan<byte> data)
    {
        var options = new SkewEstimationOptions
        {
            MaxAngle = FuzzInput.Pick([15, 45, 0.5, 5, 30], FuzzInput.ReadByte(ref data)),
            Precision = FuzzInput.Pick([0.05, 0.5, 0.01, 0.2], FuzzInput.ReadByte(ref data)),
        };
        using var image = CreateImage(ref data, maxSide: 40);
        var before = Snapshot(image);
        double angle;
        try
        {
            angle = image.Frames[0].EstimateSkew(options);
        }
        catch (UnsupportedImageFeatureException)
        {
            return;
        }

        FuzzAssert.Equal(before, Snapshot(image), "Skew estimation changed the image");
        FuzzAssert.True(double.IsFinite(angle) && Math.Abs(angle) <= options.MaxAngle, $"The angle {angle} is beyond {options.MaxAngle}");
        FuzzAssert.Equal(angle, image.Frames[0].EstimateSkew(options), "Two skew estimates of the same frame differ");
    }

    private static PointD ReadPoint(ref ReadOnlySpan<byte> data, double scale)
        => new((sbyte)FuzzInput.ReadByte(ref data) * scale, (sbyte)FuzzInput.ReadByte(ref data) * scale);

    private static void CheckTransform(ProjectiveTransform transform)
    {
        FuzzAssert.True(double.IsFinite(transform.Determinant) && transform.Determinant != 0, $"The homography {transform} is singular");
        FuzzAssert.True(transform.TryInvert(out _), $"The homography {transform} cannot be inverted");
    }

    private static bool HasCollinearTriple(List<PointD> points)
    {
        for (var i = 0; i < points.Count; i++)
        {
            for (var j = i + 1; j < points.Count; j++)
            {
                for (var k = j + 1; k < points.Count; k++)
                {
                    var cross = ((points[j].X - points[i].X) * (points[k].Y - points[i].Y)) - ((points[j].Y - points[i].Y) * (points[k].X - points[i].X));
                    if (Math.Abs(cross) < 1e-6)
                        return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<FuzzSeed> GetHomographySeeds()
    {
        // The cases of the unit tests: exact correspondences, a page and its corners, outliers, collinear points, the identity
        yield return new FuzzSeed("exact", (byte[])[0, 5, 3, 0xF8, 2, 6, 7, 0xFD, 0xFC, 10, 20, 0xF0, 30, 40, 0x90, 0x70, 0x10, 0, 0, 0x20, 0x30]);
        yield return new FuzzSeed("page", (byte[])[1, 31, 18, 0, 0, 0x7F, 42, 0x7F, 0, 0x7F, 0x7F, 0x7F, 0x7F, 12, 0x7F, 0, 0x7F]);
        yield return new FuzzSeed("outliers", (byte[])[3, 0, 0, 5, 7, 10, 0, 15, 7, 10, 10, 15, 17, 0, 10, 5, 17, 5, 5, 10, 12, 40, 50, 0x90, 2, 8, 8, 0xA0, 0x60]);
        yield return new FuzzSeed("collinear", (byte[])[1, 0, 0, 0, 0, 1, 1, 1, 0, 2, 2, 1, 1, 3, 3, 0, 1]);
        yield return new FuzzSeed("identity", (byte[])[0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 2, 3, 0xFF, 0xFE, 10, 0x80]);
        yield return new FuzzSeed("many-samples", (byte[])[0xFE, 4, 1, 3, 2, 6, 5, 9, 8, 12, 11, 15, 14, 18, 17, 21, 20, 24, 23]);
    }

    private static IEnumerable<FuzzSeed> GetSkewSeeds()
    {
        // The cases of the unit tests: lines of text, light text on dark paper, a blank frame, a transparent frame
        yield return new FuzzSeed("text-lines", (byte[])[0, 0, 2, 39, 39, 0, 250, 250, 250, 250, 30, 30, 30, 250, 250, 250]);
        yield return new FuzzSeed("light-on-dark", (byte[])[3, 2, 0, 39, 29, 0, 20, 30, 40, 255, 240, 240, 240, 255]);
        yield return new FuzzSeed("blank", (byte[])[0, 0, 1, 20, 20, 0, 250, 250, 250]);
        yield return new FuzzSeed("transparent", (byte[])[1, 1, 0, 19, 19, 0, 0, 0, 0, 0]);
        yield return new FuzzSeed("fine", (byte[])[2, 2, 2, 31, 31, 0, 0, 255, 255, 0, 255, 0]);
    }
}
