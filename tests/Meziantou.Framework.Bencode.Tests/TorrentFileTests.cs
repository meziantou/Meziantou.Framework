using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
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
