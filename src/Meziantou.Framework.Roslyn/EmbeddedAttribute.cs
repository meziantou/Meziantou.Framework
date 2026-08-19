#if !MEZIANTOU_FRAMEWORK_ROSLYN_ENABLE_WARNINGS
#pragma warning disable
#endif
#nullable enable
#if !MEZIANTOU_FRAMEWORK_ROSLYN_DISABLE_EMBEDDEDATTRIBUTE && !MEZIANTOU_FRAMEWORK_ROSLYN_DISABLE_EMBEDDEDATTRIBUTE_DECLARATION
// The compiler only matches the attribute by its full name, so the usages don't need to be restricted
#pragma warning disable CA1018 // Mark attributes with AttributeUsageAttribute
namespace Microsoft.CodeAnalysis;

// The type is partial and has no attribute, so it can be merged with the declarations emitted by other source generators or packages
internal sealed partial class EmbeddedAttribute : global::System.Attribute
{
}
#endif
