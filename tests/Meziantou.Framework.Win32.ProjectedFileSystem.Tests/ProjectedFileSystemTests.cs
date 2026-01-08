namespace Meziantou.Framework.Win32.ProjectedFileSystem;

public sealed class ProjectedFileSystemTests
{
    [ProjectedFileSystemFact]
    public void BasicEnumerationAndFileRead()
    {
        var guid = Guid.NewGuid();
        var fullPath = Path.Combine(Path.GetTempPath(), "projFS", guid.ToString("N"));
        try
        {
            Directory.CreateDirectory(fullPath);

            using var vfs = new SampleVirtualFileSystem(fullPath);
            var options = new ProjectedFileSystemStartOptions
            {
                UseNegativePathCache = false,
                Notifications =
                {
                    new Notification(
                        PRJ_NOTIFY_TYPES.FILE_HANDLE_CLOSED_FILE_DELETED |
                        PRJ_NOTIFY_TYPES.FILE_HANDLE_CLOSED_FILE_MODIFIED |
                        PRJ_NOTIFY_TYPES.FILE_HANDLE_CLOSED_NO_MODIFICATION |
                        PRJ_NOTIFY_TYPES.FILE_OPENED |
                        PRJ_NOTIFY_TYPES.FILE_OVERWRITTEN |
                        PRJ_NOTIFY_TYPES.FILE_PRE_CONVERT_TO_FULL |
                        PRJ_NOTIFY_TYPES.FILE_RENAMED |
                        PRJ_NOTIFY_TYPES.HARDLINK_CREATED |
                        PRJ_NOTIFY_TYPES.NEW_FILE_CREATED |
                        PRJ_NOTIFY_TYPES.PRE_DELETE |
                        PRJ_NOTIFY_TYPES.PRE_RENAME |
                        PRJ_NOTIFY_TYPES.PRE_SET_HARDLINK),
                },
            };

            vfs.Start(options);

            // Get content
            var files = Directory.GetFiles(fullPath);
            foreach (var result in files)
            {
                var fi = new FileInfo(result);
                var length = fi.Length;
            }

            var directories = Directory.GetDirectories(fullPath);
            Assert.Single(directories);
            Assert.Equal("folder", Path.GetFileName(directories[0]));

            // Get unknown file
            var fi2 = new FileInfo(Path.Combine(fullPath, "unknownfile.txt"));
            Assert.False(fi2.Exists);
            Assert.Throws<FileNotFoundException>(() => fi2.Length);
            Assert.Equal([1], File.ReadAllBytes(Path.Combine(fullPath, "a")));
            using var stream = File.OpenRead(Path.Combine(fullPath, "b"));
            Assert.Equal(1, stream.ReadByte());
            Assert.Equal(2, stream.ReadByte());
        }
        finally
        {
            try
            {
                Directory.Delete(fullPath, recursive: true);
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// Verifies that files in deeply nested subdirectories can be accessed.
    ///
    /// PrjWritePlaceholderInfo requires the full relative path from the virtualization root
    /// (e.g., "Foo\Bar") when creating placeholders, not just the entry name ("Bar").
    ///
    /// This test navigates through a nested directory structure (Foo/Bar/Baz.txt) and
    /// verifies that all levels are accessible and file content can be read.
    /// </summary>
    [ProjectedFileSystemFact]
    public void NestedSubdirectoryAccess()
    {
        var guid = Guid.NewGuid();
        var fullPath = Path.Combine(Path.GetTempPath(), "projFS", guid.ToString("N"));
        try
        {
            Directory.CreateDirectory(fullPath);
            using var vfs = new NestedVirtualFileSystem(fullPath);
            vfs.Start(options: null);

            // Navigate into nested subdirectories (Foo -> Bar -> Baz.txt)
            var fooDir = Path.Combine(fullPath, "Foo");
            Assert.True(Directory.Exists(fooDir), "Foo directory should be accessible");

            var barDir = Path.Combine(fooDir, "Bar");
            Assert.True(Directory.Exists(barDir), "Foo\\Bar directory should be accessible");

            var bazFile = Path.Combine(barDir, "Baz.txt");
            Assert.True(File.Exists(bazFile), "Foo\\Bar\\Baz.txt should be accessible");

            // Read the file content
            var content = File.ReadAllText(bazFile);
            Assert.Equal("Hello from Foo Bar", content);
        }
        finally
        {
            try { Directory.Delete(fullPath, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Verifies that large file content is read correctly at all offsets.
    ///
    /// ProjFS may request file data starting at arbitrary offsets, especially for files
    /// larger than the internal buffer size. When reading in chunks, each request specifies
    /// a byteOffset where reading should begin.
    ///
    /// This test uses a file with predictable byte patterns (byte[i] = i % 256) to verify
    /// that data at any position matches the expected value.
    /// </summary>
    [ProjectedFileSystemFact]
    public void LargeFileReadAtOffset()
    {
        var guid = Guid.NewGuid();
        var fullPath = Path.Combine(Path.GetTempPath(), "projFS", guid.ToString("N"));
        try
        {
            Directory.CreateDirectory(fullPath);
            using var vfs = new LargeFileVirtualFileSystem(fullPath);
            vfs.Start(options: null);

            var filePath = Path.Combine(fullPath, "largefile.bin");

            // Read the entire file and verify content integrity
            // The file contains predictable data: byte[i] = i % 256
            var allData = File.ReadAllBytes(filePath);
            Assert.Equal(10000, allData.Length);

            // Verify data at various positions - with the bug, data would be from offset 0
            // Check beginning
            Assert.Equal(0, allData[0]);
            Assert.Equal(1, allData[1]);

            // Check middle (offset 5000) - this is the critical test
            // At position 5000, expected value is 5000 % 256 = 136
            Assert.Equal((byte)(5000 % 256), allData[5000]);

            // Check near end (offset 9999)
            // At position 9999, expected value is 9999 % 256 = 15
            Assert.Equal((byte)(9999 % 256), allData[9999]);

            // Verify a range of bytes to ensure no corruption
            for (var i = 4000; i < 6000; i++)
            {
                Assert.Equal((byte)(i % 256), allData[i]);
            }
        }
        finally
        {
            try { Directory.Delete(fullPath, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Verifies that large file content is read correctly from a non-seekable stream.
    ///
    /// When the stream returned by OpenRead does not support seeking (e.g., a network
    /// or pipe stream), ProjFS may still request data starting at an arbitrary byte offset.
    /// The implementation must manually read and discard bytes up to the requested offset
    /// before returning the actual data.
    ///
    /// This test uses predictable byte patterns (byte[i] = i % 256) to verify that data
    /// at any position matches the expected value, even with a non-seekable stream.
    /// </summary>
    [ProjectedFileSystemFact]
    public void NonSeekableStreamFileRead()
    {
        var guid = Guid.NewGuid();
        var fullPath = Path.Combine(Path.GetTempPath(), "projFS", guid.ToString("N"));
        try
        {
            Directory.CreateDirectory(fullPath);
            using var vfs = new NonSeekableStreamVirtualFileSystem(fullPath);
            vfs.Start(options: null);

            var filePath = Path.Combine(fullPath, "nonseekabledatafile.bin");

            // Read the entire file and verify content integrity
            var allData = File.ReadAllBytes(filePath);
            Assert.Equal(10000, allData.Length);

            // Verify data at various positions
            Assert.Equal(0, allData[0]);
            Assert.Equal(1, allData[1]);

            // Check middle (offset 5000) - critical test for non-seekable offset handling
            Assert.Equal((byte)(5000 % 256), allData[5000]);

            // Check near end
            Assert.Equal((byte)(9999 % 256), allData[9999]);

            // Verify a range of bytes to ensure no corruption
            for (var i = 4000; i < 6000; i++)
            {
                Assert.Equal((byte)(i % 256), allData[i]);
            }
        }
        finally
        {
            try { Directory.Delete(fullPath, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Verifies that directory enumeration returns consistent results without duplicates.
    ///
    /// ProjFS merges on-disk placeholders with provider-returned entries during enumeration.
    /// This merge algorithm requires entries to be sorted using PrjFileNameCompare order.
    /// When placeholders exist on disk and the provider returns entries, ProjFS expects
    /// both lists to be sorted so it can merge them correctly.
    ///
    /// This test creates placeholders by accessing entries, then re-enumerates to verify
    /// the merge produces no duplicates.
    /// </summary>
    [ProjectedFileSystemFact]
    public void DirectoryEnumerationNoDuplicates()
    {
        var guid = Guid.NewGuid();
        var fullPath = Path.Combine(Path.GetTempPath(), "projFS", guid.ToString("N"));
        try
        {
            Directory.CreateDirectory(fullPath);
            using var vfs = new UnsortedEntriesVirtualFileSystem(fullPath);
            vfs.Start(options: null);

            // Step 1: First enumeration - ProjFS queries provider for entries
            var firstEnum = Directory.GetFileSystemEntries(fullPath).OrderBy(Path.GetFileName).ToList();
            Assert.Equal(5, firstEnum.Count);

            // Step 2: Access each entry to create on-disk placeholders
            // This is critical - ProjFS creates placeholder metadata on disk when entries are accessed
            foreach (var entry in firstEnum)
            {
                // Accessing attributes forces ProjFS to create a placeholder
                _ = File.GetAttributes(entry);
            }

            // Step 3: Re-enumerate after placeholders exist on disk
            var secondEnum = Directory.GetFileSystemEntries(fullPath).OrderBy(Path.GetFileName).ToList();
            var thirdEnum = Directory.GetFileSystemEntries(fullPath).OrderBy(Path.GetFileName).ToList();

            // All enumerations should return exactly 5 unique entries
            Assert.Equal(5, secondEnum.Count);
            Assert.Equal(5, thirdEnum.Count);

            // Verify the expected entries exist (in sorted order by filename)
            var expectedNames = new[] { "apple.txt", "banana", "mango.txt", "yellow.txt", "zebra.txt" };
            var actualNames = firstEnum.Select(Path.GetFileName).ToArray();
            Assert.Equal(expectedNames, actualNames);

            // Ensure all enumerations are identical (no duplicates creeping in)
            Assert.True(firstEnum.SequenceEqual(secondEnum), "Second enumeration should match first");
            Assert.True(secondEnum.SequenceEqual(thirdEnum), "Third enumeration should match second");
        }
        finally
        {
            try { Directory.Delete(fullPath, recursive: true); } catch { }
        }
    }
}
