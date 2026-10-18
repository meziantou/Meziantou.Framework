using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;
using System.Text;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// A font face of a tree (the font face set of the document, or the @font-face rules of a shadow root), with its parsed
/// descriptors (https://drafts.csswg.org/css-fonts-4/#font-face-rule).
/// </summary>
internal sealed class WebFontFace
{
    public WebFontFace(FontFace face)
    {
        Face = face;
        _signature = GetSignature(face);
        Family = FontFaceSet.ParseFamilies(face.Family) is [var name] ? name : face.Family;
        Ranges = UnicodeRangeSet.Parse(face.UnicodeRange);
        Font = face.Status == FontFaceLoadStatus.Loaded && face.FontFile is { Faces: [var font, ..] } ? font : null;
        Capabilities = FontSelectionCapabilities.FromDescriptors(face.GetDescriptorValue("font-weight"), face.GetDescriptorValue("font-width"), face.GetDescriptorValue("font-style"), Font);
        Descriptors = new FontFaceDescriptorValues(
            ParseOverride(face.GetDescriptorValue("size-adjust")) ?? 1,
            ParseOverride(face.GetDescriptorValue("ascent-override")),
            ParseOverride(face.GetDescriptorValue("descent-override")),
            ParseOverride(face.GetDescriptorValue("line-gap-override")),
            FontDescription.ParseVariationSettings(face.GetDescriptorValue("font-variation-settings")),
            ParseFeatureSettings(face.GetDescriptorValue("font-feature-settings")));

        static double? ParseOverride(string value) => FontDescription.ParsePercentage(value) is { } percentage and >= 0 ? percentage / 100 : null;

        static FontFeatureList? ParseFeatureSettings(string value)
        {
            var features = new List<FontFeature>();
            FontFeatureList.ParseFeatureSettings(value, features);
            return features.Count == 0 ? null : new FontFeatureList([.. features]);
        }
    }

    private readonly Signature _signature;

    /// <summary>The state of a font face that the font matching reads: its descriptors (the ones of its @font-face rule, which may change) and its font.</summary>
    private readonly record struct Signature(string Family, string UnicodeRange, string Weight, string Width, string Style, string SizeAdjust, string AscentOverride, string DescentOverride, string LineGapOverride, string VariationSettings, string FeatureSettings, FontFaceLoadStatus Status, FontFile? FontFile);

    private static Signature GetSignature(FontFace face) => new(
        face.Family,
        face.UnicodeRange,
        face.GetDescriptorValue("font-weight"),
        face.GetDescriptorValue("font-width"),
        face.GetDescriptorValue("font-style"),
        face.GetDescriptorValue("size-adjust"),
        face.GetDescriptorValue("ascent-override"),
        face.GetDescriptorValue("descent-override"),
        face.GetDescriptorValue("line-gap-override"),
        face.GetDescriptorValue("font-variation-settings"),
        face.GetDescriptorValue("font-feature-settings"),
        face.Status,
        face.FontFile);

    /// <summary>Whether the descriptors and the font of the font face are still the ones this object was created from.</summary>
    public bool IsCurrent => GetSignature(Face) == _signature;

    public FontFace Face { get; }

    /// <summary>The family name of the font face.</summary>
    public string Family { get; }

    /// <summary>The unicode-range of the font face.</summary>
    public UnicodeRangeSet Ranges { get; }

    /// <summary>The font of the font face when it is loaded, else null.</summary>
    public OpenTypeFont? Font { get; }

    /// <summary>The styles of the font face (its font-weight, font-width and font-style descriptors).</summary>
    public FontSelectionCapabilities Capabilities { get; }

    /// <summary>The descriptors that change the metrics and the variations of the font.</summary>
    public FontFaceDescriptorValues Descriptors { get; }
}

