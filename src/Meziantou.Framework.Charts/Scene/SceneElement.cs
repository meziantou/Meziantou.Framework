using System.Collections.ObjectModel;

namespace Meziantou.Framework.Charts;

/// <summary>An element of a <see cref="ChartScene"/>.</summary>
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
        DataAttributeValues.ThrowIfInvalidName(name, nameof(name));
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
}
