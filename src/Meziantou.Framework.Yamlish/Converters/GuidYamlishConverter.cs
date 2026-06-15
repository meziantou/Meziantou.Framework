namespace Meziantou.Framework.Yamlish;

internal sealed class GuidYamlishConverter : ScalarYamlishConverter<Guid>
{
    protected override Guid Parse(string value) => Guid.Parse(value);

    protected override string Format(Guid value) => value.ToString("D");
}