/// <summary>
/// The font matching of a document (https://drafts.csswg.org/css-fonts-4/#font-matching-algorithm): creates the
/// <see cref="FontFallbackList"/> of the font properties of the elements, from the font faces of the document (the font
/// face set: the @font-face rules of its style sheets and the faces added by scripts), the @font-face rules of its shadow
/// roots (https://drafts.csswg.org/css-scoping-1/#shadow-names), and the bundled fonts, which are the installed fonts.
/// </summary>
/// <remarks>
/// The lists are cached until the font faces change: a style sheet or the tree changes, a font face is loaded, added or
/// removed (<see cref="Invalidate"/> increments <see cref="Version"/>, which also invalidates the computed styles).
/// </remarks>
internal sealed class FontSelector
{
    private readonly Document _document;
    private readonly Dictionary<FontDescription, FontFallbackList> _lists = [];
    private readonly Dictionary<Node, List<WebFontFace>> _faces = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<FontFace, WebFontFace> _parsedFaces = new(ReferenceEqualityComparer.Instance);
    private readonly List<int> _selectedIndices = [];

    /// <summary>The font faces of the @font-face rules of the shadow roots (kept while the rules are in the document, so that they stay loaded).</summary>
    private readonly Dictionary<CssFontFaceRule, FontFace> _shadowFaces = new(ReferenceEqualityComparer.Instance);
    private (int Dom, int Style, int Version, MediaEnvironment? Environment) _snapshot = (-1, -1, -1, null);

    internal FontSelector(Document document)
    {
        _document = document;
    }

    /// <summary>The version of the font faces: it changes when a font face is loaded or fails to load, is added or removed, or when its descriptors change.</summary>
    public int Version { get; private set; }

    /// <summary>Invalidates the font lists (the font faces changed).</summary>
    public void Invalidate() => Version++;

    /// <summary>The fonts of the initial values of the font properties (e.g. for the font-relative units of the root's font-size).</summary>
    public FontFallbackList InitialFonts => GetFonts(FontDescription.Initial);

    /// <summary>Returns the fonts of a description.</summary>
    public FontFallbackList GetFonts(FontDescription description)
    {
        EnsureCurrent();
        if (!_lists.TryGetValue(description, out var list))
        {
            list = CreateList(description);
            _lists[description] = list;
        }

        return list;
    }

    /// <summary>
    /// Returns the shadow root whose @font-face rules the families of the font-family of a style reference: the tree scope of
    /// the declaration of font-family (inherited values keep their tree scope), or rather the innermost shadow root of its
    /// chain of trees that has @font-face rules; null for the document.
    /// </summary>
    public ShadowRoot? GetFontFamilyScope(ComputedStyle style)
    {
        for (var current = style; current is not null; current = current.Parent)
        {
            if (!current.TryGetCascadedValue("font-family", out var cascaded) || cascaded.Value.Value.Trim() is "inherit" or "unset")
                continue;

            if (cascaded.TreeScope is not ShadowRoot shadowRoot)
                return null;

            EnsureCurrent();
            for (Node root = shadowRoot; root is ShadowRoot scope; root = scope.Host.GetRoot())
            {
                if (GetFaces(scope).Count > 0)
                    return scope;
            }

            return null;
        }

        return null;
    }

    /// <summary>
    /// Starts loading a font face used by the text (https://drafts.csswg.org/css-font-loading/#font-face-load), when the
    /// document can fetch it (it has a browsing context, or it is the font source of a worker: the text of a canvas).
    /// </summary>
    internal void RequestLoad(FontFace face)
    {
        if (face.Status == FontFaceLoadStatus.Unloaded && (_document.BrowsingContext is not null || _document.FontSourceWorker is not null))
        {
            _ = face.Load();
        }
    }

