using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Internals;

internal static class VariableResolverExtensions
{
    /// <summary>The key of <see cref="Dependency.Metadata"/> that holds the name of the variable or property the version comes from.</summary>
    public const string PropertyMetadataKey = "property";

    /// <summary>
    /// Reports a dependency whose version references variables. The version location is the location of the definition
    /// when the version is exactly one reference to a variable, and is not updatable otherwise.
    /// </summary>
    public static void ReportDependency(this ScanFileContext context, DependencyScanner scanner, string? name, string rawVersion, ResolvedValue resolved, DependencyType type, Location? nameLocation, ReadOnlySpan<KeyValuePair<string, object?>> metadata = default)
    {
        var versionLocation = resolved.Value is not null && resolved.Location is not null ? resolved.Location : new NonUpdatableLocation(context);
        if (resolved.VariableName is null)
        {
            context.ReportDependency(scanner, name, rawVersion, resolved.Value, type, nameLocation, versionLocation, tags: [], metadata);
            return;
        }

        KeyValuePair<string, object?>[] allMetadata = [.. metadata, KeyValuePair.Create<string, object?>(PropertyMetadataKey, resolved.VariableName)];
        context.ReportDependency(scanner, name, rawVersion, resolved.Value, type, nameLocation, versionLocation, tags: [], allMetadata);
    }
}
