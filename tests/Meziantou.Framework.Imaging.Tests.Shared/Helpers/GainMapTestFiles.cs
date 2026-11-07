using Meziantou.Framework.Imaging.TestHarness;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The gain map fixtures of the codec test projects and their reference renditions, written by
/// <c>tools/Meziantou.Framework.Imaging.GainMapFixtures/generate.sh</c> (synthetic scenes, libultrahdr 1.4.0 and libavif
/// 1.3.0): the files are read from the repository.
/// </summary>
internal static class GainMapTestFiles
{
    /// <summary>Reads a file of the repository.</summary>
    /// <param name="path">The path relative to the root of the repository, with '/' separators.</param>
    public static byte[] Read(string path) => File.ReadAllBytes(RepositoryPaths.Root / path);

    /// <summary>Reads a raw file of little-endian half floats.</summary>
    public static float[] ReadHalfFloats(string path)
    {
        var bytes = Read(path);
        var values = new float[bytes.Length / 2];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = (float)BitConverter.ToHalf(bytes, 2 * i);
        }

        return values;
    }

    /// <summary>
    /// Compares the colors of a rendition with reference linear values: every component within
    /// <paramref name="tolerance"/> of the reference, relative to the larger of the reference and <paramref name="floor"/>
    /// (dark components are compared in absolute terms), and the median of these relative differences within
    /// <paramref name="medianTolerance"/>.
    /// </summary>
    /// <param name="expected">The reference values, <paramref name="channels"/> per pixel (the first three are red, green and blue).</param>
    public static void AssertClose(Image<RgbaFloat> actual, float[] expected, int channels, double tolerance, double medianTolerance, double floor = 0.1, string context = "")
    {
        Assert.HasCount(actual.Width * actual.Height * channels, expected);
        var differences = new List<double>(expected.Length);
        var frame = actual.Frames[0];
        for (var y = 0; y < actual.Height; y++)
        {
            for (var x = 0; x < actual.Width; x++)
            {
                var pixel = frame[x, y];
                var offset = ((y * actual.Width) + x) * channels;
                ReadOnlySpan<float> components = [pixel.R, pixel.G, pixel.B];
                for (var c = 0; c < 3; c++)
                {
                    var reference = expected[offset + c];
                    var difference = Math.Abs(components[c] - reference) / Math.Max(Math.Abs(reference), floor);
                    if (difference > tolerance)
                        Assert.Fail($"{context}: pixel ({x}, {y}) channel {c} is {components[c]}, the reference is {reference} (relative difference {difference}).");

                    differences.Add(difference);
                }
            }
        }

        differences.Sort();
        var median = differences[differences.Count / 2];
        Assert.True(median <= medianTolerance, $"{context}: the median relative difference is {median}.");
    }
}
