using Meziantou.Framework.Compression.Internals.Lzma;

namespace Meziantou.Framework.Compression;

/// <summary>The options of the compression of an <see cref="LZipStream"/>.</summary>
public sealed class LZipCompressionOptions
{
    /// <summary>The smallest dictionary size of lzip: 4 KiB.</summary>
    public const int MinDictionarySize = 1 << 12;

    /// <summary>The largest dictionary size of lzip: 512 MiB.</summary>
    public const int MaxDictionarySize = 1 << 29;

    /// <summary>Gets or sets the preset, from 0 (fastest) to 9 (smallest output), with the parameters of the presets of xz. The default is 6.</summary>
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

    /// <summary>
    /// Gets or sets the dictionary size in bytes, from 4 KiB to 512 MiB, or <see langword="null"/> to use the one of the preset.
    /// The header of a member stores the size in one byte: it stores the smallest size it can represent that is at least this size.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 4 KiB and 512 MiB.</exception>
    public int? DictionarySize
    {
        get;
        set
        {
            if (value is { } size)
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(size, MinDictionarySize, nameof(value));
                ArgumentOutOfRangeException.ThrowIfGreaterThan(size, MaxDictionarySize, nameof(value));
            }

            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the number of bytes of uncompressed data of each member, or 0 to write a single member. The default is 0.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public long MemberSize
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }

    internal LzmaEncoderSettings CreateSettings()
    {
        // The lzip format uses lc=3, lp=0 and pb=2
        var settings = LzmaEncoderSettings.FromPreset(Preset);
        if (DictionarySize is { } dictionarySize)
        {
            settings.DictionarySize = dictionarySize;
        }

        return settings;
    }
}
