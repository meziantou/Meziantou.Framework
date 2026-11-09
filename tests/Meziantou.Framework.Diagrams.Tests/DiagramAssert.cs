using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Meziantou.Framework.SnapshotTesting;

namespace Meziantou.Framework.Diagrams.Tests;

/// <summary>Checks the SVG of a diagram: well-formed XML, the snapshot, and the image of the documentation when it has one.</summary>
internal static class DiagramAssert
{
    private const string UpdateDocumentationImagesVariable = "MEZIANTOU_DIAGRAMS_UPDATE_DOCS_IMAGES";

    public static void Svg(string svg, string? documentationImage = null, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1, [CallerMemberName] string? memberName = null)
    {
        var document = XDocument.Parse(svg);
        Assert.Equal("svg", document.Root!.Name.LocalName);
        Snapshot.Validate(svg, SnapshotType.Svg, settings: null, filePath, lineNumber, memberName);
        if (documentationImage is not null)
        {
            AssertDocumentationImage(documentationImage, svg);
        }
    }

    public static void Svg(Diagram diagram, string? documentationImage = null, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1, [CallerMemberName] string? memberName = null)
        => Svg(diagram.ToSvg(), documentationImage, filePath, lineNumber, memberName);

    private static void AssertDocumentationImage(string name, string svg)
    {
        var path = Path.Combine(GetRepositoryRoot(), "docs", "Diagrams", "images", name + ".svg");
        if (File.Exists(path) && string.Equals(File.ReadAllText(path), svg, StringComparison.Ordinal))
            return;

        if (Environment.GetEnvironmentVariable(UpdateDocumentationImagesVariable) is not "1")
        {
            Assert.Fail($"docs/Diagrams/images/{name}.svg is missing or different. Run the test with the environment variable {UpdateDocumentationImagesVariable}=1 to write the image, look at it, and commit it.");
        }

        // The test runs for each target framework at the same time: publish the file with a rename
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporaryPath, svg);
        File.Move(temporaryPath, path, overwrite: true);
    }

    // The deterministic builds of CI map the source paths to /_/, so the root is found from the test assembly
    internal static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Path.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The Git repository root cannot be found.");
    }
}
