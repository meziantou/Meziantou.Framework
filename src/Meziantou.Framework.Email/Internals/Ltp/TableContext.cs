using System.Buffers.Binary;
using Meziantou.Framework.Email.Internals.Ndb;

namespace Meziantou.Framework.Email.Internals.Ltp;

/// <summary>A table context ([MS-PST] 2.3.4): rows of properties, such as the subfolders or the messages of a folder.</summary>
internal sealed class TableContext
{
    public const byte Signature = 0x7C;

    private readonly byte[][] _rowBlocks;
    private readonly int[] _rowBlockStarts;

    public TableContext(NdbNode node)
        : this(new HeapOnNode(node))
    {
    }

    public TableContext(HeapOnNode heap)
    {
        var node = heap.Node;
        Heap = heap;
        if (Heap.ClientSignature != Signature)
        {
            throw new InvalidPstException($"Node 0x{node.Nid:X} is not a table context (signature 0x{Heap.ClientSignature:X2}).");
        }

        var info = Heap.GetItem(Heap.UserRoot);
        if (info.Length < 22 || info[0] != Signature)
        {
            throw new InvalidPstException($"The table context of node 0x{node.Nid:X} has an invalid header.");
        }

        int columnCount = info[1];
        if (info.Length < 22 + (columnCount * 8))
        {
            throw new InvalidPstException($"The table context of node 0x{node.Nid:X} has an invalid header.");
        }

        End4Bytes = BinaryPrimitives.ReadUInt16LittleEndian(info[2..]);
        End2Bytes = BinaryPrimitives.ReadUInt16LittleEndian(info[4..]);
        End1Byte = BinaryPrimitives.ReadUInt16LittleEndian(info[6..]);
        RowSize = BinaryPrimitives.ReadUInt16LittleEndian(info[8..]);
        var hidRowIndex = BinaryPrimitives.ReadUInt32LittleEndian(info[10..]);
        var hnidRows = BinaryPrimitives.ReadUInt32LittleEndian(info[14..]);
        if (End4Bytes > End2Bytes || End2Bytes > End1Byte || End1Byte > RowSize || RowSize < 4)
        {
            throw new InvalidPstException($"The table context of node 0x{node.Nid:X} has an invalid row layout.");
        }

        var columns = new TableColumn[columnCount];
        for (var i = 0; i < columnCount; i++)
        {
            var descriptor = info.Slice(22 + (i * 8), 8);
            var column = new TableColumn(
                MapiPropertyTag.FromValue(BinaryPrimitives.ReadUInt32LittleEndian(descriptor)),
                BinaryPrimitives.ReadUInt16LittleEndian(descriptor[4..]),
                descriptor[6],
                descriptor[7]);
            if (column.Offset + column.Size > End1Byte || End1Byte + (column.CellExistenceBit / 8) >= RowSize)
            {
                throw new InvalidPstException($"The column {column.Tag} of the table context of node 0x{node.Nid:X} is outside the row.");
            }

            columns[i] = column;
        }

        Columns = columns;

        // The row index maps the identifier of each row to its position in the row matrix
        var rowIndex = new List<(uint RowId, int Index)>();
        if (hidRowIndex != 0)
        {
            var tree = new BTreeOnHeap(Heap, hidRowIndex, node.Reader.Options.MaxTreeDepth);
            if (tree.KeySize != 4 || tree.DataSize is not (2 or 4))
            {
                throw new InvalidPstException($"The row index of the table context of node 0x{node.Nid:X} has records of an invalid size.");
            }

            tree.ForEach((key, data) =>
            {
                var index = data.Length == 2 ? BinaryPrimitives.ReadUInt16LittleEndian(data) : (int)BinaryPrimitives.ReadUInt32LittleEndian(data);
                rowIndex.Add((BinaryPrimitives.ReadUInt32LittleEndian(key), index));
            });
        }

        rowIndex.Sort((a, b) => a.Index.CompareTo(b.Index));
        RowIds = rowIndex;

        // The row matrix: in the heap when it is small, in a subnode otherwise, where a row never spans two blocks
        if (rowIndex.Count == 0 || hnidRows == 0)
        {
            _rowBlocks = [];
        }
        else if (HeapOnNode.IsHid(hnidRows))
        {
            _rowBlocks = [Heap.GetItem(hnidRows).ToArray()];
        }
        else
        {
            var subnode = node.GetSubnode(hnidRows) ?? throw new InvalidPstException($"The row matrix 0x{hnidRows:X} of the table context of node 0x{node.Nid:X} does not exist.");
            _rowBlocks = subnode.ReadDataBlocks();
        }

        _rowBlockStarts = new int[_rowBlocks.Length + 1];
        for (var i = 0; i < _rowBlocks.Length; i++)
        {
            _rowBlockStarts[i + 1] = _rowBlockStarts[i] + (_rowBlocks[i].Length / RowSize);
        }

        foreach (var (rowId, index) in rowIndex)
        {
            if (index < 0 || index >= _rowBlockStarts[^1])
            {
                throw new InvalidPstException($"The row 0x{rowId:X} of the table context of node 0x{node.Nid:X} is outside the row matrix.");
            }
        }
    }

    public HeapOnNode Heap { get; }

    public int End4Bytes { get; }

    public int End2Bytes { get; }

    public int End1Byte { get; }

    public int RowSize { get; }

    public IReadOnlyList<TableColumn> Columns { get; }

    /// <summary>The identifier and the position of the rows, in the order of the row matrix.</summary>
    public IReadOnlyList<(uint RowId, int Index)> RowIds { get; }

    public int RowCount => RowIds.Count;

    public uint GetRowId(int row) => RowIds[row].RowId;

    /// <summary>Returns the properties of a row (the cells that exist).</summary>
    public List<RawProperty> GetRow(int row)
    {
        var data = GetRowData(RowIds[row].Index);
        var result = new List<RawProperty>(Columns.Count);
        foreach (var column in Columns)
        {
            var bitmapByte = data[End1Byte + (column.CellExistenceBit / 8)];
            if ((bitmapByte & (0x80 >> (column.CellExistenceBit % 8))) == 0)
            {
                continue;
            }

            var cell = data.Slice(column.Offset, column.Size);
            var fixedSize = PropertyTypes.GetFixedSize(column.Tag.Type);
            if (fixedSize > 0 && fixedSize <= column.Size && !(fixedSize == 16 && column.Size == 4))
            {
                result.Add(RawProperty.FromInline(column.Tag, cell[..fixedSize].ToArray()));
            }
            else if (column.Size == 4)
            {
                result.Add(RawProperty.FromHnid(column.Tag, BinaryPrimitives.ReadUInt32LittleEndian(cell)));
            }
            else
            {
                throw new InvalidPstException($"The column {column.Tag} of the table context of node 0x{Heap.Node.Nid:X} has an invalid size ({column.Size}).");
            }
        }

        return result;
    }

    private ReadOnlySpan<byte> GetRowData(int index)
    {
        var block = Array.BinarySearch(_rowBlockStarts, index);
        if (block < 0)
        {
            block = ~block - 1;
        }

        while (block + 1 < _rowBlockStarts.Length && _rowBlockStarts[block + 1] <= index)
        {
            block++;
        }

        return _rowBlocks[block].AsSpan((index - _rowBlockStarts[block]) * RowSize, RowSize);
    }
}
