namespace Meziantou.Framework.Yamlish;

internal sealed class TypeYamlishConverter : YamlishConverter
{
    public override bool CanConvert(Type typeToConvert) => typeof(Type).IsAssignableFrom(typeToConvert);

    public override object Read(YamlishNode node, Type typeToConvert, YamlishSerializerOptions options)
    {
        var value = ConverterUtilities.GetScalarValue(node, typeToConvert);
        return Type.GetType(value, throwOnError: true)!;
    }

    public override YamlishNode Write(object value, Type typeToConvert, YamlishSerializerOptions options)
    {
        var type = (Type)value;
        return new YamlishScalar(type.AssemblyQualifiedName ?? type.FullName ?? type.Name);
    }
}
