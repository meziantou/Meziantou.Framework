namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A device to emulate, as the device descriptors of Playwright (<c>playwright.devices</c>): the size of its viewport and of
/// its screen, its device pixel ratio, whether it is a mobile device with a touch screen, and its user agent string. The
/// library has no list of devices: copy the values of a descriptor of Playwright, or describe your own device.
/// </summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../../tests/Meziantou.Framework.WebPlatform.Tests/Dom/CssomViewTests.cs" ref:region="DeviceDescriptor_Example" ref:dedent="true">
/// // The values of the "iPhone 13" device of Playwright
/// var iPhone13 = new DeviceDescriptor
/// {
///     Width = 390,
///     Height = 664,
///     ScreenWidth = 390,
///     ScreenHeight = 844,
///     DevicePixelRatio = 3,
///     IsMobile = true,
///     HasTouch = true,
///     UserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 15_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/15.0 Mobile/15E148 Safari/604.1",
/// };
///
/// using var context = new BrowsingContext(new BrowsingContextOptions
/// {
///     Viewport = iPhone13.CreateViewport(),
///     UserAgent = iPhone13.UserAgent,
/// });
/// var document = context.Navigate("https://example.com/", _ =&gt; "&lt;!DOCTYPE html&gt;&lt;p&gt;Hello&lt;/p&gt;");
///
/// Assert.True(document.DefaultView!.MatchMedia("(pointer: coarse) and (max-device-width: 500px)").Matches);
/// Assert.Equal(iPhone13.UserAgent, document.DefaultView.Navigator.UserAgent);
/// </code>
/// </example>
public sealed record DeviceDescriptor
{
    /// <summary>Gets or sets the width of the viewport, in CSS pixels (<c>viewport.width</c> in Playwright).</summary>
    public required double Width { get; init; }

    /// <summary>Gets or sets the height of the viewport, in CSS pixels (<c>viewport.height</c> in Playwright).</summary>
    public required double Height { get; init; }

    /// <summary>Gets or sets the width of the screen, in CSS pixels (<c>screen.width</c> in Playwright), or null for the width of the viewport.</summary>
    public double? ScreenWidth { get; init; }

    /// <summary>Gets or sets the height of the screen, in CSS pixels (<c>screen.height</c> in Playwright), or null for the height of the viewport.</summary>
    public double? ScreenHeight { get; init; }

    /// <summary>Gets or sets the number of device pixels per CSS pixel (<c>deviceScaleFactor</c> in Playwright). The default is 1.</summary>
    public double DevicePixelRatio { get; init; } = 1;

    /// <summary>Gets or sets whether the device is a mobile device (<c>isMobile</c> in Playwright, see <see cref="ViewportOptions.IsMobile"/>).</summary>
    public bool IsMobile { get; init; }

    /// <summary>Gets or sets whether the device has a touch screen (<c>hasTouch</c> in Playwright, see <see cref="ViewportOptions.HasTouch"/>).</summary>
    public bool HasTouch { get; init; }

    /// <summary>Gets or sets the user agent string of the device (<c>userAgent</c> in Playwright, see <see cref="BrowsingContextOptions.UserAgent"/>), or null.</summary>
    public string? UserAgent { get; init; }

    /// <summary>
    /// Returns the viewport of the device: the options of <paramref name="viewport"/> (the preferences of the user, the media
    /// type...) with the size, the screen, the device pixel ratio and the kind of device of the descriptor.
    /// </summary>
    /// <param name="viewport">The other options of the viewport, or null for <see cref="ViewportOptions.Default"/>.</param>
    /// <returns>The options of the viewport, to set in <see cref="BrowsingContextOptions.Viewport"/>.</returns>
    public ViewportOptions CreateViewport(ViewportOptions? viewport = null) => (viewport ?? ViewportOptions.Default) with
    {
        Width = Width,
        Height = Height,
        ScreenWidth = ScreenWidth,
        ScreenHeight = ScreenHeight,
        DevicePixelRatio = DevicePixelRatio,
        IsMobile = IsMobile,
        HasTouch = HasTouch,
    };
}
