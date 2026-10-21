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
}
