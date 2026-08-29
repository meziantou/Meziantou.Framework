using System.Security.Cryptography;
using Meziantou.Framework.Bencode.Torrent;

namespace Meziantou.Framework.Bencode.Tests;

public sealed class TorrentFileTests
{
    private static readonly byte[] SingleFileTorrent = Encoding.ASCII.GetBytes("d8:announce14:https://t.test13:creation datei1700000000e4:infod6:lengthi123e4:name8:file.txt12:piece lengthi16384e6:pieces20:01234567890123456789ee");
    private static readonly byte[] SingleFileInfo = Encoding.ASCII.GetBytes("d6:lengthi123e4:name8:file.txt12:piece lengthi16384e6:pieces20:01234567890123456789e");

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

    private static Stream OpenUbuntuTorrentResourceStream()
    {
        var assembly = typeof(TorrentFileTests).Assembly;
        return assembly.GetManifestResourceStream("files/ubuntu.torrent")!;
    }
}
