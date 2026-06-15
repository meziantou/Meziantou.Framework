namespace Meziantou.Framework.Yamlish;

internal sealed class DateOnlyYamlishConverter : ScalarYamlishConverter<DateOnly>
{
    protected override DateOnly Parse(string value) => DateOnly.Parse(value, CultureInfo.InvariantCulture);

    protected override string Format(DateOnly value) => value.ToString("O", CultureInfo.InvariantCulture);
}
