using System.Buffers;
using System.Globalization;
using System.IO.Pipelines;
using System.Text;

namespace Meziantou.Framework.Bencode;

internal sealed class BencodePipeReaderDecoder
{
    private readonly PipeReader _reader;
    private ReadOnlySequence<byte> _buffer;
    private bool _hasPendingReadResult;
    private long _position;

    private BencodePipeReaderDecoder(PipeReader reader)
    {
        _reader = reader;
    }

    public static async ValueTask<BencodeValue> ParseAsync(PipeReader reader, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var decoder = new BencodePipeReaderDecoder(reader);
        try
        {
            var value = await decoder.ParseValueAsync(cancellationToken).ConfigureAwait(false);
            await decoder.EnsureNoTrailingDataAsync(cancellationToken).ConfigureAwait(false);
            return value;
        }
        finally
        {
            decoder.AdvanceReader();
        }
    }

    private async ValueTask<BencodeValue> ParseValueAsync(CancellationToken cancellationToken)
    {
        var token = await PeekByteAsync(cancellationToken).ConfigureAwait(false);
        return token switch
        {
            (byte)'i' => await ParseIntegerAsync(cancellationToken).ConfigureAwait(false),
            >= (byte)'0' and <= (byte)'9' => await ParseStringAsync(cancellationToken).ConfigureAwait(false),
            (byte)'l' => await ParseListAsync(cancellationToken).ConfigureAwait(false),
            (byte)'d' => await ParseDictionaryAsync(cancellationToken).ConfigureAwait(false),
            null => throw new FormatException("Unexpected end of bencode data."),
            _ => throw new FormatException($"Invalid bencode token '{(char)token.GetValueOrDefault()}' at position {_position}."),
        };
    }

    private async ValueTask<BencodeInteger> ParseIntegerAsync(CancellationToken cancellationToken)
    {
        Consume(1); // i

        var integerText = new ArrayBufferWriter<byte>();
        while (true)
        {
            var next = await TryReadByteAsync(cancellationToken).ConfigureAwait(false);
            if (next is null)
                throw new FormatException("Unterminated bencode integer.");

            if (next == (byte)'e')
                break;

            AppendByte(integerText, next.Value);
        }

        if (!IsValidInteger(integerText.WrittenSpan))
            throw new FormatException("Invalid bencode integer format.");

        var integerString = Encoding.ASCII.GetString(integerText.WrittenSpan);
        if (!long.TryParse(integerString, CultureInfo.InvariantCulture, out var value))
            throw new FormatException("Invalid bencode integer value.");

        return new BencodeInteger(value);
    }

    private async ValueTask<BencodeString> ParseStringAsync(CancellationToken cancellationToken)
    {
        var lengthText = new ArrayBufferWriter<byte>();
        while (true)
        {
            var next = await TryReadByteAsync(cancellationToken).ConfigureAwait(false);
            if (next is null)
                throw new FormatException("Invalid bencode string length format.");

            if (next is >= (byte)'0' and <= (byte)'9')
            {
                AppendByte(lengthText, next.Value);
                continue;
            }

            if (next == (byte)':')
                break;

            throw new FormatException("Invalid bencode string length format.");
        }

        if (lengthText.WrittenCount == 0)
            throw new FormatException("Invalid bencode string length format.");

        var lengthSpan = lengthText.WrittenSpan;
        if (lengthSpan.Length > 1 && lengthSpan[0] == (byte)'0')
            throw new FormatException("Bencode string length cannot have leading zeroes.");

        var lengthString = Encoding.ASCII.GetString(lengthSpan);
        if (!int.TryParse(lengthString, CultureInfo.InvariantCulture, out var stringLength) || stringLength < 0)
            throw new FormatException("Invalid bencode string length.");

        var stringBytes = new byte[stringLength];
        if (!await TryReadExactlyAsync(stringBytes.AsMemory(), cancellationToken).ConfigureAwait(false))
            throw new FormatException("Unexpected end of bencode string data.");

        return new BencodeString(stringBytes);
    }

