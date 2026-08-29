namespace Meziantou.Framework.MediaTags.Tests;

public sealed class MediaFileWriteTests
{
    private static string GetTestFilePath(string fileName) => Path.Combine("TestFiles", fileName);

    [Fact]
    public void WriteTags_PreservesFilePermissions()
    {
        if (OperatingSystem.IsWindows())
            return; // Unix file modes only

        var tempFile = Path.GetTempFileName() + ".mp3";
        try
        {
            File.Copy(GetTestFilePath("basic.mp3"), tempFile, overwrite: true);
            File.SetUnixFileMode(tempFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            var writeResult = MediaFile.WriteTags(tempFile, new MediaTagInfo { Title = "Title" });

            Assert.True(writeResult.IsSuccess);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void WriteTags_WritesThroughSymbolicLink()
    {
        if (OperatingSystem.IsWindows())
            return; // Creating a symbolic link needs elevation on Windows

        var directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var targetFile = Path.Combine(directory, "target.mp3");
            var linkFile = Path.Combine(directory, "link.mp3");
            File.Copy(GetTestFilePath("basic.mp3"), targetFile, overwrite: true);
            File.CreateSymbolicLink(linkFile, targetFile);

            var writeResult = MediaFile.WriteTags(linkFile, new MediaTagInfo { Title = "Through The Link" });

            Assert.True(writeResult.IsSuccess);
            Assert.NotNull(new FileInfo(linkFile).LinkTarget);
            Assert.Equal("Through The Link", MediaFile.ReadTags(targetFile).Value.Title);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WriteTags_FailedWrite_DoesNotLeaveATemporaryFile()
    {
        var directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            // Detected as OGG by extension, but it holds no OGG pages, so the writer fails
            var file = Path.Combine(directory, "not-really.ogg");
            File.WriteAllBytes(file, [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08]);

            var writeResult = MediaFile.WriteTags(file, new MediaTagInfo { Title = "Title" });

            Assert.False(writeResult.IsSuccess);
            Assert.Equal([file], Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WriteTags_FailedWrite_LeavesTheOriginalFileUntouched()
    {
        var directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var file = Path.Combine(directory, "not-really.ogg");
            byte[] original = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08];
            File.WriteAllBytes(file, original);

            var writeResult = MediaFile.WriteTags(file, new MediaTagInfo { Title = "Title" });

            Assert.False(writeResult.IsSuccess);
            Assert.Equal(original, File.ReadAllBytes(file));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
