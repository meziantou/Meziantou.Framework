namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The virtual viewport of the top-level browsing context and the preferences of the user: nothing is rendered, but the
/// media queries (<c>@media</c>, the <c>media</c> of the style sheets) and the viewport-relative lengths (<c>vw</c>,
/// <c>vh</c>...) of the computed styles are evaluated against them. The viewport of a nested browsing context is the
/// size given by the <c>width</c> and <c>height</c> attributes of its <c>iframe</c> (300x150 by default).
/// </summary>
public sealed record ViewportOptions
{
    /// <summary>Gets the default viewport: 800x600 CSS pixels, a device pixel ratio of 1, a screen and no preferences.</summary>
    public static ViewportOptions Default { get; } = new();

    /// <summary>Gets or sets the width of the viewport, in CSS pixels. The default is 800.</summary>
    public double Width { get; init; } = 800;

    /// <summary>Gets or sets the height of the viewport, in CSS pixels. The default is 600.</summary>
    public double Height { get; init; } = 600;

    /// <summary>Gets or sets the number of device pixels per CSS pixel (the <c>resolution</c> media feature). The default is 1.</summary>
    public double DevicePixelRatio { get; init; } = 1;

    /// <summary>Gets or sets the media type: <c>screen</c> (the default) or <c>print</c>.</summary>
    public string MediaType { get; init; } = "screen";

    /// <summary>Gets or sets the preferred color scheme (the <c>prefers-color-scheme</c> media feature, and the used color scheme of <c>light-dark()</c> and the system colors).</summary>
    public PreferredColorScheme PreferredColorScheme { get; init; }

    /// <summary>Gets or sets whether the user prefers reduced motion (the <c>prefers-reduced-motion</c> media feature).</summary>
    public bool PrefersReducedMotion { get; init; }

    /// <summary>Gets or sets whether the user prefers reduced transparency (the <c>prefers-reduced-transparency</c> media feature).</summary>
    public bool PrefersReducedTransparency { get; init; }

    /// <summary>
    /// Gets or sets the contrast preferred by the user (the <c>prefers-contrast</c> media feature). In forced colors mode, the
    /// preference is the contrast of the forced colors palette instead. The default is <see cref="PreferredContrast.NoPreference"/>.
    /// </summary>
    public PreferredContrast PreferredContrast { get; init; }

    /// <summary>
    /// Gets or sets whether a forced colors mode is active (https://drafts.csswg.org/css-color-adjust-1/#forced, the
    /// <c>forced-colors</c> media feature): the colors of the pages are replaced with the system colors of
    /// <see cref="ForcedColorsTheme"/>, unless an element opts out with <c>forced-color-adjust</c>.
    /// </summary>
    public bool ForcedColors { get; init; }

    /// <summary>
    /// Gets or sets the color palette of the forced colors mode (the values of the system colors when <see cref="ForcedColors"/>
    /// is set). The default is null: <see cref="ForcedColorsTheme.Dark"/> when <see cref="PreferredColorScheme"/> is dark,
    /// <see cref="ForcedColorsTheme.Light"/> otherwise. A palette whose canvas is clearly dark or light also sets the preferred
    /// color scheme of the pages (the <c>prefers-color-scheme</c> media feature).
    /// </summary>
    public ForcedColorsTheme? ForcedColorsTheme { get; init; }

    /// <summary>
    /// Gets or sets the thickness of the classic scrollbars, in CSS pixels
    /// (https://drafts.csswg.org/css-overflow-3/#classic-scrollbars): the scrollbars of the scroll containers and of the viewport
    /// take this space from their content box when they are displayed (<c>clientWidth</c> excludes them). The default is 15,
    /// the scrollbars of Chrome on Windows and Linux (which the web-platform-tests assume). 0 gives overlay scrollbars, which
    /// take no space (as on macOS and the mobile devices).
    /// </summary>
    public double ScrollbarThickness { get; init; } = 15;

    /// <summary>
    /// Gets or sets whether the classic scrollbars are painted and react to the pointer (a press on the track scrolls by a
    /// page, a drag of the thumb scrolls). The default is true. When it is false, the scrollbars still take their space
    /// (<see cref="ScrollbarThickness"/>), which is left empty: the background of the scroll container, or of the canvas for the
    /// viewport, shows in their gutters. The overlay scrollbars (a <see cref="ScrollbarThickness"/> of 0) are never painted.
    /// </summary>
    public bool ScrollbarsVisible { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the text insertion caret is painted (https://drafts.csswg.org/css-ui-4/#insertion-caret): the caret
    /// of the focused text control, or of the collapsed selection in the focused editable content, when the top-level browsing
    /// context has the system focus (<see cref="BrowsingContext.HasSystemFocus"/>). The default is true.
    /// </summary>
    public bool CaretVisible { get; init; } = true;

    /// <summary>
    /// Gets or sets the time during which the caret is displayed, then hidden, when it blinks (<c>caret-animation: auto</c>).
    /// The default is <see cref="TimeSpan.Zero"/>: the caret does not blink, so that the screenshots do not depend on the time
    /// they are taken at (the browsers blink every 500 ms or so). The blinking is driven by the timers of the event loop (its
    /// <see cref="BrowsingContextOptions.TimeProvider"/>): it starts when the caret is displayed, starts again when the caret
    /// moves, and stops when the document is no longer painted.
    /// </summary>
    public TimeSpan CaretBlinkInterval { get; init; }

    /// <summary>
    /// Gets or sets whether the scrolls of the user are animated, as in the browsers: the scrolls of the keys and of the presses
    /// on the track of a scrollbar are smooth scrolls (https://drafts.csswg.org/cssom-view/#concept-smooth-scroll) advanced by
    /// the rendering updates of the event loop (on its <see cref="BrowsingContextOptions.TimeProvider"/>), which a later key
    /// retargets, a gesture of the wheel lasts until no wheel event follows for 100 ms, and the snapping at the end of a gesture
    /// is animated. The default is false: the scrolls of the user are instant and each wheel event is a complete gesture, so
    /// that a screenshot taken right after an input does not depend on the time it is taken at.
    /// </summary>
    public bool SmoothUserScrolling { get; init; }
}

/// <summary>The color scheme preferred by the user (https://drafts.csswg.org/mediaqueries-5/#prefers-color-scheme).</summary>
public enum PreferredColorScheme
{
    /// <summary>A light color scheme.</summary>
    Light,

    /// <summary>A dark color scheme.</summary>
    Dark,
}

/// <summary>The contrast preferred by the user (https://drafts.csswg.org/mediaqueries-5/#prefers-contrast).</summary>
public enum PreferredContrast
{
    /// <summary>The user has made no preference known.</summary>
    NoPreference,

    /// <summary>The user prefers an interface that has a higher level of contrast.</summary>
    More,

    /// <summary>The user prefers an interface that has a lower level of contrast.</summary>
    Less,

    /// <summary>The user wants a specific set of colors, whose contrast is neither particularly high nor low.</summary>
    Custom,
}
