namespace Meziantou.Framework.MediaTags;

/// <summary>
/// Represents the metadata tags of a media file.
/// </summary>
public sealed class MediaTagInfo
{
    private const int MaxYear = 9999;

    // Core fields
    /// <summary>Gets or sets the title of the track.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the performing artist.</summary>
    public string? Artist { get; set; }

    /// <summary>Gets or sets the album name.</summary>
    public string? Album { get; set; }

    /// <summary>Gets or sets the album artist.</summary>
    public string? AlbumArtist { get; set; }

    /// <summary>Gets or sets the genre.</summary>
    public string? Genre { get; set; }

    /// <summary>Gets or sets the year of release. Must be between 0 and 9999.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or greater than 9999.</exception>
    public int? Year
    {
        get => field;
        set
        {
            if (value is { } year && year is < 0 or > MaxYear)
                throw new ArgumentOutOfRangeException(nameof(value), year, $"The value must be between 0 and {MaxYear}.");

            field = value;
        }
    }

    /// <summary>Gets or sets the track number. Must not be negative.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int? TrackNumber
    {
        get => field;
        set => field = ValidateNonNegative(value);
    }

    /// <summary>Gets or sets the total number of tracks. Must not be negative.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int? TrackTotal
    {
        get => field;
        set => field = ValidateNonNegative(value);
    }

    /// <summary>Gets or sets the disc number. Must not be negative.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int? DiscNumber
    {
        get => field;
        set => field = ValidateNonNegative(value);
    }

    /// <summary>Gets or sets the total number of discs. Must not be negative.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int? DiscTotal
    {
        get => field;
        set => field = ValidateNonNegative(value);
    }

    // Extended fields
    /// <summary>Gets or sets a comment or description.</summary>
    public string? Comment { get; set; }

    /// <summary>Gets or sets the track lyrics.</summary>
    public string? Lyrics { get; set; }

    /// <summary>Gets or sets the ISRC (International Standard Recording Code).</summary>
    public string? Isrc { get; set; }

    /// <summary>Gets or sets the composer.</summary>
    public string? Composer { get; set; }

    /// <summary>Gets or sets the conductor.</summary>
    public string? Conductor { get; set; }

    /// <summary>Gets or sets the copyright information.</summary>
    public string? Copyright { get; set; }

    /// <summary>Gets or sets the beats per minute. Must not be negative.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int? Bpm
    {
        get => field;
        set => field = ValidateNonNegative(value);
    }

    /// <summary>Gets or sets the media duration.</summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>Gets or sets whether this track is part of a compilation.</summary>
    public bool? IsCompilation { get; set; }

    /// <summary>Gets the list of embedded pictures (album art).</summary>
    public IList<MediaPicture> Pictures { get; } = new List<MediaPicture>();

    /// <summary>Gets or sets ReplayGain loudness normalization values.</summary>
    public ReplayGainInfo? ReplayGain { get; set; }

    // MusicBrainz
    /// <summary>Gets or sets the MusicBrainz recording ID.</summary>
    public string? MusicBrainzTrackId { get; set; }

    /// <summary>Gets or sets the MusicBrainz artist ID.</summary>
    public string? MusicBrainzArtistId { get; set; }

    /// <summary>Gets or sets the MusicBrainz album ID.</summary>
    public string? MusicBrainzAlbumId { get; set; }

    /// <summary>Gets or sets the MusicBrainz release group ID.</summary>
    public string? MusicBrainzReleaseGroupId { get; set; }

    /// <summary>Gets the collection of custom or format-specific metadata fields.</summary>
    public IDictionary<string, string> CustomFields { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets the source format that the tags were read from.</summary>
    public MediaFormat? Format { get; set; }

    private static int? ValidateNonNegative(int? value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(propertyName, value, "The value must not be negative.");

        return value;
    }
}
