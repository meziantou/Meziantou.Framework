using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Css.Om;

/// <summary>The status of a <see cref="FontFace"/> (https://drafts.csswg.org/css-font-loading/#enumdef-fontfaceloadstatus).</summary>
/// <seealso href="https://drafts.csswg.org/css-font-loading/#enumdef-fontfaceloadstatus"/>
[IdlInterface("FontFaceLoadStatus", "https://drafts.csswg.org/css-font-loading/#enumdef-fontfaceloadstatus")]
public enum FontFaceLoadStatus
{
    /// <summary>The font face is not loaded yet and no load was requested.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfaceloadstatus-unloaded"/>
    [IdlMember("unloaded", "https://drafts.csswg.org/css-font-loading/#dom-fontfaceloadstatus-unloaded")]
    Unloaded,

    /// <summary>The font face is being loaded.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfaceloadstatus-loading"/>
    [IdlMember("loading", "https://drafts.csswg.org/css-font-loading/#dom-fontfaceloadstatus-loading")]
    Loading,

    /// <summary>The font face is loaded and ready to use.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfaceloadstatus-loaded"/>
    [IdlMember("loaded", "https://drafts.csswg.org/css-font-loading/#dom-fontfaceloadstatus-loaded")]
    Loaded,

    /// <summary>The font face failed to load or to parse.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfaceloadstatus-error"/>
    [IdlMember("error", "https://drafts.csswg.org/css-font-loading/#dom-fontfaceloadstatus-error")]
    Error,
}

/// <summary>The descriptors of a <see cref="FontFace"/> (https://drafts.csswg.org/css-font-loading/#dictdef-fontfacedescriptors).</summary>
/// <seealso href="https://drafts.csswg.org/css-font-loading/#dictdef-fontfacedescriptors"/>
[IdlInterface("FontFaceDescriptors", "https://drafts.csswg.org/css-font-loading/#dictdef-fontfacedescriptors")]
public sealed class FontFaceDescriptors
{
    /// <summary>Gets the value of the <c>font-style</c> descriptor of the font face.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfacedescriptors-style"/>
    [IdlMember("style", "https://drafts.csswg.org/css-font-loading/#dom-fontfacedescriptors-style")]
    public string Style { get; init; } = "normal";

    /// <summary>Gets the value of the <c>font-weight</c> descriptor of the font face.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfacedescriptors-weight"/>
    [IdlMember("weight", "https://drafts.csswg.org/css-font-loading/#dom-fontfacedescriptors-weight")]
    public string Weight { get; init; } = "normal";

    /// <summary>The legacy name alias of <see cref="Width"/>, used when <see cref="Width"/> is null.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfacedescriptors-stretch"/>
    [IdlMember("stretch", "https://drafts.csswg.org/css-font-loading/#dom-fontfacedescriptors-stretch")]
    public string Stretch { get; init; } = "normal";

    /// <summary>The width of the font face, or null to use <see cref="Stretch"/>.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfacedescriptors-width"/>
    [IdlMember("width", "https://drafts.csswg.org/css-font-loading/#dom-fontfacedescriptors-width")]
    public string? Width { get; init; }

    /// <summary>Gets the value of the <c>unicode-range</c> descriptor of the font face.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfacedescriptors-unicoderange"/>
    [IdlMember("unicodeRange", "https://drafts.csswg.org/css-font-loading/#dom-fontfacedescriptors-unicoderange")]
    public string UnicodeRange { get; init; } = "U+0-10FFFF";

    /// <summary>Gets the value of the <c>font-feature-settings</c> descriptor of the font face.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfacedescriptors-featuresettings"/>
    [IdlMember("featureSettings", "https://drafts.csswg.org/css-font-loading/#dom-fontfacedescriptors-featuresettings")]
    public string FeatureSettings { get; init; } = "normal";

    /// <summary>Gets the value of the <c>font-variation-settings</c> descriptor of the font face.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfacedescriptors-variationsettings"/>
    [IdlMember("variationSettings", "https://drafts.csswg.org/css-font-loading/#dom-fontfacedescriptors-variationsettings")]
    public string VariationSettings { get; init; } = "normal";

