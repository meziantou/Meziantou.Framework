using System.Buffers.Binary;
using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Asf;

/// <summary>
/// Maps the attributes of an ASF file to <see cref="MediaTagInfo"/>, using the names Windows Media Player and MusicBrainz
/// Picard use.
/// </summary>
/// <remarks>
/// The title, the artist, the copyright and the comment are in the content description object; the other attributes are in
/// the extended content description object, or in the metadata library object when they are too large for it. Every text
/// attribute that no property represents is a custom field, and every other attribute is a native field.
/// </remarks>
internal static class AsfFields
{
    // Names used when a content description value is too long for its 16-bit length
    public const string Title = "Title";
    public const string Author = "Author";
    public const string Copyright = "Copyright";
    public const string Description = "Description";
    public const string Rating = "Rating";

    public const string Album = "WM/AlbumTitle";
    public const string AlbumArtist = "WM/AlbumArtist";
    public const string Genre = "WM/Genre";
    public const string Year = "WM/Year";
    public const string TrackNumber = "WM/TrackNumber";
    public const string ZeroBasedTrack = "WM/Track";
    public const string TrackTotal = "TotalTracks";
    public const string PartOfSet = "WM/PartOfSet";
    public const string Composer = "WM/Composer";
    public const string Conductor = "WM/Conductor";
    public const string Lyrics = "WM/Lyrics";
    public const string Isrc = "WM/ISRC";
    public const string Bpm = "WM/BeatsPerMinute";
    public const string IsCompilation = "WM/IsCompilation";
    public const string Picture = "WM/Picture";

    public const string MusicBrainzTrackId = "MusicBrainz/Track Id";
    public const string MusicBrainzArtistId = "MusicBrainz/Artist Id";
    public const string MusicBrainzAlbumId = "MusicBrainz/Album Id";
    public const string MusicBrainzReleaseGroupId = "MusicBrainz/Release Group Id";

    private static bool IsNumericType(AsfDataType type) => type is AsfDataType.String or AsfDataType.DWord or AsfDataType.QWord or AsfDataType.Word;

    /// <summary>Gets whether the reader reads an attribute into a property or a custom field, rather than into a native field.</summary>
    public static bool IsReadIntoProperty(string name, AsfDataType type)
    {
        if (type == AsfDataType.String)
            return true;

        if (Matches(name, Year) || Matches(name, TrackNumber) || Matches(name, ZeroBasedTrack) || Matches(name, TrackTotal) || Matches(name, Bpm))
            return IsNumericType(type);

        if (Matches(name, IsCompilation))
            return type is AsfDataType.Bool or AsfDataType.DWord;

        return Matches(name, Picture) && type == AsfDataType.Bytes;
    }

    /// <summary>Applies the file-level attributes. Fields already set are kept, so the first attribute of a name wins.</summary>
    public static void Apply(IEnumerable<AsfAttribute> attributes, MediaTagInfo tags, List<MediaNativeField> nativeFields)
    {
        int? zeroBasedTrack = null;
        foreach (var attribute in attributes)
        {
            // An attribute the writer refuses (a name too long, an unknown data type, a value of the wrong size) is
            // malformed. It is skipped, so the tags read can always be written back.
            if (!attribute.IsFileLevel || attribute.Name.Length > AsfAttributes.MaxNameLength || !HasValidValue(attribute.Type, attribute.Value.Length))
                continue;

            if (!IsReadIntoProperty(attribute.Name, attribute.Type))
            {
                nativeFields.Add(new MediaNativeField { TagType = MediaTagType.Asf, Id = attribute.Name, Data = attribute.Value.ToArray(), Flags = (int)attribute.Type });
                continue;
            }

            if (Matches(attribute.Name, Picture))
            {
                if (TryReadPicture(attribute.Value.Span, out var picture))
                    tags.Pictures.Add(picture);

                continue;
            }

            if (Matches(attribute.Name, ZeroBasedTrack))
            {
                if (AsfAttributes.TryReadNumber(attribute, out var track) && track < int.MaxValue)
                    zeroBasedTrack ??= (int)track + 1;

                continue;
            }

            if (attribute.Type != AsfDataType.String)
            {
                if (AsfAttributes.TryReadNumber(attribute, out var number))
                    ApplyNumber(attribute.Name, number, tags);

                continue;
            }

            // The writer does not write a custom field without a name, so the reader does not read one.
            var value = AsfAttributes.ReadString(attribute.Value.Span);
            if (value.Length > 0 && !TryApplyText(attribute.Name, value, tags) && attribute.Name.Length > 0)
                tags.CustomFields.TryAdd(attribute.Name, value);
        }

        tags.TrackNumber ??= zeroBasedTrack;
    }