    private void EnsureCurrent()
    {
        var engine = _document.GetStyleEngine();
        var snapshot = (_document.DomVersion, CssStyleVersion.Current, Version, engine.Environment);
        if (snapshot.DomVersion == _snapshot.Dom && snapshot.Current == _snapshot.Style && snapshot.Version == _snapshot.Version && ReferenceEquals(snapshot.Environment, _snapshot.Environment))
            return;

        // The style version is process-wide (it changes with the style sheets of the other documents) and the DOM version
        // changes with any mutation: the lists are kept when the font faces are still the same. The styles of the elements
        // then keep sharing the lists (the texts of the elements that share a list are shaped together, see
        // InlineLayoutAlgorithm.CanShapeTogether) and their caches of shaped texts.
        var facesMayBeCurrent = snapshot.Version == _snapshot.Version && snapshot.Environment == _snapshot.Environment;
        _snapshot = snapshot;
        if (facesMayBeCurrent && HasCurrentFaces())
            return;

        _lists.Clear();
        _faces.Clear();
        _parsedFaces.Clear();

        // The faces of the removed @font-face rules of the shadow roots are released
        if (_shadowFaces.Count > 0)
        {
            foreach (var rule in _shadowFaces.Keys.Where(rule => rule.ParentStyleSheet is null).ToList())
            {
                _shadowFaces.Remove(rule);
            }
        }
    }

    /// <summary>
    /// Returns the font faces of a tree, in the order of their definition: for the document, the font faces of its font face
    /// set whose @font-face rule applies (ordered by cascade layer, https://drafts.csswg.org/css-cascade-5/#layering), then
    /// the font faces added to the set; for a shadow root, the faces of its @font-face rules.
    /// </summary>
    internal List<WebFontFace> GetFaces(Node root)
    {
        if (_faces.TryGetValue(root, out var faces))
            return faces;

        faces = [];
        foreach (var face in CollectFaces(root))
        {
            faces.Add(GetParsedFace(face));
        }

        _faces[root] = faces;
        return faces;
    }

    /// <summary>The font faces of a tree, in the order of their definition (see <see cref="GetFaces"/>).</summary>
    private List<FontFace> CollectFaces(Node root)
    {
        var faces = new List<FontFace>();
        var engine = _document.GetStyleEngine();
        var rules = engine.GetFontFaceRules(root);
        if (root is ShadowRoot)
        {
            foreach (var rule in OrderByLayer(rules))
            {
                if (!_shadowFaces.TryGetValue(rule, out var face) || face.Urls != rule.Style.GetPropertyValue("src"))
                {
                    // The loads of the font faces of the shadow trees are tracked by the font source of the document (its
                    // ready promise waits for them, as in the browsers: WPT css/css-shadow/font-face-*.html), although they are
                    // not in the set
                    face = new FontFace(rule, _document);
                    face.Sets.Add(_document.GetFontsForMatching());
                    _shadowFaces[rule] = face;
                }

                faces.Add(face);
            }
        }
        else if (rules.Count > 0 || _document.FontsOrNull is not null)
        {
            var set = _document.GetFontsForMatching();
            var cssFaces = new List<(FontFace Face, int Layer, int Index)>();
            var index = 0;
            foreach (var face in set.GetFacesForMatching())
            {
                if (face.Rule is { } rule)
                {
                    if (rules.TryGetValue(rule, out var layer))
                    {
                        cssFaces.Add((face, layer, index++));
                    }
                }
                else
                {
                    cssFaces.Add((face, int.MaxValue, index++));
                }
            }

            cssFaces.Sort(static (x, y) => x.Layer != y.Layer ? x.Layer.CompareTo(y.Layer) : x.Index.CompareTo(y.Index));
            foreach (var (face, _, _) in cssFaces)
            {
                faces.Add(face);
            }
        }

        return faces;

        static IEnumerable<CssFontFaceRule> OrderByLayer(IReadOnlyDictionary<CssFontFaceRule, int> rules) => rules.Select((pair, index) => (pair.Key, pair.Value, index)).OrderBy(item => item.Value).ThenBy(item => item.index).Select(item => item.Key);
    }

