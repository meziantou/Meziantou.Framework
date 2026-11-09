using System.Collections.Immutable;

namespace Meziantou.Framework.PublicApiGenerator;

internal sealed record PublicApiModel(string AssemblyName, string AssemblyAttributesSource, ImmutableArray<PublicApiTypeModel> Types, ImmutableArray<string> AssemblyReferences)
{
    /// <summary>Gets the names of the assemblies listed in the <c>.deps.json</c> file of the assembly, or <see langword="null"/> when it has none.</summary>
    public IReadOnlySet<string>? ExternalAssemblyNames { get; init; }

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
