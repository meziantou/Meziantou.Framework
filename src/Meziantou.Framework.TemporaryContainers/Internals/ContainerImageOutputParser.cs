namespace Meziantou.Framework.TemporaryContainers.Internals;

internal static class ContainerImageOutputParser
{
    private const string LoadedImageMarker = "Loaded image";

    /// <summary>Reads the image reference out of the output of an image load, which looks like <c>Loaded image: repo:tag</c> or <c>Loaded image ID: sha256:...</c>. Returns <see langword="null"/> when the output has no such line; callers decide whether that is an error.</summary>
    public static string? TryParseLoadedImage(string output)
    {
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            var markerIndex = trimmed.IndexOf(LoadedImageMarker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
                continue;

            var rest = trimmed[(markerIndex + LoadedImageMarker.Length)..];
            var colonIndex = rest.IndexOf(':', StringComparison.Ordinal);
            if (colonIndex >= 0)
                return rest[(colonIndex + 1)..].Trim();
        }

        return null;
    }
}
