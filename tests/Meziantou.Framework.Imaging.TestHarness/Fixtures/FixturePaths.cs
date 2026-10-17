namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>Resolves manifest paths safely inside the corpus root.</summary>
internal static class FixturePaths
{
    public static FullPath? TryResolve(FullPath rootDirectory, string relativePath, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(relativePath) || relativePath.Contains('\\', StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
        {
            error = $"path '{relativePath}' must be relative and use '/' separators.";
            return null;
        }

        var fullPath = rootDirectory / relativePath;
        if (!fullPath.IsChildOf(rootDirectory))
        {
            error = $"path '{relativePath}' escapes the fixture root.";
            return null;
        }

        return fullPath;
    }
}
