using System.Text;

namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>Builds the names the library assigns to the resources it creates.</summary>
internal static class ResourceNaming
{
    public const string Prefix = "meziantou-tc-";

    /// <summary>Builds a deterministic name from a reuse identifier, so the same identifier always resolves to the same resource.</summary>
    /// <param name="reuseId">The reuse identifier.</param>
    /// <returns>A name accepted by every supported runtime.</returns>
    public static string GetReuseName(string reuseId)
    {
        // Runtimes only accept letters, digits and a few separators, and the name must not start with a separator, which
        // the prefix guarantees.
        var builder = new StringBuilder(Prefix, Prefix.Length + reuseId.Length);
        foreach (var ch in reuseId)
            builder.Append(char.IsAsciiLetterOrDigit(ch) || ch is '_' or '.' or '-' ? ch : '-');

        return builder.ToString();
    }

    /// <summary>Builds a random name for a resource the library owns.</summary>
    /// <returns>A name accepted by every supported runtime.</returns>
    public static string GetRandomName() => Prefix + Guid.NewGuid().ToString("N");
}
