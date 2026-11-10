using System.IO.Pipelines;
using System.Security.Cryptography;
using Meziantou.Framework.Bencode.Torrent;

namespace Meziantou.Framework.Bencode.Tests;

public sealed class TorrentFileTests
{
    private static readonly byte[] SingleFileTorrent = Encoding.ASCII.GetBytes("d8:announce14:https://t.test13:creation datei1700000000e4:infod6:lengthi123e4:name8:file.txt12:piece lengthi16384e6:pieces20:01234567890123456789ee");
    private static readonly byte[] SingleFileInfo = Encoding.ASCII.GetBytes("d6:lengthi123e4:name8:file.txt12:piece lengthi16384e6:pieces20:01234567890123456789e");

    [Fact]
    public async Task ReadmeExample_Torrent()
    {
        #region TorrentFile_ParseAndWrite
        var content = "d8:announce14:https://t.test4:infod6:lengthi123e4:name8:file.txt12:piece lengthi16384e6:pieces20:01234567890123456789ee"u8.ToArray();

        // Parse from a stream, such as File.OpenRead("file.torrent")
        await using var input = new MemoryStream(content);
        var torrent = await TorrentFile.ParseAsync(input);

        Assert.Equal("https://t.test", torrent.Announce);
        Assert.Equal("file.txt", torrent.Info.Name);
        Assert.Equal("5A47092756A964C26715F69510A7B35B63B3B01C", Convert.ToHexString(torrent.GetInfoHashSha1()));

        // Write to a stream, such as File.Create("copy.torrent")
        await using var output = new MemoryStream();
        await torrent.WriteToAsync(output, canonical: true);
        #endregion

        Assert.Equal(content, output.ToArray());
    }

    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "BitTorrent v1 piece hashes are SHA-1.")]
    [Fact]
    public void Documentation_CreateTorrent()
    {
        #region TorrentFile_Create
        var content = new byte[40_000]; // The file to share, such as File.ReadAllBytes("video.mp4")
        var pieceLength = 16 * 1024;

        // Each piece of the content is identified by its SHA-1 hash
        var pieces = new List<byte>();
        for (var offset = 0; offset < content.Length; offset += pieceLength)
        {
            var piece = content.AsSpan(offset, Math.Min(pieceLength, content.Length - offset));
            pieces.AddRange(SHA1.HashData(piece));
        }

        var torrent = new TorrentFile
        {
            Announce = "https://tracker.example.com/announce",
            CreatedBy = "MyApp",
            CreationDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Info = new TorrentInfo
            {
                Name = "video.mp4",
                PieceLength = pieceLength,
                Pieces = pieces.ToArray(),
                Length = content.Length,
            },
        };

        // Or WriteToAsync to save it, such as to File.Create("video.torrent")
        var data = torrent.ToUtf8ByteArray();

        var parsed = TorrentFile.Parse(data);
        Assert.Equal("video.mp4", parsed.Info.Name);
        Assert.Equal(torrent.GetInfoHashSha1(), parsed.GetInfoHashSha1());
        #endregion
    }

    [Fact]
    public void Documentation_MultiFileTorrent()
    {
        #region TorrentFile_MultiFile
        var data = "d4:infod5:filesld6:lengthi100e4:pathl6:images5:a.pngeed6:lengthi50e4:pathl9:readme.mdeee4:name7:project12:piece lengthi16384e6:pieces20:01234567890123456789ee"u8;
        var torrent = TorrentFile.Parse(data);

        // A torrent has either a Length (one file) or Files (a folder named Info.Name)
        Assert.Null(torrent.Info.Length);
        Assert.NotNull(torrent.Info.Files);

        var files = torrent.Info.Files.Select(file => $"{torrent.Info.Name}/{string.Join('/', file.Path)} ({file.Length} bytes)");
        Assert.Equal(["project/images/a.png (100 bytes)", "project/readme.md (50 bytes)"], files);
        #endregion
    }

    [Fact]
    public void Documentation_TryParse()
    {
        #region TorrentFile_TryParse
        // Not bencode
        Assert.False(TorrentFile.TryParse("not a torrent"u8, out _));

        // The 'info' dictionary has no 'piece length' and no 'pieces'
        Assert.False(TorrentFile.TryParse("d4:infod6:lengthi1e4:name1:xee"u8, out _));

        // A path segment that would leave the download folder
        Assert.False(TorrentFile.TryParse("d4:infod5:filesld6:lengthi1e4:pathl2:..6:passwdeee4:name4:test12:piece lengthi16384e6:pieces20:01234567890123456789ee"u8, out _));
        #endregion
    }

    [Fact]
    public void Parse_SingleFileTorrent()
    {
        var torrent = TorrentFile.Parse(SingleFileTorrent);

        Assert.Equal("https://t.test", torrent.Announce);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), torrent.CreationDate);
        Assert.Equal("file.txt", torrent.Info.Name);
        Assert.Equal(16384, torrent.Info.PieceLength);
        Assert.Equal(123, torrent.Info.Length);
        Assert.Null(torrent.Info.Files);
        Assert.Equal("01234567890123456789", Encoding.ASCII.GetString(torrent.Info.Pieces.Span));
    }

    [Fact]
    public async Task ParseAsync_SingleFileTorrent()
    {
        await using var stream = new MemoryStream(SingleFileTorrent);

        var torrent = await TorrentFile.ParseAsync(stream);

        Assert.Equal("file.txt", torrent.Info.Name);
        Assert.Equal(123, torrent.Info.Length);
    }

    [Fact]
    public void TryParse_InvalidContent_ReturnsFalse()
    {
        var parsed = TorrentFile.TryParse("invalid"u8, out var torrent);

        Assert.False(parsed);
        Assert.Null(torrent);
    }

    [Theory]
    [InlineData("l2:..4:.ssh6:id_rsae")]
    [InlineData("l1:.6:id_rsae")]
    [InlineData("l3:a/be")]
    [InlineData("l4:a\\\\be")]
    public void Parse_UnsafePathSegment_Throws(string pathList)
    {
        Assert.Throws<FormatException>(() => TorrentFile.Parse(MultiFileTorrent(pathList)));
    }

    [Theory]
    [InlineData("2:..")]
    [InlineData("1:.")]
    [InlineData("3:a/b")]
    public void Parse_UnsafeName_Throws(string name)
    {
        Assert.Throws<FormatException>(() => TorrentFile.Parse(MultiFileTorrent("l8:file.bine", name)));
    }

    [Theory]
    [InlineData("l7:a:b.mp3e", "a:b.mp3")]
    [InlineData("l3:..ae", "..a")]
    [InlineData("l7:sub dir8:file.bine", "sub dir")]
    public void Parse_LegitimatePathSegment_IsAccepted(string pathList, string expectedFirstSegment)
    {
        var torrent = TorrentFile.Parse(MultiFileTorrent(pathList));

        Assert.NotNull(torrent.Info.Files);
        Assert.Equal(expectedFirstSegment, torrent.Info.Files[0].Path[0]);
    }

    [Fact]
    public void ToUtf8ByteArray_UnsafePathSegment_Throws()
    {
        var torrent = new TorrentFile
        {
            Info = new TorrentInfo
            {
                Name = "test",
                PieceLength = 16,
                Pieces = "01234567890123456789"u8.ToArray(),
                Files = [new TorrentInfoFile { Length = 1, Path = ["..", "escaped.bin"] }],
            },
        };

        Assert.Throws<FormatException>(() => torrent.ToUtf8ByteArray());
    }

    private static byte[] MultiFileTorrent(string pathList, string name = "4:test")
    {
        return Encoding.ASCII.GetBytes($"d4:infod5:filesld6:lengthi1e4:path{pathList}ee4:name{name}12:piece lengthi16384e6:pieces20:01234567890123456789ee");
    }

    [Fact]
    public void TryParse_NonUtf8Comment_ReturnsFalse()
    {
        var data = new List<byte>("d7:comment2:"u8.ToArray());
        data.AddRange([0xFF, 0xFE]);
        data.AddRange("4:infod6:lengthi123e4:name8:file.txt12:piece lengthi16384e6:pieces20:01234567890123456789ee"u8.ToArray());

        var parsed = TorrentFile.TryParse(data.ToArray(), out var torrent);

        Assert.False(parsed);
        Assert.Null(torrent);
    }

    [Fact]
    public void Parse_NonUtf8Name_ThrowsFormatException()
    {
        var data = new List<byte>("d4:infod6:lengthi123e4:name2:"u8.ToArray());
        data.AddRange([0xFF, 0xFE]);
        data.AddRange("12:piece lengthi16384e6:pieces20:01234567890123456789ee"u8.ToArray());

        var exception = Assert.Throws<FormatException>(() => TorrentFile.Parse(data.ToArray()));
        Assert.IsType<DecoderFallbackException>(exception.InnerException);
    }

    [Fact]
    public void Parse_NonUtf8PathSegment_ThrowsFormatException()
    {
        var data = new List<byte>("d4:infod5:filesld6:lengthi1e4:pathl2:"u8.ToArray());
        data.AddRange([0xFF, 0xFE]);
        data.AddRange("eee4:name4:test12:piece lengthi16384e6:pieces20:01234567890123456789ee"u8.ToArray());

        Assert.Throws<FormatException>(() => TorrentFile.Parse(data.ToArray()));
    }

    [Fact]
    public void Parse_NonAsciiUtf8Comment_IsStillSupported()
    {
        var data = new List<byte>("d7:comment"u8.ToArray());
        var comment = Encoding.UTF8.GetBytes("caf\u00e9 \u2013 caf\u00e9");
        data.AddRange(Encoding.ASCII.GetBytes(comment.Length + ":"));
        data.AddRange(comment);
        data.AddRange("4:infod6:lengthi123e4:name8:file.txt12:piece lengthi16384e6:pieces20:01234567890123456789ee"u8.ToArray());

        var torrent = TorrentFile.Parse(data.ToArray());

        Assert.Equal("caf\u00e9 \u2013 caf\u00e9", torrent.Comment);
    }

    [Fact]
    public void TryParse_ValidContent_ReturnsTheTorrent()
    {
        // No null-forgiving operator inside the branch: NotNullWhen(true) must narrow `torrent` here,
        // otherwise this test does not compile under warnings-as-errors.
        if (TorrentFile.TryParse(SingleFileTorrent, out var torrent))
        {
            Assert.Equal("file.txt", torrent.Info.Name);
            Assert.Equal(123, torrent.Info.Length);
        }
        else
        {
            Assert.Fail("Expected the torrent to parse.");
        }
    }

    [Fact]
    public async Task WriteToAsync_RoundTrip()
    {
        var torrent = TorrentFile.Parse(SingleFileTorrent);

        await using var stream = new MemoryStream();
        await torrent.WriteToAsync(stream, canonical: true);
        var roundTrip = TorrentFile.Parse(stream.ToArray());

        Assert.Equal(torrent.Announce, roundTrip.Announce);
        Assert.Equal(torrent.Info.Name, roundTrip.Info.Name);
        Assert.Equal(torrent.Info.Length, roundTrip.Info.Length);
        Assert.Equal(torrent.CreationDate, roundTrip.CreationDate);
    }

    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "BitTorrent v1 info-hash is SHA-1.")]
    [Fact]
    public void GetInfoHash_ComputesDeterministicHashes()
    {
        var torrent = TorrentFile.Parse(SingleFileTorrent);

        Assert.Equal(SHA1.HashData(SingleFileInfo), torrent.GetInfoHashSha1());
        Assert.Equal(SHA256.HashData(SingleFileInfo), torrent.GetInfoHashSha256());
    }

    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "BitTorrent v1 info-hash is SHA-1.")]
    [Theory]
    [InlineData("d6:lengthi123e4:name8:file.txt12:piece lengthi16384e6:pieces20:012345678901234567896:source9:MyTrackere")]
    [InlineData("d6:lengthi123e6:md5sum32:000102030405060708090a0b0c0d0e0f4:name8:file.txt12:piece lengthi16384e6:pieces20:01234567890123456789e")]
    [InlineData("d6:lengthi123e4:name8:file.txt12:piece lengthi16384e6:pieces20:012345678901234567897:privatei0ee")]
    public void GetInfoHash_UsesTheBytesTheInfoDictionaryWasParsedFrom(string info)
    {
        var infoBytes = Encoding.ASCII.GetBytes(info);
        var torrent = TorrentFile.Parse(Encoding.ASCII.GetBytes("d8:announce14:https://t.test4:info" + info + "e"));

        Assert.Equal(SHA1.HashData(infoBytes), torrent.GetInfoHashSha1());
        Assert.Equal(SHA256.HashData(infoBytes), torrent.GetInfoHashSha256());
    }

    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "BitTorrent v1 info-hash is SHA-1.")]
    [Fact]
    public void GetInfoHash_NonCanonicalKeyOrder_UsesTheOriginalOrder()
    {
        var info = "d4:name8:file.txt6:lengthi123e12:piece lengthi16384e6:pieces20:01234567890123456789e";
        var torrent = TorrentFile.Parse(Encoding.ASCII.GetBytes("d4:info" + info + "e"));

        Assert.Equal(SHA1.HashData(Encoding.ASCII.GetBytes(info)), torrent.GetInfoHashSha1());
    }

    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "BitTorrent v1 info-hash is SHA-1.")]
    [Fact]
    public async Task GetInfoHashAsync_MatchesTheSynchronousParse()
    {
        var info = "d6:lengthi123e4:name8:file.txt12:piece lengthi16384e6:pieces20:012345678901234567896:source9:MyTrackere";
        var content = Encoding.ASCII.GetBytes("d8:announce14:https://t.test4:info" + info + "e");

        await using var stream = new MemoryStream(content);
        var torrent = await TorrentFile.ParseAsync(stream);

        Assert.Equal(SHA1.HashData(Encoding.ASCII.GetBytes(info)), torrent.GetInfoHashSha1());
    }

    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "BitTorrent v1 info-hash is SHA-1.")]
    [Fact]
    public void GetInfoHash_ConstructedTorrent_UsesTheCanonicalEncoding()
    {
        var torrent = new TorrentFile
        {
            Info = new TorrentInfo
            {
                Name = "file.txt",
                PieceLength = 16384,
                Pieces = "01234567890123456789"u8.ToArray(),
                Length = 123,
            },
        };

        Assert.Equal(SHA1.HashData(SingleFileInfo), torrent.GetInfoHashSha1());
    }

    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "BitTorrent v1 info-hash is SHA-1.")]
    [Fact]
    public void GetInfoHash_AfterReplacingInfo_UsesTheNewInfo()
    {
        var info = "d6:lengthi123e4:name8:file.txt12:piece lengthi16384e6:pieces20:012345678901234567896:source9:MyTrackere";
        var torrent = TorrentFile.Parse(Encoding.ASCII.GetBytes("d4:info" + info + "e"));

        Assert.Equal(SHA1.HashData(Encoding.ASCII.GetBytes(info)), torrent.GetInfoHashSha1());

        torrent.Info = new TorrentInfo
        {
            Name = "file.txt",
            PieceLength = 16384,
            Pieces = "01234567890123456789"u8.ToArray(),
            Length = 123,
        };

        Assert.Equal(SHA1.HashData(SingleFileInfo), torrent.GetInfoHashSha1());
    }

    [Fact]
    public void Parse_ExtensionFields()
    {
        var content = Encoding.ASCII.GetBytes("d8:encoding5:UTF-84:infod6:lengthi123e6:md5sum4:abcd4:name8:file.txt12:piece lengthi16384e6:pieces20:012345678901234567896:source9:MyTrackere5:nodesll9:127.0.0.1i6881eel4:hosti1eee9:publisher7:someone13:publisher-url19:https://pub.test/ab8:url-listl20:https://seed.test/abee");

        var torrent = TorrentFile.Parse(content);

        Assert.Equal(["https://seed.test/ab"], torrent.UrlList);
        Assert.Equal("UTF-8", torrent.Encoding);
        Assert.Equal("someone", torrent.Publisher);
        Assert.Equal("https://pub.test/ab", torrent.PublisherUrl);
        Assert.Equal("MyTracker", torrent.Info.Source);
        Assert.Equal("abcd", torrent.Info.Md5Sum);

        Assert.NotNull(torrent.Nodes);
        Assert.Equal(2, torrent.Nodes.Count);
        Assert.Equal("127.0.0.1", torrent.Nodes[0].Host);
        Assert.Equal(6881, torrent.Nodes[0].Port);
        Assert.Equal("host", torrent.Nodes[1].Host);
        Assert.Equal(1, torrent.Nodes[1].Port);
    }

    [Fact]
    public void Parse_UrlListAsASingleString_IsReadAsAOneItemList()
    {
        var content = Encoding.ASCII.GetBytes("d4:infod6:lengthi123e4:name8:file.txt12:piece lengthi16384e6:pieces20:01234567890123456789e8:url-list20:https://seed.test/abe");

        var torrent = TorrentFile.Parse(content);

        Assert.Equal(["https://seed.test/ab"], torrent.UrlList);
    }

    [Fact]
    public void Parse_HttpSeeds()
    {
        var content = Encoding.ASCII.GetBytes("d9:httpseedsl20:https://seed.test/abe4:infod6:lengthi123e4:name8:file.txt12:piece lengthi16384e6:pieces20:01234567890123456789ee");

        var torrent = TorrentFile.Parse(content);

        Assert.Equal(["https://seed.test/ab"], torrent.HttpSeeds);
    }

    [Fact]
    public void Parse_PerFileMd5Sum()
    {
        var content = Encoding.ASCII.GetBytes("d4:infod5:filesld6:lengthi1e6:md5sum4:abcd4:pathl8:file.bineee4:name4:test12:piece lengthi16384e6:pieces20:01234567890123456789ee");

        var torrent = TorrentFile.Parse(content);

        Assert.NotNull(torrent.Info.Files);
        Assert.Equal("abcd", torrent.Info.Files[0].Md5Sum);
    }

    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "BitTorrent v1 info-hash is SHA-1.")]
    [Fact]
    public void RoundTrip_ExtensionFields_ArePreserved()
    {
        var content = Encoding.ASCII.GetBytes("d8:encoding5:UTF-89:httpseedsl20:https://seed.test/cde4:infod6:lengthi123e6:md5sum4:abcd4:name8:file.txt12:piece lengthi16384e6:pieces20:012345678901234567896:source9:MyTrackere5:nodesll9:127.0.0.1i6881eee9:publisher7:someone13:publisher-url19:https://pub.test/ab8:url-listl20:https://seed.test/abee");
        var original = TorrentFile.Parse(content);
        Assert.NotNull(original.UrlList);
        Assert.NotNull(original.HttpSeeds);

        var roundTrip = TorrentFile.Parse(original.ToUtf8ByteArray());

        Assert.Equal(original.UrlList, roundTrip.UrlList);
        Assert.Equal(original.HttpSeeds, roundTrip.HttpSeeds);
        Assert.Equal(original.Encoding, roundTrip.Encoding);
        Assert.Equal(original.Publisher, roundTrip.Publisher);
        Assert.Equal(original.PublisherUrl, roundTrip.PublisherUrl);
        Assert.Equal(original.Info.Source, roundTrip.Info.Source);
        Assert.Equal(original.Info.Md5Sum, roundTrip.Info.Md5Sum);
        Assert.Equal(original.GetInfoHashSha1(), roundTrip.GetInfoHashSha1());

        Assert.NotNull(roundTrip.Nodes);
        Assert.Equal("127.0.0.1", roundTrip.Nodes[0].Host);
        Assert.Equal(6881, roundTrip.Nodes[0].Port);
    }

    [Theory]
    [InlineData("8:url-listi1e")]
    [InlineData("9:httpseeds20:https://seed.test/ab")]
    [InlineData("5:nodesl9:127.0.0.1e")]
    [InlineData("5:nodesll9:127.0.0.1i70000eee")]
    [InlineData("5:nodesll9:127.0.0.14:portee")]
    [InlineData("8:encodingi1e")]
    public void Parse_MalformedExtensionField_Throws(string field)
    {
        var content = Encoding.ASCII.GetBytes("d" + field + "4:infod6:lengthi123e4:name8:file.txt12:piece lengthi16384e6:pieces20:01234567890123456789ee");

        Assert.Throws<FormatException>(() => TorrentFile.Parse(content));
    }

    [Fact]
    public void ToUtf8ByteArray_NodePortOutOfRange_Throws()
    {
        var torrent = TorrentFile.Parse(SingleFileTorrent);
        torrent.Nodes = [new TorrentNode { Host = "127.0.0.1", Port = 70000 }];

        Assert.Throws<FormatException>(() => torrent.ToUtf8ByteArray());
    }

    [Fact]
    public void ToArray_BothLengthAndFiles_Throws()
    {
        var torrent = new TorrentFile
        {
            Info = new TorrentInfo
            {
                Name = "test",
                PieceLength = 16,
                Pieces = "01234567890123456789"u8.ToArray(),
                Length = 1,
                Files =
                [
                    new TorrentInfoFile
                    {
                        Length = 1,
                        Path = ["test.bin"],
                    },
                ],
            },
        };

        Assert.Throws<FormatException>(() => torrent.ToUtf8ByteArray());
    }

    [Fact]
    public async Task Documentation_V2Torrent()
    {
        #region TorrentFile_V2
        // A hybrid torrent, created by libtorrent, holds both the BitTorrent v1 and v2 keys
        await using var stream = OpenResourceStream("hybrid.torrent"); // Such as File.OpenRead("file.torrent")
        var torrent = await TorrentFile.ParseAsync(stream);

        Assert.Equal(2, torrent.Info.MetaVersion);
        Assert.NotNull(torrent.Info.FileTree);
        var files = torrent.Info.FileTree.Select(file => $"{string.Join('/', file.Path)} ({file.Length} bytes)");
        Assert.Equal(["data.bin (40000 bytes)", "docs/readme.txt (14 bytes)", "empty.bin (0 bytes)"], files);

        // The SHA-256 merkle root of each file, and the piece hashes of the files larger than a piece
        Assert.Equal(32, torrent.Info.FileTree[0].PiecesRoot.Length);
        Assert.NotNull(torrent.PieceLayers);
        Assert.Equal(torrent.Info.FileTree[0].PiecesRoot.ToArray(), torrent.PieceLayers[0].PiecesRoot.ToArray());

        // The v1 info-hash is the SHA-1 of the 'info' dictionary, and the v2 info-hash its SHA-256
        Assert.Equal("9AAF1926E4A8E378CC2D2BD2F71008D0C819ABF7", Convert.ToHexString(torrent.GetInfoHashSha1()));
        Assert.Equal("EB59F42E745CCF50245148E82AEE5F7817D572D5F274A27F323C5286EA7691C3", Convert.ToHexString(torrent.GetInfoHashSha256()));
        #endregion
    }

    [Theory]
    [InlineData("hybrid.torrent", "EB59F42E745CCF50245148E82AEE5F7817D572D5F274A27F323C5286EA7691C3")]
    [InlineData("v2.torrent", "CB217D4C147CD4A71B43D19758AC2B2601F12C5852671354993E6397BE1B59EC")]
    public async Task RoundTrip_V2Torrent_WritesTheSameBytes(string fileName, string expectedInfoHash)
    {
        var content = await ReadResourceAsync(fileName);
        var torrent = TorrentFile.Parse(content);

        // libtorrent writes canonical bencode, and the model keeps every key, so writing the torrent gives the file back
        var written = torrent.ToUtf8ByteArray();
        Assert.Equal(content, written);

        var reparsed = TorrentFile.Parse(written);
        Assert.Equal(expectedInfoHash, Convert.ToHexString(reparsed.GetInfoHashSha256()));

        // Replacing the info hashes the model instead of the parsed bytes: it is the same torrent
        torrent.Info = torrent.Info;
        Assert.Equal(expectedInfoHash, Convert.ToHexString(torrent.GetInfoHashSha256()));
    }

    [Fact]
    public async Task Parse_V2OnlyTorrent()
    {
        var torrent = TorrentFile.Parse(await ReadResourceAsync("v2.torrent"));

        Assert.Equal("sample", torrent.Info.Name);
        Assert.Equal(2, torrent.Info.MetaVersion);
        Assert.True(torrent.Info.Pieces.IsEmpty);
        Assert.Null(torrent.Info.Length);
        Assert.Null(torrent.Info.Files);
        Assert.NotNull(torrent.Info.FileTree);
        Assert.Equal([["data.bin"], ["docs", "readme.txt"], ["empty.bin"]], torrent.Info.FileTree.Select(file => file.Path));
        Assert.True(torrent.Info.FileTree[2].PiecesRoot.IsEmpty);

        Assert.NotNull(torrent.PieceLayers);
        var layer = Assert.Single(torrent.PieceLayers);
        Assert.Equal(3 * 32, layer.Hashes.Length);
    }

    [Fact]
    public async Task Parse_HybridTorrent_PaddingFilesKeepTheirAttributes()
    {
        var torrent = TorrentFile.Parse(await ReadResourceAsync("hybrid.torrent"));

        Assert.NotNull(torrent.Info.Files);
        var padding = torrent.Info.Files[1];
        Assert.Equal([".pad", "9152"], padding.Path);
        var attr = Assert.IsType<BencodeString>(padding.AdditionalKeys[new BencodeString("attr"u8.ToArray())]);
        Assert.Equal("p", attr.ToUtf8String());
        Assert.Empty(torrent.Info.Files[0].AdditionalKeys);
    }

    [Fact]
    public async Task ParseAsync_PipeReader_ReadsTheTorrentAsTheDataArrives()
    {
        var content = await ReadResourceAsync("hybrid.torrent");
        var pipe = new Pipe();
        var parseTask = TorrentFile.ParseAsync(pipe.Reader).AsTask();

        for (var offset = 0; offset < content.Length; offset += 7)
        {
            await pipe.Writer.WriteAsync(content.AsMemory(offset, Math.Min(7, content.Length - offset)));
        }

        await pipe.Writer.CompleteAsync();
        var torrent = await parseTask.WaitAsync(TimeSpan.FromSeconds(30));
        await pipe.Reader.CompleteAsync();

        var expected = TorrentFile.Parse(content);
        Assert.Equal(expected.GetInfoHashSha1(), torrent.GetInfoHashSha1());
        Assert.Equal(expected.GetInfoHashSha256(), torrent.GetInfoHashSha256());
        Assert.Equal(content, torrent.ToUtf8ByteArray());
    }

    [Fact]
    public async Task ParseAsync_Stream_IsLeftOpen()
    {
        var stream = new MemoryStream(SingleFileTorrent);
        await TorrentFile.ParseAsync(stream);

        Assert.True(stream.CanRead);
    }

    [Theory]
    [InlineData("d4:infod6:lengthi1e4:name1:a12:piece lengthi16384e6:pieces20:01234567890123456789ee1:x")]
    [InlineData("li1ee")]
    [InlineData("d4:infoi1ee")]
    [InlineData("d4:infod6:lengthi1e4:name1:a12:piece lengthi16384e6:pieces20:01234567890123456789e4:infod6:lengthi1e4:name1:a12:piece lengthi16384e6:pieces20:01234567890123456789ee")]
    public async Task ParseAsync_InvalidTorrent_Throws(string content)
    {
        var data = Encoding.ASCII.GetBytes(content);

        Assert.False(TorrentFile.TryParse(data, out _));
        await using var stream = new MemoryStream(data);
        await Assert.ThrowsAsync<FormatException>(async () => await TorrentFile.ParseAsync(stream));
    }

    [Fact]
    public void Documentation_AdditionalKeys()
    {
        #region TorrentFile_AdditionalKeys
        // 'x-custom' and 'x-tag' are not part of the model
        var data = "d4:infod6:lengthi123e4:name8:file.txt12:piece lengthi16384e6:pieces20:012345678901234567895:x-tag3:abce8:x-custom5:valuee"u8.ToArray();
        var torrent = TorrentFile.Parse(data);

        var custom = (BencodeString)torrent.AdditionalKeys[new BencodeString("x-custom"u8.ToArray())];
        Assert.Equal("value", custom.ToUtf8String());
        Assert.True(torrent.Info.AdditionalKeys.ContainsKey(new BencodeString("x-tag"u8.ToArray())));

        // They are written back, so the 'info' dictionary and the info-hash do not change
        var written = torrent.ToUtf8ByteArray();
        Assert.Equal(torrent.GetInfoHashSha1(), TorrentFile.Parse(written).GetInfoHashSha1());

        // Add or remove keys like in any dictionary
        torrent.AdditionalKeys.Add(new BencodeString("x-other"u8.ToArray()), new BencodeInteger(1));
        #endregion

        Assert.HasCount(2, TorrentFile.Parse(torrent.ToUtf8ByteArray()).AdditionalKeys);
    }

    [Fact]
    public void ToUtf8ByteArray_AdditionalKeyOfTheModel_Throws()
    {
        var torrent = TorrentFile.Parse(SingleFileTorrent);
        torrent.Info.AdditionalKeys.Add(new BencodeString("private"u8.ToArray()), new BencodeInteger(1));

        Assert.Throws<FormatException>(() => torrent.ToUtf8ByteArray());
    }

    [Fact]
    public void Create_V2Torrent()
    {
        var torrent = new TorrentFile
        {
            Info = new TorrentInfo
            {
                Name = "folder",
                PieceLength = 16384,
                MetaVersion = 2,
                FileTree =
                [
                    new TorrentFileTreeEntry { Path = ["b", "file.txt"], Length = 10, PiecesRoot = new byte[32] },
                    new TorrentFileTreeEntry { Path = ["a.txt"], Length = 0 },
                    new TorrentFileTreeEntry { Path = ["b", "big.bin"], Length = 20000, PiecesRoot = Enumerable.Repeat((byte)1, 32).ToArray() },
                ],
            },
            PieceLayers = [new TorrentPieceLayer { PiecesRoot = Enumerable.Repeat((byte)1, 32).ToArray(), Hashes = new byte[64] }],
        };

        var parsed = TorrentFile.Parse(torrent.ToUtf8ByteArray());

        // The file tree is written as a tree whose keys are sorted
        Assert.NotNull(parsed.Info.FileTree);
        Assert.Equal([["a.txt"], ["b", "big.bin"], ["b", "file.txt"]], parsed.Info.FileTree.Select(file => file.Path));
        Assert.Equal(torrent.GetInfoHashSha256(), parsed.GetInfoHashSha256());
        Assert.NotNull(parsed.PieceLayers);
        Assert.Equal(64, Assert.Single(parsed.PieceLayers).Hashes.Length);
    }

    [Theory]
    [InlineData(new[] { "a" }, new[] { "a" })]
    [InlineData(new[] { "a" }, new[] { "a", "b" })]
    [InlineData(new[] { "a", "b" }, new[] { "a" })]
    public void ToUtf8ByteArray_ConflictingFileTreePaths_Throws(string[] first, string[] second)
    {
        var torrent = new TorrentFile
        {
            Info = new TorrentInfo
            {
                Name = "folder",
                PieceLength = 16384,
                MetaVersion = 2,
                FileTree =
                [
                    new TorrentFileTreeEntry { Path = first, Length = 0 },
                    new TorrentFileTreeEntry { Path = second, Length = 0 },
                ],
            },
        };

        Assert.Throws<FormatException>(() => torrent.ToUtf8ByteArray());
    }

    [Theory]
    [InlineData("d9:file treed1:ad0:d6:lengthi0eeee4:name1:a12:piece lengthi16384ee")] // 'file tree' without 'meta version'
    [InlineData("d12:meta versioni2e4:name1:a12:piece lengthi16384ee")] // 'meta version' 2 without 'file tree'
    [InlineData("d9:file treedee12:meta versioni2e4:name1:a12:piece lengthi16384ee")] // no file
    [InlineData("d9:file treed1:adee12:meta versioni2e4:name1:a12:piece lengthi16384ee")] // empty directory
    [InlineData("d9:file treed1:ad0:d6:lengthi0ee1:bd0:d6:lengthi0eeeee12:meta versioni2e4:name1:a12:piece lengthi16384ee")] // file and directory
    [InlineData("d9:file treed0:d6:lengthi0eee12:meta versioni2e4:name1:a12:piece lengthi16384ee")] // file without a name
    [InlineData("d9:file treed2:..d0:d6:lengthi0eeee12:meta versioni2e4:name1:a12:piece lengthi16384ee")] // unsafe segment
    [InlineData("d9:file treed1:ad0:d6:lengthi1eeee12:meta versioni2e4:name1:a12:piece lengthi16384ee")] // no 'pieces root'
    [InlineData("d9:file treed1:ad0:d6:lengthi1e11:pieces root3:abceeee12:meta versioni2e4:name1:a12:piece lengthi16384ee")] // short 'pieces root'
    [InlineData("d9:file treed1:ad0:d6:lengthi0eeee12:meta versioni2e4:name1:a12:piece lengthi16385ee")] // piece length not a power of two
    [InlineData("d9:file treed1:ad0:d6:lengthi0eeee12:meta versioni2e4:name1:a12:piece lengthi8192ee")] // piece length below 16 KiB
    [InlineData("d9:file treed1:ad0:d6:lengthi0eeee6:lengthi0e12:meta versioni2e4:name1:a12:piece lengthi16384ee")] // partial v1 keys
    public void Parse_InvalidV2Info_Throws(string info)
    {
        Assert.False(TorrentFile.TryParse(Encoding.ASCII.GetBytes("d4:info" + info + "e"), out _));
    }

    [Theory]
    [InlineData("12:piece layersi1e")]
    [InlineData("12:piece layersd3:abc32:01234567890123456789012345678901e")]
    [InlineData("12:piece layersd32:0123456789012345678901234567890131:0123456789012345678901234567890e")]
    [InlineData("12:piece layersd32:01234567890123456789012345678901i1ee")]
    public void Parse_InvalidPieceLayers_Throws(string field)
    {
        var content = Encoding.ASCII.GetBytes("d4:infod6:lengthi123e4:name8:file.txt12:piece lengthi16384e6:pieces20:01234567890123456789e" + field + "e");

        Assert.False(TorrentFile.TryParse(content, out _));
    }

    [Theory]
    [InlineData("4:infod6:lengthi99999999999999999999e4:name1:a12:piece lengthi16384e6:pieces20:01234567890123456789e")]
    [InlineData("4:infod6:lengthi1e4:name1:a12:piece lengthi99999999999999999999e6:pieces20:01234567890123456789e")]
    [InlineData("13:creation datei99999999999999999999e4:infod6:lengthi1e4:name1:a12:piece lengthi16384e6:pieces20:01234567890123456789e")]
    [InlineData("4:infod6:lengthi1e12:meta versioni99999999999e4:name1:a12:piece lengthi16384e6:pieces20:01234567890123456789e")]
    public void Parse_IntegerOutOfRange_Throws(string content)
    {
        Assert.False(TorrentFile.TryParse(Encoding.ASCII.GetBytes("d" + content + "e"), out _));
    }

    [Fact]
    public void Parse_LargePrivateValue_IsPrivate()
    {
        var torrent = TorrentFile.Parse("d4:infod6:lengthi1e4:name1:a12:piece lengthi16384e6:pieces20:012345678901234567897:privatei99999999999999999999eee"u8);

        Assert.True(torrent.Info.IsPrivate);
    }

    [Fact]
    public void Documentation_Encoding()
    {
        #region TorrentFile_Encoding
        // A torrent whose 'encoding' key says its text is in GBK, a Chinese code page
        var gbk = CodePagesEncodingProvider.Instance.GetEncoding("GBK")!;
        var data = Concat("d7:comment4:", gbk.GetBytes("中文"), "8:encoding3:GBK4:infod6:lengthi1e4:name8:", gbk.GetBytes("文件.txt"), "12:piece lengthi16384e6:pieces20:01234567890123456789ee");

        var torrent = TorrentFile.Parse(data);
        Assert.Equal("GBK", torrent.Encoding);
        Assert.Equal("文件.txt", torrent.Info.Name);
        Assert.Equal("中文", torrent.Comment);

        // Writing uses the same encoding
        Assert.Equal(data, torrent.ToUtf8ByteArray());
        #endregion
    }

    [Theory]
    [InlineData("windows-1251", "Привет")]
    [InlineData("shift_jis", "日本語")]
    [InlineData("iso-8859-1", "café")]
    [InlineData("utf-16", "x")]
    public void Encoding_TextFieldsUseTheNamedEncoding(string encodingName, string text)
    {
        var torrent = new TorrentFile
        {
            Encoding = encodingName,
            Comment = text,
            Info = new TorrentInfo { Name = text, PieceLength = 16384, Pieces = "01234567890123456789"u8.ToArray(), Length = 1 },
        };

        var data = torrent.ToUtf8ByteArray();
        var encoding = CodePagesEncodingProvider.Instance.GetEncoding(encodingName) ?? Encoding.GetEncoding(encodingName);
        var name = ((BencodeString)((BencodeDictionary)((BencodeDictionary)BencodeDocument.Parse(data).Root)[new BencodeString("info"u8.ToArray())])[new BencodeString("name"u8.ToArray())]).Value.ToArray();
        Assert.Equal(encoding.GetBytes(text), name);

        var parsed = TorrentFile.Parse(data);
        Assert.Equal(text, parsed.Info.Name);
        Assert.Equal(text, parsed.Comment);
        Assert.Equal(torrent.GetInfoHashSha1(), parsed.GetInfoHashSha1());
    }

    [Theory]
    [InlineData("UTF-8")]
    [InlineData("utf8")]
    [InlineData("not-an-encoding")]
    [InlineData("")]
    public void Encoding_Utf8OrUnknown_TextIsUtf8(string encodingName)
    {
        var torrent = TorrentFile.Parse(Concat("d8:encoding", Encoding.ASCII.GetBytes(encodingName.Length + ":" + encodingName), "4:infod6:lengthi1e4:name5:", Encoding.UTF8.GetBytes("été"), "12:piece lengthi16384e6:pieces20:01234567890123456789ee"));

        Assert.Equal(encodingName, torrent.Encoding);
        Assert.Equal("été", torrent.Info.Name);
    }

    [Fact]
    public void Encoding_InvalidBytes_Throws()
    {
        // 0x81 starts a GBK sequence that a space cannot end
        var data = Concat("d8:encoding3:GBK4:infod6:lengthi1e4:name3:", new byte[] { 0x61, 0x81, 0x20 }, "12:piece lengthi16384e6:pieces20:01234567890123456789ee");

        Assert.False(TorrentFile.TryParse(data, out _));
    }

    [Fact]
    public void Encoding_UnencodableCharacters_Throws()
    {
        var torrent = TorrentFile.Parse(SingleFileTorrent);
        torrent.Encoding = "windows-1252";
        torrent.Comment = "日本";

        Assert.Throws<FormatException>(() => torrent.ToUtf8ByteArray());
    }

    [Fact]
    public void PublicApi_DoesNotExposeSyncStreamMethods()
    {
        Assert.Null(typeof(TorrentFile).GetMethod(nameof(TorrentFile.Parse), [typeof(Stream)]));
        Assert.Null(typeof(TorrentFile).GetMethod("WriteTo", [typeof(Stream), typeof(bool)]));
    }

    [Fact]
    public async Task Parse_UbuntuTorrentResource()
    {
        await using var stream = OpenUbuntuTorrentResourceStream();
        var torrent = await TorrentFile.ParseAsync(stream);

        Assert.Equal("https://torrent.ubuntu.com/announce", torrent.Announce);
        Assert.Equal("Ubuntu CD releases.ubuntu.com", torrent.Comment);
        Assert.Equal("mktorrent 1.1", torrent.CreatedBy);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1776959050), torrent.CreationDate);

        Assert.NotNull(torrent.AnnounceList);
        Assert.Equal(2, torrent.AnnounceList.Count);
        Assert.Equal(["https://torrent.ubuntu.com/announce"], torrent.AnnounceList[0]);
        Assert.Equal(["https://ipv6.torrent.ubuntu.com/announce"], torrent.AnnounceList[1]);

        Assert.Equal("ubuntu-26.04-live-server-amd64.iso", torrent.Info.Name);
        Assert.Equal(262144, torrent.Info.PieceLength);
        Assert.Equal(2918598656, torrent.Info.Length);
        Assert.Null(torrent.Info.Files);
        Assert.Equal(222680, torrent.Info.Pieces.Length);

        Assert.Equal(Convert.FromHexString("e1fc140a6391357fa1cf08ddb70274f9c05eb88b"), torrent.GetInfoHashSha1());
        Assert.Equal(Convert.FromHexString("25815c7847dc512b89e0d5e33a31ab1d950e551e26c7e82eb2ff91a79e6c8072"), torrent.GetInfoHashSha256());
    }

    private static Stream OpenUbuntuTorrentResourceStream() => OpenResourceStream("ubuntu.torrent");

    private static Stream OpenResourceStream(string fileName)
    {
        var assembly = typeof(TorrentFileTests).Assembly;
        return assembly.GetManifestResourceStream("files/" + fileName)!;
    }

    private static async Task<byte[]> ReadResourceAsync(string fileName)
    {
        await using var stream = OpenResourceStream(fileName);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    private static byte[] Concat(params object[] parts)
    {
        return [.. parts.SelectMany(part => part switch
        {
            string text => Encoding.ASCII.GetBytes(text),
            byte[] bytes => bytes,
            _ => throw new ArgumentOutOfRangeException(nameof(parts)),
        })];
    }
}
