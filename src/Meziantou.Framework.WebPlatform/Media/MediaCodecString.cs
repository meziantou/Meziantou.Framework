using System.Globalization;

namespace Meziantou.Framework.WebPlatform.Media;

/// <summary>The codecs of the WebCodecs codec registry (https://w3c.github.io/webcodecs/codec_registry.html).</summary>
public enum MediaCodec
{
    /// <summary>FLAC (<c>flac</c>).</summary>
    Flac,

    /// <summary>MP3 (<c>mp3</c>).</summary>
    Mp3,

    /// <summary>AAC (<c>mp4a.40.2</c>, <c>mp4a.40.5</c>, <c>mp4a.40.29</c>, <c>mp4a.67</c>...).</summary>
    Aac,

    /// <summary>Opus (<c>opus</c>).</summary>
    Opus,

    /// <summary>Vorbis (<c>vorbis</c>).</summary>
    Vorbis,

    /// <summary>G.711 µ-law (<c>ulaw</c>).</summary>
    Ulaw,

    /// <summary>G.711 A-law (<c>alaw</c>).</summary>
    Alaw,

    /// <summary>Linear PCM, unsigned 8-bit (<c>pcm-u8</c>).</summary>
    PcmU8,

    /// <summary>Linear PCM, signed 16-bit little-endian (<c>pcm-s16</c>).</summary>
    PcmS16,

    /// <summary>Linear PCM, signed 24-bit little-endian (<c>pcm-s24</c>).</summary>
    PcmS24,

    /// <summary>Linear PCM, signed 32-bit little-endian (<c>pcm-s32</c>).</summary>
    PcmS32,

    /// <summary>Linear PCM, 32-bit float little-endian (<c>pcm-f32</c>).</summary>
    PcmF32,

    /// <summary>AV1 (<c>av01.P.LLT.DD</c>).</summary>
    Av1,

    /// <summary>AVC / H.264 (<c>avc1.PPCCLL</c>, <c>avc3.PPCCLL</c>).</summary>
    Avc,

    /// <summary>HEVC / H.265 (<c>hev1...</c>, <c>hvc1...</c>).</summary>
    Hevc,

    /// <summary>VP8 (<c>vp8</c>).</summary>
    Vp8,

    /// <summary>VP9 (<c>vp09.PP.LL.DD</c>).</summary>
    Vp9,
}

/// <summary>
/// A codec string of the WebCodecs codec registry (https://w3c.github.io/webcodecs/codec_registry.html), parsed by
/// <see cref="TryParse"/>: the codec and, for the video codecs, the profile, the level and the bit depth. The strings are
/// case-sensitive and must be exact: an ambiguous string (e.g. <c>vp9</c>), a string with spaces, or a string whose
/// profile or level is not defined (e.g. <c>vp09.99.99.08</c>) is rejected. A <see cref="MediaCodecBackend"/> can use it to
/// answer the support of the configurations.
/// </summary>
public sealed class MediaCodecString
{
    private static readonly int[] Vp9Levels = [10, 11, 20, 21, 30, 31, 40, 41, 50, 51, 52, 60, 61, 62];
    private static readonly int[] AvcProfiles = [0x42, 0x4D, 0x58, 0x64, 0x6E, 0x7A, 0xF4, 0x2C, 0x53, 0x56, 0x76, 0x80, 0x8A, 0x8B];

    private MediaCodecString(string value, MediaCodec codec, int profile = 0, int level = 0, int bitDepth = 8, bool isHighTier = false)
    {
        Value = value;
        Codec = codec;
        Profile = profile;
        Level = level;
        BitDepth = bitDepth;
        IsHighTier = isHighTier;
    }

    /// <summary>Gets the codec string.</summary>
    public string Value { get; }

    /// <summary>Gets the codec.</summary>
    public MediaCodec Codec { get; }

