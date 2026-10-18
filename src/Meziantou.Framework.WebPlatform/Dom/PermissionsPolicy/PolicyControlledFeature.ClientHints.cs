namespace Meziantou.Framework.WebPlatform.Dom;

internal sealed partial class PolicyControlledFeature
{
    /// <summary>
    /// https://wicg.github.io/ua-client-hints/#getHighEntropyValues: the feature that lets a document get the high-entropy
    /// values of <c>navigator.userAgentData.getHighEntropyValues()</c>.
    /// </summary>
    public static PolicyControlledFeature UserAgentHighEntropyValues => ClientHintFeatures.HighEntropyValues;

    /// <summary>
    /// The policy-controlled client hints features (https://wicg.github.io/client-hints-infrastructure/#policy-controlled-features),
    /// which delegate the client hints to the requests to other origins (see <see cref="ClientHintToken.Feature"/>): the
    /// default allowlist of the low-entropy hints is <c>*</c>, the one of the other hints is <c>'self'</c>. The class is
    /// initialized by the initializer of <see cref="Supported"/>, whatever the order of the partial declarations.
    /// </summary>
    private static class ClientHintFeatures
    {
        public static readonly PolicyControlledFeature HighEntropyValues = new("ch-ua-high-entropy-values", isAllowedForAllByDefault: true);

        public static readonly PolicyControlledFeature[] All =
        [
            new("ch-device-memory", isAllowedForAllByDefault: false),
            new("ch-downlink", isAllowedForAllByDefault: false),
            new("ch-dpr", isAllowedForAllByDefault: false),
            new("ch-ect", isAllowedForAllByDefault: false),
            new("ch-prefers-color-scheme", isAllowedForAllByDefault: false),
            new("ch-prefers-reduced-motion", isAllowedForAllByDefault: false),
            new("ch-prefers-reduced-transparency", isAllowedForAllByDefault: false),
            new("ch-rtt", isAllowedForAllByDefault: false),
            new("ch-save-data", isAllowedForAllByDefault: true),
            new("ch-ua", isAllowedForAllByDefault: true),
            new("ch-ua-arch", isAllowedForAllByDefault: false),
            new("ch-ua-bitness", isAllowedForAllByDefault: false),
            new("ch-ua-form-factors", isAllowedForAllByDefault: false),
            new("ch-ua-full-version", isAllowedForAllByDefault: false),
            new("ch-ua-full-version-list", isAllowedForAllByDefault: false),
            HighEntropyValues,
            new("ch-ua-mobile", isAllowedForAllByDefault: true),
            new("ch-ua-model", isAllowedForAllByDefault: false),
            new("ch-ua-platform", isAllowedForAllByDefault: true),
            new("ch-ua-platform-version", isAllowedForAllByDefault: false),
            new("ch-ua-wow64", isAllowedForAllByDefault: false),
            new("ch-viewport-height", isAllowedForAllByDefault: false),
            new("ch-viewport-width", isAllowedForAllByDefault: false),
            new("ch-width", isAllowedForAllByDefault: false),
        ];
    }
}
