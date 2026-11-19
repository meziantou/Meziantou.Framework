using System.Text;
using System.Text.Json.Nodes;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Fuzz;

// The parsers of the Payment Request API: the payment method manifests (https://w3c.github.io/payment-method-manifest/#validate-and-parse),
// the Link header that locates them, and the payment method identifiers (https://w3c.github.io/payment-method-id/)
public static partial class WebPlatformFuzzTargets
{
    private const string PaymentMethodManifestUrl = "https://pay.example/pay/manifest.json";

    /// <summary>The tokens of the payment method manifest target: the JSON syntax, the members of a manifest and the parts of the URLs.</summary>
    private static string[] PaymentMethodManifestTokens =>
    [
        "{", "}", "[", "]", ":", ",", "\"", "\\u", "\\uD800", "null", "true", "1",
        "\"default_applications\"", "\"supported_origins\"", "\"https://\"", "\"http://\"", "https://", "pay.example", ":8443", "/", "?", "#", "@", "user:pass@", "..", "\uFEFF",
    ];

    /// <summary>The tokens of the Link header target: the delimiters of a link-value, the escapes of the quoted strings and the relation types.</summary>
    private static string[] LinkHeaderTokens =>
    [
        "<", ">", ";", "=", "\"", "\\", ",", " ", "\t", "\n", "rel", "REL", "payment-method-manifest", "PAYMENT-METHOD-MANIFEST", "stylesheet", "anchor", "https://pay.example/manifest.json",
    ];

    /// <summary>
    /// Parses the input as a payment method manifest, and checks the properties of the result: the default applications are
    /// distinct <c>https:</c> URLs, the supported origins are distinct serializations of <c>https:</c> origins, and the manifest
    /// written back as JSON parses to the same manifest. The input as a payment method identifier is valid when it is a valid
    /// URL-based identifier or a valid standardized identifier.
    /// </summary>
    public static void PaymentMethodManifestTarget(ReadOnlySpan<byte> data)
    {
        var manifest = PaymentMethodManifest.Parse(data, PaymentMethodManifestUrl);
        if (manifest is not null)
        {
            FuzzAssert.Equal(manifest.DefaultApplications.Count, manifest.DefaultApplications.Distinct(StringComparer.Ordinal).Count(), "The default applications are not distinct");
            foreach (var application in manifest.DefaultApplications)
            {
                FuzzAssert.True(UrlParser.Parse(application, baseUrl: null) is { Scheme: "https" } url && url.Serialize() == application, $"The default application {FuzzAssert.Format(application)} is not a serialized https: URL");
            }

            FuzzAssert.Equal(manifest.SupportedOrigins.Count, manifest.SupportedOrigins.Distinct(StringComparer.Ordinal).Count(), "The supported origins are not distinct");
            foreach (var origin in manifest.SupportedOrigins)
            {
                FuzzAssert.True(UrlParser.Parse(origin, baseUrl: null) is { Scheme: "https" } url && url.SerializeOrigin() == origin, $"The supported origin {FuzzAssert.Format(origin)} is not the serialization of an https: origin");
            }

            var json = new JsonObject();
            if (manifest.DefaultApplications.Count > 0)
            {
                json["default_applications"] = new JsonArray([.. manifest.DefaultApplications.Select(application => JsonValue.Create(application))]);
            }

            if (manifest.SupportedOrigins.Count > 0)
            {
                json["supported_origins"] = new JsonArray([.. manifest.SupportedOrigins.Select(origin => JsonValue.Create(origin))]);
            }

            var reparsed = PaymentMethodManifest.Parse(Encoding.UTF8.GetBytes(json.ToJsonString()), PaymentMethodManifestUrl);
            FuzzAssert.True(reparsed is not null, $"The manifest {FuzzAssert.Format(json.ToJsonString())} written back from {FuzzAssert.Format(FuzzText.Decode(data))} is not valid");
            FuzzAssert.Equal(string.Join('\n', manifest.DefaultApplications), string.Join('\n', reparsed!.DefaultApplications), "The default applications of the manifest written back");
            FuzzAssert.Equal(string.Join('\n', manifest.SupportedOrigins), string.Join('\n', reparsed.SupportedOrigins), "The supported origins of the manifest written back");
        }

        var identifier = FuzzText.Decode(data);
        FuzzAssert.Equal(PaymentMethodIdentifier.IsUrlBased(identifier) || (UrlParser.Parse(identifier, baseUrl: null) is null && PaymentMethodIdentifier.IsValidStandardized(identifier)), PaymentMethodIdentifier.IsValid(identifier), $"The validity of the payment method identifier {FuzzAssert.Format(identifier)}");
    }

