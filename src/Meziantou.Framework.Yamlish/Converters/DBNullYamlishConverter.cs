namespace Meziantou.Framework.Yamlish;

internal sealed class DBNullYamlishConverter : ScalarYamlishConverter<DBNull>
{
    protected override DBNull Parse(string value)
    {
        return value is "null" ? DBNull.Value : throw new FormatException($"Cannot convert '{value}' to '{typeof(DBNull)}'.");
    }

    protected override string Format(DBNull value) => "null";
}
