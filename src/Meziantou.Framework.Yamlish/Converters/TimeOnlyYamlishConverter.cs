namespace Meziantou.Framework.Yamlish;

internal sealed class TimeOnlyYamlishConverter : ScalarYamlishConverter<TimeOnly>
{
    protected override TimeOnly Parse(string value) => TimeOnly.Parse(value, CultureInfo.InvariantCulture);

    protected override string Format(TimeOnly value) => value.ToString("O", CultureInfo.InvariantCulture);
}
