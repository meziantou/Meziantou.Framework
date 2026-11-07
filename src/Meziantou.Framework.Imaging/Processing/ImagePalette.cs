using System.Collections;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// An immutable palette of 1 to 256 colors with 8-bit samples and straight alpha, for a color reduction
/// (<see cref="QuantizeOptions.Palette"/>). Build one from the pixels of frames with <see cref="PaletteBuilder"/>, or list
/// the colors.
/// </summary>
/// <remarks>
/// <para>
/// The colors keep their order, without duplicates (the first occurrence is kept). Every fully transparent color is the same
/// entry, transparent black: the color hidden by a transparent pixel is not part of a palette.
/// </para>
/// <para>
/// When it maps a color, a reduction compares an opaque pixel with the opaque entries only (a palette without opaque entry
/// excepted), so opaque areas never become translucent; a translucent pixel is compared with every entry. The encoders
/// restrict the entries to what their format stores: see <see cref="Formats.GifEncoder.Quantization"/>,
/// <see cref="Formats.BmpEncoder.Quantization"/> and <see cref="Formats.PngEncoder.Quantization"/>.
/// </para>
/// </remarks>
public sealed class ImagePalette : IReadOnlyList<Rgba32>
{
    /// <summary>The largest number of colors of a palette.</summary>
    public const int MaxCount = 256;

    private readonly Rgba32[] _colors;
    private readonly int[] _keys;

    /// <summary>Initializes a new instance of the <see cref="ImagePalette"/> class.</summary>
    /// <param name="colors">The colors.</param>
    /// <exception cref="ArgumentException"><paramref name="colors"/> has no color, or more than 256 distinct colors.</exception>
    public ImagePalette(ReadOnlySpan<Rgba32> colors)
    {
        var unique = new List<Rgba32>(Math.Min(colors.Length, MaxCount));
        var keys = new List<int>(unique.Capacity);
        foreach (var color in colors)
        {
            var key = PaletteQuantizer.GetKey(color.R, color.G, color.B, color.A);
            if (keys.Contains(key))
                continue;

            if (keys.Count == MaxCount)
                throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"A palette holds at most {MaxCount} distinct colors."), nameof(colors));

            keys.Add(key);
            unique.Add(color.A == 0 ? default : color);
        }

        if (unique.Count == 0)
            throw new ArgumentException("A palette holds at least one color.", nameof(colors));

        _colors = [.. unique];
        _keys = [.. keys];
    }

    /// <summary>Initializes a new instance of the <see cref="ImagePalette"/> class.</summary>
    /// <param name="colors">The colors.</param>
    /// <exception cref="ArgumentNullException"><paramref name="colors"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="colors"/> has no color, or more than 256 distinct colors.</exception>
    public ImagePalette(IEnumerable<Rgba32> colors)
        : this(ToArray(colors))
    {
    }

    /// <summary>Gets the number of colors.</summary>
    public int Count => _colors.Length;

    /// <summary>Gets the colors, in palette order.</summary>
    public ReadOnlySpan<Rgba32> Colors => _colors;

    /// <summary>Gets a value indicating whether the palette has a fully transparent entry.</summary>
    public bool HasTransparentColor => Array.IndexOf(_keys, PaletteQuantizer.TransparentKey) >= 0;

    /// <summary>Gets the keys of the colors, in palette order (<see cref="PaletteQuantizer.GetKey"/>).</summary>
    internal ReadOnlySpan<int> Keys => _keys;

    /// <summary>Gets a color.</summary>
    /// <param name="index">The index of the color.</param>
    /// <returns>The color.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or not less than <see cref="Count"/>.</exception>
    public Rgba32 this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _colors.Length);
            return _colors[index];
        }
    }

    /// <summary>Returns an enumerator over the colors, in palette order.</summary>
    /// <returns>The enumerator.</returns>
    public IEnumerator<Rgba32> GetEnumerator() => ((IEnumerable<Rgba32>)_colors).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Creates a palette from the keys of a quantizer palette.</summary>
    internal static ImagePalette FromKeys(ReadOnlySpan<int> keys)
    {
        var colors = new Rgba32[keys.Length];
        for (var i = 0; i < keys.Length; i++)
        {
            colors[i] = ToColor(keys[i]);
        }

        return new ImagePalette(colors);
    }

    /// <summary>Gets the color of a key.</summary>
    internal static Rgba32 ToColor(int key) => key == PaletteQuantizer.TransparentKey ? default : new Rgba32((byte)(key >> 16), (byte)(key >> 8), (byte)key, (byte)PaletteQuantizer.GetAlpha(key));

    private static Rgba32[] ToArray(IEnumerable<Rgba32> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        return [.. colors];
    }
}
