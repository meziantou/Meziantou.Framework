namespace Meziantou.Framework.PublicApiGenerator;

internal static class MetadataNameHelper
{
    public static string RemoveGenericArity(string name)
    {
        var index = name.IndexOf('`', StringComparison.Ordinal);
        if (index < 0)
            return name;

        return name[..index];
    }

    public static int GetGenericArity(string metadataName)
    {
        var index = metadataName.IndexOf('`', StringComparison.Ordinal);
        if (index < 0)
            return 0;

        return int.TryParse(metadataName.AsSpan(index + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var arity) ? arity : 0;
    }
}
