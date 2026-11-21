using System.Buffers.Binary;

namespace Meziantou.Framework.Email.Internals.Ndb;

/// <summary>The header of the file ([MS-PST] 2.2.2.6).</summary>
internal sealed class NdbHeader
{
    public const uint Magic = 0x4E444221; // "!BDN"
    public const ushort MagicClientPst = 0x4D53; // "SM"
    public const ushort MagicClientOst = 0x4F53; // "SO"

    public required NdbLayout Layout { get; init; }

    public required ushort MagicClient { get; init; }

    public required ushort Version { get; init; }

    public required ushort ClientVersion { get; init; }

    public required PstEncryption Encryption { get; init; }

    public required ulong NextBid { get; init; }

    public required ulong NextPageBid { get; init; }

    public required uint Unique { get; init; }

    public required uint[] NextNids { get; init; }

    public required ulong FileEof { get; init; }

    public required ulong LastAMapOffset { get; init; }

    public required ulong AMapFree { get; init; }

    public required ulong PMapFree { get; init; }

    public required Bref NodeBTreeRoot { get; init; }

    public required Bref BlockBTreeRoot { get; init; }

    public required byte AMapValid { get; init; }

    public static NdbHeader Read(ReadOnlySpan<byte> data, PstReadOptions options)
    {
        if (data.Length < 512 || BinaryPrimitives.ReadUInt32LittleEndian(data) != Magic)
        {
            throw new InvalidPstException("The file is not an Outlook data file: the signature '!BDN' is missing.");
        }

        var magicClient = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
        var version = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]);
        var clientVersion = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
        var layout = version switch
        {
            14 or 15 => NdbLayout.Ansi,
            23 => NdbLayout.Unicode,
            36 => NdbLayout.Unicode4K,
            _ => throw new UnsupportedPstFeatureException($"The version {version} of the file format is not supported."),
        };

        if (data.Length < layout.HeaderSize)
        {
            throw new InvalidPstException("The header of the file is truncated.");
        }

        if (options.VerifyChecksums)
        {
            var partial = PstCrc.Compute(data.Slice(8, 471));
            if (partial != BinaryPrimitives.ReadUInt32LittleEndian(data[4..]))
            {
                throw new InvalidPstException("The CRC of the header does not match its content.");
            }

            if (!layout.IsAnsi && PstCrc.Compute(data.Slice(8, 516)) != BinaryPrimitives.ReadUInt32LittleEndian(data[524..]))
            {
                throw new InvalidPstException("The CRC of the header does not match its content.");
            }
        }

        int cryptOffset;
        ulong nextBid;
        ulong nextPageBid;
        uint unique;
        int nidsOffset;
        int rootOffset;
        if (layout.IsAnsi)
        {
            nextBid = BinaryPrimitives.ReadUInt32LittleEndian(data[24..]);
            nextPageBid = BinaryPrimitives.ReadUInt32LittleEndian(data[28..]);
            unique = BinaryPrimitives.ReadUInt32LittleEndian(data[32..]);
            nidsOffset = 36;
            rootOffset = 164;
            cryptOffset = 461;
        }
        else
        {
            nextPageBid = BinaryPrimitives.ReadUInt64LittleEndian(data[32..]);
            unique = BinaryPrimitives.ReadUInt32LittleEndian(data[40..]);
            nidsOffset = 44;
            rootOffset = 180;
            cryptOffset = 513;
            nextBid = BinaryPrimitives.ReadUInt64LittleEndian(data[516..]);
        }

        var encryption = data[cryptOffset] switch
        {
            0 => PstEncryption.None,
            1 => PstEncryption.Permute,
            2 => PstEncryption.Cyclic,
            var value => throw new UnsupportedPstFeatureException($"The encryption method 0x{value:X2} is not supported."),
        };

        var nids = new uint[32];
        for (var i = 0; i < nids.Length; i++)
        {
            nids[i] = BinaryPrimitives.ReadUInt32LittleEndian(data[(nidsOffset + (i * 4))..]);
        }

        var root = data[rootOffset..];
        var size = layout.IdSize;
        return new NdbHeader
        {
            Layout = layout,
            MagicClient = magicClient,
            Version = version,
            ClientVersion = clientVersion,
            Encryption = encryption,
            NextBid = nextBid,
            NextPageBid = nextPageBid,
            Unique = unique,
            NextNids = nids,
            FileEof = ReadId(root[4..], size),
            LastAMapOffset = ReadId(root[(4 + size)..], size),
            AMapFree = ReadId(root[(4 + (2 * size))..], size),
            PMapFree = ReadId(root[(4 + (3 * size))..], size),
            NodeBTreeRoot = new Bref(ReadId(root[(4 + (4 * size))..], size), ReadId(root[(4 + (5 * size))..], size)),
            BlockBTreeRoot = new Bref(ReadId(root[(4 + (6 * size))..], size), ReadId(root[(4 + (7 * size))..], size)),
            AMapValid = root[4 + (8 * size)],
        };
    }

    public static ulong ReadId(ReadOnlySpan<byte> data, int size) => size == 4 ? BinaryPrimitives.ReadUInt32LittleEndian(data) : BinaryPrimitives.ReadUInt64LittleEndian(data);
}
