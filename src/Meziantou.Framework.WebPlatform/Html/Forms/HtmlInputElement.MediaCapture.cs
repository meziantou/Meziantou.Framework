using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

// HTML Media Capture (https://w3c.github.io/html-media-capture/)
public partial class HtmlInputElement
{
    // The keywords of the capture attribute: user, environment, and the empty string (the implementation-specific state, which
    // is also the missing value default and the invalid value default, and has no canonical keyword)
    private static readonly EnumeratedAttribute CaptureAttribute = new(
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["user"] = 0,
            ["environment"] = 1,
            [""] = 2,
        },
        ["user", "environment", null],
        MissingValueDefault: 2,
        InvalidValueDefault: 2,
        EmptyValueDefault: -1);

    /// <summary>
    /// Gets or sets the preferred facing mode of the camera that the file picker uses to capture the file (the <c>capture</c>
    /// content attribute, https://w3c.github.io/html-media-capture/#the-capture-attribute): <c>user</c>, <c>environment</c>, or
    /// the empty string (the default facing mode of the user agent) when the attribute is missing or invalid. The library does
    /// not capture files: <see cref="BrowsingContextOptions.ShowFilePicker"/> decides the files of the element.
    /// </summary>
    /// <seealso href="https://w3c.github.io/html-media-capture/#dom-htmlinputelement-capture"/>
    [IdlMember("capture", "https://w3c.github.io/html-media-capture/#dom-htmlinputelement-capture")]
    public string Capture
    {
        get => Reflection.GetEnumerated(this, "capture", CaptureAttribute);
        set => Reflection.SetString(this, "capture", value);
    }
}
