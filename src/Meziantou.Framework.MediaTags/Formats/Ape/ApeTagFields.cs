using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Ape;

/// <summary>
/// Maps the items of an APE tag to <see cref="MediaTagInfo"/>, using the keys of the APEv2 specification and foobar2000.
/// </summary>
/// <remarks>
/// Keys are case-insensitive. Text items are UTF-8, and a picture is a binary item named after its type, such as
/// <c>Cover Art (Front)</c>, whose value is a file name, a null character and the image.
/// </remarks>
internal static class ApeTagFields
{
    public const string Title = "Title";
    public const string Artist = "Artist";
    public const string Album = "Album";
    public const string AlbumArtist = "Album Artist";
    public const string Year = "Year";
    public const string Track = "Track";
    public const string Disc = "Disc";
    public const string Genre = "Genre";
    public const string Comment = "Comment";
    public const string Lyrics = "Lyrics";
    public const string Composer = "Composer";
    public const string Conductor = "Conductor";
    public const string Copyright = "Copyright";
    public const string Isrc = "ISRC";
    public const string Bpm = "BPM";
    public const string Compilation = "Compilation";

    private const string CoverArtPrefix = "Cover Art (";

    /// <summary>The keys of the pictures, indexed by <see cref="MediaPictureType"/>.</summary>
    private static readonly string[] PictureKeys =
    [
        "Cover Art (Other)", "Cover Art (Icon)", "Cover Art (Other Icon)", "Cover Art (Front)", "Cover Art (Back)", "Cover Art (Leaflet)",
        "Cover Art (Media)", "Cover Art (Lead Artist)", "Cover Art (Artist)", "Cover Art (Conductor)", "Cover Art (Band)", "Cover Art (Composer)",
        "Cover Art (Lyricist)", "Cover Art (Recording Location)", "Cover Art (During Recording)", "Cover Art (During Performance)",
        "Cover Art (Video Capture)", "Cover Art (Fish)", "Cover Art (Illustration)", "Cover Art (Band Logotype)", "Cover Art (Publisher Logotype)",
    ];

