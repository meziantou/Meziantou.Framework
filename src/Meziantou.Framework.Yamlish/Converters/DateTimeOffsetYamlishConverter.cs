namespace Meziantou.Framework.Yamlish.Converters;

internal sealed class DateTimeOffsetYamlishConverter : ScalarYamlishConverter<DateTimeOffset>
{
    protected override DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    protected override string Format(DateTimeOffset value)
    {
        if (value.Offset == TimeSpan.Zero)
            return value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

        return value.ToString("O", CultureInfo.InvariantCulture);
    }
}
