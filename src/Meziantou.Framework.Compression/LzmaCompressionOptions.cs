using Meziantou.Framework.Compression.Internals.Lzma;

namespace Meziantou.Framework.Compression;

/// <summary>The options of the compression of an <see cref="LzmaStream"/>.</summary>
public sealed class LzmaCompressionOptions
{
    /// <summary>Gets or sets the preset of xz, from 0 (fastest) to 9 (smallest output). The default is 6.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 9.</exception>
    public int Preset
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 9);
            field = value;
        }
    } = 6;

    /// <summary>Gets or sets the dictionary size in bytes, from 4 KiB to 1.5 GiB, or <see langword="null"/> to use the one of the preset.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 4 KiB and 1.5 GiB.</exception>
    public int? DictionarySize
    {
        get;
        set
        {
            if (value is { } size)
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(size, LzmaConstants.MinDictionarySize, nameof(value));
                ArgumentOutOfRangeException.ThrowIfGreaterThan(size, LzmaEncoderSettings.MaxDictionarySize, nameof(value));
            }

            field = value;
        }
    }

    /// <summary>Gets or sets the number of literal context bits (lc), from 0 to 8. The default is 3.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 8.</exception>
    public int LiteralContextBits
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 8);
            field = value;
        }
    } = 3;

    /// <summary>Gets or sets the number of literal position bits (lp), from 0 to 4. The default is 0.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 4.</exception>
    public int LiteralPositionBits
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 4);
            field = value;
        }
    }

    /// <summary>Gets or sets the number of position bits (pb), from 0 to 4. The default is 2.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 4.</exception>
    public int PositionBits
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 4);
            field = value;
        }
    } = 2;

    /// <summary>Gets or sets a value indicating whether to use the slower variant of the preset, which may compress better (<c>xz --extreme</c>).</summary>
    public bool Extreme { get; set; }

    internal LzmaEncoderSettings CreateSettings()
    {
        var settings = LzmaEncoderSettings.FromPreset(Preset, Extreme);
        if (DictionarySize is { } dictionarySize)
        {
            settings.DictionarySize = dictionarySize;
        }

        settings.LiteralContextBits = LiteralContextBits;
        settings.LiteralPositionBits = LiteralPositionBits;
        settings.PositionBits = PositionBits;
        return settings;
    }
}
