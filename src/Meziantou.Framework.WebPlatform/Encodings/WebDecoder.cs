using System.Runtime.CompilerServices;
using System.Text;

namespace Meziantou.Framework.WebPlatform.Encodings;

internal enum DecodeStatus
{
    /// <summary>All the input was consumed. More input may be provided (unless the decoder was flushed).</summary>
    NeedMoreData,

    /// <summary>The output buffer is too small to continue. Call again with the remaining input and a new output buffer.</summary>
    OutputFull,

    /// <summary>A decoding error occurred and the decoder is in fatal mode.</summary>
    Error,

    /// <summary>The decoder returned "finished"; any remaining input is ignored.</summary>
    Finished,
}

/// <summary>
/// Base class for the decoders of the Encoding Standard (https://encoding.spec.whatwg.org/#decoders).
/// Derived classes implement the handler algorithm for a single byte; this class implements
/// "process a queue", including the "restore to I/O queue" operation and the error mode.
/// </summary>
internal abstract class WebDecoder
{
    /// <summary>Minimum free space required in the output buffer to process one item (two code points, each up to two UTF-16 code units).</summary>
    public const int MinimumOutputLength = 4;

    protected const int EndOfQueue = -1;

    // Bytes restored to the I/O queue. They are processed before the rest of the input.
    private readonly byte[] _restored = new byte[4];
    private int _restoredCount;
    private bool _finished;

    protected WebDecoder(bool fatal)
    {
        Fatal = fatal;
    }

    public bool Fatal { get; }

    /// <summary>Result of the handler algorithm.</summary>
    internal enum HandlerResult
    {
        Continue,
        Finished,
        Error,
        CodePoint,
        TwoCodePoints,
    }

    /// <summary>
    /// Runs the decoder's handler for <paramref name="value"/> (a byte, or <see cref="EndOfQueue"/>).
    /// </summary>
    protected abstract HandlerResult Handle(int value, out int codePoint1, out int codePoint2);

    /// <summary>
    /// Runs the handler for one byte (or <c>-1</c>, the end of the queue) outside of "process a queue", for the encoding
    /// detector (<see cref="LegacyEncodingDetector"/>), which scores each byte sequence and each error. The bytes that the
    /// handler restores to the I/O queue are dropped: <paramref name="restoredCount"/> is their number.
    /// </summary>
    internal HandlerResult HandleSingle(int value, out int codePoint1, out int codePoint2, out int restoredCount)
    {
        var result = Handle(value, out codePoint1, out codePoint2);
        restoredCount = _restoredCount;
        _restoredCount = 0;
        return result;
    }

    /// <summary>Resets the decoder state to a new instance.</summary>
    public void Reset()
    {
        _restoredCount = 0;
        _finished = false;
        ResetState();
    }

    protected abstract void ResetState();

    /// <summary>Restores a byte to the I/O queue: it will be the next byte processed.</summary>
    protected void Restore(byte value)
    {
        // _restored is a stack: the last element is the next byte to read.
        _restored[_restoredCount++] = value;
    }

    /// <summary>Restores « first, second » to the I/O queue.</summary>
    protected void Restore(byte first, byte second)
    {
        Restore(second);
        Restore(first);
    }

    /// <summary>Restores « first, second, third » to the I/O queue.</summary>
    protected void Restore(byte first, byte second, byte third)
    {
        Restore(third);
        Restore(second);
        Restore(first);
    }

    /// <summary>
    /// Decodes <paramref name="input"/> into <paramref name="output"/>.
    /// When <paramref name="flush"/> is true and the whole input is consumed, the end-of-queue is processed.
    /// </summary>
    public DecodeStatus Decode(ReadOnlySpan<byte> input, Span<char> output, bool flush, out int bytesConsumed, out int charsWritten)
    {
        var inputIndex = 0;
        var outputIndex = 0;
        try
        {
            if (_finished)
            {
                inputIndex = input.Length;
                return DecodeStatus.Finished;
            }

            while (true)
            {
                if (output.Length - outputIndex < MinimumOutputLength)
                    return DecodeStatus.OutputFull;

                int value;
                if (_restoredCount > 0)
                {
                    value = _restored[--_restoredCount];
                }
                else if (inputIndex < input.Length)
                {
                    if (TryDecodeFast(input[inputIndex..], output[outputIndex..], out var fastConsumed, out var fastWritten))
                    {
                        inputIndex += fastConsumed;
                        outputIndex += fastWritten;
                        continue;
                    }

                    value = input[inputIndex++];
                }
                else if (flush)
                {
                    value = EndOfQueue;
                }
                else
                {
                    return DecodeStatus.NeedMoreData;
                }

                switch (Handle(value, out var codePoint1, out var codePoint2))
                {
                    case HandlerResult.Continue:
                        break;

                    case HandlerResult.Finished:
                        _finished = true;
                        inputIndex = input.Length;
                        _restoredCount = 0;
                        return DecodeStatus.Finished;

                    case HandlerResult.Error:
                        if (Fatal)
                            return DecodeStatus.Error;

                        output[outputIndex++] = '\U0000FFFD';
                        break;

                    case HandlerResult.CodePoint:
                        outputIndex += WriteCodePoint(codePoint1, output[outputIndex..]);
                        break;

                    case HandlerResult.TwoCodePoints:
                        outputIndex += WriteCodePoint(codePoint1, output[outputIndex..]);
                        outputIndex += WriteCodePoint(codePoint2, output[outputIndex..]);
                        break;
                }
            }
        }
        finally
        {
            bytesConsumed = inputIndex;
            charsWritten = outputIndex;
        }
    }

    /// <summary>
    /// Optional fast path, called when the decoder is in its initial state and no byte was restored.
    /// Implementations decode a prefix of the input that does not require the per-byte handler and must
    /// return false when they cannot make progress.
    /// </summary>
    protected virtual bool TryDecodeFast(ReadOnlySpan<byte> input, Span<char> output, out int bytesConsumed, out int charsWritten)
    {
        bytesConsumed = 0;
        charsWritten = 0;
        return false;
    }

    protected bool HasRestoredBytes => _restoredCount > 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int WriteCodePoint(int codePoint, Span<char> output)
    {
        if (codePoint < 0x10000)
        {
            output[0] = (char)codePoint;
            return 1;
        }

        return new Rune(codePoint).EncodeToUtf16(output);
    }

    /// <summary>Decodes a complete byte sequence.</summary>
    public string DecodeToString(ReadOnlySpan<byte> input)
    {
        var builder = new StringBuilder(input.Length);
        if (!DecodeTo(input, builder, flush: true))
            throw new DecoderFallbackException("The input is not valid for the encoding");

        return builder.ToString();
    }

    /// <summary>Decodes bytes and appends the result to <paramref name="builder"/>. Returns false on error in fatal mode.</summary>
    public bool DecodeTo(ReadOnlySpan<byte> input, StringBuilder builder, bool flush)
    {
        Span<char> buffer = stackalloc char[1024];
        while (true)
        {
            var status = Decode(input, buffer, flush, out var consumed, out var written);
            builder.Append(buffer[..written]);
            input = input[consumed..];
            switch (status)
            {
                case DecodeStatus.OutputFull:
                    continue;
                case DecodeStatus.Error:
                    return false;
                default:
                    return true;
            }
        }
    }
}
