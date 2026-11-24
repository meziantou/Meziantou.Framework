using System.Buffers;
using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Unicode;

namespace Meziantou.Framework.Csv.Internals;

/// <summary>Splits UTF-16 (<see cref="char"/>) or UTF-8 (<see cref="byte"/>) CSV data into records and columns.</summary>
/// <remarks>
/// <para>
/// The data is read into a buffer, which is scanned in batches of records: one pass finds the separators, the quotes and the line terminators of up to
/// <see cref="MaxBatchRows"/> records with SIMD, and stores where each record starts and where each of its columns ends. Reading a record then only takes
/// the next entry of the batch. The SIMD masks are kept from one record to the next, so each element is compared once, and the separators are found
/// from the masks without reading the data again.
/// </para>
/// <para>
/// The values are slices of the buffer; a quoted value that contains escaped quotes is unescaped into a scratch buffer the first time it is read. The
/// scan state is kept when a record does not fit in the buffer, so a long record is never scanned twice. The buffer is only moved or grown when every
/// record of the batch has been read.
/// </para>
/// </remarks>
internal sealed class CsvParser<T> : IDisposable
    where T : unmanaged, IBinaryInteger<T>
{
    private const int MaxBatchRows = 128;

    private static readonly ConcurrentDictionary<int, SearchValues<T>> SpecialValuesCache = new();
    private static readonly T Cr = T.CreateTruncating('\r');
    private static readonly T Lf = T.CreateTruncating('\n');
    private static readonly T Space = T.CreateTruncating(' ');

    private readonly CsvSource<T>? _source;
    private readonly bool _hasQuote;
    private readonly T _quote;
    private readonly bool _hasComment;
    private readonly T _comment;
    private readonly bool _trimOuter;
    private readonly bool _trimInside;
    private readonly bool _skipEmptyLines;
    private readonly bool _validateColumnCount;
    private readonly bool _strict;
    private readonly CsvBadDataHandler? _badDataHandler;
    private readonly bool _hasHeaderRow;
    private readonly int _maxRowLength;
    private readonly IEqualityComparer<string> _columnNameComparer;
    private readonly CsvStringPool? _stringPool;
    private int _vectorSize;

    private T _separator;
    private byte _separatorByte;
    private byte _quoteByte;
    private bool _detectSeparator;
    private SearchValues<T> _specialValues;

    // Data: the valid elements are _data[.._dataEnd]. In streaming mode, _data is _buffer.
    private T[]? _buffer;
    private ReadOnlyMemory<T> _data;

    // The array or the string behind _data, to get the span of a record without the cost of ReadOnlyMemory<T>.Span
    private T[]? _array;
    private readonly int _arrayOffset;
    private readonly string? _string;
    private readonly int _stringOffset;
    private int _dataEnd;
    private bool _endOfData;
    private long _bufferOffset; // Position in the data of _data[0]
    private bool _bomChecked;
    private bool _disposed;

    // Batch of scanned records. The columns of the records of the batch, then the columns of the record being scanned, are stored in _columnEnds.
    private RowEntry[] _rows;
    private int _batchCount;
    private int _batchIndex;
    private int[] _columnEnds;
    private int _columnTotal;

    // Current record
    private int _rowStart;
    private int _rowLength;
    private int _rowColumnStart;
    private int _columnCount;
    private bool _rowIsPlain;
    private long _lineNumber;
    private long _recordIndex = -1;
    private int _expectedColumnCount = -1;
    private CsvHeader? _header;

    // State of the scan, kept when more data is needed
    private ScanState _scanState;
    private int _scanRowStart;
    private int _scanPosition;
    private bool _scanInQuote;
    private int _scanLineTerminators;
    private bool _scanRowHasQuote;
    private int _scanColumnStart;
    private bool _scanSkipRow; // The bad data handler asked to skip the record being scanned
    private long _nextLineNumber = 1;

    // Unescaped values of the current record
    private T[]? _scratch;
    private int _scratchUsed;
    private int _rowVersion;
    private UnescapedValue[] _unescapedValues = [];

    public CsvParser(CsvReaderOptions options, CsvSource<T>? source, ReadOnlyMemory<T> data)
    {
        CsvReaderOptions.Validate(options, utf8: typeof(T) == typeof(byte));

        // Rented once the options are valid: invalid options throw before anything needs to go back to the pool
        _rows = ArrayPool<RowEntry>.Shared.Rent(MaxBatchRows);
        _columnEnds = ArrayPool<int>.Shared.Rent(256);

        _source = source;
        _hasQuote = options.Quote.HasValue;
        _quote = T.CreateTruncating(options.Quote.GetValueOrDefault());
        _hasComment = options.CommentCharacter.HasValue;
        _comment = T.CreateTruncating(options.CommentCharacter.GetValueOrDefault());
        _trimOuter = options.Trim.HasFlag(CsvTrimOptions.Outer);
        _trimInside = options.Trim.HasFlag(CsvTrimOptions.InsideQuotes);
        _skipEmptyLines = options.SkipEmptyLines;
        _validateColumnCount = options.ValidateColumnCount;
        _badDataHandler = options.BadDataHandler;

        // The handler decides what to do with the errors of strict mode: they must be detected
        _strict = options.Strict || _badDataHandler is not null;
        _hasHeaderRow = options.HasHeaderRow;
        _maxRowLength = options.MaxRowLength;
        _vectorSize = CsvSimd.Resolve(options.VectorSize);
        _columnNameComparer = options.ColumnNameComparer;
        _stringPool = options.PoolStrings ? new CsvStringPool() : null;
        FormatProvider = options.FormatProvider ?? CultureInfo.InvariantCulture;

        _detectSeparator = options.Separator is null;
        SetSeparator(options.Separator ?? CsvReaderOptions.DefaultSeparator);

        if (source is null)
        {
            _data = data;
            if (unsafe(MemoryMarshal.TryGetArray(data, out var segment)))
            {
                _array = segment.Array;
                _arrayOffset = segment.Offset;
            }
            else if (typeof(T) == typeof(char) && MemoryMarshal.TryGetString(unsafe(Unsafe.As<ReadOnlyMemory<T>, ReadOnlyMemory<char>>(ref data)), out var text, out var textStart, out _))
            {
                _string = text;
                _stringOffset = textStart;
            }

            _dataEnd = data.Length;
            _endOfData = true;
        }
        else
        {
            _buffer = ArrayPool<T>.Shared.Rent(options.BufferSize);
            _data = _buffer;
            _array = _buffer;
        }
    }

    private enum ScanState
    {
        Start,
        Comment,
        Scanning,
    }

    private enum ParseStatus
    {
        Record,
        NeedMoreData,
        EndOfData,
    }

    public IFormatProvider FormatProvider { get; }

    public CsvHeader? Header => _header;

    public int ColumnCount => _columnCount;

    public long RecordIndex => _recordIndex;

    public long LineNumber => _lineNumber;

    public long Position => _bufferOffset + _rowStart;

    public char Separator => ToChar(_separator);

    public ReadOnlySpan<T> RowSpan
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_array is not null)
                return new ReadOnlySpan<T>(_array, _arrayOffset + _rowStart, _rowLength);

            if (typeof(T) == typeof(char) && _string is not null)
            {
                var text = _string.AsSpan(_stringOffset + _rowStart, _rowLength);
                return unsafe(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<char, T>(ref MemoryMarshal.GetReference(text)), text.Length));
            }

            return _data.Span.Slice(_rowStart, _rowLength);
        }
    }

    // The values of the current record are its raw values: it has no quote character and the values are not trimmed
    public bool RowIsPlain => _rowIsPlain;

    // -1, then the end of each column of the current record, relative to the start of the record: column i is between ends[i] + 1 and ends[i + 1]
    public ReadOnlySpan<int> RowColumnEnds => new(_columnEnds, _rowColumnStart, _columnCount + 1);

    [MemberNotNull(nameof(_specialValues))]
    private void SetSeparator(char separator)
    {
        _separator = T.CreateTruncating(separator);
        _separatorByte = (byte)separator;

        // Without a quote character, the SIMD path looks for line feeds twice instead
        _quoteByte = _hasQuote ? byte.CreateTruncating(_quote) : (byte)'\n';

        // The SIMD path saturates UTF-16 code units to 0xFF or 0x00 (see CsvSimd): a separator or a quote that is U+0000 or from U+00FF would match
        // characters above U+00FE
        if (typeof(T) == typeof(char) && (separator is '\0' or >= (char)0xFF || (_hasQuote && ToChar(_quote) is '\0' or >= (char)0xFF)))
        {
            _vectorSize = 0;
        }

        _specialValues = GetSpecialValues(separator, _hasQuote ? ToChar(_quote) : separator);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Read()
    {
        // Fast path: the next record of the batch, when there is no header to read and no column count to check
        if (_batchIndex < _batchCount && !_validateColumnCount && (_header is not null || !_hasHeaderRow))
        {
            PopRow();
            _recordIndex++;
            return true;
        }

        return ReadSlow();
    }

    private bool ReadSlow()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (true)
        {
            var status = ParseRow();
            if (status is ParseStatus.NeedMoreData)
            {
                Fill();
                continue;
            }

            if (status is ParseStatus.EndOfData)
                return false;

            if (AcceptRecord())
                return true;
        }
    }

    public ValueTask<bool> ReadAsync(CancellationToken cancellationToken)
    {
        if (_batchIndex < _batchCount && !_validateColumnCount && (_header is not null || !_hasHeaderRow) && !cancellationToken.IsCancellationRequested)
        {
            PopRow();
            _recordIndex++;
            return new ValueTask<bool>(true);
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled<bool>(cancellationToken);

        while (true)
        {
            var status = ParseRow();
            if (status is ParseStatus.NeedMoreData)
                return ReadAsyncCore(cancellationToken);

            if (status is ParseStatus.EndOfData)
                return new ValueTask<bool>(false);

            if (AcceptRecord())
                return new ValueTask<bool>(true);
        }
    }

    private async ValueTask<bool> ReadAsyncCore(CancellationToken cancellationToken)
    {
        while (true)
        {
            await FillAsync(cancellationToken).ConfigureAwait(false);
            while (true)
            {
                var status = ParseRow();
                if (status is ParseStatus.NeedMoreData)
                    break;

                if (status is ParseStatus.EndOfData)
                    return false;

                if (AcceptRecord())
                    return true;
            }
        }
    }

    private bool AcceptRecord()
    {
        if (_hasHeaderRow && _header is null)
        {
            var names = new string[_columnCount];
            for (var i = 0; i < names.Length; i++)
            {
                GetField(i, out var value, out _, out _);
                names[i] = GetString(value);
            }

            _header = new CsvHeader(names, _columnNameComparer);
            if (_validateColumnCount)
            {
                _expectedColumnCount = names.Length;
            }

            return false;
        }

        _recordIndex++;
        if (_validateColumnCount)
        {
            if (_expectedColumnCount < 0)
            {
                _expectedColumnCount = _columnCount;
            }
            else if (_columnCount != _expectedColumnCount)
            {
                var message = $"The record has {_columnCount.ToString(CultureInfo.InvariantCulture)} columns, but {_expectedColumnCount.ToString(CultureInfo.InvariantCulture)} were expected.";
                return ReportBadData(CsvBadDataKind.ColumnCountMismatch, message, _recordIndex, _lineNumber, Position, columnIndex: null);
            }
        }

        return true;
    }

    private ParseStatus ParseRow()
    {
        if (_batchIndex < _batchCount)
        {
            PopRow();
            return ParseStatus.Record;
        }

        // Every record of the batch is read: the columns of the record being scanned move to the start of the array
        _batchIndex = 0;
        _batchCount = 0;
        if (_scanState is ScanState.Scanning && _scanColumnStart > 0)
        {
            var count = _columnTotal - _scanColumnStart;
            Array.Copy(_columnEnds, _scanColumnStart, _columnEnds, 0, count);
            _columnTotal = count;
        }
        else if (_scanState is not ScanState.Scanning)
        {
            _columnTotal = 0;
        }

        _scanColumnStart = 0;

        var data = _data.Span[.._dataEnd];
        if (_scanState is ScanState.Start)
        {
            if (!_bomChecked)
            {
                if (_scanRowStart >= _dataEnd)
                    return _endOfData ? ParseStatus.EndOfData : ParseStatus.NeedMoreData;

                if (!TrySkipByteOrderMark(data))
                    return ParseStatus.NeedMoreData;
            }

            if (_detectSeparator)
            {
                if (_scanRowStart >= _dataEnd)
                    return _endOfData ? ParseStatus.EndOfData : ParseStatus.NeedMoreData;

                if (!TryDetectSeparator(data))
                    return ParseStatus.NeedMoreData;
            }
        }

        ScanBatch(data);
        if (_batchCount > 0)
        {
            PopRow();
            return ParseStatus.Record;
        }

        return _endOfData ? ParseStatus.EndOfData : ParseStatus.NeedMoreData;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void PopRow()
    {
        ref readonly var row = ref _rows[_batchIndex++];
        _rowStart = row.Start;
        _rowLength = row.Length;
        _rowColumnStart = row.ColumnStart;
        _columnCount = row.ColumnCount;
        _lineNumber = row.LineNumber;
        _rowIsPlain = !row.HasQuote && !_trimOuter;
        _rowVersion++;
        _scratchUsed = 0;
    }

    private bool TrySkipByteOrderMark(ReadOnlySpan<T> data)
    {
        // The byte order mark is only skipped at the start of the data
        if (typeof(T) == typeof(byte))
        {
            ReadOnlySpan<byte> bom = [0xEF, 0xBB, 0xBF];
            var bytes = unsafe(MemoryMarshal.Cast<T, byte>(data[_scanRowStart..]));
            if (bytes.Length < bom.Length && !_endOfData && bom.StartsWith(bytes))
                return false;

            if (bytes.StartsWith(bom))
            {
                _scanRowStart += 3;
            }
        }
        else if (data[_scanRowStart] == T.CreateTruncating((char)0xFEFF))
        {
            _scanRowStart++;
        }

        _scanPosition = _scanRowStart;
        _bomChecked = true;
        return true;
    }

    private bool TryDetectSeparator(ReadOnlySpan<T> data)
    {
        const int MaxDetectionLength = 64 * 1024;
        var line = data[_scanRowStart..];
        var lineEnd = line.IndexOfAny(Cr, Lf);
        if (lineEnd < 0 && !_endOfData && line.Length < MaxDetectionLength)
            return false;

        if (lineEnd >= 0)
        {
            line = line[..lineEnd];
        }

        ReadOnlySpan<char> candidates = [',', ';', '\t', '|'];
        Span<int> counts = unsafe(stackalloc int[candidates.Length]);
        counts.Clear();
        var inQuote = false;
        foreach (var value in line)
        {
            if (_hasQuote && value == _quote)
            {
                inQuote = !inQuote;
                continue;
            }

            if (inQuote)
                continue;

            for (var i = 0; i < candidates.Length; i++)
            {
                if (value == T.CreateTruncating(candidates[i]))
                {
                    counts[i]++;
                }
            }
        }

        var best = 0;
        for (var i = 1; i < candidates.Length; i++)
        {
            if (counts[i] > counts[best])
            {
                best = i;
            }
        }

        var separator = candidates[best];
        if (_hasQuote && T.CreateTruncating(separator) == _quote)
        {
            separator = CsvReaderOptions.DefaultSeparator;
        }

        SetSeparator(separator);
        _detectSeparator = false;
        return true;
    }

    /// <summary>Scans the records of the buffer, from the saved scan state, until the batch is full or more data is needed.</summary>
    /// <remarks>
    /// The outer loop starts the records and skips the comment lines; the inner loop goes through the special characters of a record. Only the state
    /// used for every special character is in locals, so that the JIT keeps it in registers: the options, the line numbers and the batch, used once
    /// per record or less, are read from the fields. The SIMD masks are kept from one record to the next.
    /// </remarks>
    private void ScanBatch(ReadOnlySpan<T> data)
    {
        var end = data.Length;
        ref var dataRef = ref unsafe(MemoryMarshal.GetReference(data));

        var state = _scanState;
        var rowStart = _scanRowStart;
        var position = _scanPosition;
        var inQuote = _scanInQuote;
        var columnStart = _scanColumnStart;
        var columnEnds = _columnEnds;
        var columnTotal = _columnTotal;

        ulong mask = 0;
        ulong separators = 0;
        var maskBase = 0;
        var nextBlock = position;
        ulong pendingMask = 0;
        ulong pendingSeparators = 0;
        var pendingBase = -1;
        while (true)
        {
            if (state is not ScanState.Scanning)
            {
                if (state is ScanState.Start)
                {
                    if (_batchCount == MaxBatchRows || rowStart >= end)
                    {
                        position = rowStart;
                        goto Save;
                    }

                    position = rowStart;
                    if (_hasComment && unsafe(Unsafe.Add(ref dataRef, rowStart)) == _comment)
                    {
                        state = ScanState.Comment;

                        // The mask may report the characters of the comment, which are skipped: the next block starts after the comment
                        mask = 0;
                        nextBlock = 0;
                    }
                    else
                    {
                        state = ScanState.Scanning;
                        inQuote = false;
                        _scanLineTerminators = 0;
                        _scanRowHasQuote = false;
                        columnStart = columnTotal;

                        // Each record starts with the end of a virtual column before it, so the start of a value is the end of the previous one plus one
                        if (columnTotal == columnEnds.Length)
                        {
                            columnEnds = Grow(columnEnds, columnTotal + 1);
                        }

                        columnEnds[columnTotal++] = -1;
                    }
                }

                if (state is ScanState.Comment)
                {
                    var lineEnd = data[position..].IndexOfAny(Cr, Lf);
                    if (lineEnd < 0)
                    {
                        if (!_endOfData)
                        {
                            position = end;
                            goto Save;
                        }

                        lineEnd = end;
                    }
                    else
                    {
                        lineEnd += position;
                        if (data[lineEnd] == Lf)
                        {
                            lineEnd++;
                        }
                        else if (lineEnd + 1 < end)
                        {
                            lineEnd += data[lineEnd + 1] == Lf ? 2 : 1;
                        }
                        else if (_endOfData)
                        {
                            lineEnd++;
                        }
                        else
                        {
                            position = lineEnd;
                            goto Save;
                        }

                        _nextLineNumber++;
                    }

                    rowStart = lineEnd;
                    state = ScanState.Start;
                    continue;
                }
            }

            // Scan the record until its line terminator
            int contentEnd;
            int rowEnd;
            while (true)
            {
                int index;
                if (mask != 0)
                {
                    var bit = BitOperations.TrailingZeroCount(mask);
                    mask &= mask - 1;
                    index = maskBase + bit;

                    // Most special characters are separators: the mask says it without reading the data, and the capacity was ensured for the block.
                    // A separator is never before the position: the position only skips quotes, line feeds and comments, and a comment clears the mask.
                    // The separator bits are cleared while in a quoted value.
                    if (((separators >> bit) & 1) != 0)
                    {
                        columnEnds[columnTotal++] = index - rowStart;
                        continue;
                    }

                    if (index < position)
                        continue;
                }
                else
                {
                    var blockStart = Math.Max(position, nextBlock);
                    var vectorSize = _vectorSize;
                    if (vectorSize != 0 && end - blockStart >= vectorSize)
                    {
                        // Each element of the block adds at most two column ends: a separator adds one, a line terminator ends a record and starts the next one
                        if (columnEnds.Length - columnTotal < 2 * vectorSize)
                        {
                            columnEnds = Grow(columnEnds, columnTotal + (2 * vectorSize));
                        }

                        if (pendingBase == blockStart)
                        {
                            mask = pendingMask;
                            separators = pendingSeparators;
                        }
                        else
                        {
                            CsvSimd.GetMasks(ref unsafe(Unsafe.Add(ref dataRef, blockStart)), vectorSize, _separatorByte, _quoteByte, out separators, out mask);
                        }

                        maskBase = blockStart;
                        nextBlock = blockStart + vectorSize;

                        // The masks of the next block are computed now: when the end of this block is mispredicted, they are not on the critical path
                        if (end - nextBlock >= vectorSize)
                        {
                            CsvSimd.GetMasks(ref unsafe(Unsafe.Add(ref dataRef, nextBlock)), vectorSize, _separatorByte, _quoteByte, out pendingSeparators, out pendingMask);
                            pendingBase = nextBlock;
                        }
                        else
                        {
                            pendingBase = -1;
                        }

                        // Inside a quoted value, the separators are part of the value: they go through the general path, which checks the state
                        if (inQuote)
                        {
                            separators = 0;
                        }

                        continue;
                    }

                    var relativeIndex = blockStart < end ? data[blockStart..].IndexOfAny(_specialValues) : -1;
                    if (relativeIndex < 0)
                    {
                        if (!_endOfData)
                        {
                            position = end;
                            goto Save;
                        }

                        if (inQuote && _strict)
                        {
                            if (_batchCount > 0)
                                goto RestartRow;

                            ReportScanError(CsvBadDataKind.UnterminatedQuotedValue, "A quoted value is not closed before the end of the data.", rowStart, columnTotal - columnStart - 1);
                        }

                        contentEnd = end;
                        rowEnd = end;
                        break;
                    }

                    index = blockStart + relativeIndex;
                    nextBlock = index + 1;
                    if (columnEnds.Length == columnTotal)
                    {
                        columnEnds = Grow(columnEnds, columnTotal + 1);
                    }
                }

                var value = unsafe(Unsafe.Add(ref dataRef, index));
                if (inQuote)
                {
                    if (value == _quote)
                    {
                        if (index + 1 < end)
                        {
                            var next = unsafe(Unsafe.Add(ref dataRef, index + 1));
                            if (next == _quote)
                            {
                                position = index + 2;
                                continue;
                            }

                            if (_strict && next != _separator && next != Cr && next != Lf && !(_trimOuter && next == Space))
                            {
                                if (_batchCount > 0)
                                    goto RestartRow;

                                ReportScanError(CsvBadDataKind.CharactersAfterClosingQuote, "A quoted value is followed by characters that are not a separator.", rowStart, columnTotal - columnStart - 1);
                            }

                            inQuote = false;
                            position = index + 1;
                            continue;
                        }

                        if (_endOfData)
                        {
                            inQuote = false;
                            position = index + 1;
                            continue;
                        }

                        position = index;
                        goto Save;
                    }

                    if (value == Lf)
                    {
                        _scanLineTerminators++;
                        position = index + 1;
                        continue;
                    }

                    if (value == Cr)
                    {
                        if (index + 1 < end)
                        {
                            position = unsafe(Unsafe.Add(ref dataRef, index + 1)) == Lf ? index + 2 : index + 1;
                            _scanLineTerminators++;
                            continue;
                        }

                        if (_endOfData)
                        {
                            _scanLineTerminators++;
                            position = index + 1;
                            continue;
                        }

                        position = index;
                        goto Save;
                    }

                    position = index + 1;
                    continue;
                }

                if (value == Lf)
                {
                    contentEnd = index;
                    rowEnd = index + 1;
                    break;
                }

                if (value == _separator)
                {
                    columnEnds[columnTotal++] = index - rowStart;
                    position = index + 1;
                    continue;
                }

                if (value == Cr)
                {
                    if (index + 1 < end)
                    {
                        contentEnd = index;
                        if (unsafe(Unsafe.Add(ref dataRef, index + 1)) == Lf)
                        {
                            rowEnd = index + 2;

                            // The line feed is consumed with the carriage return: its bit is removed so that the loop does not go through it
                            var offset = index + 1 - maskBase;
                            if ((uint)offset < 64)
                            {
                                mask &= ~(1UL << offset);
                            }
                        }
                        else
                        {
                            rowEnd = index + 1;
                        }

                        break;
                    }

                    if (_endOfData)
                    {
                        contentEnd = index;
                        rowEnd = index + 1;
                        break;
                    }

                    position = index;
                    goto Save;
                }

                // A quote character: it opens a quoted value at the start of a value
                if (_hasQuote && value == _quote)
                {
                    _scanRowHasQuote = true;
                    var fieldStart = rowStart + columnEnds[columnTotal - 1] + 1;
                    if (index == fieldStart || (_trimOuter && IsSpaces(data[fieldStart..index])))
                    {
                        inQuote = true;
                        separators = 0;
                    }
                    else if (_strict)
                    {
                        if (_batchCount > 0)
                            goto RestartRow;

                        ReportScanError(CsvBadDataKind.QuoteInUnquotedValue, "A value that is not quoted contains a quote character.", rowStart, columnTotal - columnStart - 1);
                    }
                }

                position = index + 1;
            }

            // The record is complete: it has a line terminator unless it ends at the end of the data
            var length = contentEnd - rowStart;
            if (_source is not null && length > _maxRowLength)
            {
                if (_batchCount > 0)
                    goto RestartRow;

                throw CreateScanException($"The record is longer than the maximum row length ({_maxRowLength.ToString(CultureInfo.InvariantCulture)}).", rowStart, _nextLineNumber, columnIndex: null);
            }

            if (length > 0 || columnTotal > columnStart + 1)
            {
                if (columnEnds.Length == columnTotal)
                {
                    columnEnds = Grow(columnEnds, columnTotal + 1);
                }

                columnEnds[columnTotal++] = length;
            }

            var columnCount = columnTotal - columnStart - 1;
            if (_scanSkipRow)
            {
                SkipScannedRow();
                columnTotal = columnStart;
            }
            else if (!_skipEmptyLines || columnCount > 0)
            {
                _rows[_batchCount++] = new RowEntry(rowStart, length, columnStart, columnCount, _nextLineNumber, _scanRowHasQuote);
            }

            _nextLineNumber += _scanLineTerminators + (rowEnd > contentEnd ? 1 : 0);
            rowStart = rowEnd;
            position = rowEnd;
            state = ScanState.Start;
        }

    // An error in a record that follows records of the batch is reported when the record is read: the scan stops before it, and restarts from it
    RestartRow:
        columnTotal = columnStart;
        state = ScanState.Start;
        position = rowStart;

    Save:
        _scanState = state;
        _scanRowStart = rowStart;
        _scanPosition = position;
        _scanInQuote = inQuote;
        _scanColumnStart = columnStart;
        _columnEnds = columnEnds;
        _columnTotal = columnTotal;
    }

    // Creating SearchValues is costly compared to reading a short file: the instances are shared by the readers with the same separator and quote
    private static SearchValues<T> GetSpecialValues(char separator, char quote)
    {
        var key = (separator << 16) | quote;
        if (SpecialValuesCache.TryGetValue(key, out var result))
            return result;

        Span<char> chars = ['\r', '\n', separator, quote];
        if (typeof(T) == typeof(char))
        {
            result = (SearchValues<T>)(object)SearchValues.Create(chars);
        }
        else
        {
            Span<byte> bytes = [(byte)chars[0], (byte)chars[1], (byte)chars[2], (byte)chars[3]];
            result = (SearchValues<T>)(object)SearchValues.Create(bytes);
        }

        if (SpecialValuesCache.Count < 64)
        {
            SpecialValuesCache.TryAdd(key, result);
        }

        return result;
    }

    // Returns the new array instead of resizing a ref parameter: passing a local by reference would keep it out of the registers in the scan loop.
    // The field is updated too: the old array goes back to the pool, and a scan that throws does not save its locals, so the field must not keep it.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private int[] Grow(int[] array, int minimumLength)
    {
        var result = ArrayPool<int>.Shared.Rent(Math.Max(array.Length * 2, minimumLength));
        array.AsSpan().CopyTo(result);
        _columnEnds = result;
        ArrayPool<int>.Shared.Return(array);
        return result;
    }

    private static bool IsSpaces(ReadOnlySpan<T> value) => !value.ContainsAnyExcept(Space);

    private static char ToChar(T value) => (char)ushort.CreateTruncating(value);

    private void Fill()
    {
        if (!PrepareFill())
            return;

        var read = _source!.Read(_buffer.AsSpan(_dataEnd));
        CompleteFill(read);
    }

    private async ValueTask FillAsync(CancellationToken cancellationToken)
    {
        if (!PrepareFill())
            return;

        var read = await _source!.ReadAsync(_buffer.AsMemory(_dataEnd), cancellationToken).ConfigureAwait(false);
        CompleteFill(read);
    }

    private bool PrepareFill()
    {
        if (_source is null || _endOfData)
        {
            _endOfData = true;
            return false;
        }

        // Only called when every record of the batch is read: the data before the record being scanned is not needed anymore
        var buffer = _buffer!;
        var shift = _scanRowStart;
        var pending = _dataEnd - shift;
        if (pending >= _maxRowLength)
            throw CreateScanException($"The record is longer than the maximum row length ({_maxRowLength.ToString(CultureInfo.InvariantCulture)}).", shift, _nextLineNumber, columnIndex: null);

        if (shift > 0)
        {
            buffer.AsSpan(shift, pending).CopyTo(buffer);
            _bufferOffset += shift;
            _dataEnd = pending;
            _scanRowStart = 0;
            _scanPosition -= shift;
            _rowStart -= shift; // Keeps Position of the current record
        }

        if (_dataEnd == buffer.Length)
        {
            var newSize = (int)Math.Min((long)buffer.Length * 2, Array.MaxLength);
            var newBuffer = ArrayPool<T>.Shared.Rent(Math.Max(newSize, buffer.Length + 1));
            buffer.AsSpan(0, _dataEnd).CopyTo(newBuffer);
            ArrayPool<T>.Shared.Return(buffer);
            _buffer = newBuffer;
            _data = newBuffer;
            _array = newBuffer;
        }

        return true;
    }

    private void CompleteFill(int read)
    {
        if (read <= 0)
        {
            _endOfData = true;
        }
        else
        {
            _dataEnd += read;
        }
    }

    public void GetField(int index, out ReadOnlySpan<T> value, out ReadOnlySpan<T> raw, out bool isQuoted) => GetField(RowSpan, index, out value, out raw, out isQuoted);

    // The records pass the span of the row they got once, which is cheaper than getting the span of the memory for each value
    public void GetField(ReadOnlySpan<T> row, int index, out ReadOnlySpan<T> value, out ReadOnlySpan<T> raw, out bool isQuoted)
    {
        if ((uint)index >= (uint)_columnCount)
            ThrowColumnIndexOutOfRange(index);

        var columnEnds = _columnEnds;
        var columnIndex = _rowColumnStart + index;
        var start = columnEnds[columnIndex] + 1;
        raw = row.Slice(start, columnEnds[columnIndex + 1] - start);
        GetValue(index, raw, out value, out isQuoted);
    }

    // Removes the quotes and the spaces of a value, as the options say
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void GetValue(int index, ReadOnlySpan<T> raw, out ReadOnlySpan<T> value, out bool isQuoted)
    {
        if (!_trimOuter && (!_hasQuote || raw.IsEmpty || raw[0] != _quote))
        {
            value = raw;
            isQuoted = false;
            return;
        }

        GetValueSlow(index, raw, out value, out isQuoted);
    }

    private void GetValueSlow(int index, ReadOnlySpan<T> raw, out ReadOnlySpan<T> value, out bool isQuoted)
    {
        var span = raw;
        if (_trimOuter)
        {
            span = span.Trim(Space);
        }

        if (_hasQuote && !span.IsEmpty && span[0] == _quote)
        {
            isQuoted = true;
            value = Unescape(index, span);
            if (_trimInside)
            {
                value = value.Trim(Space);
            }
        }
        else
        {
            isQuoted = false;
            value = span;
        }
    }

    private ReadOnlySpan<T> Unescape(int index, ReadOnlySpan<T> value)
    {
        var quote = _quote;
        var content = value[1..];
        var quoteIndex = content.IndexOf(quote);
        if (quoteIndex < 0)
            return content; // Not closed

        if (quoteIndex == content.Length - 1)
            return content[..quoteIndex];

        if ((uint)index < (uint)_unescapedValues.Length && _unescapedValues[index].Version == _rowVersion)
        {
            var cached = _unescapedValues[index];
            return _scratch.AsSpan(cached.Start, cached.Length);
        }

        // Each column is unescaped once per record and the unescaped value is never longer than the raw value:
        // the scratch buffer never grows while the values of the current record are in use
        if (_scratch is null || (_scratchUsed == 0 && _scratch.Length < _rowLength))
        {
            if (_scratch is not null)
            {
                ArrayPool<T>.Shared.Return(_scratch);
            }

            _scratch = ArrayPool<T>.Shared.Rent(Math.Max(_rowLength, 256));
        }

        var start = _scratchUsed;
        var destination = _scratch.AsSpan(start);
        var written = 0;
        while (true)
        {
            // The first quote was found above; the next ones are searched after each escaped quote
            if (quoteIndex < 0)
            {
                content.CopyTo(destination[written..]);
                written += content.Length;
                break;
            }

            content[..quoteIndex].CopyTo(destination[written..]);
            written += quoteIndex;
            if (quoteIndex + 1 < content.Length && content[quoteIndex + 1] == quote)
            {
                destination[written++] = quote;
                content = content[(quoteIndex + 2)..];
                quoteIndex = content.IndexOf(quote);
                continue;
            }

            // Closing quote: the rest of the value is literal
            var rest = content[(quoteIndex + 1)..];
            rest.CopyTo(destination[written..]);
            written += rest.Length;
            break;
        }

        _scratchUsed += written;
        if (_unescapedValues.Length <= index)
        {
            Array.Resize(ref _unescapedValues, Math.Max(index + 1, _columnCount));
        }

        _unescapedValues[index] = new UnescapedValue(_rowVersion, start, written);
        return _scratch.AsSpan(start, written);
    }

    public bool TryGetColumnIndex(string columnName, out int index)
    {
        ArgumentNullException.ThrowIfNull(columnName);
        if (_header is null)
            throw new InvalidOperationException("The reader has no header row: set CsvReaderOptions.HasHeaderRow to read values by column name.");

        return _header.TryGetIndex(columnName, out index);
    }

    public string GetString(ReadOnlySpan<T> value)
    {
        if (typeof(T) == typeof(char))
        {
            var chars = unsafe(MemoryMarshal.Cast<T, char>(value));
            return _stringPool is not null ? _stringPool.GetOrAdd(chars) : new string(chars);
        }

        var bytes = unsafe(MemoryMarshal.Cast<T, byte>(value));
        if (_stringPool is not null && bytes.Length <= CsvStringPool.MaxLength)
        {
            Span<char> buffer = unsafe(stackalloc char[CsvStringPool.MaxLength]);
            Utf8.ToUtf16(bytes, buffer, out _, out var charsWritten, replaceInvalidSequences: true);
            return _stringPool.GetOrAdd(buffer[..charsWritten]);
        }

        return Utf8Transcoder.GetString(bytes);
    }

    /// <summary>Creates an exception about the current record.</summary>
    public CsvException CreateException(string message, int? columnIndex = null, Exception? innerException = null)
    {
        return new CsvException(message, _recordIndex >= 0 ? _recordIndex : null, _lineNumber > 0 ? _lineNumber : _nextLineNumber, Position, columnIndex, GetColumnName(columnIndex), innerException);
    }

    /// <summary>Creates an exception about the record being scanned, which follows the last record read.</summary>
    private CsvException CreateScanException(string message, int rowStart, long lineNumber, int? columnIndex)
    {
        return new CsvException(message, ScannedRecordIndex, lineNumber, _bufferOffset + rowStart, columnIndex, GetColumnName(columnIndex));
    }

    // The index of the record being scanned, which follows the last record read, or null when it is the header row
    private long? ScannedRecordIndex => _hasHeaderRow && _header is null ? null : _recordIndex + 1;

    /// <summary>Reports malformed data in the record being scanned, which is the first record of the batch. The scan then continues as in non-strict mode.</summary>
    /// <remarks>Throws when the data must not be read; sets <see cref="_scanSkipRow"/> when the record must be skipped once scanned.</remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReportScanError(CsvBadDataKind kind, string message, int rowStart, int columnIndex)
    {
        // Once the record is skipped, its other errors do not matter
        if (_scanSkipRow)
            return;

        if (!ReportBadData(kind, message, ScannedRecordIndex, _nextLineNumber, _bufferOffset + rowStart, columnIndex))
        {
            _scanSkipRow = true;
        }
    }

    // The record is complete and skipped: it is not added to the batch, but it counts in the record indexes, as the indexes reported for the next errors
    private void SkipScannedRow()
    {
        if (!_hasHeaderRow || _header is not null)
        {
            _recordIndex++;
        }

        _scanSkipRow = false;
    }

    /// <summary>Calls the bad data handler, or applies <see cref="CsvReaderOptions.Strict"/> when there is none.</summary>
    /// <returns><see langword="true"/> to accept the data; <see langword="false"/> to skip the record.</returns>
    private bool ReportBadData(CsvBadDataKind kind, string message, long? recordIndex, long lineNumber, long position, int? columnIndex)
    {
        var context = new CsvBadDataContext(kind, message, recordIndex, lineNumber, position, columnIndex, GetColumnName(columnIndex));
        var action = _badDataHandler is null ? CsvBadDataAction.Throw : _badDataHandler(context);
        return action switch
        {
            CsvBadDataAction.Accept => true,
            CsvBadDataAction.Skip => false,
            CsvBadDataAction.Throw => throw context.Exception,
            _ => throw new InvalidOperationException($"The bad data handler returned an invalid action ({action})."),
        };
    }

    private string? GetColumnName(int? columnIndex)
    {
        if (columnIndex is { } index && _header is not null && (uint)index < (uint)_header.ColumnCount)
            return _header.ColumnNames[index];

        return null;
    }

    [DoesNotReturn]
    private void ThrowColumnIndexOutOfRange(int index)
    {
        throw new ArgumentOutOfRangeException(nameof(index), index, $"The record has {_columnCount.ToString(CultureInfo.InvariantCulture)} columns.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _batchCount = 0;
        _batchIndex = 0;
        _source?.Dispose();
        if (_buffer is not null)
        {
            ArrayPool<T>.Shared.Return(_buffer);
            _buffer = null;
            _array = null;
            _data = default;
        }

        ArrayPool<int>.Shared.Return(_columnEnds);
        _columnEnds = [];
        ArrayPool<RowEntry>.Shared.Return(_rows);
        _rows = [];

        if (_scratch is not null)
        {
            ArrayPool<T>.Shared.Return(_scratch);
            _scratch = null;
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct RowEntry(int start, int length, int columnStart, int columnCount, long lineNumber, bool hasQuote)
    {
        // The flag is in the lowest bit of the column count, so that an entry is 24 bytes
        private readonly int _columnCountAndFlag = (columnCount << 1) | (hasQuote ? 1 : 0);

        public int Start { get; } = start;

        public int Length { get; } = length;

        public int ColumnStart { get; } = columnStart;

        public long LineNumber { get; } = lineNumber;

        public int ColumnCount => _columnCountAndFlag >> 1;

        public bool HasQuote => (_columnCountAndFlag & 1) != 0;
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct UnescapedValue(int Version, int Start, int Length);
}
