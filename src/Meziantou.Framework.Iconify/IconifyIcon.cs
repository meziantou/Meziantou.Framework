using Meziantou.Framework.Internals;

namespace Meziantou.Framework;

/// <summary>
/// Represents an Iconify icon: the content of an SVG image and its view box.
/// </summary>
/// <remarks>
/// The rotation and the flips of the icon, and of its aliases, are already applied to <see cref="Body"/> and to the view box.
/// </remarks>
public sealed class IconifyIcon
{
    private string? _svg;

    /// <summary>Initializes a new instance of the <see cref="IconifyIcon"/> class.</summary>
    /// <param name="prefix">The prefix of the icon set, such as <c>mdi</c>.</param>
    /// <param name="name">The name of the icon in the icon set, such as <c>account-circle</c>.</param>
    /// <param name="body">The content of the <c>svg</c> element.</param>
    /// <param name="left">The left coordinate of the view box.</param>
    /// <param name="top">The top coordinate of the view box.</param>
    /// <param name="width">The width of the view box.</param>
    /// <param name="height">The height of the view box.</param>
    /// <param name="isHidden"><see langword="true"/> when the icon set keeps the icon for backward compatibility only.</param>
    public IconifyIcon(string prefix, string name, string body, double left, double top, double width, double height, bool isHidden = false)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(body);

        Prefix = prefix;
        Name = name;
        Body = body;
        Left = left;
        Top = top;
        Width = width;
        Height = height;
        IsHidden = isHidden;
    }

    /// <summary>Gets the prefix of the icon set, such as <c>mdi</c>.</summary>
    public string Prefix { get; }

    /// <summary>Gets the name of the icon in the icon set, such as <c>account-circle</c>.</summary>
    public string Name { get; }

    /// <summary>Gets the content of the <c>svg</c> element (paths, shapes, groups), without the <c>svg</c> element itself.</summary>
    public string Body { get; }

    /// <summary>Gets the left coordinate of the view box.</summary>
    public double Left { get; }

    /// <summary>Gets the top coordinate of the view box.</summary>
    public double Top { get; }

    /// <summary>Gets the width of the view box.</summary>
    public double Width { get; }

    /// <summary>Gets the height of the view box.</summary>
    public double Height { get; }

    /// <summary>Gets a value indicating whether the icon set keeps the icon for backward compatibility only.</summary>
    public bool IsHidden { get; }

    /// <summary>
    /// Gets the SVG image of the icon. Its <c>width</c> and <c>height</c> attributes are the size of the view box.
    /// </summary>
    public string Svg => _svg ??= IconifySvgBuilder.BuildSvg(Body, Left, Top, Width, Height);

    /// <summary>Returns the SVG image of the icon.</summary>
    /// <returns>The value of <see cref="Svg"/>.</returns>
    public override string ToString() => Svg;
}
