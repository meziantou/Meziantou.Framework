namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The scroll setting of a WebVTT region (https://w3c.github.io/webvtt/#enumdef-scrollsetting).</summary>
[IdlInterface("ScrollSetting", "https://w3c.github.io/webvtt/#enumdef-scrollsetting")]
public enum ScrollSetting
{
    /// <summary>The cues of the region are not scrolled: a new cue is added below the previous ones.</summary>
    [IdlMember("", "https://w3c.github.io/webvtt/#webvtt-region-scroll-none")]
    None,

    /// <summary>The cues of the region are added at the bottom of the region and push the previous ones up.</summary>
    [IdlMember("up", "https://w3c.github.io/webvtt/#dom-scrollsetting-up")]
    Up,
}

/// <summary>
/// A WebVTT region (https://w3c.github.io/webvtt/#the-vttregion-interface): a part of the video viewport in which the cues
/// of the region are rendered.
/// </summary>
[IdlInterface("VTTRegion", "https://w3c.github.io/webvtt/#the-vttregion-interface")]
public sealed class VttRegion
{
    private double _width = 100;
    private double _regionAnchorX;
    private double _regionAnchorY = 100;
    private double _viewportAnchorX;
    private double _viewportAnchorY = 100;

    /// <summary>Creates a region (https://w3c.github.io/webvtt/#dom-vttregion-vttregion).</summary>
    [IdlMember("constructor", "https://w3c.github.io/webvtt/#dom-vttregion-vttregion")]
    public VttRegion()
    {
    }

    /// <summary>Gets or sets the identifier of the region (https://w3c.github.io/webvtt/#dom-vttregion-id).</summary>
    [IdlMember("id", "https://w3c.github.io/webvtt/#dom-vttregion-id")]
    public string Id
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = "";

    /// <summary>Gets or sets the width of the region, a percentage of the width of the video (https://w3c.github.io/webvtt/#dom-vttregion-width).</summary>
    /// <exception cref="DomException">An <c>IndexSizeError</c> when the value is negative or greater than 100.</exception>
    [IdlMember("width", "https://w3c.github.io/webvtt/#dom-vttregion-width")]
    public double Width
    {
        get => _width;
        set => _width = ValidatePercentage(value);
    }

    /// <summary>Gets or sets the height of the region, a number of lines (https://w3c.github.io/webvtt/#dom-vttregion-lines).</summary>
    [IdlMember("lines", "https://w3c.github.io/webvtt/#dom-vttregion-lines")]
    public uint Lines { get; set; } = 3;

    /// <summary>Gets or sets the horizontal offset of the anchor of the region, a percentage of its width (https://w3c.github.io/webvtt/#dom-vttregion-regionanchorx).</summary>
    /// <exception cref="DomException">An <c>IndexSizeError</c> when the value is negative or greater than 100.</exception>
    [IdlMember("regionAnchorX", "https://w3c.github.io/webvtt/#dom-vttregion-regionanchorx")]
    public double RegionAnchorX
    {
        get => _regionAnchorX;
        set => _regionAnchorX = ValidatePercentage(value);
    }

    /// <summary>Gets or sets the vertical offset of the anchor of the region, a percentage of its height (https://w3c.github.io/webvtt/#dom-vttregion-regionanchory).</summary>
    /// <exception cref="DomException">An <c>IndexSizeError</c> when the value is negative or greater than 100.</exception>
    [IdlMember("regionAnchorY", "https://w3c.github.io/webvtt/#dom-vttregion-regionanchory")]
    public double RegionAnchorY
    {
        get => _regionAnchorY;
        set => _regionAnchorY = ValidatePercentage(value);
    }

    /// <summary>Gets or sets the horizontal offset of the anchor of the region in the video viewport, a percentage of the width of the video (https://w3c.github.io/webvtt/#dom-vttregion-viewportanchorx).</summary>
    /// <exception cref="DomException">An <c>IndexSizeError</c> when the value is negative or greater than 100.</exception>
    [IdlMember("viewportAnchorX", "https://w3c.github.io/webvtt/#dom-vttregion-viewportanchorx")]
    public double ViewportAnchorX
    {
        get => _viewportAnchorX;
        set => _viewportAnchorX = ValidatePercentage(value);
    }

    /// <summary>Gets or sets the vertical offset of the anchor of the region in the video viewport, a percentage of the height of the video (https://w3c.github.io/webvtt/#dom-vttregion-viewportanchory).</summary>
    /// <exception cref="DomException">An <c>IndexSizeError</c> when the value is negative or greater than 100.</exception>
    [IdlMember("viewportAnchorY", "https://w3c.github.io/webvtt/#dom-vttregion-viewportanchory")]
    public double ViewportAnchorY
    {
        get => _viewportAnchorY;
        set => _viewportAnchorY = ValidatePercentage(value);
    }

    /// <summary>Gets or sets the scroll setting of the region (https://w3c.github.io/webvtt/#dom-vttregion-scroll).</summary>
    [IdlMember("scroll", "https://w3c.github.io/webvtt/#dom-vttregion-scroll")]
    public ScrollSetting Scroll { get; set; }

    private static double ValidatePercentage(double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentException("The value must be a finite number", nameof(value));

        if (value is < 0 or > 100)
            throw DomException.IndexSize("The value must be in the range [0, 100]");

        return value;
    }

    internal void SetParsedWidth(double width) => _width = width;

    internal void SetParsedRegionAnchor(double x, double y)
    {
        _regionAnchorX = x;
        _regionAnchorY = y;
    }

    internal void SetParsedViewportAnchor(double x, double y)
    {
        _viewportAnchorX = x;
        _viewportAnchorY = y;
    }
}