    /// <summary>
    /// Whether the font faces of the trees the lists were created from (<see cref="_faces"/>) are still the same font faces,
    /// in the same order, with the same descriptors and fonts: the lists are then still valid.
    /// </summary>
    private bool HasCurrentFaces()
    {
        foreach (var (root, faces) in _faces)
        {
            if (!root.IsConnected)
                return false;

            var current = CollectFaces(root);
            if (current.Count != faces.Count)
                return false;

            for (var i = 0; i < current.Count; i++)
            {
                if (!ReferenceEquals(current[i], faces[i].Face) || !faces[i].IsCurrent)
                    return false;
            }
        }

        return true;
    }

    private WebFontFace GetParsedFace(FontFace face)
    {
        if (!_parsedFaces.TryGetValue(face, out var parsed))
        {
            parsed = new WebFontFace(face);
            _parsedFaces[face] = parsed;
        }

        return parsed;
    }

    /// <summary>https://drafts.csswg.org/css-fonts-4/#font-matching-algorithm</summary>
    private FontFallbackList CreateList(FontDescription description)
    {
        var families = new List<FontFallbackList.FamilyEntry>();
        foreach (var family in FontFamilyName.ParseList(description.Family))
        {
            // A generic family is mapped to a bundled family (the generic families without bundled font are skipped)
            if (family.IsGeneric)
            {
                if (BundledFonts.GetGenericFamily(family.Name) is { } genericFamily && CreateInstalledEntry(genericFamily, description) is { } generic)
                {
                    families.Add(generic);

                    // A generic family is a composite face of the installed fonts, which covers the scripts of the system
                    // (https://drafts.csswg.org/css-fonts-4/#generic-font-families): the fallback fonts installed by the host
                    // render the characters of their scripts before the next families of the list
                    AddHostFallbacks(families, description);
                }

                continue;
            }

            // The families defined by font faces hide the installed families of the same name: a family whose faces all failed
            // to load is missing (https://drafts.csswg.org/css-fonts-4/#font-style-matching)
            if (FindWebFaces(description.TreeScope, family.Name) is { } webFaces)
            {
                if (CreateWebEntry(webFaces, family.Name, description) is { } web)
                {
                    families.Add(web);
                }

                continue;
            }

            if (CreateInstalledEntry(family.Name, description) is { } installed)
            {
                families.Add(installed);
            }
        }

        // The installed font fallback (https://drafts.csswg.org/css-fonts-4/#font-style-matching): the default font, then the
        // other bundled fonts (Ahem is a test font: it is used only when it is named)
        var fallbacks = new List<FontFallbackList.FamilyEntry>(BundledFonts.FallbackFamilies.Count);
        foreach (var fallback in BundledFonts.FallbackFamilies)
        {
            fallbacks.Add(CreateInstalledEntry(fallback, description)!);
        }

        // Then the fallback fonts installed by the host, for the characters of their ranges
        AddHostFallbacks(fallbacks, description);
        return new FontFallbackList(this, description, [.. families], [.. fallbacks]);
    }

    /// <summary>Adds the fonts of the installed font fallback that the host installed (<see cref="InstalledFontCollection.AddFallback"/>).</summary>
    private void AddHostFallbacks(List<FontFallbackList.FamilyEntry> entries, FontDescription description)
    {
        if (_document.InstalledFonts is not { } installedFonts)
            return;

        foreach (var fallback in installedFonts.Fallbacks)
        {
            entries.Add(new FontFallbackList.FallbackFontEntry(CreateMatchedFont([fallback.Font.Font], [fallback.Font.Capabilities], description), fallback.Ranges));
        }
    }

