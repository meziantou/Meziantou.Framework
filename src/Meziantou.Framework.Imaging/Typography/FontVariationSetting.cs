using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Typography;

/// <summary>The value of an axis of a variable font: a weight of 650 (<c>wght</c>), a width of 80 (<c>wdth</c>).</summary>
/// <remarks>The tags and the ranges of the axes of a font are in <see cref="Font.VariationAxes"/>.</remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct FontVariationSetting : IEquatable<FontVariationSetting>
{
    private readonly string? _tag;

    /// <summary>Initializes a new instance of the <see cref="FontVariationSetting"/> struct.</summary>
    /// <param name="tag">The tag of the axis: four printable ASCII characters, such as <c>wght</c>.</param>
    /// <param name="value">The value, in the units of the axis.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tag"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="tag"/> is not four printable ASCII characters, or <paramref name="value"/> is not a finite number.</exception>
    public FontVariationSetting(string tag, double value)
    {
        ArgumentNullException.ThrowIfNull(tag);
        if (tag.Length != 4 || tag.AsSpan().ContainsAnyExceptInRange(' ', '~'))
            throw new ArgumentException("The tag of an axis is four printable ASCII characters.", nameof(tag));

        if (!double.IsFinite(value))
            throw new ArgumentException("The value of an axis is a finite number.", nameof(value));

        _tag = tag;
        Value = value;
    }

    /// <summary>Gets the tag of the axis.</summary>
    public string Tag => _tag ?? "    ";

    /// <summary>Gets the value, in the units of the axis.</summary>
    public double Value { get; }

    internal uint TagValue => ShapingFeature.ToTag(Tag);

    /// <inheritdoc />
    public bool Equals(FontVariationSetting other) => Value.Equals(other.Value) && string.Equals(Tag, other.Tag, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is FontVariationSetting other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Tag, Value);

    /// <summary>Returns the setting in the syntax of CSS: <c>"wght" 650</c>.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"\"{Tag}\" {Value}");

    /// <summary>Tells whether two settings are the same axis and the same value.</summary>
    public static bool operator ==(FontVariationSetting left, FontVariationSetting right) => left.Equals(right);

    /// <summary>Tells whether two settings differ.</summary>
    public static bool operator !=(FontVariationSetting left, FontVariationSetting right) => !left.Equals(right);
}
