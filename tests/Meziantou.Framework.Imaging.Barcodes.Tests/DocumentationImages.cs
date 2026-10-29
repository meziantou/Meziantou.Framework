using Meziantou.Framework.Imaging.Formats;
using Xunit.Sdk;

namespace Meziantou.Framework.Imaging.Barcodes.Tests;

/// <summary>
/// Checks the images of the documentation site (docs/38-Barcodes/images): a test runs an example of the documentation,
/// and compares the pixels of the image it draws with the committed PNG. With the environment variable
/// <see cref="UpdateVariable"/> set to 1, a missing or different image is written instead (see docs/AGENTS.md).
/// </summary>
internal static class DocumentationImages
{
    private const string UpdateVariable = "MEZIANTOU_BARCODES_UPDATE_DOCS_IMAGES";

    private static readonly FullPath RepositoryRoot = FullPath.FromPath(AppContext.BaseDirectory).FindRequiredGitRepositoryRoot();
    private static readonly FullPath ImagesDirectory = RepositoryRoot / "docs" / "38-Barcodes" / "images";

    /// <summary>Compares an image drawn by an example with docs/38-Barcodes/images/<paramref name="name"/>.png.</summary>
    public static void AssertImage(string name, Image<Rgba32> image)
    {
        var expectedPath = ImagesDirectory / (name + ".png");
        var difference = File.Exists(expectedPath) ? Compare(expectedPath, image) : "the image does not exist";
        if (difference is null)
            return;

        var relativePath = expectedPath.MakePathRelativeTo(RepositoryRoot).Replace('\\', '/');
        if (Environment.GetEnvironmentVariable(UpdateVariable) is not "1")
            throw new XunitException($"{relativePath}: {difference}. Run the test with the environment variable {UpdateVariable}=1 to write the image, look at it, and commit it.");

        // The test runs for each target framework at the same time: publish the file with a rename
        Directory.CreateDirectory(expectedPath.Parent);
        var temporaryPath = expectedPath.Parent / (name + "." + Guid.NewGuid().ToString("N") + ".tmp");
        using (var stream = File.Create(temporaryPath))
        {
            image.Save(stream, new PngEncoder { Optimization = PngOptimization.MinimumSize });
        }

        File.Move(temporaryPath, expectedPath, overwrite: true);
    }

    /// <summary>Compares the size and the pixels of the committed image with an image, at 8 bits per sample.</summary>
    private static string? Compare(string expectedPath, Image<Rgba32> actual)
    {
        using var expected = Image.Load<Rgba32>(expectedPath);
        if (expected.Size != actual.Size)
            return $"the size changed from {expected.Size} to {actual.Size}";

        var expectedBytes = new byte[expected.Width * expected.Height * 4];
        var actualBytes = new byte[expectedBytes.Length];
        expected.Frames[0].CopyPixelBytesTo(expectedBytes);
        actual.Frames[0].CopyPixelBytesTo(actualBytes);
        if (!expectedBytes.AsSpan().SequenceEqual(actualBytes))
            return "the pixels changed";

        return null;
    }
}
