namespace Meziantou.Framework.Yamlish.Converters;

internal sealed class IntPtrYamlishConverter : ScalarYamlishConverter<IntPtr>
{
    protected override IntPtr Parse(string value) => new(long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture));

    protected override string Format(IntPtr value) => value.ToInt64().ToString(CultureInfo.InvariantCulture);
}