    private static bool HasValidValue(AsfDataType type, int length)
    {
        if (type is < AsfDataType.String or > AsfDataType.Guid)
            return false;

        var expectedLength = AsfAttributes.GetValueLength(type);
        return expectedLength < 0 || length == expectedLength;
    }

    private static void ApplyNumber(string name, ulong number, MediaTagInfo tags)
    {
        if (Matches(name, IsCompilation))
        {
            tags.IsCompilation ??= number != 0;
            return;
        }

        if (number > int.MaxValue)
            return;

        var value = (int)number;
        if (Matches(name, Year))
        {
            if (value <= 9999)
                tags.Year ??= value;
        }
        else if (Matches(name, TrackNumber))
        {
            tags.TrackNumber ??= value;
        }
        else if (Matches(name, TrackTotal))
        {
            tags.TrackTotal ??= value;
        }
        else if (Matches(name, Bpm))
        {
            tags.Bpm ??= value;
        }
    }

    /// <returns><see langword="false"/> when the attribute is not one of the properties, or its value cannot be parsed, and it is a custom field.</returns>
    private static bool TryApplyText(string name, string value, MediaTagInfo tags)
    {
        if (Matches(name, Title)) { tags.Title ??= value; return true; }
        if (Matches(name, Author)) { tags.Artist ??= value; return true; }
        if (Matches(name, Copyright)) { tags.Copyright ??= value; return true; }
        if (Matches(name, Description)) { tags.Comment ??= value; return true; }
        if (Matches(name, Album)) { tags.Album ??= value; return true; }
        if (Matches(name, AlbumArtist)) { tags.AlbumArtist ??= value; return true; }
        if (Matches(name, Genre)) { tags.Genre ??= value; return true; }
        if (Matches(name, Composer)) { tags.Composer ??= value; return true; }
        if (Matches(name, Conductor)) { tags.Conductor ??= value; return true; }
        if (Matches(name, Lyrics)) { tags.Lyrics ??= value; return true; }
        if (Matches(name, Isrc)) { tags.Isrc ??= value; return true; }
        if (Matches(name, MusicBrainzTrackId)) { tags.MusicBrainzTrackId ??= value; return true; }
        if (Matches(name, MusicBrainzArtistId)) { tags.MusicBrainzArtistId ??= value; return true; }
        if (Matches(name, MusicBrainzAlbumId)) { tags.MusicBrainzAlbumId ??= value; return true; }
        if (Matches(name, MusicBrainzReleaseGroupId)) { tags.MusicBrainzReleaseGroupId ??= value; return true; }

        // ffmpeg writes the year as "date"
        if (Matches(name, Year) || Matches(name, "Date"))
        {
            if (value.Length < 4 || !int.TryParse(value.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year))
                return false;

            tags.Year ??= year;
            return true;
        }

        if (Matches(name, TrackNumber))
        {
            if (!TagFieldMapping.TryParseNumberPair(value, out var number, out var total))
                return false;

            if (tags.TrackNumber is null)
            {
                tags.TrackTotal ??= total;
                tags.TrackNumber = number;
            }

            return true;
        }

        if (Matches(name, PartOfSet))
        {
            if (!TagFieldMapping.TryParseNumberPair(value, out var number, out var total))
                return false;

            if (tags.DiscNumber is null && tags.DiscTotal is null)
            {
                tags.DiscTotal = total;
                tags.DiscNumber = TagFieldMapping.GetPosition(number, total);
            }

            return true;
        }

        if (Matches(name, TrackTotal) || Matches(name, Bpm))
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                return false;

            ApplyNumber(name, (ulong)number, tags);
            return true;
        }

