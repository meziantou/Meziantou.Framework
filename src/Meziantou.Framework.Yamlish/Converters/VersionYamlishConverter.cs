namespace Meziantou.Framework.Yamlish;

internal sealed class VersionYamlishConverter : ScalarYamlishConverter<Version>
{
    protected override Version Parse(string value) => Version.Parse(value);

    protected override string Format(Version value) => value.ToString();
}
