namespace Meziantou.Framework.Assertions.Architecture;

/// <summary>Provides extension methods on collections of <see cref="TypeElement"/>.</summary>
public static class TypeElementExtensions
{
    private const string SliceSegment = "{slice}";

    /// <summary>Groups types into slices by namespace, such as the modules of an application, to check the dependencies between the slices.</summary>
    /// <param name="types">The types to group.</param>
    /// <param name="pattern">
    /// A namespace pattern with one <c>{slice}</c> segment, whose value names the slice: <c>MyApp.Modules.{slice}</c> puts <c>MyApp.Modules.Orders</c> and <c>MyApp.Modules.Orders.Data</c> in the slice <c>Orders</c>.
    /// A <c>*</c> segment matches any segment. The types whose namespace does not match the pattern are in no slice.
    /// </param>
    /// <returns>The slices, sorted by name, each with its types in the order of <paramref name="types"/>.</returns>
    /// <exception cref="ArgumentException">The pattern does not have exactly one <c>{slice}</c> segment.</exception>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Assertions.Tests/Architecture/DocumentationTests.cs" ref:region="Architecture_Slices" ref:dedent="true">
    /// var architecture = ArchitectureModel.FromAssemblyContaining&lt;Order&gt;();
    /// var modules = architecture.Types.SliceByNamespace("SampleApp.Modules.{slice}");
    ///
    /// var exception = Assert.Throws&lt;AssertionException&gt;(() =&gt; Assert.Architecture.DoesNotHaveCycles(modules));
    /// Assert.StartsWith("""
    ///     Assert.Architecture.DoesNotHaveCycles() assertion failed: 1 cycle between slices.
    ///     Slices expression: modules
    ///     - Billing -&gt; Shipping -&gt; Billing
    ///         SampleApp.Modules.Billing.Invoice.Shipment has a property of type SampleApp.Modules.Shipping.Shipment
    ///     """.ReplaceLineEndings("\n"), exception.Message);
    /// </code>
    /// </example>
    public static IReadOnlyList<IGrouping<string, TypeElement>> SliceByNamespace(this IEnumerable<TypeElement> types, string pattern)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(pattern);

        var segments = pattern.Split('.');
        var sliceIndex = Array.IndexOf(segments, SliceSegment);
        if (sliceIndex < 0 || Array.LastIndexOf(segments, SliceSegment) != sliceIndex)
            throw new ArgumentException($"The pattern '{pattern}' must have exactly one '{SliceSegment}' segment, such as 'MyApp.Modules.{SliceSegment}'.", nameof(pattern));

        return types
            .Select(type => (Type: type, Slice: GetSliceName(type, segments, sliceIndex)))
            .Where(item => item.Slice is not null)
            .GroupBy(item => item.Slice!, item => item.Type, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToList();
    }

    private static string? GetSliceName(TypeElement type, string[] segments, int sliceIndex)
    {
        var namespaceSegments = type.Namespace.Split('.');
        if (namespaceSegments.Length < segments.Length)
            return null;

        for (var i = 0; i < segments.Length; i++)
        {
            if (i != sliceIndex && segments[i] is not "*" && !string.Equals(segments[i], namespaceSegments[i], StringComparison.Ordinal))
                return null;
        }

        return namespaceSegments[sliceIndex];
    }
}
