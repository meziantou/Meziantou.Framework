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

    // Video (MP4)
    /// <summary>Gets or sets the kind of media, such as a movie or a TV show. Only MP4 files store it.</summary>
    public MediaKind? MediaKind { get; set; }

    /// <summary>Gets or sets a short description of the content. Only MP4 files store it.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets a long description of the content. Only MP4 files store it.</summary>
    public string? LongDescription { get; set; }

    /// <summary>Gets or sets the name of the TV show. Only MP4 files store it.</summary>
    public string? TvShow { get; set; }

    /// <summary>Gets or sets the TV network that broadcast the episode. Only MP4 files store it.</summary>
    public string? TvNetwork { get; set; }

    /// <summary>Gets or sets the identifier of the TV episode, such as <c>S01E02</c>. Only MP4 files store it.</summary>
    public string? TvEpisodeId { get; set; }

    /// <summary>Gets or sets the TV season number. Must not be negative. Only MP4 files store it.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int? TvSeason
    {
        get => field;
        set => field = ValidateNonNegative(value);
    }

    /// <summary>Gets or sets the TV episode number. Must not be negative. Only MP4 files store it.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int? TvEpisode
    {
        get => field;
        set => field = ValidateNonNegative(value);
    }

    /// <summary>Gets or sets the width, in pixels, of the video stream. It is read from the stream and never written.</summary>
    public int? VideoWidth { get; set; }

    /// <summary>Gets or sets the height, in pixels, of the video stream. It is read from the stream and never written.</summary>
    public int? VideoHeight { get; set; }

    /// <summary>Gets the collection of custom or format-specific metadata fields.</summary>
    public IDictionary<string, string> CustomFields { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the frames, items and attributes of the tag that no other property represents, as stored in the file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reading sets a list, empty when the format has none. Writing a list replaces the fields of the tag type the format writes,
    /// and ignores the fields of other tag types. Writing <see langword="null"/> keeps the fields of the existing file, which is
    /// how a new <see cref="MediaTagInfo"/> changes the common fields without removing the format-specific ones.
    /// </para>
    /// <para>
    /// A field that a property represents, such as the ID3v2 <c>TIT2</c> frame, cannot be written as a native field: the write
    /// fails with <see cref="MediaTagError.InvalidTagData"/>. User-defined text fields (ID3v2 <c>TXXX</c> frames, MP4 freeform
    /// items, APEv2 text items and ASF text attributes) are in <see cref="CustomFields"/>.
    /// </para>
    /// </remarks>
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "null keeps the fields of the file")]
    public IList<MediaNativeField>? NativeFields { get; set; }

    /// <summary>Gets or sets the source format that the tags were read from.</summary>
    public MediaFormat? Format { get; set; }

    /// <summary>
    /// Gets or sets the duration a reader computed from the audio stream rather than read from a tag.
    /// </summary>
    /// <remarks>
    /// A writer must not store this value as a tag: for a variable bit rate MP3 it is only an estimate, and once
    /// written it would take precedence over the audio on every later read. A duration the caller set differs
    /// from it, and is written.
    /// </remarks>
    internal TimeSpan? DurationFromAudio { get; set; }

    private static int? ValidateNonNegative(int? value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(propertyName, value, "The value must not be negative.");

        return value;
    }
}
