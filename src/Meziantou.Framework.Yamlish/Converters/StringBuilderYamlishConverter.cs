using System.Text;

namespace Meziantou.Framework.Yamlish;

internal sealed class StringBuilderYamlishConverter : ScalarYamlishConverter<StringBuilder>
{
    protected override StringBuilder Parse(string value) => new(value);

    protected override string Format(StringBuilder value) => value.ToString();
}
