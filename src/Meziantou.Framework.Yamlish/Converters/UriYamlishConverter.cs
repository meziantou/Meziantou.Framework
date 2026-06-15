namespace Meziantou.Framework.Yamlish;

internal sealed class UriYamlishConverter : ScalarYamlishConverter<Uri>
{
    protected override Uri Parse(string value) => new(value, UriKind.RelativeOrAbsolute);

    protected override string Format(Uri value) => value.OriginalString;
}
