using System.Collections.Immutable;

namespace Meziantou.Framework.PublicApiGenerator;

internal sealed record PublicApiModel(string AssemblyName, ImmutableArray<PublicApiTypeModel> Types)
{
    public string ToNormalizedString()
    {
        var sb = new StringBuilder();
        foreach (var type in Types.OrderBy(value => value.QualifiedName, StringComparer.Ordinal))
        {
            sb.AppendLine(type.QualifiedName);
            sb.AppendLine(type.Source);
        }

        return sb.ToString();
    }
}
