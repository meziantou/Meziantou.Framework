namespace Meziantou.Framework.VectorGraphics.Tests;

/// <summary>
/// Checks the images of the documentation site (docs/VectorGraphics/images): a test writes the SVG of an example of the
/// documentation and compares it with the committed file. With the environment variable <see cref="UpdateVariable"/>
/// set to 1, a missing or different image is written instead (see docs/AGENTS.md).
/// </summary>
internal static class DocumentationImages
{
    private const string UpdateVariable = "MEZIANTOU_VECTORGRAPHICS_UPDATE_DOCS_IMAGES";

    public static void AssertSvg(string name, string svg)
    {
        var path = Path.Combine(GetRepositoryRoot(), "docs", "VectorGraphics", "images", name + ".svg");
        if (File.Exists(path) && string.Equals(File.ReadAllText(path).ReplaceLineEndings("\n"), svg, StringComparison.Ordinal))
            return;

        if (Environment.GetEnvironmentVariable(UpdateVariable) is not "1")
            Assert.Fail($"docs/VectorGraphics/images/{name}.svg is missing or different. Run the test with the environment variable {UpdateVariable}=1 to write the image, look at it, and commit it.");

        // The test runs for each target framework at the same time: publish the file with a rename
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporaryPath, svg);
        File.Move(temporaryPath, path, overwrite: true);
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
