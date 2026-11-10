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

    /// <summary>Gets or sets the opacity of the element, from 0 to 1.</summary>
    public double Opacity { get; set; } = 1;

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
