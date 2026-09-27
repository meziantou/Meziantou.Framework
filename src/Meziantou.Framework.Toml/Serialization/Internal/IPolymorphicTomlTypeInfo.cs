namespace Meziantou.Framework.Toml.Serialization.Internal;

// Metadata that already dispatches to the derived types, whether from reflection or from generated code: it is not wrapped
// again, which would handle the discriminator twice
internal interface IPolymorphicTomlTypeInfo
{
}
