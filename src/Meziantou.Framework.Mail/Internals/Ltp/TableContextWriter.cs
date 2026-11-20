using System.Buffers.Binary;
using Meziantou.Framework.Mail.Internals.Ndb;

namespace Meziantou.Framework.Mail.Internals.Ltp;

/// <summary>Builds a table context ([MS-PST] 2.3.4) in the content of a node.</summary>
internal static class TableContextWriter
{
    public static readonly PstPropertyTag RowIdTag = new(PropertyIds.LtpRowId, PstPropertyType.Integer32);
    public static readonly PstPropertyTag RowVersionTag = new(PropertyIds.LtpRowVersion, PstPropertyType.Integer32);

    /// <summary>Writes a table.</summary>
    /// <param name="node">The node of the table.</param>
    /// <param name="columns">The columns, besides the row identifier and the row version, which every table has.</param>
    /// <param name="rows">The rows: the row identifier (the NID of the object of the row) and the values of the cells that exist.</param>
    /// <param name="layout">The layout of the file.</param>
    public static void Write(NodeContent node, IEnumerable<PstPropertyTag> columns, IReadOnlyList<(uint RowId, IReadOnlyList<WriterProperty> Cells)> rows, NdbLayout layout)
    {
        var maxBlockSize = layout.MaxLeafDataSize;
        var heap = new HeapWriter(TableContext.Signature, maxBlockSize);

        // Outlook allocates the header of the row index (HID 0x20), then the TCINFO (0x40): some readers expect them there
        var hidRowIndex = heap.Allocate(new byte[8]);

        // The cells of the rows can add columns (the properties Outlook copies from the objects of the rows)
        var allColumns = new HashSet<PstPropertyTag>(columns);
        foreach (var (_, cells) in rows)
        {
            foreach (var cell in cells)
            {
                allColumns.Add(cell.Tag);
            }
        }

        allColumns.Remove(RowIdTag);
        allColumns.Remove(RowVersionTag);

        // Layout: the row id and the row version, then the 8- and 4-byte cells, the 2-byte cells, the 1-byte cells and the cell existence bitmap
        var ordered = allColumns.OrderBy(column => column.Value).ToList();
        var descriptors = new List<TableColumn> { new(RowIdTag, 0, 4, 0), new(RowVersionTag, 4, 4, 1) };
        var offset = 8;
        var bit = 2;
        foreach (var size in new[] { 8, 4, 2, 1 })
        {
            foreach (var column in ordered.Where(column => GetCellSize(column.Type) == size))
            {
                descriptors.Add(new TableColumn(column, offset, size, bit++));
                offset += size;
            }
        }

        var end4 = descriptors.Where(d => d.Size >= 4).Max(d => d.Offset + d.Size);
        var end2 = Math.Max(end4, descriptors.Where(d => d.Size == 2).Select(d => d.Offset + d.Size).DefaultIfEmpty(0).Max());
        var end1 = Math.Max(end2, descriptors.Where(d => d.Size == 1).Select(d => d.Offset + d.Size).DefaultIfEmpty(0).Max());
        var rowSize = end1 + ((descriptors.Count + 7) / 8);
        if (descriptors.Count > 255)
        {
            throw new InvalidOperationException("A table cannot have more than 255 columns.");
        }

        var hidInfo = heap.Allocate(new byte[22 + (descriptors.Count * 8)]);

        // The rows
        var byTag = descriptors.ToDictionary(d => d.Tag);
        var matrix = new List<byte[]>(rows.Count);
        var seen = new HashSet<uint>();
        foreach (var (rowId, cells) in rows)
        {
            if (!seen.Add(rowId))
            {
                throw new InvalidOperationException($"The row 0x{rowId:X} is added twice to a table.");
            }

            var row = new byte[rowSize];
            BinaryPrimitives.WriteUInt32LittleEndian(row, rowId);
            row[end1] |= 0xC0; // The row id and the row version always exist
            foreach (var cell in cells)
            {
                if (cell.Tag == RowIdTag || cell.Tag == RowVersionTag)
                {
                    continue;
                }

                var descriptor = byTag[cell.Tag];
                var fixedSize = PropertyTypes.GetFixedSize(cell.Tag.Type);
                if (cell.Stream is null && fixedSize > 0 && fixedSize <= 8)
                {
                    cell.Value.AsSpan(0, Math.Min(fixedSize, cell.Value?.Length ?? 0)).CopyTo(row.AsSpan(descriptor.Offset));
                }
                else
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(descriptor.Offset), PropertyContextWriter.StoreValue(heap, node, cell));
                }

                row[end1 + (descriptor.CellExistenceBit / 8)] |= (byte)(0x80 >> (descriptor.CellExistenceBit % 8));
            }

