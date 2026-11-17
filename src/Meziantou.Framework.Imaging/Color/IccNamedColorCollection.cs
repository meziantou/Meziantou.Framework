using System.Collections;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging;

/// <summary>The colors of an ICC named color profile, in the order of the profile, and their lookup by name.</summary>
/// <remarks>
/// <para>
/// Version 2 and 4 profiles store their colors in a <c>namedColor2Tag</c> (ICC.1:2022 section 10.17): a 7-bit ASCII name
/// made of a prefix, a root name and a suffix, the media-relative colorimetry in the connection space of the profile, and
/// optionally the device values of its data color space. Version 5 (iccMAX) profiles store them in a <c>namedColorTag</c>
/// (ICC.2:2023 section 9.2.104) with a UTF-8 name and values for several tints; the collection holds the color at full
/// tint (1.0). The spectral data of an iccMAX color are not read.
/// </para>
/// <para>Instances are immutable and thread-safe. A name that appears several times refers to its first color.</para>
/// </remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../samples/Meziantou.Framework.Imaging.Samples/ColorExamples.cs" ref:region="Imaging_NamedColors" ref:dedent="true">
/// public static (byte[] Rgb, IReadOnlyList&lt;double&gt; Inks) SpotColor(string swatchBookPath, string name)
/// {
///     var swatchBook = new IccProfile(new MetadataBlob(File.ReadAllBytes(swatchBookPath)));
///     var colors = IccNamedColorCollection.Create(swatchBook);
///     if (!colors.TryGetValue(name, out var color))
///         throw new ArgumentException($"The swatch book has no color named '{name}'.", nameof(name));
///
///     // The colorimetry of the color, converted to the device values of the display (relative colorimetric intent)
///     var transform = IccNamedColorTransform.Create(swatchBook, IccProfile.Srgb);
///     var rgb = new byte[transform.DestinationChannelCount];
///     transform.Convert(color, rgb);
///
///     // On the press the profile was made for, its device values (here CMYK inks) reproduce the color exactly
///     return (rgb, color.DeviceValues);
/// }
/// </code>
/// </example>
public sealed class IccNamedColorCollection : IReadOnlyList<IccNamedColor>
{
    private readonly IccNamedColor[] _colors;
    private readonly Dictionary<string, int> _indexes;

    private IccNamedColorCollection(IccProfile profile, IccNamedColor[] colors, int deviceChannelCount)
    {
        Profile = profile;
        _colors = colors;
        DeviceChannelCount = deviceChannelCount;
        _indexes = new Dictionary<string, int>(colors.Length, StringComparer.Ordinal);

        // Colors that share their name share its string: it is hashed once, so that a long name referenced by many
        // colors is not hashed again and again (the reader bounds the characters of the distinct names it reads)
        var added = new HashSet<string>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < colors.Length; i++)
        {
            if (added.Add(colors[i].Name))
            {
                _indexes.TryAdd(colors[i].Name, i);
            }
        }
    }

    /// <summary>Gets the named color profile the colors were read from.</summary>
    public IccProfile Profile { get; }

    /// <summary>Gets the number of device values of each color (<see cref="IccNamedColor.DeviceValues"/>); 0 when the profile gives none.</summary>
    public int DeviceChannelCount { get; }

    /// <summary>Gets the number of colors.</summary>
    public int Count => _colors.Length;

    /// <summary>Gets the color at an index, in the order of the profile.</summary>
    /// <param name="index">The index.</param>
    /// <returns>The color.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or not less than <see cref="Count"/>.</exception>
    public IccNamedColor this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _colors.Length);
            return _colors[index];
        }
    }

    /// <summary>Gets the color with a name (ordinal, case-sensitive comparison).</summary>
    /// <param name="name">The full name of the color.</param>
    /// <returns>The first color with this name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyNotFoundException">No color has this name.</exception>
    public IccNamedColor this[string name]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(name);
            if (!_indexes.TryGetValue(name, out var index))
                throw new KeyNotFoundException($"The named color profile has no color named '{name}'.");

            return _colors[index];
        }
    }

    /// <summary>Reads the colors of a named color profile.</summary>
    /// <param name="profile">A profile of the <see cref="IccProfileClass.NamedColor"/> class.</param>
    /// <returns>The colors.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="profile"/> is not a named color profile.</exception>
    /// <exception cref="InvalidImageContentException">The profile is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">
    /// The profile is valid but its colors cannot be read: the connection space is neither CIEXYZ nor CIELAB (a spectral
    /// iccMAX profile), or an iccMAX color has no value at full tint or stores its colorimetry as integers.
    /// </exception>
    public static IccNamedColorCollection Create(IccProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.ProfileClass != IccProfileClass.NamedColor)
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The profile is not a named color profile: its class is {profile.ProfileClass}."), nameof(profile));

        var colors = IccNamedColorReader.Read(profile.Data.Span, "named color", out var deviceChannelCount);
        return new IccNamedColorCollection(profile, colors, deviceChannelCount);
    }

    /// <summary>Gets the color with a name (ordinal, case-sensitive comparison).</summary>
    /// <param name="name">The full name of the color.</param>
    /// <param name="color">The first color with this name, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a color has this name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public bool TryGetValue(string name, [NotNullWhen(true)] out IccNamedColor? color)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_indexes.TryGetValue(name, out var index))
        {
            color = _colors[index];
            return true;
        }

        color = null;
        return false;
    }

    /// <summary>Gets the index of the first color with a name (ordinal, case-sensitive comparison).</summary>
    /// <param name="name">The full name of the color.</param>
    /// <returns>The index, or -1 when no color has this name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public int IndexOf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _indexes.TryGetValue(name, out var index) ? index : -1;
    }

    /// <inheritdoc/>
    public IEnumerator<IccNamedColor> GetEnumerator() => ((IEnumerable<IccNamedColor>)_colors).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
