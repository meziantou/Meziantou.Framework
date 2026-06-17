namespace Meziantou.Framework.Yamlish.Converters;

internal sealed class DBNullYamlishConverter : YamlishConverter<DBNull>
{
    public override bool HandleNullValues => true;

    public override DBNull Read(YamlishNode node, YamlishSerializerOptions options)
    {
        var value = ConverterUtilities.GetScalarValue(node, typeof(DBNull));
        return value is "null" ? DBNull.Value : throw new FormatException($"Cannot convert '{value}' to '{typeof(DBNull)}'.");
    }

    public override YamlishNode Write(DBNull value, YamlishSerializerOptions options)
    {
        return YamlishScalar.CreateNull();
    }
}
