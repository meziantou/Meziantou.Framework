using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Meziantou.Framework.MediaTags.Formats.VorbisComment;

internal static class VorbisCommentWriter
{
    private const string VendorString = "Meziantou.Framework.MediaTags";

    public static byte[] Build(MediaTagInfo tags, bool includePictures = true)
    {
        var comments = new List<string>();

        AddField(comments, VorbisCommentFieldNames.Title, tags.Title);
        AddField(comments, VorbisCommentFieldNames.Artist, tags.Artist);
        AddField(comments, VorbisCommentFieldNames.Album, tags.Album);
        AddField(comments, VorbisCommentFieldNames.AlbumArtist, tags.AlbumArtist);
        AddField(comments, VorbisCommentFieldNames.Genre, tags.Genre);

        if (tags.Year is not null)
            AddField(comments, VorbisCommentFieldNames.Date, tags.Year.Value.ToString("D4", CultureInfo.InvariantCulture));

        if (tags.TrackNumber is not null)
            AddField(comments, VorbisCommentFieldNames.TrackNumber, tags.TrackNumber.Value.ToString(CultureInfo.InvariantCulture));
        if (tags.TrackTotal is not null)
            AddField(comments, VorbisCommentFieldNames.TrackTotal, tags.TrackTotal.Value.ToString(CultureInfo.InvariantCulture));
        if (tags.DiscNumber is not null)
            AddField(comments, VorbisCommentFieldNames.DiscNumber, tags.DiscNumber.Value.ToString(CultureInfo.InvariantCulture));
        if (tags.DiscTotal is not null)
            AddField(comments, VorbisCommentFieldNames.DiscTotal, tags.DiscTotal.Value.ToString(CultureInfo.InvariantCulture));

        AddField(comments, VorbisCommentFieldNames.Comment, tags.Comment);
        AddField(comments, VorbisCommentFieldNames.Lyrics, tags.Lyrics);
        AddField(comments, VorbisCommentFieldNames.Isrc, tags.Isrc);
        AddField(comments, VorbisCommentFieldNames.Composer, tags.Composer);
        AddField(comments, VorbisCommentFieldNames.Conductor, tags.Conductor);
        AddField(comments, VorbisCommentFieldNames.Copyright, tags.Copyright);

        if (tags.Bpm is not null)
            AddField(comments, VorbisCommentFieldNames.Bpm, tags.Bpm.Value.ToString(CultureInfo.InvariantCulture));
        if (tags.IsCompilation is not null)
            AddField(comments, VorbisCommentFieldNames.Compilation, tags.IsCompilation.Value ? "1" : "0");

        // ReplayGain
        if (tags.ReplayGain is not null)
        {
            var rg = tags.ReplayGain.Value;
            if (rg.TrackGain is not null)
                AddField(comments, VorbisCommentFieldNames.ReplayGainTrackGain, rg.TrackGain.Value.ToString("F2", CultureInfo.InvariantCulture) + " dB");
            if (rg.TrackPeak is not null)
                AddField(comments, VorbisCommentFieldNames.ReplayGainTrackPeak, rg.TrackPeak.Value.ToString("F6", CultureInfo.InvariantCulture));
            if (rg.AlbumGain is not null)
                AddField(comments, VorbisCommentFieldNames.ReplayGainAlbumGain, rg.AlbumGain.Value.ToString("F2", CultureInfo.InvariantCulture) + " dB");
            if (rg.AlbumPeak is not null)
                AddField(comments, VorbisCommentFieldNames.ReplayGainAlbumPeak, rg.AlbumPeak.Value.ToString("F6", CultureInfo.InvariantCulture));
        }

        // MusicBrainz
        AddField(comments, VorbisCommentFieldNames.MusicBrainzTrackId, tags.MusicBrainzTrackId);
        AddField(comments, VorbisCommentFieldNames.MusicBrainzArtistId, tags.MusicBrainzArtistId);
        AddField(comments, VorbisCommentFieldNames.MusicBrainzAlbumId, tags.MusicBrainzAlbumId);
        AddField(comments, VorbisCommentFieldNames.MusicBrainzReleaseGroupId, tags.MusicBrainzReleaseGroupId);

        // Custom fields
        foreach (var (key, value) in tags.CustomFields)
        {
            AddField(comments, key, value);
        }

        // Pictures as METADATA_BLOCK_PICTURE (for OGG; FLAC uses native PICTURE blocks)
        if (includePictures)
        {
            foreach (var picture in tags.Pictures)
            {
                var pictureBlock = Flac.FlacPictureBlock.Build(picture);
                AddField(comments, VorbisCommentFieldNames.MetadataBlockPicture, Convert.ToBase64String(pictureBlock));
            }
        }

        return Serialize(comments);
    }

    private static byte[] Serialize(List<string> comments)
    {
        var vendorBytes = Encoding.UTF8.GetBytes(VendorString);

        // Calculate total size
        var size = 4 + vendorBytes.Length + 4;
        foreach (var comment in comments)
        {
            size += 4 + Encoding.UTF8.GetByteCount(comment);
        }

        var result = new byte[size];
        var offset = 0;

        // Vendor string
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset), (uint)vendorBytes.Length);
        offset += 4;
        vendorBytes.CopyTo(result, offset);
        offset += vendorBytes.Length;

        // Comment count
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset), (uint)comments.Count);
        offset += 4;

        // Comments
        foreach (var comment in comments)
        {
            var bytes = Encoding.UTF8.GetBytes(comment);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset), (uint)bytes.Length);
            offset += 4;
            bytes.CopyTo(result, offset);
            offset += bytes.Length;
        }

        return result;
    }

    private static void AddField(List<string> comments, string fieldName, string? value)
    {
        if (value is not null)
            comments.Add(fieldName + "=" + value);
    }
}
