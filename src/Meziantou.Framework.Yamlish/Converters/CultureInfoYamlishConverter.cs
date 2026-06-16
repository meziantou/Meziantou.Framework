namespace Meziantou.Framework.Yamlish.Converters;

internal sealed class CultureInfoYamlishConverter : ScalarYamlishConverter<CultureInfo>
{
    protected override CultureInfo Parse(string value)
    {
        return value == CultureInfo.InvariantCulture.EnglishName ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(value);
    }

    protected override string Format(CultureInfo value)
    {
        return ReferenceEquals(value, CultureInfo.InvariantCulture) ? value.EnglishName : value.Name;
    }
}
