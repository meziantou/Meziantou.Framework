using System.Collections.Immutable;

namespace Meziantou.Framework.PublicApiGenerator;

internal sealed record PublicApiModel(string AssemblyName, string AssemblyAttributesSource, ImmutableArray<PublicApiTypeModel> Types)
{
    public string ToNormalizedString()
    {
        var sb = new StringBuilder();
        sb.Append(AssemblyAttributesSource);
        foreach (var type in Types.OrderBy(value => value.QualifiedName, StringComparer.Ordinal))
        {
            sb.AppendLine(type.QualifiedName);
            sb.AppendLine(type.Source);
        }

        return sb.ToString();
    }
}
