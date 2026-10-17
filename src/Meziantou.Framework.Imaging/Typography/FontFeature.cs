using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Typography;

/// <summary>
/// A feature of the OpenType Layout tables of a font, turned on or off for a text: ligatures (<c>liga</c>), kerning
/// (<c>kern</c>), small capitals (<c>smcp</c>), tabular figures (<c>tnum</c>), a stylistic set (<c>ss01</c>)...
/// </summary>
/// <remarks>
/// A feature is named by its registered tag of four characters. A value of 0 turns it off and 1 turns it on; a larger
/// value selects one of several alternates when the feature offers a choice (<c>salt</c>, <c>cv01</c>). A feature the
/// font does not have changes nothing.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct FontFeature : IEquatable<FontFeature>
{
    private readonly string? _tag;

    /// <summary>Initializes a feature setting.</summary>
    /// <param name="tag">The tag of the feature: four printable ASCII characters.</param>
    /// <param name="value">0 to turn the feature off, 1 to turn it on, or the number of an alternate.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tag"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="tag"/> is not four printable ASCII characters.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative or greater than 65535.</exception>
    public FontFeature(string tag, int value = 1)
    {
        ArgumentNullException.ThrowIfNull(tag);
        if (tag.Length != 4 || tag.AsSpan().ContainsAnyExceptInRange(' ', '~'))
            throw new ArgumentException("The tag of a feature is four printable ASCII characters.", nameof(tag));

        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, ushort.MaxValue);
        _tag = tag;
        Value = value;
    }

    /// <summary>Gets the tag of the feature.</summary>
    public string Tag => _tag ?? "    ";

    /// <summary>Gets the value of the feature: 0 is off, 1 is on, a larger value is the number of an alternate.</summary>
    public int Value { get; }

    internal uint TagValue => ShapingFeature.ToTag(Tag);

    /// <inheritdoc />
    public bool Equals(FontFeature other) => Value == other.Value && string.Equals(Tag, other.Tag, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is FontFeature other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Tag, Value);

    /// <summary>Returns the feature in the form <c>liga=0</c>.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Tag}={Value}");

    /// <summary>Determines whether two feature settings are the same.</summary>
    public static bool operator ==(FontFeature left, FontFeature right) => left.Equals(right);

    /// <summary>Determines whether two feature settings differ.</summary>
    public static bool operator !=(FontFeature left, FontFeature right) => !left.Equals(right);
}
