// The example is shown by the XML documentation of StronglyTypedIdDefaultsAttribute and compiled in CI to keep it in sync with the API.
// It lives in a sample because the assembly-level defaults apply to every strongly-typed identifier of the assembly.
#pragma warning disable CA1050 // Declare types in namespaces: the example declares its type next to the top-level statements
#pragma warning disable MA0047 // Declare types in namespaces
#pragma warning disable MA0048 // File name must match type name
using Meziantou.Framework.Annotations;

#region StronglyTypedIdDefaultsAttribute_Example
[assembly: StronglyTypedIdDefaults(GuidGenerationStrategy = GuidGenerationStrategy.Version7, GenerateNewtonsoftJsonConverter = false)]

// Usage:
var productId = ProductId.New(); // Uses Guid.CreateVersion7()
Console.WriteLine(productId.Value.Version); // 7

[StronglyTypedId<Guid>]
public partial struct ProductId
{
}
#endregion
