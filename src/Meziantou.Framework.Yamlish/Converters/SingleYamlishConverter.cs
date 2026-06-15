namespace Meziantou.Framework.Yamlish;

internal sealed class SingleYamlishConverter : ScalarYamlishConverter<float>
{
    protected override float Parse(string value) => float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

    protected override string Format(float value) => value.ToString("R", CultureInfo.InvariantCulture);
}
