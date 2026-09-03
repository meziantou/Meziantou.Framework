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
    public void ReadTags_UnreadableFile_ReturnsIoError()
    {
        if (OperatingSystem.IsWindows())
            return; // Unix file modes only

        var tempFile = Path.GetTempFileName() + ".mp3";
        try
        {
            File.Copy(GetTestFilePath("basic.mp3"), tempFile, overwrite: true);
            File.SetUnixFileMode(tempFile, UnixFileMode.None);

            var result = MediaFile.ReadTags(tempFile);

            Assert.False(result.IsSuccess);
            Assert.Equal(MediaTagError.IoError, result.Error);
        }
        finally
        {
            File.SetUnixFileMode(tempFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void WriteTags_UnwritableFile_ReturnsIoError()
    {
        if (OperatingSystem.IsWindows())
            return; // Unix file modes only

        var tempFile = Path.GetTempFileName() + ".mp3";
        try
        {
            File.Copy(GetTestFilePath("basic.mp3"), tempFile, overwrite: true);
            File.SetUnixFileMode(tempFile, UnixFileMode.None);

            var result = MediaFile.WriteTags(tempFile, new MediaTagInfo { Title = "Title" });

            Assert.False(result.IsSuccess);
            Assert.Equal(MediaTagError.IoError, result.Error);
        }
        finally
        {
            File.SetUnixFileMode(tempFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ReadTags_DirectoryPath_ReturnsIoError()
    {
        var directory = Directory.CreateTempSubdirectory();
        var directoryPath = Path.Combine(directory.FullName, "looks-like-a-file.mp3");
        Directory.CreateDirectory(directoryPath);
        try
        {
            var result = MediaFile.ReadTags(directoryPath);

            Assert.False(result.IsSuccess);
            Assert.Equal(MediaTagError.IoError, result.Error);
        }
        finally
        {
            directory.Delete(recursive: true);
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

    [Theory]
    [InlineData("document.wav")]
    [InlineData("document.aiff")]
    [InlineData("document.m4a")]
    [InlineData("document.flac")]
    [InlineData("document.ogg")]
    public void WriteTags_FileThatIsNotAudio_IsRefusedAndLeavesTheFileIntact(string fileName)
    {
        // The format is detected from the extension when the magic bytes are unrecognised, so a mis-named file
        // reaches the writer. It must not be replaced by a freshly synthesized container.
        var directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var file = Path.Combine(directory, fileName);
            var original = Encoding.ASCII.GetBytes(new string('X', 4096));
            File.WriteAllBytes(file, original);

            var result = MediaFile.WriteTags(file, new MediaTagInfo { Title = "Title" });

            Assert.False(result.IsSuccess);
            Assert.Equal(original, File.ReadAllBytes(file));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WriteTags_FileThatIsNotAudioWithAnMp3Extension_KeepsEveryOriginalByte()
    {
        // MPEG audio has no container to validate: a decoder resynchronises on the first frame it recognises.
        // The write is allowed, but it must only prepend a tag, never replace what was there.
        var directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var file = Path.Combine(directory, "document.mp3");
            var original = Encoding.ASCII.GetBytes(new string('X', 4096));
            File.WriteAllBytes(file, original);

            var result = MediaFile.WriteTags(file, new MediaTagInfo { Title = "Title" });

            Assert.True(result.IsSuccess);
            var written = File.ReadAllBytes(file);
            Assert.True(written.AsSpan().IndexOf(original) >= 0, "The original bytes are no longer in the file.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("basic.wav")]
    [InlineData("basic.aiff")]
    [InlineData("basic.m4a")]
    public void WriteTags_TruncatedFile_IsRefusedAndLeavesTheFileIntact(string fixture)
    {
        // A partially downloaded file parses up to the point it was cut. Rebuilding from that partial parse
        // would drop every chunk after it, the audio included, and report success.
        var directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var file = Path.Combine(directory, fixture);
            var truncated = File.ReadAllBytes(GetTestFilePath(fixture));
            truncated = truncated.AsSpan(0, truncated.Length - 50).ToArray();
            File.WriteAllBytes(file, truncated);

            var result = MediaFile.WriteTags(file, new MediaTagInfo { Title = "Title" });

            Assert.False(result.IsSuccess);
            Assert.Equal(MediaTagError.CorruptFile, result.Error);
            Assert.Equal(truncated, File.ReadAllBytes(file));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WriteTags_Mp3WhoseTagSizeRunsPastTheEndOfTheFile_IsRefused()
    {
        var directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var file = Path.Combine(directory, "overstated.mp3");
            var bytes = File.ReadAllBytes(GetTestFilePath("basic.mp3"));

            // Declare a tag far larger than the file. Trusting it drops the audio from the output.
            bytes[6] = 0x00;
            bytes[7] = 0x00;
            bytes[8] = 0x7F;
            bytes[9] = 0x7F;
            File.WriteAllBytes(file, bytes);

            var result = MediaFile.WriteTags(file, new MediaTagInfo { Title = "Title" });

            Assert.False(result.IsSuccess);
            Assert.Equal(MediaTagError.CorruptFile, result.Error);
            Assert.Equal(bytes, File.ReadAllBytes(file));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WriteTags_DoesNotTouchAnUnrelatedFileNamedAfterTheTarget()
    {
        // The temporary file used to stage the new content must not have a name a caller could already be
        // using, and must not be a name an attacker can pre-create as a symbolic link.
        var directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var file = Path.Combine(directory, "song.mp3");
            File.Copy(GetTestFilePath("basic.mp3"), file, overwrite: true);

            var sibling = file + ".tmp";
            File.WriteAllText(sibling, "unrelated user data");

            var result = MediaFile.WriteTags(file, new MediaTagInfo { Title = "Title" });

            Assert.True(result.IsSuccess);
            Assert.True(File.Exists(sibling));
            Assert.Equal("unrelated user data", File.ReadAllText(sibling));
            Assert.Equal([file, sibling], Directory.GetFiles(directory).Order(StringComparer.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WriteTags_ReplacesTheExistingTagsInsteadOfMergingThem()
    {
        var directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var file = Path.Combine(directory, "all_fields.flac");
            File.Copy(GetTestFilePath("all_fields.flac"), file, overwrite: true);
            Assert.NotNull(MediaFile.ReadTags(file).Value.Artist);

            Assert.True(MediaFile.WriteTags(file, new MediaTagInfo { Title = "Only Title" }).IsSuccess);

            var tags = MediaFile.ReadTags(file).Value;
            Assert.Equal("Only Title", tags.Title);
            Assert.Null(tags.Artist);
            Assert.Null(tags.Album);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("basic.mp3")]
    [InlineData("basic.flac")]
    [InlineData("basic.m4a")]
    [InlineData("basic.wav")]
    [InlineData("basic.aiff")]
    [InlineData("basic.ogg")]
    public void RemoveTags_LeavesNoTagsBehind(string fixture)
    {
        var directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var file = Path.Combine(directory, fixture);
            File.Copy(GetTestFilePath(fixture), file, overwrite: true);
            Assert.NotNull(MediaFile.ReadTags(file).Value.Title);

            Assert.True(MediaFile.RemoveTags(file).IsSuccess);

            var tags = MediaFile.ReadTags(file).Value;
            Assert.Null(tags.Title);
            Assert.Null(tags.Artist);
            Assert.Null(tags.Album);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RemoveTags_Mp3_WritesNeitherAnId3v2NorAnId3v1Tag()
    {
        var directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var file = Path.Combine(directory, "basic.mp3");
            File.Copy(GetTestFilePath("basic.mp3"), file, overwrite: true);

            Assert.True(MediaFile.RemoveTags(file).IsSuccess);

            var bytes = File.ReadAllBytes(file);
            Assert.False(bytes.AsSpan().StartsWith("ID3"u8));
            Assert.False(bytes.AsSpan(bytes.Length - 128, 3).SequenceEqual("TAG"u8));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WriteTags_WithoutId3v1_WritesNoId3v1Tag()
    {
        var directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var file = Path.Combine(directory, "basic.mp3");
            File.Copy(GetTestFilePath("basic.mp3"), file, overwrite: true);

            var options = new MediaTagWriteOptions { WriteId3v1Tag = false, Id3v2PaddingSize = 0 };
            Assert.True(MediaFile.WriteTags(file, new MediaTagInfo { Title = "Title" }, options).IsSuccess);

            var bytes = File.ReadAllBytes(file);
            Assert.False(bytes.AsSpan(bytes.Length - 128, 3).SequenceEqual("TAG"u8));
            Assert.Equal("Title", MediaFile.ReadTags(file).Value.Title);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ReadTags_NonSeekableStream_ReportsAnIoErrorRatherThanACorruptFile()
    {
        using var stream = new NonSeekableStream(File.ReadAllBytes(GetTestFilePath("basic.mp3")));

        var result = MediaFile.ReadTags(stream, MediaFormat.Mp3);

        Assert.False(result.IsSuccess);
        Assert.Equal(MediaTagError.IoError, result.Error);
    }

    [Fact]
    public void ReadTags_FormatOutsideTheEnum_ReturnsUnsupportedFormat()
    {
        using var stream = new MemoryStream(File.ReadAllBytes(GetTestFilePath("basic.mp3")));

        var result = MediaFile.ReadTags(stream, (MediaFormat)99);

        Assert.False(result.IsSuccess);
        Assert.Equal(MediaTagError.UnsupportedFormat, result.Error);
    }

    [Fact]
    public void WriteTags_FormatOutsideTheEnum_ReturnsUnsupportedFormat()
    {
        using var input = new MemoryStream(File.ReadAllBytes(GetTestFilePath("basic.mp3")));
        using var output = new MemoryStream();

        var result = MediaFile.WriteTags(input, output, new MediaTagInfo(), (MediaFormat)99);

        Assert.False(result.IsSuccess);
        Assert.Equal(MediaTagError.UnsupportedFormat, result.Error);
    }

    [Fact]
    public void DefaultResult_IsAFailureThatCarriesAnError()
    {
        // IsSuccess is annotated so that Error is not null when it is false; a default value must honour that.
        var result = default(MediaTagResult);
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);

        var genericResult = default(MediaTagResult<MediaTagInfo>);
        Assert.False(genericResult.IsSuccess);
        Assert.NotNull(genericResult.Error);
    }

    [Fact]
    public void ReadTags_StreamPositionedAtTheStartOfTheFile_ReadsFromThere()
    {
        // The stream overloads read from the current position, which is what makes them usable for a media
        // file embedded in a larger stream.
        var file = File.ReadAllBytes(GetTestFilePath("basic.mp3"));
        var padded = new byte[64 + file.Length];
        file.CopyTo(padded, 64);

        using var stream = new MemoryStream(padded) { Position = 64 };
        var result = MediaFile.ReadTags(stream);

        Assert.True(result.IsSuccess);
        Assert.Equal("Test Title", result.Value.Title);
    }

    [Fact]
    public void PublicMethods_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => MediaFile.ReadTags(stream: null!));
        Assert.Throws<ArgumentNullException>(() => MediaFile.WriteTags(GetTestFilePath("basic.mp3"), tags: null!));
        Assert.Throws<ArgumentNullException>(() => MediaFile.DetectFormat(stream: null!));
        Assert.Throws<ArgumentException>(() => MediaFile.ReadTags(filePath: ""));
    }

    [Fact]
    public void MediaTagInfo_NegativeNumbers_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MediaTagInfo { Year = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new MediaTagInfo { Year = 10000 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new MediaTagInfo { TrackNumber = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new MediaTagInfo { TrackTotal = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new MediaTagInfo { DiscNumber = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new MediaTagInfo { DiscTotal = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new MediaTagInfo { Bpm = -1 });
    }

    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
