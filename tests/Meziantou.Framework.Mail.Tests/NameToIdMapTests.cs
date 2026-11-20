using System.Buffers.Binary;
using System.Text;
using Meziantou.Framework.Mail.Internals;
using Meziantou.Framework.Mail.Internals.Ltp;
using Meziantou.Framework.Mail.Internals.Ndb;

namespace Meziantou.Framework.Mail.Tests;

public sealed class NameToIdMapTests
{
    public static TheoryData<string> OutlookFiles => ["java-libpst-dist-list.pst", "libyal-outlook.pst", "outlook-pst-rs-Empty.pst", "tika-variousBodyTypes.pst", "java-libpst-example-2013.ost"];

    /// <summary>The hash buckets of the files Outlook wrote contain each entry of the entry stream, with the hash the writer computes.</summary>
    [Theory]
    [MemberData(nameof(OutlookFiles))]
    public void Buckets_MatchOutlook(string fileName)
    {
        using var file = PstFile.Open(PstOracleTests.GetPath(fileName));
        var node = file.Reader.GetNode(Nids.NameToIdMap)!;
        var pc = new PropertyContext(node);
        var source = new PropertySource(file.Context, pc.Heap, node);
        byte[] Get(ushort id) => pc.Properties.FirstOrDefault(p => p.Tag.Id == id) is { Tag.Type: PstPropertyType.Binary } p ? source.ReadBytes(p) : [];

        var entries = Get(NameToIdMap.EntryStreamId);
        var strings = Get(NameToIdMap.StringStreamId);
        var bucketCount = pc.Properties.First(p => p.Tag.Id == NameToIdMap.BucketCountId).Inline!;
        var count = BinaryPrimitives.ReadInt32LittleEndian(bucketCount);
        var buckets = new Dictionary<int, HashSet<ulong>>();
        for (var i = 0; i < count; i++)
        {
            var bucket = Get((ushort)(0x1000 + i));
            var set = new HashSet<ulong>();
            for (var offset = 0; offset + 8 <= bucket.Length; offset += 8)
            {
                set.Add(BinaryPrimitives.ReadUInt64LittleEndian(bucket.AsSpan(offset)));
            }

            buckets[i] = set;
        }

        Assert.Equal(251, count);
        for (var offset = 0; offset + 8 <= entries.Length; offset += 8)
        {
            var record = NameToIdMapWriter.CreateBucketRecord(entries.AsSpan(offset, 8), strings);
            var bucket = NameToIdMapWriter.GetBucket(record, count);
            Assert.Contains(BinaryPrimitives.ReadUInt64LittleEndian(record), buckets[bucket]);
        }
    }
}
