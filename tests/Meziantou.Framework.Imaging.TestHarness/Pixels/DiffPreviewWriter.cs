namespace Meziantou.Framework.Imaging.TestHarness.Pixels;

/// <summary>
/// Writes diagnostic previews for a failed comparison: <c>.expected.png</c>, <c>.actual.png</c>, <c>.diff.png</c> (red
/// intensity proportional to the per-pixel maximum error) and <c>.report.txt</c> with the complete numeric detail
/// (16-bit samples are reported exactly; previews are reduced to 8 bits for display only).
/// </summary>
/// <remarks>
/// Previews are encoded by a minimal test-only PNG writer (RGBA8, BCL zlib) that is independent of the library.
/// They are never read back and never used as references.
/// </remarks>
public static class DiffPreviewWriter
{
    /// <summary>The maximum number of samples listed in the report.</summary>
    public const int MaxReportedSamples = 10_000;

    /// <summary>Writes the previews.</summary>
    /// <param name="directory">The destination directory (created if needed).</param>
    /// <param name="name">The base file name.</param>
    /// <param name="expected">The reference.</param>
    /// <param name="actual">The actual pixels, if available.</param>
    /// <param name="result">The comparison result.</param>
    /// <returns>The written file paths.</returns>
    public static IReadOnlyList<FullPath> Write(FullPath directory, string name, RawPixelBuffer expected, RawPixelBuffer? actual, PixelComparisonResult result)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(result);
        Directory.CreateDirectory(directory);
        var baseName = TestArtifacts.SanitizeFileName(name);
        var files = new List<FullPath>();

        files.Add(WritePng(directory / (baseName + ".expected.png"), expected.Width, expected.Height, ToPreview(expected)));
        if (actual is not null)
        {
            files.Add(WritePng(directory / (baseName + ".actual.png"), actual.Width, actual.Height, ToPreview(actual)));
            if (actual.Layout == expected.Layout && actual.Width == expected.Width && actual.Height == expected.Height)
            {
                files.Add(WritePng(directory / (baseName + ".diff.png"), expected.Width, expected.Height, Difference(expected, actual)));
            }
        }

        var report = directory / (baseName + ".report.txt");
        File.WriteAllText(report, BuildReport(expected, actual, result));
        files.Add(report);
        return files;
    }

    private static string BuildReport(RawPixelBuffer expected, RawPixelBuffer? actual, PixelComparisonResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine(result.Describe());
        if (actual is null || actual.Layout != expected.Layout || actual.Width != expected.Width || actual.Height != expected.Height)
            return sb.ToString();

        sb.AppendLine().AppendLine("All differing samples (x,y channel: expected, actual, error):");
        var layout = expected.Layout;
        var listed = 0;
        for (var i = 0; i < expected.SampleCount && listed < MaxReportedSamples; i++)
        {
            var e = expected.GetSampleAt(i);
            var a = actual.GetSampleAt(i);
            if (e == a)
                continue;

            var pixel = i / layout.ChannelCount;
            var channel = i % layout.ChannelCount;
            sb.AppendLine(new SampleMismatch(pixel % expected.Width, pixel / expected.Width, channel, layout.GetChannelName(channel), e, a).Format(layout));
            listed++;
        }

        return sb.ToString();
    }

    private static byte[] ToPreview(RawPixelBuffer buffer)
    {
        var layout = buffer.Layout;
        var rgba = new byte[buffer.Width * buffer.Height * 4];
        for (var p = 0; p < buffer.Width * buffer.Height; p++)
        {
            int Sample(int c) => layout.BytesPerSample == 1 ? buffer.GetSampleAt((p * layout.ChannelCount) + c) : buffer.GetSampleAt((p * layout.ChannelCount) + c) >> 8;
            byte r, g, b, a;
            if (layout.IsColor)
            {
                (r, g, b) = ((byte)Sample(0), (byte)Sample(1), (byte)Sample(2));
            }
            else
            {
                r = g = b = (byte)Sample(0);
            }

            a = layout.HasAlpha ? (byte)Sample(layout.AlphaChannel) : byte.MaxValue;
            rgba[p * 4] = r;
            rgba[(p * 4) + 1] = g;
            rgba[(p * 4) + 2] = b;
            rgba[(p * 4) + 3] = a;
        }

        return rgba;
    }

    private static byte[] Difference(RawPixelBuffer expected, RawPixelBuffer actual)
    {
        var channels = expected.Layout.ChannelCount;
        var max = expected.Layout.MaxSampleValue;
        var rgba = new byte[expected.Width * expected.Height * 4];
        for (var p = 0; p < expected.Width * expected.Height; p++)
        {
            var error = 0;
            for (var c = 0; c < channels; c++)
            {
                error = Math.Max(error, Math.Abs(expected.GetSampleAt((p * channels) + c) - actual.GetSampleAt((p * channels) + c)));
            }

            rgba[p * 4] = error == 0 ? (byte)0 : (byte)(64 + (191L * error / max));
            rgba[(p * 4) + 3] = byte.MaxValue;
        }

        return rgba;
    }

    private static FullPath WritePng(FullPath path, int width, int height, byte[] rgba)
    {
        File.WriteAllBytes(path, PreviewPngWriter.Encode(width, height, rgba));
        return path;
    }
}
