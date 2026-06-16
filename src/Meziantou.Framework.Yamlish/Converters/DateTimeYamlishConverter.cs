namespace Meziantou.Framework.Yamlish.Converters;

internal sealed class DateTimeYamlishConverter : ScalarYamlishConverter<DateTime>
{
    protected override DateTime Parse(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    protected override string Format(DateTime value) => value.ToString("O", CultureInfo.InvariantCulture);
}
