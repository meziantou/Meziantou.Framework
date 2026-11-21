using System.Buffers.Binary;

namespace Meziantou.Framework.Email.Internals.Ltp;

/// <summary>Builds a B-tree-on-heap ([MS-PST] 2.3.2) from records sorted by key.</summary>
internal static class BTreeOnHeapWriter
{
    /// <summary>Allocates the records and the index levels in the heap, and returns the HID of the BTHHEADER.</summary>
    /// <remarks>Outlook allocates the header first: some readers expect it at the first HID (0x20) of the heap.</remarks>
    public static uint Write(HeapWriter heap, IReadOnlyList<(byte[] Key, byte[] Data)> records, int keySize, int dataSize, uint headerHid = 0)
    {
        if (headerHid == 0)
        {
            headerHid = heap.Allocate(new byte[8]);
        }

        uint root = 0;
        var levels = 0;
        if (records.Count > 0)
        {
            var recordSize = keySize + dataSize;
            var perItem = HeapWriter.MaxItemSize / recordSize;
            var level = new List<(byte[] Key, uint Hid)>();
            for (var start = 0; start < records.Count; start += perItem)
            {
                var count = Math.Min(perItem, records.Count - start);
                var item = new byte[count * recordSize];
                for (var i = 0; i < count; i++)
                {
                    records[start + i].Key.CopyTo(item, i * recordSize);
                    records[start + i].Data.CopyTo(item, (i * recordSize) + keySize);
                }

                level.Add((records[start].Key, heap.Allocate(item)));
            }

            var entrySize = keySize + 4;
            var perIndex = HeapWriter.MaxItemSize / entrySize;
            while (level.Count > 1)
            {
                levels++;
                var next = new List<(byte[] Key, uint Hid)>();
                for (var start = 0; start < level.Count; start += perIndex)
                {
                    var count = Math.Min(perIndex, level.Count - start);
                    var item = new byte[count * entrySize];
                    for (var i = 0; i < count; i++)
                    {
                        level[start + i].Key.CopyTo(item, i * entrySize);
                        BinaryPrimitives.WriteUInt32LittleEndian(item.AsSpan((i * entrySize) + keySize), level[start + i].Hid);
                    }

                    next.Add((level[start].Key, heap.Allocate(item)));
                }

                level = next;
            }

            root = level[0].Hid;
        }

        var header = new byte[8];
        header[0] = BTreeOnHeap.Signature;
        header[1] = (byte)keySize;
        header[2] = (byte)dataSize;
        header[3] = (byte)levels;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), root);
        heap.Update(headerHid, header);
        return headerHid;
    }
}
