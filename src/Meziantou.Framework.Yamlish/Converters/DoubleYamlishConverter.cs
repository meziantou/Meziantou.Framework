namespace Meziantou.Framework.Yamlish.Converters;

internal sealed class DoubleYamlishConverter : ScalarYamlishConverter<double>
{
    protected override double Parse(string value) => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

    protected override string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
