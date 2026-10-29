namespace Meziantou.Framework.MediaTags.Samples;

internal static class ReadmeExamples
{
    public static void UpdateTags()
    {
        #region MediaFile_UpdateTags
        var read = MediaFile.ReadTags("song.mp3");
        if (!read.IsSuccess)
            return;

        var tags = read.Value;
        tags.Title = "New Title";
        tags.Year = 2026;

        tags.Pictures.Clear();
        tags.Pictures.Add(new MediaPicture
        {
            PictureType = MediaPictureType.FrontCover,
            MimeType = "image/png",
            Description = "Album cover",
            Data = File.ReadAllBytes("cover.png"),
        });

        var writeResult = MediaFile.WriteTags("song.mp3", tags);
        if (!writeResult.IsSuccess)
        {
            Console.WriteLine($"{writeResult.Error}: {writeResult.ErrorMessage}");
        }
        #endregion
    }

    public static void ReplaceTags()
    {
        #region MediaFile_ReplaceTags
        // This deletes the artist, album, artwork and everything else the file had.
        MediaFile.WriteTags("song.mp3", new MediaTagInfo { Title = "New Title" });
        #endregion
    }

    public static void RemoveTags()
    {
        #region MediaFile_RemoveTags
        var result = MediaFile.RemoveTags("song.mp3");
        if (!result.IsSuccess)
        {
            Console.WriteLine($"{result.Error}: {result.ErrorMessage}");
        }
        #endregion
    }

    public static void WriteOptions()
    {
        #region MediaFile_WriteOptions
        var tags = MediaFile.ReadTags("song.mp3").Value;
        tags.Title = "A title longer than the 30 characters of an ID3v1 field";

        var options = new MediaTagWriteOptions
        {
            WriteId3v1Tag = false,  // Only write the ID3v2 tag
            Id3v2PaddingSize = 0,   // The smallest possible tag
        };
        MediaFile.WriteTags("song.mp3", tags, options);
        #endregion
    }

    public static void ScanLibrary()
    {
        #region MediaFile_ScanLibrary
        foreach (var path in Directory.EnumerateFiles("Music", "*", SearchOption.AllDirectories))
        {
            var result = MediaFile.ReadTags(path);
            if (result.Error is MediaTagError.UnsupportedFormat)
                continue; // Not an audio file, such as a playlist or a cover image

            if (!result.IsSuccess)
            {
                Console.WriteLine($"{path}: {result.Error}: {result.ErrorMessage}");
                continue;
            }

            var tags = result.Value;
            Console.WriteLine($"{tags.Artist} - {tags.Album} - {tags.TrackNumber}. {tags.Title} ({tags.Duration})");
        }
        #endregion
    }
}