    /// <summary>
    /// Parses each line of the input as a link-value of a <c>Link</c> header, and checks that a parsed value written back
    /// (with quoted parameter values) parses to the same target and parameters, and that the payment method manifest found
    /// in the lines is the target of one of them.
    /// </summary>
    public static void LinkHeaderTarget(string input)
    {
        var values = input.Split('\n');
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (!LinkHeader.TryParse(value, out var target, out var parameters))
                continue;

            targets.Add(target);
            var serialized = LinkHeader.Serialize(target, parameters);
            FuzzAssert.True(LinkHeader.TryParse(serialized, out var reparsedTarget, out var reparsedParameters), $"The link-value {FuzzAssert.Format(serialized)} written back from {FuzzAssert.Format(value)} is not valid");
            FuzzAssert.Equal(target, reparsedTarget, $"The target of the link-value {FuzzAssert.Format(serialized)}");
            FuzzAssert.Equal(string.Join('\n', parameters), string.Join('\n', reparsedParameters), $"The parameters of the link-value {FuzzAssert.Format(serialized)}");
        }

        if (LinkHeader.FindPaymentMethodManifest(values) is { } manifest)
        {
            FuzzAssert.True(targets.Contains(manifest), $"The payment method manifest {FuzzAssert.Format(manifest)} is not the target of a link of {FuzzAssert.Format(input)}");
        }
    }

    private static IEnumerable<FuzzSeed> GetPaymentMethodManifestSeeds()
    {
        string[] inputs =
        [
            """{"default_applications": ["app.json", "https://pay.example/pay/app.json"], "supported_origins": ["https://bob.example", "https://bob.example/"], "other": 1}""",
            """{"supported_origins": ["https://bob.example:8443"]}""",
            """{"default_applications": ["first.json"], "default_applications": ["last.json"]}""",
            "\uFEFF{}",
            """{"default_applications": []}""",
            """{"default_applications": "app.json"}""",
            """{"default_applications": ["app.json", 1]}""",
            """{"default_applications": ["http://pay.example/app.json"]}""",
            """{"default_applications": ["https://"]}""",
            """{"supported_origins": "*"}""",
            """{"supported_origins": null}""",
            """{"supported_origins": ["https://bob.example/pay"]}""",
            """{"supported_origins": ["https://bob.example/?q"]}""",
            """{"supported_origins": ["https://bob.example/#f"]}""",
            """{"supported_origins": ["https://user:pass@bob.example"]}""",
            """{"supported_origins": ["/relative"]}""",
            """{"default_applications": ["\uD800.json"]}""",
            "[]",
            "not json",
            "https://example.com/pay",
            "secure-payment-confirmation",
            "a--b",
        ];
        return inputs.Select((input, index) => new FuzzSeed($"payment-method-manifest-{index}", Encoding.UTF8.GetBytes(input)));
    }

    private static IEnumerable<FuzzSeed> GetLinkHeaderSeeds()
    {
        string[] inputs =
        [
            "<https://pay.example/manifest.json>; rel=\"payment-method-manifest\"",
            "<manifest.json>; rel=payment-method-manifest",
            "<manifest.json>; rel=\"PAYMENT-METHOD-MANIFEST\"",
            "<manifest.json>; rel=\"other payment-method-manifest\"; foo=bar",
            "<other.json>; rel=\"other\"\n<first.json>; rel=\"payment-method-manifest\"\n<second.json>; rel=\"payment-method-manifest\"",
            "<manifest.json>; rel=\"stylesheet\"",
            "<manifest.json>; rel=\"stylesheet\"; rel=\"payment-method-manifest\"",
            "manifest.json; rel=\"payment-method-manifest\"",
            "<manifest.json>; rel=\"payment-method-manifest",
            "<manifest.json> rel=\"payment-method-manifest\"",
            "<a>; title=\"a \\\"quoted\\\" \\\\ value\"; anchor",
        ];
        return inputs.Select((input, index) => new FuzzSeed($"link-header-{index}", Encoding.UTF8.GetBytes(input)));
    }
}
