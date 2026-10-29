using Meziantou.Framework.Imaging;
using Xunit.Sdk;

namespace Meziantou.Framework.Barcodes.Tests;

/// <summary>
/// Checks the images of the documentation site (docs/38-Barcodes/images): a test runs an example of the documentation,
/// and compares the pixels of the PNG it produces with the committed one. With the environment variable
/// <see cref="UpdateVariable"/> set to 1, a missing or different image is written instead (see docs/AGENTS.md).
/// </summary>
internal static class DocumentationImages
{
    private const string UpdateVariable = "MEZIANTOU_BARCODES_UPDATE_DOCS_IMAGES";

    private static readonly FullPath RepositoryRoot = FullPath.FromPath(AppContext.BaseDirectory).FindRequiredGitRepositoryRoot();
    private static readonly FullPath ImagesDirectory = RepositoryRoot / "docs" / "38-Barcodes" / "images";

    /// <summary>Compares a PNG file produced by an example with docs/38-Barcodes/images/<paramref name="name"/>.png.</summary>
    public static void AssertPng(string name, byte[] png)
    {
        var expectedPath = ImagesDirectory / (name + ".png");
        var difference = File.Exists(expectedPath) ? Compare(File.ReadAllBytes(expectedPath), png) : "the image does not exist";
        if (difference is null)
            return;

        var relativePath = expectedPath.MakePathRelativeTo(RepositoryRoot).Replace('\\', '/');
        if (Environment.GetEnvironmentVariable(UpdateVariable) is not "1")
            throw new XunitException($"{relativePath}: {difference}. Run the test with the environment variable {UpdateVariable}=1 to write the image, look at it, and commit it.");

        // The test runs for each target framework at the same time: publish the file with a rename
        Directory.CreateDirectory(expectedPath.Parent);
        var temporaryPath = expectedPath.Parent / (name + "." + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllBytes(temporaryPath, png);
        File.Move(temporaryPath, expectedPath, overwrite: true);
    }

    /// <summary>Compares the size and the pixels of two PNG files, at 8 bits per sample.</summary>
    private static string? Compare(byte[] expectedPng, byte[] actualPng)
    {
        using var expected = Image.Load<Rgba32>(expectedPng);
        using var actual = Image.Load<Rgba32>(actualPng);
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
