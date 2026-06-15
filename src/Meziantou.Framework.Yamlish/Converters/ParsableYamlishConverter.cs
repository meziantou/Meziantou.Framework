namespace Meziantou.Framework.Yamlish;

internal abstract class ParsableYamlishConverter<T> : ScalarYamlishConverter<T>
    where T : IParsable<T>, IFormattable
{
    protected override T Parse(string value) => T.Parse(value, CultureInfo.InvariantCulture);

    protected override string Format(T value) => value.ToString(format: null, CultureInfo.InvariantCulture);
}
