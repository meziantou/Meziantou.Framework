using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#sanitizer</summary>
[IdlInterface("Sanitizer", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#sanitizer")]
public sealed class Sanitizer
{
    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizer-sanitizer</summary>
    /// <param name="configuration">The configuration, or null for the built-in safe default configuration ("default").</param>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizer-sanitizer")]
    public Sanitizer(SanitizerConfigOrSanitizerPresets? configuration = null)
    {
        Configuration = configuration?.Value is SanitizerConfig config
            ? Configure(config, permissiveDefaults: true)
            : Configure(SanitizerConfiguration.CreateDefault(), permissiveDefaults: true);
    }

    private Sanitizer(SanitizerConfiguration configuration)
    {
        Configuration = configuration;
    }

    /// <summary>The configuration (https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#sanitizer-configuration).</summary>
    internal SanitizerConfiguration Configuration { get; }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#sanitizer-configure</summary>
    private static SanitizerConfiguration Configure(SanitizerConfig configuration, bool permissiveDefaults) => Configure(SanitizerConfiguration.Canonicalize(configuration, permissiveDefaults), permissiveDefaults);

    private static SanitizerConfiguration Configure(SanitizerConfiguration configuration, bool permissiveDefaults)
    {
        configuration.ApplyDefaults(permissiveDefaults);
        if (!configuration.IsValid())
            throw new TypeErrorException("The sanitizer configuration is not valid");

        return configuration;
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#get-a-sanitizer-instance-from-options:
    /// a Sanitizer is used as is; a configuration or a preset creates a new Sanitizer.
    /// </summary>
    internal static Sanitizer FromOptions(SanitizerOrSanitizerConfigOrSanitizerPresets spec, bool safe)
    {
        return spec.Value switch
        {
            Sanitizer sanitizer => sanitizer,
            SanitizerConfig config => new Sanitizer(Configure(config, permissiveDefaults: !safe)),
            _ => new Sanitizer(Configure(SanitizerConfiguration.CreateDefault(), permissiveDefaults: !safe)),
        };
    }

    /// <summary>
    /// https://w3c.github.io/trusted-types/dist/spec/#get-a-sanitizer-from-dictionary: a new Sanitizer configured with the
    /// permissive defaults from the configuration of a sanitizer, a configuration or a preset.
    /// </summary>
    /// <exception cref="TypeErrorException">The configuration is not valid.</exception>
    internal static Sanitizer FromDictionary(SanitizerOrSanitizerConfigOrSanitizerPresets spec)
    {
        return spec.Value switch
        {
            Sanitizer sanitizer => new Sanitizer(Configure(sanitizer.Configuration.Clone(), permissiveDefaults: true)),
            SanitizerConfig config => new Sanitizer(Configure(config, permissiveDefaults: true)),
            _ => new Sanitizer(Configure(SanitizerConfiguration.CreateDefault(), permissiveDefaults: true)),
        };
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizer-get</summary>
    [IdlMember("get", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizer-get")]
    public SanitizerConfig Get() => Configuration.ToSanitizerConfig();

    /// <summary>
    /// Adds an element to the allow list of the configuration; returns whether the configuration changed.
    /// </summary>
    [IdlMember("allowElement", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizer-allowelement")]
    public bool AllowElement(SanitizerElementWithAttributes element) => Configuration.AllowElement(SanitizerConfiguration.CanonicalizeElementWithAttributes(element));

    /// <summary>
    /// Adds an element to the remove list of the configuration; returns whether the configuration changed.
    /// </summary>
    [IdlMember("removeElement", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizer-removeelement")]
    public bool RemoveElement(SanitizerElement element) => Configuration.RemoveElement(SanitizerConfiguration.CanonicalizeElement(element));

    /// <summary>
    /// Adds an element to the list of elements replaced with their children; returns whether the configuration changed.
    /// </summary>
    [IdlMember("replaceElementWithChildren", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizer-replaceelementwithchildren")]
    public bool ReplaceElementWithChildren(SanitizerElement element) => Configuration.ReplaceElementWithChildren(SanitizerConfiguration.CanonicalizeElement(element));

    /// <summary>
    /// Adds a processing instruction to the allow list of the configuration; returns whether the configuration changed.
    /// </summary>
    [IdlMember("allowProcessingInstruction", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizer-allowprocessinginstruction")]
    public bool AllowProcessingInstruction(SanitizerPI pi) => Configuration.AllowProcessingInstruction(SanitizerConfiguration.CanonicalizeProcessingInstruction(pi));

    /// <summary>
    /// Adds a processing instruction to the remove list of the configuration; returns whether the configuration
    /// changed.
    /// </summary>
    [IdlMember("removeProcessingInstruction", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizer-removeprocessinginstruction")]
    public bool RemoveProcessingInstruction(SanitizerPI pi) => Configuration.RemoveProcessingInstruction(SanitizerConfiguration.CanonicalizeProcessingInstruction(pi));

    /// <summary>
    /// Adds an attribute to the allow list of the configuration; returns whether the configuration changed.
    /// </summary>
    [IdlMember("allowAttribute", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizer-allowattribute")]
    public bool AllowAttribute(SanitizerAttribute attribute) => Configuration.AllowAttribute(SanitizerConfiguration.CanonicalizeAttribute(attribute));

    /// <summary>
    /// Adds an attribute to the remove list of the configuration; returns whether the configuration changed.
    /// </summary>
    [IdlMember("removeAttribute", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizer-removeattribute")]
    public bool RemoveAttribute(SanitizerAttribute attribute) => Configuration.RemoveAttribute(SanitizerConfiguration.CanonicalizeAttribute(attribute));

    /// <summary>Sets whether comments are allowed; returns whether the configuration changed.</summary>
    [IdlMember("setComments", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizer-setcomments")]
    public bool SetComments(bool allow) => Configuration.SetComments(allow);

    /// <summary>
    /// Sets whether custom <c>data-*</c> attributes are allowed; returns whether the configuration changed.
    /// </summary>
    [IdlMember("setDataAttributes", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizer-setdataattributes")]
    public bool SetDataAttributes(bool allow) => Configuration.SetDataAttributes(allow);

    /// <summary>Sets whether <c>javascript:</c> URLs are allowed; returns whether the configuration changed.</summary>
    [IdlMember("setJavascriptURLs", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizer-setjavascripturls")]
    public bool SetJavaScriptUrls(bool allow) => Configuration.SetJavaScriptUrls(allow);

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizer-removeunsafe</summary>
    [IdlMember("removeUnsafe", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizer-removeunsafe")]
    public bool RemoveUnsafe() => Configuration.RemoveUnsafe();
}