    /// <summary>Returns the font faces of a family in the scope of a shadow root (or of the document), or null when no font face defines the family.</summary>
    private List<WebFontFace>? FindWebFaces(ShadowRoot? scope, string family)
    {
        Node? root = scope;
        while (true)
        {
            var faces = GetFaces(root ?? _document);
            List<WebFontFace>? result = null;
            foreach (var face in faces)
            {
                if (FamilyNamesEqual(face.Family, family))
                {
                    (result ??= []).Add(face);
                }
            }

            if (result is not null)
                return result;

            if (root is not ShadowRoot shadowRoot)
                return null;

            root = shadowRoot.Host.GetRoot() as ShadowRoot;
        }
    }

    private FontFallbackList.WebFamilyEntry? CreateWebEntry(List<WebFontFace> faces, string family, FontDescription description)
    {
        // The faces that failed to load are not present in the family
        faces.RemoveAll(face => face.Face.Status == FontFaceLoadStatus.Error);
        if (faces.Count == 0)
            return null;

        var capabilities = new FontSelectionCapabilities[faces.Count];
        for (var i = 0; i < faces.Count; i++)
        {
            capabilities[i] = faces[i].Capabilities;
        }

        var selected = FontMatcher.Select(capabilities, description.Weight, description.Style, description.Width, _selectedIndices, description.SynthesizeStyle);

        // The faces with the same descriptors are a composite face; when several faces remain, the last one defined wins
        var last = _selectedIndices[^1];
        var composite = new List<WebFontFace>();
        foreach (var index in _selectedIndices)
        {
            if (capabilities[index] == capabilities[last])
            {
                composite.Add(faces[index]);
            }
        }

        return new FontFallbackList.WebFamilyEntry([.. composite], description, selected, CreateInstalledEntry(family, description));
    }

    /// <summary>The entry of an installed family: a bundled family, else a family installed by the host; null when the family is not installed.</summary>
    private FontFallbackList.InstalledFamilyEntry? CreateInstalledEntry(string family, FontDescription description)
    {
        var bundled = BundledFonts.GetFamilyFonts(family);
        if (bundled.Count > 0)
        {
            var fonts = new OpenTypeFont[bundled.Count];
            var capabilities = new FontSelectionCapabilities[bundled.Count];
            for (var i = 0; i < bundled.Count; i++)
            {
                fonts[i] = bundled[i].Font;
                capabilities[i] = bundled[i].Capabilities;
            }

            return new FontFallbackList.InstalledFamilyEntry(CreateMatchedFont(fonts, capabilities, description));
        }

        if (_document.InstalledFonts?.GetFamilyFonts(family) is { Count: > 0 } installed)
        {
            var fonts = new OpenTypeFont[installed.Count];
            var capabilities = new FontSelectionCapabilities[installed.Count];
            for (var i = 0; i < installed.Count; i++)
            {
                fonts[i] = installed[i].Font;
                capabilities[i] = installed[i].Capabilities;
            }

            return new FontFallbackList.InstalledFamilyEntry(CreateMatchedFont(fonts, capabilities, description));
        }

        return null;
    }

    /// <summary>Selects the face of an installed family for a description (https://drafts.csswg.org/css-fonts-4/#font-style-matching).</summary>
    private static MatchedFont CreateMatchedFont(OpenTypeFont[] fonts, FontSelectionCapabilities[] capabilities, FontDescription description)
    {
        var indices = new List<int>(fonts.Length);
        var selected = FontMatcher.Select(capabilities, description.Weight, description.Style, description.Width, indices, description.SynthesizeStyle);
        return new MatchedFont(fonts[indices[0]], face: null, description, selected, FontFaceDescriptorValues.Default, capabilities[indices[0]]);
    }

    /// <summary>
    /// https://drafts.csswg.org/css-fonts-4/#localized-name-matching: the family names are matched case-insensitively (the
    /// default caseless matching, approximated with the invariant case folding of .NET).
    /// </summary>
    internal static bool FamilyNamesEqual(string x, string y) => string.Equals(x, y, StringComparison.OrdinalIgnoreCase) || (!Ascii.IsValid(x) && string.Equals(x.ToUpperInvariant(), y.ToUpperInvariant(), StringComparison.Ordinal));
}