            matrix.Add(row);
        }

        // The row matrix: in the heap when it is small, in a subnode otherwise, where every block but the last is full
        uint hnidRows = 0;
        if (matrix.Count > 0)
        {
            if (matrix.Count * rowSize <= HeapWriter.MaxItemSize)
            {
                hnidRows = heap.Allocate(matrix.SelectMany(row => row).ToArray());
            }
            else
            {
                var rowsPerBlock = maxBlockSize / rowSize;
                var blocks = new List<byte[]>();
                for (var start = 0; start < matrix.Count; start += rowsPerBlock)
                {
                    var count = Math.Min(rowsPerBlock, matrix.Count - start);
                    var block = new byte[start + count < matrix.Count ? maxBlockSize : count * rowSize];
                    for (var i = 0; i < count; i++)
                    {
                        matrix[start + i].CopyTo(block, i * rowSize);
                    }

                    blocks.Add(block);
                }

                hnidRows = node.AllocateSubnodeNid(NidType.Ltp);
                node.Subnodes[hnidRows] = new NodeContent { HeapBlocks = blocks };
            }
        }

        // The row index: the row identifiers, sorted, with their position in the row matrix
        // The row index of an ANSI file stores the positions on 16 bits
        var indexDataSize = layout.IsAnsi ? 2 : 4;
        if (layout.IsAnsi && rows.Count > ushort.MaxValue + 1)
        {
            throw new InvalidOperationException("A table of an ANSI file cannot have more than 65,536 rows.");
        }
        var index = rows.Select((row, position) =>
        {
            var key = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(key, row.RowId);
            var data = new byte[indexDataSize];
            if (indexDataSize == 2)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(data, (ushort)position);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(data, (uint)position);
            }

            return (Key: key, Data: data, RowId: row.RowId);
        }).OrderBy(entry => entry.RowId).Select(entry => (entry.Key, entry.Data)).ToList();
        BTreeOnHeapWriter.Write(heap, index, 4, indexDataSize, hidRowIndex);

        var info = new byte[22 + (descriptors.Count * 8)];
        info[0] = TableContext.Signature;
        info[1] = (byte)descriptors.Count;
        BinaryPrimitives.WriteUInt16LittleEndian(info.AsSpan(2), (ushort)end4);
        BinaryPrimitives.WriteUInt16LittleEndian(info.AsSpan(4), (ushort)end2);
        BinaryPrimitives.WriteUInt16LittleEndian(info.AsSpan(6), (ushort)end1);
        BinaryPrimitives.WriteUInt16LittleEndian(info.AsSpan(8), (ushort)rowSize);
        BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(10), hidRowIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(14), hnidRows);
        var i2 = 0;
        foreach (var descriptor in descriptors.OrderBy(d => d.Tag.Value))
        {
            var span = info.AsSpan(22 + (i2++ * 8));
            BinaryPrimitives.WriteUInt32LittleEndian(span, descriptor.Tag.Value);
            BinaryPrimitives.WriteUInt16LittleEndian(span[4..], (ushort)descriptor.Offset);
            span[6] = (byte)descriptor.Size;
            span[7] = (byte)descriptor.CellExistenceBit;
        }

        heap.Update(hidInfo, info);
        heap.UserRoot = hidInfo;
        node.HeapBlocks = heap.Build();
    }

    /// <summary>Returns the size of the cell of a column: the value of fixed-size types up to 8 bytes, an HNID (4 bytes) otherwise.</summary>
    public static int GetCellSize(PstPropertyType type)
    {
        var size = PropertyTypes.GetFixedSize(type);
        return size is > 0 and <= 8 ? size : 4;
    }
}