    /// <summary>Gets a value indicating whether the codec is an audio codec.</summary>
    public bool IsAudio => Codec is not (MediaCodec.Av1 or MediaCodec.Avc or MediaCodec.Hevc or MediaCodec.Vp8 or MediaCodec.Vp9);

    /// <summary>Gets the profile: the <c>seq_profile</c> of AV1, the <c>profile_idc</c> of AVC, the <c>general_profile_idc</c> of HEVC, the profile of VP9, the object type of AAC; 0 otherwise.</summary>
    public int Profile { get; }

    /// <summary>Gets the level: the <c>seq_level_idx</c> of AV1, the <c>level_idc</c> of AVC and HEVC, the level of VP9 (e.g. 10 for 1.0); 0 otherwise.</summary>
    public int Level { get; }

    /// <summary>Gets the bit depth of the samples of a video codec (8 when the string does not give it).</summary>
    public int BitDepth { get; }

    /// <summary>Gets a value indicating whether the string has the high tier of AV1 or HEVC.</summary>
    public bool IsHighTier { get; }

    /// <inheritdoc/>
    public override string ToString() => Value;

    /// <summary>Parses a codec string of the registry.</summary>
    /// <param name="value">The codec string.</param>
    /// <param name="result">The parsed codec string, or null.</param>
    /// <returns>Whether the string is a valid codec string of the registry.</returns>
    public static bool TryParse(string? value, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out MediaCodecString? result)
    {
        result = value is null || value.Length > 128 ? null : Parse(value);
        return result is not null;
    }

    private static MediaCodecString? Parse(string value)
    {
        switch (value)
        {
            case "flac": return new(value, MediaCodec.Flac);
            case "mp3": return new(value, MediaCodec.Mp3);
            case "opus": return new(value, MediaCodec.Opus);
            case "vorbis": return new(value, MediaCodec.Vorbis);
            case "ulaw": return new(value, MediaCodec.Ulaw);
            case "alaw": return new(value, MediaCodec.Alaw);
            case "pcm-u8": return new(value, MediaCodec.PcmU8);
            case "pcm-s16": return new(value, MediaCodec.PcmS16);
            case "pcm-s24": return new(value, MediaCodec.PcmS24);
            case "pcm-s32": return new(value, MediaCodec.PcmS32);
            case "pcm-f32": return new(value, MediaCodec.PcmF32);
            case "vp8": return new(value, MediaCodec.Vp8);
            case "mp4a.40.2" or "mp4a.40.02": return new(value, MediaCodec.Aac, profile: 2);
            case "mp4a.40.5" or "mp4a.40.05": return new(value, MediaCodec.Aac, profile: 5);
            case "mp4a.40.29": return new(value, MediaCodec.Aac, profile: 29);
            case "mp4a.67": return new(value, MediaCodec.Aac, profile: 0x67);
        }

        var parts = value.Split('.');
        return parts[0] switch
        {
            "vp09" => ParseVp9(value, parts),
            "av01" => ParseAv1(value, parts),
            "avc1" or "avc3" => ParseAvc(value, parts),
            "hev1" or "hvc1" => ParseHevc(value, parts),
            _ => null,
        };
    }

    /// <summary>https://www.webmproject.org/vp9/mp4/#codecs-parameter-string: vp09.PP.LL.DD[.CC.cp.tc.mc.FF]</summary>
    private static MediaCodecString? ParseVp9(string value, string[] parts)
    {
        if (parts.Length is not (4 or 5 or 6 or 7 or 8 or 9))
            return null;

        if (!TryParseDecimal(parts[1], 2, out var profile) || profile > 3)
            return null;

        if (!TryParseDecimal(parts[2], 2, out var level) || Array.IndexOf(Vp9Levels, level) < 0)
            return null;

        if (!TryParseDecimal(parts[3], 2, out var bitDepth) || bitDepth is not (8 or 10 or 12))
            return null;

        // Profiles 0 and 1 are 8-bit, profiles 2 and 3 are 10 or 12-bit
        if (profile < 2 != (bitDepth == 8))
            return null;

        for (var i = 4; i < parts.Length; i++)
        {
            if (!TryParseDecimal(parts[i], 2, out _))
                return null;
        }

        return new MediaCodecString(value, MediaCodec.Vp9, profile, level, bitDepth);
    }

