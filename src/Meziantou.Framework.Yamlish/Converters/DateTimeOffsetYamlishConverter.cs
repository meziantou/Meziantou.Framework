namespace Meziantou.Framework.Yamlish.Converters;

internal sealed class DateTimeOffsetYamlishConverter : ScalarYamlishConverter<DateTimeOffset>
{
    protected override DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    protected override string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
}
