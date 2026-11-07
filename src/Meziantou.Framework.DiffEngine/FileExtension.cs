namespace Meziantou.Framework.DiffEngine;

internal static class FileExtension
{
    public static string Normalize(string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);

        var trimmed = extension.Trim();
        return trimmed[0] == '.' ? trimmed : "." + trimmed;
    }
}
