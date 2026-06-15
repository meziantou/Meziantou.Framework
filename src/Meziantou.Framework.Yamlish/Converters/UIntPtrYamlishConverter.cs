namespace Meziantou.Framework.Yamlish;

internal sealed class UIntPtrYamlishConverter : ScalarYamlishConverter<UIntPtr>
{
    protected override UIntPtr Parse(string value) => new(ulong.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture));

    protected override string Format(UIntPtr value) => value.ToUInt64().ToString(CultureInfo.InvariantCulture);
}
