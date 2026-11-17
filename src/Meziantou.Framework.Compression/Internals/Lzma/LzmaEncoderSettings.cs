namespace Meziantou.Framework.Compression.Internals.Lzma;

/// <summary>The settings of the LZMA and LZMA2 encoders. The presets are those of xz-utils.</summary>
internal sealed class LzmaEncoderSettings
{
    /// <summary>The largest dictionary size, the one of xz-utils (1.5 GiB).</summary>
    public const int MaxDictionarySize = 1536 << 20;

    private static readonly byte[] PresetDictionaryLogs = [18, 20, 21, 22, 22, 23, 23, 24, 25, 26];

    /// <summary>Gets or sets the dictionary size, from 4 KiB to 1.5 GiB.</summary>
    public int DictionarySize { get; set; } = 1 << 23;

    /// <summary>Gets or sets the number of literal context bits (lc), from 0 to 8.</summary>
    public int LiteralContextBits { get; set; } = 3;

    /// <summary>Gets or sets the number of literal position bits (lp), from 0 to 4.</summary>
    public int LiteralPositionBits { get; set; }

    /// <summary>Gets or sets the number of position bits (pb), from 0 to 4.</summary>
    public int PositionBits { get; set; } = 2;

    /// <summary>Gets or sets the length of a match that is good enough to stop looking for a longer one, from 5 to 273.</summary>
    public int NiceLength { get; set; } = 64;

    /// <summary>Gets or sets the match finder.</summary>
    public LzmaMatchFinderKind MatchFinder { get; set; } = LzmaMatchFinderKind.BinaryTree4;

    /// <summary>Gets or sets the parser.</summary>
    public LzmaEncoderMode Mode { get; set; } = LzmaEncoderMode.Normal;

    /// <summary>Gets or sets the maximum number of candidates the match finder checks per position, or 0 to derive it from <see cref="NiceLength"/>.</summary>
    public int Depth { get; set; }

    /// <summary>Creates the settings of an xz-utils preset.</summary>
    /// <param name="preset">The preset, from 0 (fastest) to 9 (smallest output).</param>
    /// <param name="extreme">Whether to use the slower variant of the preset (<c>xz --extreme</c>).</param>
    public static LzmaEncoderSettings FromPreset(int preset, bool extreme = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(preset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(preset, 9);

        var settings = new LzmaEncoderSettings
        {
            DictionarySize = 1 << PresetDictionaryLogs[preset],
        };

        if (preset <= 3)
        {
            settings.Mode = LzmaEncoderMode.Fast;
            settings.MatchFinder = LzmaMatchFinderKind.HashChain4;
            settings.NiceLength = preset <= 1 ? 128 : 273;
            settings.Depth = preset switch
            {
                0 => 4,
                1 => 8,
                2 => 24,
                _ => 48,
            };
        }
        else
        {
            settings.Mode = LzmaEncoderMode.Normal;
            settings.MatchFinder = LzmaMatchFinderKind.BinaryTree4;
            settings.NiceLength = preset switch
            {
                4 => 16,
                5 => 32,
                _ => 64,
            };
            settings.Depth = 0;
        }

        if (extreme)
        {
            settings.Mode = LzmaEncoderMode.Normal;
            settings.MatchFinder = LzmaMatchFinderKind.BinaryTree4;
            if (preset is 3 or 5)
            {
                settings.NiceLength = 192;
                settings.Depth = 0;
            }
            else
            {
                settings.NiceLength = 273;
                settings.Depth = 512;
            }
        }

        return settings;
    }

    /// <summary>Creates a copy of the settings.</summary>
    public LzmaEncoderSettings Clone() => (LzmaEncoderSettings)MemberwiseClone();

    /// <summary>Throws an <see cref="ArgumentOutOfRangeException"/> when a setting is out of its range.</summary>
    public void Validate()
    {
        if (DictionarySize is < LzmaConstants.MinDictionarySize or > MaxDictionarySize)
            throw new ArgumentOutOfRangeException(paramName: null, "The dictionary size must be between 4 KiB and 1.5 GiB.");

        if (LiteralContextBits is < 0 or > 8)
            throw new ArgumentOutOfRangeException(paramName: null, "The number of literal context bits must be between 0 and 8.");

        if (LiteralPositionBits is < 0 or > 4)
            throw new ArgumentOutOfRangeException(paramName: null, "The number of literal position bits must be between 0 and 4.");

        if (PositionBits is < 0 or > 4)
            throw new ArgumentOutOfRangeException(paramName: null, "The number of position bits must be between 0 and 4.");

        if (NiceLength is < 5 or > LzmaConstants.MatchLenMax)
            throw new ArgumentOutOfRangeException(paramName: null, "The nice length must be between 5 and 273.");

        if (Depth < 0)
            throw new ArgumentOutOfRangeException(paramName: null, "The depth must not be negative.");

        if (!Enum.IsDefined(MatchFinder))
            throw new ArgumentOutOfRangeException(paramName: null, "The match finder is not supported.");

        if (!Enum.IsDefined(Mode))
            throw new ArgumentOutOfRangeException(paramName: null, "The mode is not supported.");
    }
}
