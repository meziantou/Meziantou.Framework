using System.Security.Cryptography;
using System.Text;

namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>Builds the names the library assigns to the resources it creates.</summary>
internal static class ResourceNaming
{
    public const string Prefix = "meziantou-tc-";

    /// <summary>The repository the images the library builds from a Dockerfile are tagged in.</summary>
    public const string BuiltImagePrefix = "meziantou-tc/";

    /// <summary>Builds a deterministic name from a reuse identifier, so the same identifier always resolves to the same resource, and two different identifiers never do.</summary>
    /// <param name="reuseId">The reuse identifier.</param>
    /// <returns>A name accepted by every supported runtime.</returns>
    public static string GetReuseName(string reuseId)
    {
        // Runtimes only accept letters, digits and a few separators, and the name must not start with a separator, which
        // the prefix guarantees.
        var builder = new StringBuilder(Prefix, Prefix.Length + reuseId.Length + 9);
        var replaced = false;
        foreach (var ch in reuseId)
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch is '_' or '.' or '-')
            {
                builder.Append(ch);
            }
            else
            {
                builder.Append('-');
                replaced = true;
            }
        }

        // Replacing characters maps several identifiers to one name ('db:pg' and 'db/pg' both become 'db-pg'), so the
        // name of an identifier that needed it carries a hash of the identifier. An identifier that is already a valid
        // name keeps the name it always had, so the resources created by an earlier version are still found.
        if (replaced)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(reuseId));
            builder.Append('-').Append(Convert.ToHexStringLower(hash, 0, 4));
        }

        return builder.ToString();
    }

    /// <summary>Builds a random name for a resource the library owns.</summary>
    /// <returns>A name accepted by every supported runtime.</returns>
    public static string GetRandomName() => Prefix + Guid.NewGuid().ToString("N");
}