        if (Matches(name, IsCompilation))
        {
            if (!bool.TryParse(value, out var isCompilation) && value is not ("0" or "1"))
                return false;

            tags.IsCompilation ??= isCompilation || value == "1";
            return true;
        }

        return TagFieldMapping.TryApplySharedField(name, value, tags);
    }

    /// <summary>Reads a WM/Picture value: the picture type, the size of the image, the MIME type, the description, and the image.</summary>
    private static bool TryReadPicture(ReadOnlySpan<byte> value, [NotNullWhen(true)] out MediaPicture? picture)
    {
        picture = null;
        if (value.Length < 5)
            return false;

        var pictureType = (MediaPictureType)value[0];
        var dataLength = BinaryPrimitives.ReadUInt32LittleEndian(value[1..]);
        var offset = 5;
        if (!TryReadTerminatedString(value, ref offset, out var mimeType) || !TryReadTerminatedString(value, ref offset, out var description))
            return false;

        if (dataLength > (uint)(value.Length - offset))
            return false;

        picture = new MediaPicture
        {
            PictureType = pictureType,
            MimeType = mimeType,
            Description = description,
            Data = value.Slice(offset, (int)dataLength).ToArray(),
        };
        return true;

        static bool TryReadTerminatedString(ReadOnlySpan<byte> data, ref int offset, out string text)
        {
            text = "";
            for (var end = offset; end + 1 < data.Length; end += 2)
            {
                if (data[end] == 0 && data[end + 1] == 0)
                {
                    text = Encoding.Unicode.GetString(data[offset..end]);
                    offset = end + 2;
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Builds the attributes to write.
    /// </summary>
    /// <param name="tags">The tags to write.</param>
    /// <param name="nativeFields">The native fields to write.</param>
    /// <param name="contentDescription">The title, author, copyright, description and rating, or <see langword="null"/> when they are all empty.</param>
    /// <param name="attributes">The attributes, in the order they are written.</param>
    /// <param name="nativeAttributes">The attributes of the native fields, in the order they are written.</param>
    /// <param name="error">Why the tags cannot be written.</param>
    /// <param name="propertyNames">Receives the names of the attributes the properties are written to, or <see langword="null"/>.</param>
    public static bool TryBuildAttributes(MediaTagInfo tags, IReadOnlyList<MediaNativeField> nativeFields, out string?[]? contentDescription, out List<AsfAttribute> attributes, out List<AsfAttribute> nativeAttributes, out string? error, HashSet<string>? propertyNames = null)
    {
        var result = new List<AsfAttribute>();
        attributes = result;
        nativeAttributes = [];
        error = null;
        contentDescription = null;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // The content description stores a value with a 16-bit length; a longer one is written as an attribute of the same name.
        var description = new string?[AsfAttributes.ContentDescriptionFieldCount];
        SetContentDescription(AsfAttributes.TitleIndex, Title, tags.Title);
        SetContentDescription(AsfAttributes.AuthorIndex, Author, tags.Artist);
        SetContentDescription(AsfAttributes.CopyrightIndex, Copyright, tags.Copyright);
        SetContentDescription(AsfAttributes.DescriptionIndex, Description, tags.Comment);
        if (Array.Exists(description, value => value is not null))
            contentDescription = description;

        AddString(Album, tags.Album);
        AddString(AlbumArtist, tags.AlbumArtist);
        AddString(Genre, tags.Genre);
        AddString(Year, tags.Year?.ToString("D4", CultureInfo.InvariantCulture));
        if (tags.TrackNumber is { } trackNumber && names.Add(TrackNumber))
            result.Add(AsfAttributes.CreateDWord(TrackNumber, (uint)trackNumber));

        if (tags.TrackTotal is { } trackTotal && names.Add(TrackTotal))
            result.Add(AsfAttributes.CreateDWord(TrackTotal, (uint)trackTotal));

        AddString(PartOfSet, TagFieldMapping.FormatNumberPair(tags.DiscNumber, tags.DiscTotal));
        AddString(Composer, tags.Composer);
        AddString(Conductor, tags.Conductor);
        AddString(Lyrics, tags.Lyrics);
        AddString(Isrc, tags.Isrc);
        AddString(Bpm, tags.Bpm?.ToString(CultureInfo.InvariantCulture));
        if (tags.IsCompilation is { } isCompilation && names.Add(IsCompilation))
            result.Add(AsfAttributes.CreateBool(IsCompilation, isCompilation));

        foreach (var (key, value) in TagFieldMapping.EnumerateReplayGainFields(tags))
        {
            AddString(key, value);
        }

        AddString(MusicBrainzTrackId, tags.MusicBrainzTrackId);
        AddString(MusicBrainzArtistId, tags.MusicBrainzArtistId);
        AddString(MusicBrainzAlbumId, tags.MusicBrainzAlbumId);
        AddString(MusicBrainzReleaseGroupId, tags.MusicBrainzReleaseGroupId);

        foreach (var picture in tags.Pictures)
        {
            if (picture.MimeType?.Contains('\0', StringComparison.Ordinal) is true || picture.Description?.Contains('\0', StringComparison.Ordinal) is true)
            {
                error = "A picture MIME type or description cannot contain a null character in an ASF file.";
                return false;
            }

            var mimeType = AsfAttributes.EncodeString(picture.MimeType ?? "image/jpeg");
            var pictureDescription = AsfAttributes.EncodeString(picture.Description ?? "");
            var value = new byte[5 + mimeType.Length + pictureDescription.Length + picture.Data.Length];
            value[0] = (byte)picture.PictureType;
            BinaryPrimitives.WriteInt32LittleEndian(value.AsSpan(1), picture.Data.Length);
            mimeType.CopyTo(value, 5);
            pictureDescription.CopyTo(value, 5 + mimeType.Length);
            picture.Data.CopyTo(value, 5 + mimeType.Length + pictureDescription.Length);
            result.Add(new AsfAttribute(Picture, AsfDataType.Bytes, value, 0, 0, default));
        }

        names.Add(Picture);
        propertyNames?.UnionWith(names);

        // A custom field named like an attribute written above is not written: the property wins.
        foreach (var (key, value) in tags.CustomFields)
        {
            if (key.Length > AsfAttributes.MaxNameLength)
            {
                error = $"A custom field name is too long for an ASF attribute ({key.Length} characters, the maximum is {AsfAttributes.MaxNameLength}).";
                return false;
            }

            if (key.Length > 0)
                AddString(key, value);
        }

        foreach (var field in nativeFields)
        {
            if (field.Id.Length > AsfAttributes.MaxNameLength)
            {
                error = $"A native field name is too long for an ASF attribute ({field.Id.Length} characters, the maximum is {AsfAttributes.MaxNameLength}).";
                return false;
            }

            var type = (AsfDataType)field.Flags;
            if (field.Flags is < (int)AsfDataType.Bytes or > (int)AsfDataType.Guid || IsReadIntoProperty(field.Id, type))
            {
                error = $"The native ASF attribute '{field.Id}' has the data type {field.Flags}: a native attribute is not text, and is not one a property of MediaTagInfo represents.";
                return false;
            }

            var length = AsfAttributes.GetValueLength(type);
            if ((length >= 0 && field.Data.Length != length) || (type == AsfDataType.Bool && field.Data.AsSpan(1).ContainsAnyExcept((byte)0)) || (type == AsfDataType.Bool && field.Data[0] > 1))
            {
                error = $"The value of the native ASF attribute '{field.Id}' does not match its data type {field.Flags}.";
                return false;
            }

            nativeAttributes.Add(new AsfAttribute(field.Id, type, field.Data, 0, 0, default));
        }

        return true;

        void SetContentDescription(int index, string name, string? value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            names.Add(name);
            if (Encoding.Unicode.GetByteCount(value) + 2 <= ushort.MaxValue)
            {
                description[index] = value;
            }
            else
            {
                result.Add(AsfAttributes.CreateString(name, value));
            }
        }

        void AddString(string name, string? value)
        {
            if (string.IsNullOrEmpty(value) || !names.Add(name))
                return;

            result.Add(AsfAttributes.CreateString(name, value));
        }
    }

    private static bool Matches(string name, string knownName) => string.Equals(name, knownName, StringComparison.OrdinalIgnoreCase);
}
