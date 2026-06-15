namespace Meziantou.Framework.Yamlish;

internal sealed class TimeSpanYamlishConverter : ScalarYamlishConverter<TimeSpan>
{
    protected override TimeSpan Parse(string value) => TimeSpan.Parse(value, CultureInfo.InvariantCulture);

    protected override string Format(TimeSpan value) => value.ToString("c", CultureInfo.InvariantCulture);
}
