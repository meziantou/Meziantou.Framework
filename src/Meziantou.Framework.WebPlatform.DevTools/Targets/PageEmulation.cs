using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.DevTools.Targets;

/// <summary>The emulation settings of a page (the <c>Emulation</c> domain, and the user agent of the <c>Network</c> domain).</summary>
internal sealed class PageEmulation
{
    public string? UserAgent { get; set; }

    public string? AcceptLanguage { get; set; }

    public string? Locale { get; set; }

    public string? TimezoneId { get; set; }

    public double? Width { get; set; }

    public double? Height { get; set; }

    public double? DeviceScaleFactor { get; set; }

    public bool IsMobile { get; set; }

    public double? ScreenWidth { get; set; }

    public double? ScreenHeight { get; set; }

    public bool HasTouch { get; set; }

    public string? Media { get; set; }

    public Dictionary<string, string> MediaFeatures { get; } = new(StringComparer.Ordinal);

    public bool ScriptExecutionDisabled { get; set; }

    /// <summary>Gets or sets whether the background of the screenshots is transparent (<c>Emulation.setDefaultBackgroundColorOverride</c> with a transparent color).</summary>
    public bool TransparentBackground { get; set; }

    public PageEmulation Clone()
    {
        var clone = new PageEmulation
        {
            UserAgent = UserAgent,
            AcceptLanguage = AcceptLanguage,
            Locale = Locale,
            TimezoneId = TimezoneId,
            Width = Width,
            Height = Height,
            DeviceScaleFactor = DeviceScaleFactor,
            IsMobile = IsMobile,
            ScreenWidth = ScreenWidth,
            ScreenHeight = ScreenHeight,
            HasTouch = HasTouch,
            Media = Media,
            ScriptExecutionDisabled = ScriptExecutionDisabled,
            TransparentBackground = TransparentBackground,
        };
        foreach (var (name, value) in MediaFeatures)
        {
            clone.MediaFeatures[name] = value;
        }

        return clone;
    }

    /// <summary>Returns the languages of <c>navigator.languages</c> (the <c>Accept-Language</c> override, or the locale), or null for the default.</summary>
    public IReadOnlyList<string>? GetLanguages()
    {
        if (AcceptLanguage is { Length: > 0 } acceptLanguage)
        {
            var languages = acceptLanguage.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(language => language.Split(';')[0].Trim())
                .Where(language => language.Length > 0)
                .ToList();
            if (languages.Count > 0)
                return languages;
        }

        return Locale is { Length: > 0 } locale ? [locale] : null;
    }

    public ViewportOptions CreateViewport()
    {
        var viewport = new ViewportOptions();
        viewport = viewport with
        {
            Width = Width is > 0 ? Width.Value : viewport.Width,
            Height = Height is > 0 ? Height.Value : viewport.Height,
            DevicePixelRatio = DeviceScaleFactor is > 0 ? DeviceScaleFactor.Value : viewport.DevicePixelRatio,
            IsMobile = IsMobile,
            ScreenWidth = ScreenWidth is > 0 ? ScreenWidth : null,
            ScreenHeight = ScreenHeight is > 0 ? ScreenHeight : null,
            HasTouch = HasTouch,
            MediaType = Media is { Length: > 0 } media ? media : "screen",
        };

        foreach (var (name, value) in MediaFeatures)
        {
            viewport = name switch
            {
                "prefers-color-scheme" => viewport with { PreferredColorScheme = value == "dark" ? PreferredColorScheme.Dark : PreferredColorScheme.Light },
                "prefers-reduced-motion" => viewport with { PrefersReducedMotion = value == "reduce" },
                "prefers-reduced-transparency" => viewport with { PrefersReducedTransparency = value == "reduce" },
                "forced-colors" => viewport with { ForcedColors = value == "active" },
                "prefers-contrast" => viewport with { PreferredContrast = value == "more" ? PreferredContrast.More : PreferredContrast.NoPreference },
                _ => viewport,
            };
        }

        return viewport;
    }
}
