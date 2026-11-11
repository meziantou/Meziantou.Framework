using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.VectorGraphics;

/// <summary>An element of a <see cref="Scene"/>.</summary>
public abstract class SceneElement
{
    private Dictionary<string, string>? _dataAttributes;
    private ReadOnlyDictionary<string, string>? _dataAttributesView;

    private protected SceneElement()
    {
    }

    /// <summary>
    /// Gets or sets the identifier of the element in the SVG output (its <c>id</c> attribute), or <see langword="null"/>
    /// for none: a script or a style sheet of a web page finds the element with it. The other renderers ignore it.
    /// </summary>
    /// <remarks>
    /// The identifiers of the elements of a scene must be different: <see cref="SvgSceneRenderer"/> throws
    /// <see cref="InvalidOperationException"/> when two elements have the same one. The identifiers the renderer
    /// generates (of the gradients, the clip paths, ...) avoid them.
    /// </remarks>
    /// <exception cref="ArgumentException">The value is not a valid identifier (<see cref="IsValidId"/>).</exception>
    public string? Id
    {
        get;
        set
        {
            if (value is not null && !IsValidId(value))
                throw new ArgumentException($"The identifier '{value}' is not valid: use an ASCII letter followed by ASCII letters, digits, hyphens, underscores and dots.", nameof(value));

            field = value;
        }
    }

    /// <summary>Gets or sets the CSS class of the element in the SVG output.</summary>
    public string? CssClass { get; set; }

    /// <summary>Gets or sets the text shown when hovering the element (an SVG <c>title</c>).</summary>
    public string? Tooltip { get; set; }

    /// <summary>
    /// Gets the <c>data-*</c> attributes of the element in the SVG output, by name without the <c>data-</c> prefix. Scripts
    /// of a web page read them with <c>element.dataset</c>. Set them with <see cref="SetDataAttribute"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string> DataAttributes => _dataAttributesView ?? ReadOnlyDictionary<string, string>.Empty;

    /// <summary>Sets a <c>data-*</c> attribute of the element in the SVG output, or removes it.</summary>
    /// <param name="name">The name of the attribute without the <c>data-</c> prefix: lowercase ASCII letters, digits, hyphens, underscores and dots.</param>
    /// <param name="value">The value, or <see langword="null"/> to remove the attribute.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or contains another character.</exception>
    public void SetDataAttribute(string name, string? value)
    {
        ThrowIfInvalidDataAttributeName(name, nameof(name));
        if (value is null)
        {
            _dataAttributes?.Remove(name);
            return;
        }

        if (_dataAttributes is null)
        {
            _dataAttributes = new(StringComparer.Ordinal);
            _dataAttributesView = _dataAttributes.AsReadOnly();
        }

        _dataAttributes[name] = value;
    }

    /// <summary>
    /// Gets or sets the transform of the element, or <see langword="null"/> for none. For a group, it transforms its
    /// children.
    /// </summary>
    /// <remarks>
    /// The element is drawn as if it were the only child of a group with this transform: its clips, its mask, its
    /// filter, its opacity and its hyperlink are in the coordinates of its parent, around what it draws. The rotation of
    /// a text (<see cref="SceneText.Rotation"/>) applies before the transform.
    /// </remarks>
    public SceneTransform? Transform { get; set; }

    /// <summary>
    /// Gets or sets the address the element links to, or <see langword="null"/> for none. The SVG output wraps the element
    /// in an <c>a</c> element, the PDF output adds a link over what it draws (absolute addresses only), and an image
    /// ignores it.
    /// </summary>
    public string? Hyperlink { get; set; }

    /// <summary>Gets or sets the opacity of the element, from 0 to 1.</summary>
    public double Opacity { get; set; } = 1;

    /// <summary>Gets or sets the effect applied to what the element draws, such as a blur or a drop shadow, or <see langword="null"/> for none.</summary>
    /// <remarks>PDF has no filters: the PDF renderer throws <see cref="NotSupportedException"/> for a scene that has one.</remarks>
    public SceneFilter? Filter { get; set; }

    /// <summary>Gets or sets the opacity mask of the element, or <see langword="null"/> for none.</summary>
    public SceneMask? Mask { get; set; }

    /// <summary>Gets or sets how the element is blended with what is drawn under it. The default is <see cref="SceneBlendMode.Normal"/>.</summary>
    public SceneBlendMode BlendMode { get; set; }

    /// <summary>
    /// Gets a rectangle that contains what the element draws, in the coordinates of its parent, or <see langword="null"/>
    /// when it draws nothing, for a renderer that places what it draws: a filter region, a link, a layer.
    /// </summary>
    /// <param name="measureText">
    /// Gives the rectangle of a text before its rotation, in the coordinates of its parent, or <see langword="null"/> when
    /// it draws nothing: the size of a text depends on the fonts of the renderer. It is not called for a text on a path
    /// (<see cref="SceneText.Path"/>), whose rectangle is the one of its path enlarged by the size of its font.
    /// </param>
    /// <param name="applyClips">
    /// Whether a group applies its clips (<see cref="SceneGroup.Clip"/>, <see cref="SceneGroup.ClipPath"/>). The filter of
    /// a group applies before its clips: the region of its filter is the rectangle without them.
    /// </param>
    /// <returns>The rectangle, or <see langword="null"/>.</returns>
    /// <remarks>
    /// The rectangle contains the strokes, with their joins and their caps, and the blurs and the shadows of the
    /// children; not the filter of the element itself. A group applies its transform, and its clips unless
    /// <paramref name="applyClips"/> is <see langword="false"/>.
    /// </remarks>
    public SceneBounds? GetBounds(Func<SceneText, SceneBounds?> measureText, bool applyClips = true)
    {
        ArgumentNullException.ThrowIfNull(measureText);
        return SceneGeometry.GetBounds(this, measureText, applyClips);
    }

    /// <summary>Indicates whether a name is a valid name of a <c>data-*</c> attribute: lowercase ASCII letters, digits, hyphens, underscores and dots, without the <c>data-</c> prefix.</summary>
    /// <param name="name">The name.</param>
    /// <returns><see langword="true"/> when the name is valid.</returns>
    public static bool IsValidDataAttributeName([NotNullWhen(true)] string? name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        foreach (var c in name)
        {
            if (!char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c is not ('-' or '_' or '.'))
                return false;
        }

        return true;
    }

    /// <summary>Indicates whether a value is a valid identifier (<see cref="Id"/>): an ASCII letter, then ASCII letters, digits, hyphens, underscores and dots.</summary>
    /// <param name="id">The identifier.</param>
    /// <returns><see langword="true"/> when the identifier is valid.</returns>
    /// <remarks>These identifiers are written in <c>url(#id)</c> references and CSS selectors without escaping.</remarks>
    public static bool IsValidId([NotNullWhen(true)] string? id)
    {
        if (string.IsNullOrEmpty(id) || !char.IsAsciiLetter(id[0]))
            return false;

        foreach (var c in id)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.'))
                return false;
        }

        return true;
    }

    /// <summary>Throws when a name is not a valid name of a <c>data-*</c> attribute.</summary>
    /// <param name="name">The name.</param>
    /// <param name="paramName">The name of the parameter, for the exception.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or contains another character.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static void ThrowIfInvalidDataAttributeName(string name, string paramName)
    {
        ArgumentException.ThrowIfNullOrEmpty(name, paramName);
        if (!IsValidDataAttributeName(name))
            throw new ArgumentException($"The data attribute name '{name}' is not valid: use lowercase ASCII letters, digits, hyphens, underscores and dots, without the 'data-' prefix.", paramName);
    }
}
