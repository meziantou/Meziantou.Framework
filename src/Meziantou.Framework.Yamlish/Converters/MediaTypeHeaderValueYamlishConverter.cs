using System.Net.Http.Headers;

namespace Meziantou.Framework.Yamlish;

internal sealed class MediaTypeHeaderValueYamlishConverter : ScalarYamlishConverter<MediaTypeHeaderValue>
{
    protected override MediaTypeHeaderValue Parse(string value) => MediaTypeHeaderValue.Parse(value);

    protected override string Format(MediaTypeHeaderValue value) => value.ToString();
}
