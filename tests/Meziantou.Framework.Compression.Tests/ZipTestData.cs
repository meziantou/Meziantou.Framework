using System.IO.Compression;
using System.Security.Cryptography;

namespace Meziantou.Framework.Compression.Tests;

/// <summary>The zip fixtures of TestData/Zip (see TestData/Zip/README.md) and the SHA-256 of their content.</summary>
internal static class ZipTestData
{
    private static readonly string[] StandardFiles = ["hello.txt", "lorem.txt", "random.bin", "empty.txt", "dir/nested.txt"];
    private static readonly string[] StreamedFiles = ["hello.txt", "lorem.txt", "random.bin", "dir/nested.txt"];

    private static readonly Dictionary<string, string> Hashes = new(StringComparer.Ordinal)
    {
        ["hello.txt"] = "d9014c4624844aa5bac314773d6b689ad467fa4e1d1a50a1b8a99d5a95f72ff5",
        ["lorem.txt"] = "3112cd5e8cf19288a5768ecb327b2727aa625305eb205185fd82ecf63cec61b6",
        ["random.bin"] = "0486250ade8703729acb211a2fe288ceec6450be9d301cf3daf76469156d7c6e",
        ["empty.txt"] = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
        ["dir/nested.txt"] = "6c201a9a8f6dd6dc13010b1a1da3b7772a4d79f38df1972d8ef21675827b9346",
        ["big.bin"] = "d60cb5854818174a1c79844293a6e82bd56aa49a129ebc81dd9343ce65d1b033",
        ["tricky.bin"] = "54a9c6e1406ebd4df19723ff14d91a015add79ee830391685a7a5ea4558e28d4",

        // The content of the stdin entry of Info-ZIP is lorem.txt, and the other names have the content of hello.txt or lorem.txt
        ["-"] = "3112cd5e8cf19288a5768ecb327b2727aa625305eb205185fd82ecf63cec61b6",
        ["café/日本.txt"] = "d9014c4624844aa5bac314773d6b689ad467fa4e1d1a50a1b8a99d5a95f72ff5",
        ["naïve.txt"] = "3112cd5e8cf19288a5768ecb327b2727aa625305eb205185fd82ecf63cec61b6",
        ["résumé/café.txt"] = "d9014c4624844aa5bac314773d6b689ad467fa4e1d1a50a1b8a99d5a95f72ff5",
        ["日本語.txt"] = "d9014c4624844aa5bac314773d6b689ad467fa4e1d1a50a1b8a99d5a95f72ff5",
        ["link"] = Convert.ToHexStringLower(SHA256.HashData("hello.txt"u8)),
        ["ntfs.txt"] = Convert.ToHexStringLower(SHA256.HashData("ntfs"u8)),
        ["unix.txt"] = Convert.ToHexStringLower(SHA256.HashData("unix"u8)),
        ["dos.txt"] = Convert.ToHexStringLower(SHA256.HashData("dos"u8)),
    };

    /// <summary>The archives, their password, and the files they contain.</summary>
    public static TheoryData<string, string?, string[]> Fixtures => new()
    {
        { "stored.zip", null, StandardFiles },
        { "deflate.zip", null, StandardFiles },
        { "deflate64.zip", null, StandardFiles },
        { "bzip2.zip", null, StandardFiles },
        { "lzma.zip", null, StandardFiles },
        { "lzma-eos-python.zip", null, StandardFiles },
        { "ppmd.zip", null, StandardFiles },
        { "xz.zip", null, StandardFiles },
        { "zstd.zip", null, StandardFiles },
        { "zipcrypto.zip", "password", StandardFiles },
        { "zipcrypto-infozip.zip", "password", StandardFiles },
        { "zipcrypto-bsdtar.zip", "password", StandardFiles },
        { "aes128.zip", "password", StandardFiles },
        { "aes192.zip", "password", StandardFiles },
        { "aes256.zip", "password", StandardFiles },
        { "aes256-stored.zip", "password", ["hello.txt", "lorem.txt"] },
        { "aes-bsdtar.zip", "password", StandardFiles },
        { "bsdtar.zip", null, StandardFiles },
        { "streamed-infozip.zip", null, StreamedFiles },
        { "stdin-infozip.zip", null, ["-"] },
        { "descriptor-python.zip", null, StandardFiles },
        { "descriptor-zip64-python.zip", null, StandardFiles },
        { "descriptor-stored.zip", null, [.. StandardFiles, "tricky.bin"] },
        { "descriptor-stored-zip64.zip", null, StandardFiles },
        { "descriptor-deflate-nosig.zip", null, StandardFiles },
        { "zip64-python.zip", null, StandardFiles },
        { "zip64-eocd.zip", null, StandardFiles },
        { "sfx.zip", null, StandardFiles },
        { "comments.zip", null, ["hello.txt"] },
        { "utf8-names.zip", null, ["café/日本.txt", "naïve.txt"] },
        { "cp437-names.zip", null, ["résumé/café.txt", "日本語.txt"] },
        { "symlink.zip", null, ["hello.txt", "link"] },
        { "timestamps.zip", null, ["ntfs.txt", "unix.txt", "dos.txt"] },
        { "empty.zip", null, [] },
    };

    /// <summary>The archives that <see cref="System.IO.Compression.ZipArchive"/> reads too.</summary>
    public static TheoryData<string, string?> BclFixtures
    {
        get
        {
            var data = new TheoryData<string, string?>
            {
                { "stored.zip", null },
                { "deflate.zip", null },
                { "deflate64.zip", null },
                { "bsdtar.zip", null },
                { "streamed-infozip.zip", null },
                { "descriptor-python.zip", null },
                { "descriptor-zip64-python.zip", null },
                { "descriptor-stored.zip", null },
                { "descriptor-deflate-nosig.zip", null },
                { "zip64-python.zip", null },
                { "zip64-eocd.zip", null },
                { "comments.zip", null },
                { "utf8-names.zip", null },
                { "symlink.zip", null },
            };
#if NET11_0_OR_GREATER
            data.Add("zipcrypto.zip", "password");
            data.Add("aes128.zip", "password");
            data.Add("aes192.zip", "password");
            data.Add("aes256.zip", "password");
            data.Add("aes256-stored.zip", "password");
#endif
            return data;
        }
    }

    public static string GetPath(string fileName) => Path.Combine(AppContext.BaseDirectory, "TestData", "Zip", fileName);

    public static string GetLegacyPath(string fileName) => Path.Combine(AppContext.BaseDirectory, "TestData", "ZipLegacy", fileName);

    public static Stream OpenRead(string fileName) => File.OpenRead(GetPath(fileName));

    public static byte[] ReadBytes(string fileName) => File.ReadAllBytes(GetPath(fileName));

    public static string GetExpectedHash(string name) => Hashes.TryGetValue(name, out var hash) ? hash : throw new InvalidOperationException("Unexpected entry: " + name);

    public static string Hash(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    public static byte[] ReadAll(Stream stream)
    {
        using (stream)
        {
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        }
    }

    /// <summary>A stream that is not seekable, whose reads return at most a number of bytes.</summary>
    internal sealed class ForwardOnlyStream(byte[] data, int maxRead) : Stream
    {
        private readonly MemoryStream _inner = new(data, writable: false);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, Math.Min(count, maxRead));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