    /// <summary>Gets the value of the <c>font-display</c> descriptor of the font face.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfacedescriptors-display"/>
    [IdlMember("display", "https://drafts.csswg.org/css-font-loading/#dom-fontfacedescriptors-display")]
    public string Display { get; init; } = "auto";

    /// <summary>Gets the value of the <c>ascent-override</c> descriptor of the font face.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfacedescriptors-ascentoverride"/>
    [IdlMember("ascentOverride", "https://drafts.csswg.org/css-font-loading/#dom-fontfacedescriptors-ascentoverride")]
    public string AscentOverride { get; init; } = "normal";

    /// <summary>Gets the value of the <c>descent-override</c> descriptor of the font face.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfacedescriptors-descentoverride"/>
    [IdlMember("descentOverride", "https://drafts.csswg.org/css-font-loading/#dom-fontfacedescriptors-descentoverride")]
    public string DescentOverride { get; init; } = "normal";

    /// <summary>Gets the value of the <c>line-gap-override</c> descriptor of the font face.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfacedescriptors-linegapoverride"/>
    [IdlMember("lineGapOverride", "https://drafts.csswg.org/css-font-loading/#dom-fontfacedescriptors-linegapoverride")]
    public string LineGapOverride { get; init; } = "normal";
}

/// <summary>
/// A font face (https://drafts.csswg.org/css-font-loading/#fontface-interface), created by a script or by an
/// <c>@font-face</c> rule of a style sheet of the document (a CSS-connected font face, whose attributes reflect the
/// descriptors of the rule).
/// </summary>
/// <remarks>
/// Loading a font face fetches its sources (with the <see cref="DocumentLoader"/> of the browsing context, or from a
/// <c>data:</c> URL) until one of them is a valid font file (an OpenType, TrueType or collection file, WOFF or WOFF2,
/// decoded and validated by the managed font reader). The <c>local()</c> sources are the bundled fonts (the installed fonts
/// of the library, see <see cref="BundledFonts"/>), matched by full name or PostScript name.
/// </remarks>
/// <seealso href="https://drafts.csswg.org/css-font-loading/#fontface"/>
[IdlInterface("FontFace", "https://drafts.csswg.org/css-font-loading/#fontface")]
public sealed class FontFace
{
    /// <summary>The attributes of the font face and the corresponding descriptors of @font-face.</summary>
    private static readonly (string Attribute, string Descriptor, string Initial)[] Attributes =
    [
        ("style", "font-style", "normal"),
        ("weight", "font-weight", "normal"),
        ("stretch", "font-width", "normal"),
        ("unicodeRange", "unicode-range", "U+0-10FFFF"),
        ("featureSettings", "font-feature-settings", "normal"),
        ("variationSettings", "font-variation-settings", "normal"),
        ("display", "font-display", "auto"),
        ("ascentOverride", "ascent-override", "normal"),
        ("descentOverride", "descent-override", "normal"),
        ("lineGapOverride", "line-gap-override", "normal"),
        ("sizeAdjust", "size-adjust", "100%"),
    ];

    private readonly TaskCompletionSource<FontFace> _fontStatusPromise = new();
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly byte[]? _data;
    private readonly string? _urls;
    private Document? _document;
    private Document? _documentDelayingTheLoadEvent;
    private bool _isStartingLoad;
    private string? _baseUrl;
    private CssFontFaceRule? _rule;
    private bool _cspChecked;
    private HashSet<string>? _blockedUrls;

    /// <summary>Creates a font face whose source is a list of URLs (the value of the <c>src</c> descriptor of @font-face, e.g. <c>url(font.woff)</c>).</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontface-fontface"/>
    [IdlMember("constructor", "https://drafts.csswg.org/css-font-loading/#dom-fontface-fontface")]
    public FontFace(string family, string source, FontFaceDescriptors? descriptors = null)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(source);
        _document = Document.CurrentGlobalDocumentOrNull;
        _baseUrl = _document?.DocumentBaseUrl.Serialize();
        if (!Initialize(family, descriptors))
            return;

        if (ParseDescriptor("src", source) is not { } urls)
        {
            Fail();
            return;
        }