    private async ValueTask<BencodeList> ParseListAsync(CancellationToken cancellationToken)
    {
        Consume(1); // l
        var result = new BencodeList();
        while (true)
        {
            var token = await PeekByteAsync(cancellationToken).ConfigureAwait(false);
            if (token is null)
                throw new FormatException("Unterminated bencode list.");

            if (token == (byte)'e')
            {
                Consume(1);
                return result;
            }

            result.Add(await ParseValueAsync(cancellationToken).ConfigureAwait(false));
        }
    }

    private async ValueTask<BencodeDictionary> ParseDictionaryAsync(CancellationToken cancellationToken)
    {
        Consume(1); // d
        var result = new BencodeDictionary();
        while (true)
        {
            var token = await PeekByteAsync(cancellationToken).ConfigureAwait(false);
            if (token is null)
                throw new FormatException("Unterminated bencode dictionary.");

            if (token == (byte)'e')
            {
                Consume(1);
                return result;
            }

            var keyBytes = await ParseStringAsync(cancellationToken).ConfigureAwait(false);
            string key;
            try
            {
                key = keyBytes.ToUtf8String();
            }
            catch (DecoderFallbackException ex)
            {
                throw new FormatException("Bencode dictionary keys must be valid UTF-8 strings.", ex);
            }

            var value = await ParseValueAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                result.Add(key, value);
            }
            catch (ArgumentException ex)
            {
                throw new FormatException($"Duplicate bencode dictionary key '{key}'.", ex);
            }
        }
    }

    private async ValueTask EnsureNoTrailingDataAsync(CancellationToken cancellationToken)
    {
        var token = await PeekByteAsync(cancellationToken).ConfigureAwait(false);
        if (token is not null)
            throw new FormatException("Unexpected trailing data after bencode value.");
    }

    private async ValueTask<bool> TryReadExactlyAsync(Memory<byte> destination, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < destination.Length)
        {
            if (_buffer.IsEmpty)
            {
                if (!await ReadMoreAsync(cancellationToken).ConfigureAwait(false))
                    return false;
            }

            var bytesToCopy = (int)Math.Min(_buffer.Length, destination.Length - offset);
            _buffer.Slice(0, bytesToCopy).CopyTo(destination.Span[offset..(offset + bytesToCopy)]);
            Consume(bytesToCopy);
            offset += bytesToCopy;
        }

        return true;
    }

    private async ValueTask<byte?> TryReadByteAsync(CancellationToken cancellationToken)
    {
        var token = await PeekByteAsync(cancellationToken).ConfigureAwait(false);
        if (token is null)
            return null;

        Consume(1);
        return token;
    }

    private async ValueTask<byte?> PeekByteAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var reader = new SequenceReader<byte>(_buffer);
            if (reader.TryPeek(out var token))
                return token;

            if (!await ReadMoreAsync(cancellationToken).ConfigureAwait(false))
                return null;
        }
    }

    private async ValueTask<bool> ReadMoreAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            AdvanceReader();

            var readResult = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            _buffer = readResult.Buffer;
            _hasPendingReadResult = true;

            if (!_buffer.IsEmpty)
                return true;

            if (readResult.IsCompleted)
                return false;
        }
    }

    private void Consume(long length)
    {
        _buffer = _buffer.Slice(_buffer.GetPosition(length));
        _position += length;
    }

    private void AdvanceReader()
    {
        if (!_hasPendingReadResult)
            return;

        _reader.AdvanceTo(_buffer.Start, _buffer.End);
        _hasPendingReadResult = false;
    }

    private static void AppendByte(ArrayBufferWriter<byte> writer, byte value)
    {
        var span = writer.GetSpan(1);
        span[0] = value;
        writer.Advance(1);
    }

    private static bool IsValidInteger(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty)
            return false;

        var offset = 0;
        if (value[0] == (byte)'-')
        {
            if (value.Length == 1)
                return false;

            if (value[1] == (byte)'0')
                return false;

            offset = 1;
        }
        else if (value.Length > 1 && value[0] == (byte)'0')
        {
            return false;
        }

        for (var i = offset; i < value.Length; i++)
        {
            if (value[i] is < (byte)'0' or > (byte)'9')
                return false;
        }

        return true;
    }
}
