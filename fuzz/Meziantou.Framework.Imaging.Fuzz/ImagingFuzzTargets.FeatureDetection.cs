using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Imaging.Fuzz;

public static partial class ImagingFuzzTargets
{
    /// <summary>
    /// <see cref="ImageProcessingExtensions.DetectLines(ImageFrame, HoughLineOptions?, CancellationToken)"/> on the first
    /// frame of an image (<see cref="CreateImage"/>) preceded by bytes of angle step, distance step, minimum votes and maximum
    /// lines. The frame is unchanged; there are at most the maximum of lines, each with at least the minimum of votes, an
    /// angle from 0 to 180, a distance within the diagonal, sorted so that no two kept lines are within both separations; a
    /// second run gives the same lines.
    /// </summary>
    private static void LinesTarget(ReadOnlySpan<byte> data)
    {
        var options = new HoughLineOptions
        {
            AngleStep = FuzzInput.Pick([1, 0.5, 2, 3, 15, 45, 0.25], FuzzInput.ReadByte(ref data)),
            DistanceStep = FuzzInput.Pick([1, 0.5, 2, 0.25, 7], FuzzInput.ReadByte(ref data)),
            MinVotes = FuzzInput.ReadByte(ref data) is var votes && votes == 0 ? null : votes % 20 + 1,
            MaxLines = 1 + (FuzzInput.ReadByte(ref data) % 40),
        };
        using var image = CreateImage(ref data, maxSide: 32);
        var before = Snapshot(image);
        IReadOnlyList<HoughLine> lines;
        try
        {
            lines = image.Frames[0].DetectLines(options);
        }
        catch (UnsupportedImageFeatureException)
        {
            return;
        }

        FuzzAssert.Equal(before, Snapshot(image), "Line detection changed the image");
        FuzzAssert.True(lines.Count <= options.MaxLines, $"{lines.Count} lines for a maximum of {options.MaxLines}");
        var diagonal = Math.Sqrt((image.Width * image.Width) + (image.Height * image.Height));
        var minVotes = options.MinVotes ?? Math.Max(2, Math.Min(image.Width, image.Height) / 4);
        foreach (var line in lines)
        {
            FuzzAssert.True(line.Angle is >= 0 and < 180, $"The angle of {line} is out of range");
            FuzzAssert.True(Math.Abs(line.Distance) <= diagonal + 2 + options.DistanceStep, $"The distance of {line} is beyond the diagonal {diagonal}");
            FuzzAssert.True(line.Votes >= minVotes, $"{line} has fewer than {minVotes} votes");
            FuzzAssert.True(line.Slope is > -90 and <= 90, $"The slope of {line} is out of range");
        }

        FuzzAssert.True(lines.SequenceEqual(image.Frames[0].DetectLines(options)), "Two line detections of the same frame differ");
    }

    /// <summary>
    /// <see cref="ImageProcessingExtensions.DetectCorners(ImageFrame, CornerDetectionOptions?, CancellationToken)"/> on the
    /// first frame of an image (<see cref="CreateImage"/>) preceded by bytes of method, window, minimum distance and maximum
    /// corners. The frame is unchanged; the corners are inside the frame, at most the maximum, sorted by response, at least the
    /// minimum distance apart, with a positive finite response; a second run gives the same corners.
    /// </summary>
    private static void CornersTarget(ReadOnlySpan<byte> data)
    {
        var options = new CornerDetectionOptions
        {
            Method = (CornerDetectionMethod)(FuzzInput.ReadByte(ref data) % 3),
            Sigma = FuzzInput.Pick([1.5, 0.5, 1, 3, 7], FuzzInput.ReadByte(ref data)),
            MinDistance = 1 + (FuzzInput.ReadByte(ref data) % 8),
            MaxCorners = 1 + (FuzzInput.ReadByte(ref data) % 64),
        };
        using var image = CreateImage(ref data, maxSide: 32);
        var before = Snapshot(image);
        IReadOnlyList<ImageCorner> corners;
        try
        {
            corners = image.Frames[0].DetectCorners(options);
        }
        catch (UnsupportedImageFeatureException)
        {
            return;
        }

        FuzzAssert.Equal(before, Snapshot(image), "Corner detection changed the image");
        FuzzAssert.True(corners.Count <= options.MaxCorners, $"{corners.Count} corners for a maximum of {options.MaxCorners}");
        for (var i = 0; i < corners.Count; i++)
        {
            var corner = corners[i];
            FuzzAssert.True(corner.X >= 0 && corner.Y >= 0 && corner.X < image.Width && corner.Y < image.Height, $"{corner} is outside the frame");
            FuzzAssert.True(double.IsFinite(corner.Response) && corner.Response > 0, $"{corner} has a response that is not positive");
            FuzzAssert.True(i == 0 || corners[i - 1].Response >= corner.Response, $"{corner} is not sorted by response");
            for (var j = 0; j < i; j++)
            {
                var dx = corners[j].X - corner.X;
                var dy = corners[j].Y - corner.Y;
                FuzzAssert.True((dx * dx) + (dy * dy) >= options.MinDistance * options.MinDistance, $"{corner} and {corners[j]} are closer than {options.MinDistance}");
            }
        }

        FuzzAssert.True(corners.SequenceEqual(image.Frames[0].DetectCorners(options)), "Two corner detections of the same frame differ");
    }

    private static IEnumerable<FuzzSeed> GetLinesSeeds()
    {
        // The cases of the unit tests: a page (an edge per side), a diagonal edge, a flat frame, finer steps
        yield return new FuzzSeed("page", (byte[])[0, 0, 0, 3, 0, 31, 23, 0, 40, 40, 40, 40, 40, 40, 240, 240, 230, 240, 240, 230]);
        yield return new FuzzSeed("diagonal", (byte[])[0, 0, 0, 31, 2, 19, 19, 0, 0, 255, 255, 255, 0, 0, 255, 255, 0, 0, 0, 255]);
        yield return new FuzzSeed("flat", (byte[])[0, 0, 0, 0, 4, 9, 9, 0, 0x10, 0x20, 0x30, 0x40]);
        yield return new FuzzSeed("fine-steps", (byte[])[6, 3, 5, 10, 2, 23, 23, 0, 0, 0, 255, 255, 0, 255]);
        yield return new FuzzSeed("coarse-steps", (byte[])[5, 4, 1, 1, 0, 15, 7, 0, 0, 0, 0, 0, 255, 255, 255, 255]);
    }

    private static IEnumerable<FuzzSeed> GetCornersSeeds()
    {
        // The cases of the unit tests: a square with each detector, a flat frame, every maximum
        yield return new FuzzSeed("square-harris", (byte[])[0, 0, 4, 3, 2, 31, 31, 0, 0, 0, 0, 0, 255, 255, 255, 255]);
        yield return new FuzzSeed("square-shi-tomasi", (byte[])[1, 0, 4, 3, 0, 31, 31, 0, 20, 20, 20, 255, 230, 230, 230, 255]);
        yield return new FuzzSeed("square-fast", (byte[])[2, 0, 4, 3, 2, 31, 31, 0, 0, 0, 0, 255, 255, 255]);
        yield return new FuzzSeed("flat-half", (byte[])[0, 1, 1, 9, 7, 11, 11, 0, 0x00, 0x38, 0x00, 0x38, 0x00, 0x38, 0x00, 0x3C]);
        yield return new FuzzSeed("noise", (byte[])[2, 2, 0, 63, 2, 15, 15, 0, 13, 250, 7, 99, 180, 3, 66, 201, 140, 17]);
    }
}