        _urls = urls;
    }

    /// <summary>Creates a font face from the binary data of a font file.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontface-fontface"/>
    [IdlMember("constructor", "https://drafts.csswg.org/css-font-loading/#dom-fontface-fontface")]
    public FontFace(string family, ReadOnlySpan<byte> source, FontFaceDescriptors? descriptors = null)
    {
        ArgumentNullException.ThrowIfNull(family);
        _document = Document.CurrentGlobalDocumentOrNull;
        if (!Initialize(family, descriptors))
            return;

        _data = source.ToArray();

        // The data is loaded by a task: the status is "loading", then "loaded" or "error"
        QueueTask(() =>
        {
            SetLoading();
            QueueTask(() =>
            {
                if (FontFile.Load(_data) is { } file)
                {
                    SetLoaded(file);
                }
                else
                {
                    SetError(new DomException("The data is not a font", "SyntaxError"));
                }
            });
        });
    }

    /// <summary>Creates the CSS-connected font face of an @font-face rule (https://drafts.csswg.org/css-font-loading/#font-face-css-connection).</summary>
    internal FontFace(CssFontFaceRule rule, Document document)
    {
        _rule = rule;
        _document = document;
        _urls = rule.Style.GetPropertyValue("src");
        _baseUrl = rule.ParentStyleSheet?.BaseUrl ?? document.DocumentBaseUrl.Serialize();
        Scripting.PromiseSlots.Register(_fontStatusPromise.Task);
    }

    /// <summary>
    /// Reports the violations of the content security policies by the URLs of the sources of the src descriptor, which the
    /// browsers check when the rule is added, although the font is only fetched when a text uses it. A blocked source is
    /// skipped when the font is loaded (its violation is reported once).
    /// </summary>
    internal void CheckContentSecurityPolicy()
    {
        if (_cspChecked || _document is not { ContentSecurityPoliciesOrNull: { Count: > 0 } } document || _urls is null)
            return;

        _cspChecked = true;
        var baseUrl = _baseUrl is { } value ? UrlParser.Parse(value, baseUrl: null) : null;
        foreach (var source in GetSources())
        {
            if (source.Url is not { } text || UrlParser.Parse(text, baseUrl) is not { } url || url.Scheme is "data")
                continue;

            if (ContentSecurityPolicy.ShouldBlockRequest(document, url, RequestDestination.Font, reportViolations: false))
            {
                (_blockedUrls ??= []).Add(url.Serialize());
                ContentSecurityPolicy.ShouldBlockRequest(document, url, RequestDestination.Font);
            }
        }
    }

    /// <summary>The @font-face rule of a CSS-connected font face, or null.</summary>
    internal CssFontFaceRule? Rule => _rule;

    /// <summary>Whether the font face is CSS-connected (https://drafts.csswg.org/css-font-loading/#css-connected).</summary>
    internal bool IsCssConnected => _rule is not null;

    /// <summary>The value of the src descriptor of the font face when it was connected to its rule.</summary>
    internal string? Urls => _urls;

    /// <summary>
    /// Returns the value of a descriptor of the font face for the font matching (e.g. <c>font-weight</c>): the value of the
    /// @font-face rule (an empty string when the rule does not declare it, which is the initial value <c>auto</c>), or the
    /// value of the attribute of a font face created by a script.
    /// </summary>
    internal string GetDescriptorValue(string descriptor)
    {
        if (_rule is not null)
            return _rule.Style.GetPropertyValue(descriptor);

        if (descriptor == "font-family")
            return _values.GetValueOrDefault("family", "");

        foreach (var (attribute, name, _) in Attributes)
        {
            if (name == descriptor)
                return _values.GetValueOrDefault(attribute, "");
        }

        return "";
    }

    /// <summary>The font file of the font face when it is loaded (decoded and validated by the managed font reader), else null.</summary>
    internal FontFile? FontFile { get; private set; }

    /// <summary>The font face sets that contain the font face.</summary>
    internal List<FontFaceSet> Sets { get; } = [];

    /// <summary>
    /// Uses the document of a font face set when the font face was created without the document of a browsing context as
    /// current global object (e.g. by .NET code): its URLs are resolved against the base URL of the document and fetched by
    /// its browsing context.
    /// </summary>
    internal void AdoptDocument(Document document)
    {
        if (_document?.BrowsingContext is not null || document.BrowsingContext is null)
            return;

        _document = document;
        _baseUrl = document.DocumentBaseUrl.Serialize();
    }

    /// <summary>Gets or sets the <c>font-family</c> descriptor of the font face.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontface-family"/>
    [IdlMember("family", "https://drafts.csswg.org/css-font-loading/#dom-fontface-family")]
    public string Family
    {
        get => GetValue("family");
        set => SetValue("family", value);
    }

    /// <summary>Gets or sets the <c>font-style</c> descriptor of the font face.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontface-style"/>
    [IdlMember("style", "https://drafts.csswg.org/css-font-loading/#dom-fontface-style")]
    public string Style
    {
        get => GetValue("style");
        set => SetValue("style", value);
    }

    /// <summary>Gets or sets the <c>font-weight</c> descriptor of the font face.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontface-weight"/>
    [IdlMember("weight", "https://drafts.csswg.org/css-font-loading/#dom-fontface-weight")]
    public string Weight
    {
        get => GetValue("weight");
        set => SetValue("weight", value);
    }

    /// <summary>The legacy name alias of <see cref="Width"/>.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontface-stretch"/>
    [IdlMember("stretch", "https://drafts.csswg.org/css-font-loading/#dom-fontface-stretch")]
    public string Stretch
    {
        get => GetValue("stretch");
        set => SetValue("stretch", value);
    }

    /// <summary>Gets or sets the <c>font-width</c> descriptor of the font face.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontface-width"/>
    [IdlMember("width", "https://drafts.csswg.org/css-font-loading/#dom-fontface-width")]
    public string Width
    {
        get => GetValue("stretch");
        set => SetValue("stretch", value);
    }

    /// <summary>Gets or sets the <c>unicode-range</c> descriptor of the font face.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontface-unicoderange"/>
    [IdlMember("unicodeRange", "https://drafts.csswg.org/css-font-loading/#dom-fontface-unicoderange")]
    public string UnicodeRange
    {
        get => GetValue("unicodeRange");
        set => SetValue("unicodeRange", value);
    }

    /// <summary>Gets or sets the <c>font-feature-settings</c> descriptor of the font face.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontface-featuresettings"/>
    [IdlMember("featureSettings", "https://drafts.csswg.org/css-font-loading/#dom-fontface-featuresettings")]
    public string FeatureSettings
    {
        get => GetValue("featureSettings");
        set => SetValue("featureSettings", value);
    }

    /// <summary>Gets or sets the <c>font-variation-settings</c> descriptor of the font face.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontface-variationsettings"/>
    [IdlMember("variationSettings", "https://drafts.csswg.org/css-font-loading/#dom-fontface-variationsettings")]
    public string VariationSettings
    {
        get => GetValue("variationSettings");
        set => SetValue("variationSettings", value);
    }

    /// <summary>Gets or sets the <c>font-display</c> descriptor of the font face.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontface-display"/>
    [IdlMember("display", "https://drafts.csswg.org/css-font-loading/#dom-fontface-display")]
    public string Display
    {
        get => GetValue("display");
        set => SetValue("display", value);
    }

    /// <summary>Gets or sets the <c>ascent-override</c> descriptor of the font face.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontface-ascentoverride"/>
    [IdlMember("ascentOverride", "https://drafts.csswg.org/css-font-loading/#dom-fontface-ascentoverride")]
    public string AscentOverride
    {
        get => GetValue("ascentOverride");
        set => SetValue("ascentOverride", value);
    }

    /// <summary>Gets or sets the <c>descent-override</c> descriptor of the font face.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontface-descentoverride"/>
    [IdlMember("descentOverride", "https://drafts.csswg.org/css-font-loading/#dom-fontface-descentoverride")]
    public string DescentOverride
    {
        get => GetValue("descentOverride");
        set => SetValue("descentOverride", value);
    }

    /// <summary>Gets or sets the <c>line-gap-override</c> descriptor of the font face.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontface-linegapoverride"/>
    [IdlMember("lineGapOverride", "https://drafts.csswg.org/css-font-loading/#dom-fontface-linegapoverride")]
    public string LineGapOverride
    {
        get => GetValue("lineGapOverride");
        set => SetValue("lineGapOverride", value);
    }

    /// <summary>Gets or sets the <c>size-adjust</c> descriptor of the font face.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontface-sizeadjust"/>
    [IdlMember("sizeAdjust", "https://drafts.csswg.org/css-font-loading/#dom-fontface-sizeadjust")]
    public string SizeAdjust
    {
        get => GetValue("sizeAdjust");
        set => SetValue("sizeAdjust", value);
    }

    /// <summary>Gets the load status of the font face.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontface-status"/>
    [IdlMember("status", "https://drafts.csswg.org/css-font-loading/#dom-fontface-status")]
    public FontFaceLoadStatus Status { get; private set; }

    /// <summary>The [[FontStatusPromise]]: fulfilled when the font face is loaded, rejected when it fails to load.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontface-loaded"/>
    [IdlMember("loaded", "https://drafts.csswg.org/css-font-loading/#dom-fontface-loaded")]
    public Task<FontFace> Loaded => _fontStatusPromise.Task;

    /// <summary>Loads a font face whose source is a list of URLs (https://drafts.csswg.org/css-font-loading/#font-face-load).</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontface-load"/>
    [IdlMember("load", "https://drafts.csswg.org/css-font-loading/#dom-fontface-load")]
    public Task<FontFace> Load()
    {
        if (_urls is null || Status != FontFaceLoadStatus.Unloaded)
            return Loaded;

        SetLoading();

        // The loads started before the load event of a document delay it (see Document.FontFacesDelayingTheLoadEvent)
        if (_document is { BrowsingContext: not null, WorkerGlobal: null, ReadyState: not DocumentReadyState.Complete } document)
        {
            document.FontFacesDelayingTheLoadEvent++;
            _documentDelayingTheLoadEvent = document;
        }

        _isStartingLoad = true;
        try
        {
            var sources = GetSources();
            LoadSource(sources, 0);
        }
        finally
        {
            _isStartingLoad = false;
        }

        return Loaded;
    }

    /// <summary>The load of the font face is complete: it no longer delays the load event of its document.</summary>
    private void StopDelayingTheLoadEvent()
    {
        if (_documentDelayingTheLoadEvent is not { } document)
            return;

        _documentDelayingTheLoadEvent = null;
        document.FontFacesDelayingTheLoadEvent--;

        // (a load that completes synchronously, e.g. without valid source, completes before the caller of Load checks the load event)
        if (!_isStartingLoad)
        {
            document.TryQueueLoadEvent();
        }
    }

    /// <summary>Parses the family and the descriptors; returns false and sets the status to "error" when one of them is invalid.</summary>
    private bool Initialize(string family, FontFaceDescriptors? descriptors)
    {
        Scripting.PromiseSlots.Register(_fontStatusPromise.Task);
        descriptors ??= new FontFaceDescriptors();
        var values = new (string Attribute, string Value)[]
        {
            ("style", descriptors.Style),
            ("weight", descriptors.Weight),
            ("stretch", descriptors.Width ?? descriptors.Stretch),
            ("unicodeRange", descriptors.UnicodeRange),
            ("featureSettings", descriptors.FeatureSettings),
            ("variationSettings", descriptors.VariationSettings),
            ("display", descriptors.Display),
            ("ascentOverride", descriptors.AscentOverride),
            ("descentOverride", descriptors.DescentOverride),
            ("lineGapOverride", descriptors.LineGapOverride),
            ("sizeAdjust", "100%"),
        };

        _values["family"] = ParseFamily(family);
        foreach (var (attribute, value) in values)
        {
            if (ParseDescriptor(GetDescriptorName(attribute), value) is not { } parsed)
            {
                foreach (var name in _values.Keys.ToList())
                {
                    _values[name] = "";
                }

                foreach (var (otherAttribute, _, _) in Attributes)
                {
                    _values[otherAttribute] = "";
                }

                Fail();
                return false;
            }

            _values[attribute] = parsed;
        }

        return true;
    }

    /// <summary>
    /// The family is the name of the family: it is serialized as a sequence of identifiers when possible, else as a string
    /// (e.g. a generic family, a CSS-wide keyword, a list or a name with consecutive spaces, https://github.com/w3c/csswg-drafts/issues/6236).
    /// </summary>
    private static string ParseFamily(string family) => SerializeFamilyName(family);

    /// <summary>Serializes a family name as identifiers separated by a space when it round-trips, else as a string.</summary>
    internal static string SerializeFamilyName(string name)
    {
        var words = name.Split(' ');
        foreach (var word in words)
        {
            if (word.Length == 0 || CssSerializer.SerializeIdentifier(word) != word || ValueMatcher.CssWideKeywords.Contains(InfraAscii.ToAsciiLowercase(word)) || InfraAscii.ToAsciiLowercase(word) is "default" or "revert-rule")
                return CssSerializer.SerializeString(name);

            // The non-ASCII code points that are not ident code points (https://drafts.csswg.org/css-syntax-3/#non-ascii-ident-code-point,
            // e.g. U+3000 IDEOGRAPHIC SPACE in a localized family name) are serialized as they are in an identifier, but would
            // not be parsed as a part of it
            foreach (var rune in word.EnumerateRunes())
            {
                if (rune.Value >= 0x80 && !CssTokenizer.IsIdentStart(rune.Value))
                    return CssSerializer.SerializeString(name);
            }
        }

        if (words.Length == 1 && IsGenericFamily(InfraAscii.ToAsciiLowercase(name)))
            return CssSerializer.SerializeString(name);

        return name;
    }

    /// <summary>https://drafts.csswg.org/css-fonts/#generic-font-families</summary>
    internal static bool IsGenericFamily(string name) => name is "serif" or "sans-serif" or "cursive" or "fantasy" or "monospace" or "system-ui" or "math" or "emoji" or "fangsong"
        or "ui-serif" or "ui-sans-serif" or "ui-monospace" or "ui-rounded" or "generic";

    private void Fail()
    {
        Status = FontFaceLoadStatus.Error;
        _fontStatusPromise.TrySetException(new DomException("The font face is invalid", "SyntaxError"));
    }

    private static string GetDescriptorName(string attribute) => attribute == "family" ? "font-family" : Array.Find(Attributes, a => a.Attribute == attribute).Descriptor;

    /// <summary>Parses a value of a descriptor of @font-face, and returns its serialization or null when it is invalid.</summary>
    private static string? ParseDescriptor(string descriptor, string value)
    {
        var components = CssParser.ParseListOfComponentValues(value, out var source);
        if (ValueMatcher.ContainsSubstitutionFunction(components))
            return null;

        return CssStyleDeclaration.ParseValue("@font-face", descriptor, components, value, source) is [var parsed] ? parsed.Value : null;
    }

    private string GetValue(string attribute)
    {
        if (_rule is not null)
        {
            var value = _rule.Style.GetPropertyValue(GetDescriptorName(attribute));
            if (attribute == "family")
                return FontFaceSet.ParseFamilies(value) is [var name] ? SerializeFamilyName(name) : value;

            return value.Length > 0 ? value : Array.Find(Attributes, a => a.Attribute == attribute).Initial;
        }

        return _values.GetValueOrDefault(attribute, "");
    }

    private void SetValue(string attribute, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (attribute == "family" && _rule is null)
        {
            _values[attribute] = SerializeFamilyName(value);
            InvalidateFontSelectors();
            return;
        }

        var parsed = ParseDescriptor(GetDescriptorName(attribute), value) ?? throw new DomException($"'{value}' is not a valid value of {attribute}", "SyntaxError");
        if (_rule is not null)
        {
            _rule.Style.SetProperty(GetDescriptorName(attribute), parsed);
            return;
        }

        _values[attribute] = parsed;
        InvalidateFontSelectors();
    }

    /// <summary>
    /// Invalidates the font matching of the documents that use the font face (the document of the font face and the
    /// documents of its font face sets): the fonts and the computed styles that depend on them are computed again.
    /// </summary>
    private void InvalidateFontSelectors()
    {
        _document?.FontSelectorOrNull?.Invalidate();
        foreach (var set in Sets)
        {
            if (!ReferenceEquals(set.Document, _document))
            {
                set.Document.FontSelectorOrNull?.Invalidate();
            }
        }
    }

    /// <summary>Disconnects the font face from its @font-face rule (the rule was removed or its src changed): it keeps the current values of the descriptors.</summary>
    internal void Disconnect()
    {
        if (_rule is null)
            return;

        _values["family"] = GetValue("family");
        foreach (var (attribute, _, _) in Attributes)
        {
            _values[attribute] = GetValue(attribute);
        }

        _rule = null;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Loading
    // ----------------------------------------------------------------------------------------------------------------

    private void SetLoading()
    {
        Status = FontFaceLoadStatus.Loading;
        foreach (var set in Sets.ToList())
        {
            set.OnFontFaceLoading(this);
        }
    }

    private void SetLoaded(FontFile file)
    {
        FontFile = file;
        Status = FontFaceLoadStatus.Loaded;
        InvalidateFontSelectors();
        _fontStatusPromise.TrySetResult(this);
        foreach (var set in Sets.ToList())
        {
            set.OnFontFaceLoaded(this, failed: false);
        }

        StopDelayingTheLoadEvent();
    }

    private void SetError(DomException exception)
    {
        Status = FontFaceLoadStatus.Error;
        InvalidateFontSelectors();
        _fontStatusPromise.TrySetException(exception);
        foreach (var set in Sets.ToList())
        {
            set.OnFontFaceLoaded(this, failed: true);
        }

        StopDelayingTheLoadEvent();
    }

    /// <summary>A source of the src descriptor: a URL and its request URL modifiers, or the name of a local() font.</summary>
    private readonly record struct FontSource(string? Url, CssRequestModifiers? Modifiers, string? LocalName);

    /// <summary>The sources of the src descriptor, in order (https://drafts.csswg.org/css-fonts-4/#src-desc); an invalid source has no URL and no name.</summary>
    private List<FontSource> GetSources()
    {
        var sources = new List<FontSource>();
        foreach (var entry in Cascade.CssValueComputer.SplitArguments(CssParser.ParseListOfComponentValues(_urls ?? "")))
        {
            var source = default(FontSource);
            foreach (var value in entry)
            {
                if (CssUrlModifiers.TryGetUrl(value, out var url, out var modifiers))
                {
                    source = new FontSource(url, modifiers, LocalName: null);
                }
                else if (value is CssFunction function && InfraAscii.EqualsIgnoreAsciiCase(function.Name, "local") && GetLocalName(function) is { } name)
                {
                    source = new FontSource(Url: null, Modifiers: null, name);
                }
            }

            sources.Add(source);
        }

        return sources;

        // https://drafts.csswg.org/css-fonts-4/#local-font-fallback: local(<family-name>), a string or a sequence of identifiers
        static string? GetLocalName(CssFunction function)
        {
            var arguments = ValueMatcher.RemoveWhitespace(function.Value);
            if (arguments is [CssPreservedToken { Type: CssTokenType.String } name])
                return name.Token.Value;

            return arguments.Count > 0 && arguments.TrueForAll(argument => argument is CssPreservedToken { Type: CssTokenType.Ident })
                ? string.Join(' ', arguments.Select(argument => ((CssPreservedToken)argument).Token.Value))
                : null;
        }
    }

    /// <summary>
    /// Returns the font of a local() source (https://drafts.csswg.org/css-fonts-4/#descdef-font-face-src): the bundled font, else
    /// the font installed by the host (<see cref="BrowsingContextOptions.InstalledFonts"/>, or the fonts installed on the
    /// machine when <see cref="BrowsingContextOptions.UseSystemFonts"/> is set), whose full name or PostScript name is the
    /// name (case-insensitive), or null.
    /// </summary>
    private FontFile? FindLocalFont(string name)
    {
        foreach (var bundled in BundledFonts.All)
        {
            if (HasName(bundled.Font))
                return bundled.Font.File;
        }

        return _document?.InstalledFonts?.FindByName(name)?.Font is { } installed ? installed.File.GetSingleFaceFile(installed) : null;

        bool HasName(OpenTypeFont font) => string.Equals(font.FullName, name, StringComparison.OrdinalIgnoreCase) || string.Equals(font.PostScriptName, name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Fetches the sources in order until one of them is a font (https://drafts.csswg.org/css-fonts/#font-face-src-loading).</summary>
    private void LoadSource(List<FontSource> sources, int index)
    {
        if (index >= sources.Count)
        {
            QueueTask(() => SetError(new DomException("The font face cannot be loaded", "NetworkError")));
            return;
        }

        // A local() font is available at once (it is loaded by a task, as a font of a data: URL)
        if (sources[index].LocalName is { } localName)
        {
            if (FindLocalFont(localName) is { } localFont)
            {
                QueueTask(() => Complete(localFont));
            }
            else
            {
                LoadSource(sources, index + 1);
            }

            return;
        }

        var baseUrl = _baseUrl is { } value ? UrlParser.Parse(value, baseUrl: null) : null;
        if (sources[index] is not { Url: { } source, Modifiers: { } modifiers } || UrlParser.Parse(source, baseUrl) is not { } url)
        {
            LoadSource(sources, index + 1);
            return;
        }

        if (_blockedUrls is not null && _blockedUrls.Contains(url.Serialize()))
        {
            LoadSource(sources, index + 1);
            return;
        }

        // A font face without document only loads the data: URLs
        var document = _document;
        if (document is null)
        {
            var data = url.Scheme == "data" && DataUrl.Process(url) is var (_, body) ? body : null;
            QueueTask(() => Complete(data is not null && SubresourceIntegrity.Matches(data, opaque: false, modifiers.IntegrityMetadata) ? FontFile.Load(data) : null));
            return;
        }

        // The font face of a worker is fetched with the environment of the worker (its loader is the one of its browsing context group)
        if (document.EnvironmentOptions?.DocumentLoader is null && url.Scheme is not ("data" or "blob"))
        {
            LoadSource(sources, index + 1);
            return;
        }

        // https://drafts.csswg.org/css-fonts/#font-fetching-requirements: the fonts are fetched with the "cors" mode and the
        // "same-origin" credentials mode, and the initiator type of their resource timing is "css"
        // (https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-initiatortype), unless the style sheet of
        // the rule is not origin-clean: its resources are not reported. The request URL modifiers set
        // the credentials mode, the integrity metadata and the referrer policy (https://drafts.csswg.org/css-values-5/#request-url-modifiers).
        // The fetch algorithm blocks the mixed content and the requests blocked by the content security policies, and checks
        // the CORS headers and the integrity metadata.
        var request = SubresourceFetching.CreateRequest(document, url, RequestDestination.Font, cors: true, modifiers.ReferrerPolicy, initiator: null, useCredentials: modifiers.UsesCredentials, integrity: modifiers.IntegrityMetadata);
        _rule?.ParentStyleSheet?.ResourceReferrer?.Apply(request, modifiers.ReferrerPolicy);
        var timing = _rule?.ParentStyleSheet is { OriginClean: false } ? null
            : document.WorkerGlobal is { } worker ? ResourceTiming.Start(worker, request, "css")
            : ResourceTiming.Start(document, request, "css");
        Fetching.Fetch(request, response =>
        {
            if (request.IsSentToLoader)
            {
                timing?.Report(response);
            }

            Complete(response.ToDocumentResource() is { IsOkStatus: true } resource ? FontFile.Load(GetBytes(resource)) : null);
        }, QueueTask);

        // The font is loaded when the response is a valid font file (https://drafts.csswg.org/css-fonts/#font-face-src-parsing:
        // the browsers sanitize the font data and reject the invalid fonts)
        void Complete(FontFile? file)
        {
            if (file is not null)
            {
                SetLoaded(file);
            }
            else
            {
                LoadSource(sources, index + 1);
            }
        }
    }

    private static ReadOnlySpan<byte> GetBytes(DocumentResource resource) => resource.ContentBytes is { } bytes ? bytes.Span : System.Text.Encoding.Latin1.GetBytes(resource.Content ?? "");

    /// <summary>Queues a task in the event loop of the document of the font face (or of the worker it represents), or runs it when there is no event loop.</summary>
    private void QueueTask(Action task)
    {
        if (_document is { WorkerGlobal: not null } or { BrowsingContext: not null })
        {
            _document.QueueGlobalTask(task);
        }
        else
        {
            task();
        }
    }

    /// <summary>Returns whether the unicode-range of the font face contains a character of the text.</summary>
    internal bool SupportsAnyCharacter(string text) => UnicodeRangeSet.Parse(UnicodeRange).ContainsAny(text);
}
