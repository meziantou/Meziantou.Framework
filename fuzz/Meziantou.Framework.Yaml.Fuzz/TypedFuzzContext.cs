using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Fuzz;

/// <summary>The source-generated metadata of the payload of <see cref="YamlTypedFuzzTarget"/>.</summary>
[YamlSerializable(typeof(YamlTypedFuzzTarget.TypedPayload))]
internal sealed partial class TypedFuzzContext : YamlSerializerContext
{
    public TypedFuzzContext(YamlSerializerOptions options)
        : base(options)
    {
    }
}
