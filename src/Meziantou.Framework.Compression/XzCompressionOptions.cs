using Meziantou.Framework.Compression.Internals.Lzma;

namespace Meziantou.Framework.Compression;

/// <summary>The options of the compression of an <see cref="XzStream"/>.</summary>
public sealed class XzCompressionOptions
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

    /// <summary>Gets or sets the integrity check of the blocks. The default is <see cref="XzCheck.Crc64"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a member of <see cref="XzCheck"/>.</exception>
    public XzCheck Check
    {
        get;
        set
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The check is not supported.");

            field = value;
        }
    } = XzCheck.Crc64;

    /// <summary>Gets or sets the number of bytes of uncompressed data of each block, or 0 to write a single block. The default is 0.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public long BlockSize
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }

    /// <summary>Gets or sets a value indicating whether to use the slower variant of the preset, which may compress better (<c>xz --extreme</c>).</summary>
    public bool Extreme { get; set; }

    internal LzmaEncoderSettings CreateSettings()
    {
        var settings = LzmaEncoderSettings.FromPreset(Preset, Extreme);
        if (DictionarySize is { } dictionarySize)
        {
            settings.DictionarySize = dictionarySize;
        }

        return settings;
    }

    internal int GetCheckType() => Check switch
    {
        XzCheck.None => 0,
        XzCheck.Crc32 => 1,
        XzCheck.Crc64 => 4,
        _ => 10,
    };
}