    /// <summary>https://aomediacodec.github.io/av1-isobmff/#codecsparam: av01.P.LLT.DD[.M.CCC.cp.tc.mc.F]</summary>
    private static MediaCodecString? ParseAv1(string value, string[] parts)
    {
        if (parts.Length is not (4 or 10))
            return null;

        if (parts[1].Length != 1 || !TryParseDecimal(parts[1], 1, out var profile) || profile > 2)
            return null;

        if (parts[2].Length != 3 || parts[2][2] is not ('M' or 'H') || !TryParseDecimal(parts[2][..2], 2, out var level) || level is > 23 and not 31)
            return null;

        if (!TryParseDecimal(parts[3], 2, out var bitDepth) || bitDepth is not (8 or 10 or 12))
            return null;

        if (parts.Length == 10)
        {
            if (parts[4] is not ("0" or "1") || parts[5].Length != 3 || parts[5].Any(c => c is not ('0' or '1')) || parts[9] is not ("0" or "1"))
                return null;

            for (var i = 6; i < 9; i++)
            {
                if (!TryParseDecimal(parts[i], 2, out _))
                    return null;
            }
        }

        return new MediaCodecString(value, MediaCodec.Av1, profile, level, bitDepth, isHighTier: parts[2][2] == 'H');
    }

    /// <summary>https://datatracker.ietf.org/doc/html/rfc6381#section-3.3: avc1.PPCCLL</summary>
    private static MediaCodecString? ParseAvc(string value, string[] parts)
    {
        if (parts.Length != 2 || parts[1].Length != 6 || !int.TryParse(parts[1], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var hex))
            return null;

        var profile = hex >> 16;
        var level = hex & 0xFF;
        if (Array.IndexOf(AvcProfiles, profile) < 0 || level is 0 or > 62)
            return null;

        return new MediaCodecString(value, MediaCodec.Avc, profile, level, bitDepth: profile is 0x6E or 0x7A or 0xF4 ? 10 : 8);
    }

    /// <summary>ISO/IEC 14496-15, E.3: hvc1.[A-C]?P.F.(L|H)LL[.CC...]</summary>
    private static MediaCodecString? ParseHevc(string value, string[] parts)
    {
        if (parts.Length is < 4 or > 10)
            return null;

        var profileText = parts[1];
        if (profileText.Length > 0 && profileText[0] is 'A' or 'B' or 'C')
            profileText = profileText[1..];

        if (!TryParseDecimal(profileText, 0, out var profile) || profile is < 1 or > 11)
            return null;

        if (parts[2].Length is 0 or > 8 || !uint.TryParse(parts[2], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _))
            return null;

        if (parts[3].Length < 2 || parts[3][0] is not ('L' or 'H') || !TryParseDecimal(parts[3][1..], 0, out var level) || level is 0 or > 255)
            return null;

        for (var i = 4; i < parts.Length; i++)
        {
            if (parts[i].Length is 0 or > 2 || !byte.TryParse(parts[i], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _))
                return null;
        }

        return new MediaCodecString(value, MediaCodec.Hevc, profile, level, bitDepth: profile == 2 ? 10 : 8, isHighTier: parts[3][0] == 'H');
    }

    /// <summary>Parses ASCII digits (exactly <paramref name="length"/> of them when it is not 0).</summary>
    private static bool TryParseDecimal(string text, int length, out int value)
    {
        value = 0;
        if (text.Length == 0 || text.Length > 9 || (length != 0 && text.Length != length))
            return false;

        foreach (var c in text)
        {
            if (!char.IsAsciiDigit(c))
                return false;

            value = (value * 10) + (c - '0');
        }

        return true;
    }
}
