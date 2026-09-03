namespace Meziantou.Framework.MediaTags.Internals;

/// <summary>
/// The ReplayGain and MusicBrainz fields shared by every key/value tag format (Vorbis Comments, ID3v2 TXXX
/// frames and MP4 freeform atoms).
/// </summary>
/// <remarks>
/// These used to be matched, parsed and formatted independently in each reader and writer, and the copies had
/// drifted apart. Every format now goes through this type so a parsing fix applies to all of them.
/// </remarks>
internal static class TagFieldMapping
{
    public const string ReplayGainTrackGain = "REPLAYGAIN_TRACK_GAIN";
    public const string ReplayGainTrackPeak = "REPLAYGAIN_TRACK_PEAK";
    public const string ReplayGainAlbumGain = "REPLAYGAIN_ALBUM_GAIN";
    public const string ReplayGainAlbumPeak = "REPLAYGAIN_ALBUM_PEAK";

    // Vorbis Comments spell the MusicBrainz identifiers differently from ID3v2 and MP4, which share the
    // iTunes spelling. Both spellings are accepted on read so a tag written by any tool is understood.
    public const string MusicBrainzTrackIdVorbis = "MUSICBRAINZ_TRACKID";
    public const string MusicBrainzArtistIdVorbis = "MUSICBRAINZ_ARTISTID";
    public const string MusicBrainzAlbumIdVorbis = "MUSICBRAINZ_ALBUMID";
    public const string MusicBrainzReleaseGroupIdVorbis = "MUSICBRAINZ_RELEASEGROUPID";

    public const string MusicBrainzTrackIdItunes = "MusicBrainz Track Id";
    public const string MusicBrainzArtistIdItunes = "MusicBrainz Artist Id";
    public const string MusicBrainzAlbumIdItunes = "MusicBrainz Album Id";
    public const string MusicBrainzReleaseGroupIdItunes = "MusicBrainz Release Group Id";

    /// <summary>
    /// Applies a field that every key/value format stores under the same name.
    /// </summary>
    /// <returns><see langword="false"/> when the name is not one of them, so the caller can store it as a custom field.</returns>
    public static bool TryApplySharedField(string fieldName, string value, MediaTagInfo tags)
    {
        if (Matches(fieldName, ReplayGainTrackGain))
        {
            if (TryParseGain(value, out var gain))
                tags.ReplayGain = (tags.ReplayGain ?? default) with { TrackGain = gain };

            return true;
        }

        if (Matches(fieldName, ReplayGainTrackPeak))
        {
            if (TryParsePeak(value, out var peak))
                tags.ReplayGain = (tags.ReplayGain ?? default) with { TrackPeak = peak };

            return true;
        }

        if (Matches(fieldName, ReplayGainAlbumGain))
        {
            if (TryParseGain(value, out var gain))
                tags.ReplayGain = (tags.ReplayGain ?? default) with { AlbumGain = gain };

            return true;
        }

        if (Matches(fieldName, ReplayGainAlbumPeak))
        {
            if (TryParsePeak(value, out var peak))
                tags.ReplayGain = (tags.ReplayGain ?? default) with { AlbumPeak = peak };

            return true;
        }

        if (Matches(fieldName, MusicBrainzTrackIdVorbis) || Matches(fieldName, MusicBrainzTrackIdItunes))
        {
            tags.MusicBrainzTrackId ??= value;
            return true;
        }

        if (Matches(fieldName, MusicBrainzArtistIdVorbis) || Matches(fieldName, MusicBrainzArtistIdItunes))
        {
            tags.MusicBrainzArtistId ??= value;
            return true;
        }

        if (Matches(fieldName, MusicBrainzAlbumIdVorbis) || Matches(fieldName, MusicBrainzAlbumIdItunes))
        {
            tags.MusicBrainzAlbumId ??= value;
            return true;
        }

        if (Matches(fieldName, MusicBrainzReleaseGroupIdVorbis) || Matches(fieldName, MusicBrainzReleaseGroupIdItunes))
        {
            tags.MusicBrainzReleaseGroupId ??= value;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Yields the ReplayGain fields to write, using the names and formats every format shares.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string>> EnumerateReplayGainFields(MediaTagInfo tags)
    {
        if (tags.ReplayGain is not { } replayGain)
            yield break;

        if (replayGain.TrackGain is { } trackGain)
            yield return new KeyValuePair<string, string>(ReplayGainTrackGain, FormatGain(trackGain));

        if (replayGain.TrackPeak is { } trackPeak)
            yield return new KeyValuePair<string, string>(ReplayGainTrackPeak, FormatPeak(trackPeak));

        if (replayGain.AlbumGain is { } albumGain)
            yield return new KeyValuePair<string, string>(ReplayGainAlbumGain, FormatGain(albumGain));

        if (replayGain.AlbumPeak is { } albumPeak)
            yield return new KeyValuePair<string, string>(ReplayGainAlbumPeak, FormatPeak(albumPeak));
    }

    /// <summary>
    /// Yields the MusicBrainz identifiers to write, under the names the given format uses.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string>> EnumerateMusicBrainzFields(MediaTagInfo tags, bool useVorbisNames)
    {
        if (tags.MusicBrainzTrackId is { } trackId)
            yield return new KeyValuePair<string, string>(useVorbisNames ? MusicBrainzTrackIdVorbis : MusicBrainzTrackIdItunes, trackId);

        if (tags.MusicBrainzArtistId is { } artistId)
            yield return new KeyValuePair<string, string>(useVorbisNames ? MusicBrainzArtistIdVorbis : MusicBrainzArtistIdItunes, artistId);

        if (tags.MusicBrainzAlbumId is { } albumId)
            yield return new KeyValuePair<string, string>(useVorbisNames ? MusicBrainzAlbumIdVorbis : MusicBrainzAlbumIdItunes, albumId);

        if (tags.MusicBrainzReleaseGroupId is { } releaseGroupId)
            yield return new KeyValuePair<string, string>(useVorbisNames ? MusicBrainzReleaseGroupIdVorbis : MusicBrainzReleaseGroupIdItunes, releaseGroupId);
    }

    public static string FormatGain(double value) => value.ToString("F2", CultureInfo.InvariantCulture) + " dB";

    public static string FormatPeak(double value) => value.ToString("F6", CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a ReplayGain gain value such as <c>-3.21 dB</c>.
    /// </summary>
    public static bool TryParseGain(string value, out double result)
    {
        var trimmed = value.AsSpan().Trim();
        if (trimmed.EndsWith(" dB", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^3].Trim();

        return TryParseDouble(trimmed, out result);
    }

    /// <summary>
    /// Parses a ReplayGain peak value.
    /// </summary>
    public static bool TryParsePeak(string value, out double result) => TryParseDouble(value.AsSpan().Trim(), out result);

    private static bool TryParseDouble(ReadOnlySpan<char> value, out double result)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
            return true;

        // Some taggers write the value with the decimal separator of the machine that produced it.
        if (value.IndexOf(',') < 0)
            return false;

        return double.TryParse(value.ToString().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    private static bool Matches(string fieldName, string knownName) => string.Equals(fieldName, knownName, StringComparison.OrdinalIgnoreCase);
}
