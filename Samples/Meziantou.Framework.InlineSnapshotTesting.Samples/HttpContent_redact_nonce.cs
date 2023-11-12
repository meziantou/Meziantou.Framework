using Meziantou.Framework.HumanReadable.ValueFormatters;
using Meziantou.Framework.InlineSnapshotTesting.Serialization;

namespace Meziantou.Framework.InlineSnapshotTesting.Samples;

public sealed class HttpContent_redact_nonce
{
    [Fact]
    public void Demo()
    {
        var settings = InlineSnapshotSettings.Default with { };
        settings.SnapshotSerializer = new HumanReadableSnapshotSerializer(options =>
        {
            options.AddHtmlFormatter(new HtmlFormatterOptions
            {
                RedactContentSecurityPolicyNonce = true,
            });
        });

        using var httpContent = new StringContent("""<p z='"' nonce="value" a=2 d="3">test</p>""", encoding: null, "text/html");

        // Json content is automatically indented
        InlineSnapshot.Validate(httpContent,
            """
            Headers:
              Content-Type: text/html; charset=utf-8
            Value: <p a="2" d="3" nonce="value" z="&quot;">test</p>
            """);
    }
}