    /// <summary>Applies the items of a tag. Fields already set are kept, so a tag read first takes precedence.</summary>
    /// <param name="items">The items.</param>
    /// <param name="tags">The tags to fill.</param>
    /// <param name="nativeFields">Receives the items no property represents, or <see langword="null"/> to ignore them.</param>
    public static void Apply(List<ApeItem> items, MediaTagInfo tags, List<MediaNativeField>? nativeFields)
    {
        var addPictures = tags.Pictures.Count == 0;
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            // Keys are unique; a tag that repeats one is malformed, and the first occurrence wins as for the other formats.
            if (!seenKeys.Add(item.Key))
                continue;

            if (item.ItemType == ApeTag.TextItemType)
            {
                var value = Encoding.UTF8.GetString(item.Value.Span).TrimEnd('\0');
                if (value.Length > 0 && !TryApplyTextField(item.Key, value, tags))
                {
                    tags.CustomFields.TryAdd(item.Key, value);
                }
            }
            else if (item.ItemType == ApeTag.BinaryItemType && TryGetPictureType(item.Key, out var pictureType))
            {
                if (addPictures)
                    tags.Pictures.Add(ReadPicture(pictureType, item.Value.Span));
            }
            else if (IsNativeItem(item.Key, item.Flags))
            {
                nativeFields?.Add(new MediaNativeField { TagType = MediaTagType.Ape, Id = item.Key, Data = item.Value.ToArray(), Flags = item.Flags & ApeTag.ItemFlagsMask });
            }
        }
    }

    /// <summary>Gets whether the reader reads an item into a native field rather than into a property.</summary>
    /// <remarks>
    /// Text items are properties or custom fields, and binary items named after a picture type are pictures. Items of the
    /// reserved type (6) have no defined meaning: they are not read, and a write removes them.
    /// </remarks>
    public static bool IsNativeItem(string key, int flags)
    {
        var itemType = flags & ApeTag.ItemTypeMask;
        return itemType == ApeTag.LinkItemType || (itemType == ApeTag.BinaryItemType && !TryGetPictureType(key, out _));
    }

    private static string? GetTextField(string key)
    {
        foreach (var candidate in (ReadOnlySpan<string>)[Title, Artist, Album, AlbumArtist, Year, Track, Disc, Genre, Comment, Lyrics, Composer, Conductor, Copyright, Isrc, Bpm, Compilation])
        {
            if (key.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        // Spellings other taggers use
        if (key.Equals("AlbumArtist", StringComparison.OrdinalIgnoreCase) || key.Equals("Album_Artist", StringComparison.OrdinalIgnoreCase))
            return AlbumArtist;

        if (key.Equals("Date", StringComparison.OrdinalIgnoreCase))
            return Year;

        if (key.Equals("Unsynced Lyrics", StringComparison.OrdinalIgnoreCase))
            return Lyrics;

        return null;
    }

    private static bool TryApplyTextField(string key, string value, MediaTagInfo tags)
    {
        switch (GetTextField(key))
        {
            case Title: tags.Title ??= value; return true;
            case Artist: tags.Artist ??= value; return true;
            case Album: tags.Album ??= value; return true;
            case AlbumArtist: tags.AlbumArtist ??= value; return true;
            case Genre: tags.Genre ??= value; return true;
            case Comment: tags.Comment ??= value; return true;
            case Lyrics: tags.Lyrics ??= value; return true;
            case Composer: tags.Composer ??= value; return true;
            case Conductor: tags.Conductor ??= value; return true;
            case Copyright: tags.Copyright ??= value; return true;
            case Isrc: tags.Isrc ??= value; return true;

            // A value that cannot be parsed is kept as a custom field, so writing the tags back does not lose it.
            case Year:
                if (value.Length < 4 || !int.TryParse(value.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year))
                    return false;

                tags.Year ??= year;
                return true;

            case Track:
                if (!TagFieldMapping.TryParseNumberPair(value, out var trackNumber, out var trackTotal))
                    return false;

                // Each part fills what a tag read first does not set, such as the total of an ID3v2 TRCK frame without one.
                tags.TrackTotal ??= trackTotal;
                tags.TrackNumber ??= TagFieldMapping.GetPosition(trackNumber, tags.TrackTotal);

                return true;

            case Disc:
                if (!TagFieldMapping.TryParseNumberPair(value, out var discNumber, out var discTotal))
                    return false;

                tags.DiscTotal ??= discTotal;
                tags.DiscNumber ??= TagFieldMapping.GetPosition(discNumber, tags.DiscTotal);

                return true;

            case Bpm:
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var bpm))
                    return false;

                tags.Bpm ??= bpm;
                return true;

            case Compilation:
                if (value is not ("0" or "1"))
                    return false;

                tags.IsCompilation ??= value == "1";
                return true;

            default:
                return TryApplySharedField(key, value, tags);
        }
    }

    private static bool TryApplySharedField(string key, string value, MediaTagInfo tags)
    {
        // A tag read after another one only fills what is missing, and the shared fields overwrite.
        if (key.StartsWith("REPLAYGAIN_", StringComparison.OrdinalIgnoreCase) && tags.ReplayGain is not null)
        {
            var copy = new MediaTagInfo { ReplayGain = null };
            if (!TagFieldMapping.TryApplySharedField(key, value, copy))
                return false;

            tags.ReplayGain = MergeReplayGain(tags.ReplayGain.Value, copy.ReplayGain);
            return true;
        }

        return TagFieldMapping.TryApplySharedField(key, value, tags);

        static ReplayGainInfo MergeReplayGain(ReplayGainInfo existing, ReplayGainInfo? added) => added is not { } value ? existing : new ReplayGainInfo
        {
            TrackGain = existing.TrackGain ?? value.TrackGain,
            TrackPeak = existing.TrackPeak ?? value.TrackPeak,
            AlbumGain = existing.AlbumGain ?? value.AlbumGain,
            AlbumPeak = existing.AlbumPeak ?? value.AlbumPeak,
        };
    }

    private static bool TryGetPictureType(string key, out MediaPictureType pictureType)
    {
        pictureType = default;
        if (!key.StartsWith(CoverArtPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        for (var i = 0; i < PictureKeys.Length; i++)
        {
            if (key.Equals(PictureKeys[i], StringComparison.OrdinalIgnoreCase))
            {
                pictureType = (MediaPictureType)i;
                return true;
            }
        }

        return false;
    }

    /// <summary>Gets the key of the item a picture is stored in. A type the format has no key for is stored as "Other".</summary>
    public static string GetPictureKey(MediaPictureType pictureType) => (int)pictureType < PictureKeys.Length ? PictureKeys[(int)pictureType] : PictureKeys[(int)MediaPictureType.Other];

    private static MediaPicture ReadPicture(MediaPictureType pictureType, ReadOnlySpan<byte> value)
    {
        // The value starts with a file name, which is the closest thing the format has to a description.
        var separator = value.IndexOf((byte)0);
        var description = separator < 0 ? "" : Encoding.UTF8.GetString(value[..separator]);
        var data = separator < 0 ? value : value[(separator + 1)..];
        return new MediaPicture
        {
            PictureType = pictureType,
            Description = description,
            MimeType = ImageMimeType.Detect(data),
            Data = data.ToArray(),
        };
    }

    /// <summary>
    /// Builds the items of a tag.
    /// </summary>
    /// <param name="tags">The tags to write.</param>
    /// <param name="nativeFields">The native fields to write.</param>
    /// <param name="items">The items.</param>
    /// <param name="error">Why the tags cannot be written.</param>
    /// <param name="propertiesOnly">Whether to only build the items of the properties and the pictures, without the custom and native fields.</param>
    public static bool TryBuildItems(MediaTagInfo tags, IReadOnlyList<MediaNativeField> nativeFields, [NotNullWhen(true)] out List<ApeItem>? items, out string? error, bool propertiesOnly = false)
    {
        var result = new List<ApeItem>();
        items = result;
        error = null;
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddText(Title, tags.Title);
        AddText(Artist, tags.Artist);
        AddText(Album, tags.Album);
        AddText(AlbumArtist, tags.AlbumArtist);
        AddText(Year, tags.Year?.ToString("D4", CultureInfo.InvariantCulture));
        AddText(Track, TagFieldMapping.FormatNumberPair(tags.TrackNumber, tags.TrackTotal));
        AddText(Disc, TagFieldMapping.FormatNumberPair(tags.DiscNumber, tags.DiscTotal));
        AddText(Genre, tags.Genre);
        AddText(Comment, tags.Comment);
        AddText(Lyrics, tags.Lyrics);
        AddText(Composer, tags.Composer);
        AddText(Conductor, tags.Conductor);
        AddText(Copyright, tags.Copyright);
        AddText(Isrc, tags.Isrc);
        AddText(Bpm, tags.Bpm?.ToString(CultureInfo.InvariantCulture));
        AddText(Compilation, tags.IsCompilation switch { null => null, true => "1", false => "0" });

        foreach (var (key, value) in TagFieldMapping.EnumerateReplayGainFields(tags))
        {
            AddText(key, value);
        }

        foreach (var (key, value) in TagFieldMapping.EnumerateMusicBrainzFields(tags, useVorbisNames: true))
        {
            AddText(key, value);
        }

        foreach (var picture in tags.Pictures)
        {
            if (picture.Description?.Contains('\0', StringComparison.Ordinal) is true)
            {
                error = "A picture description cannot contain a null character in an APE tag.";
                return false;
            }

            var key = GetPictureKey(picture.PictureType);
            if (!keys.Add(key))
            {
                error = $"An APE tag stores one picture per type, and there are several '{key}' pictures.";
                return false;
            }

            var description = Encoding.UTF8.GetBytes(picture.Description ?? "");
            var value = new byte[description.Length + 1 + picture.Data.Length];
            description.CopyTo(value, 0);
            picture.Data.CopyTo(value, description.Length + 1);
            items.Add(new ApeItem(key, ApeTag.BinaryItemType, value));
        }

        if (propertiesOnly)
            return true;

        // A custom field or a native field named like a field written above is not written: the property wins.
        var propertyKeys = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in tags.CustomFields)
        {
            if (!ApeTag.IsValidKey(key))
            {
                error = $"'{key}' is not a valid APE item key: a key has 2 to 255 printable ASCII characters, and cannot be ID3, TAG, OggS or MP+.";
                return false;
            }

            if (!keys.Contains(key))
                AddText(key, value);
        }

        foreach (var field in nativeFields)
        {
            if (!ApeTag.IsValidKey(field.Id))
            {
                error = $"'{field.Id}' is not a valid APE item key.";
                return false;
            }

            if ((field.Flags & ~ApeTag.ItemFlagsMask) != 0 || !IsNativeItem(field.Id, field.Flags))
            {
                error = $"The native APE item '{field.Id}' has the flags {field.Flags}: a native item is a binary item (2) or an external link (4), optionally read-only (1), and a binary item named after a picture type is a picture.";
                return false;
            }

            if (propertyKeys.Contains(field.Id))
                continue;

            if (!keys.Add(field.Id))
            {
                error = $"The APE tag holds several '{field.Id}' items.";
                return false;
            }

            items.Add(new ApeItem(field.Id, field.Flags, field.Data));
        }

        return true;

        void AddText(string key, string? value)
        {
            if (string.IsNullOrEmpty(value) || !keys.Add(key))
                return;

            result.Add(new ApeItem(key, ApeTag.TextItemType, Encoding.UTF8.GetBytes(value)));
        }
    }
}
