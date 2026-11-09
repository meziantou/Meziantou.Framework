using Meziantou.Framework.Imaging.Formats;
using Xunit.Sdk;

namespace Meziantou.Framework.Imaging.VectorGraphics.Tests;

/// <summary>
/// Checks the images of the documentation site (docs/Charts/images): a test runs an example of the documentation and
/// compares the pixels of its image with the committed PNG. With the environment variable <see cref="UpdateVariable"/>
/// set to 1, a missing or different image is written instead (see docs/AGENTS.md).
/// </summary>
internal static class ChartDocumentationImages
{
    private const string UpdateVariable = "MEZIANTOU_CHARTS_UPDATE_DOCS_IMAGES";

    public static void AssertImage(string name, Image<Rgba32> image)
    {
        var path = Path.Combine(GetRepositoryRoot(), "docs", "Charts", "images", name + ".png");
        var difference = File.Exists(path) ? Compare(path, image) : "the image does not exist";
        if (difference is null)
            return;

        if (Environment.GetEnvironmentVariable(UpdateVariable) is not "1")
            throw new XunitException($"docs/Charts/images/{name}.png: {difference}. Run the test with the environment variable {UpdateVariable}=1 to write the image, look at it, and commit it.");

        // The test runs for each target framework at the same time: publish the file with a rename
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = File.Create(temporaryPath))
        {
            image.Save(stream, new PngEncoder { Optimization = PngOptimization.MinimumSize });
        }

        File.Move(temporaryPath, path, overwrite: true);
    }

    private static string? Compare(string expectedPath, Image<Rgba32> actual)
    {
        using var expected = Image.Load<Rgba32>(expectedPath);
        if (expected.Size != actual.Size)
            return $"the size changed from {expected.Size} to {actual.Size}";

        var expectedBytes = new byte[expected.Width * expected.Height * 4];
        var actualBytes = new byte[expectedBytes.Length];
        expected.Frames[0].CopyPixelBytesTo(expectedBytes);
        actual.Frames[0].CopyPixelBytesTo(actualBytes);
        return expectedBytes.AsSpan().SequenceEqual(actualBytes) ? null : "the pixels changed";
    }

    // The deterministic builds of CI map the source paths to /_/, so the root is found from the test assembly
    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Path.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The Git repository root cannot be found.");
    }
}
