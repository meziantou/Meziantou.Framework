using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Meziantou.Framework.Json.Internals;

/// <summary>Reads a stream in chunks and feeds them to a <see cref="StreamingEvaluator"/>.</summary>
/// <remarks>
/// The buffer only holds the data that has not been processed yet. It grows when a value that must be materialized does
/// not fit in it, so its size depends on the largest such value rather than on the size of the document.
/// </remarks>
internal sealed class StreamingEvaluation : IDisposable
{
    private const int InitialBufferSize = 16 * 1024;

    private readonly StreamingEvaluator _evaluator;
    private readonly bool _allowMultipleValues;
    private byte[] _buffer = ArrayPool<byte>.Shared.Rent(InitialBufferSize);
    private int _start;
    private int _end;
    private bool _isFinalBlock;
    private bool _isFirstBlock = true;
    private bool _isTrailingDataRead;
    private bool _isProbing;
    private int _probeOffset;
    private int _probeDepth;
    private JsonReaderState _probeState;
    private JsonReaderState _state;

    private StreamingEvaluation(JsonPathExpression expression, JsonReaderOptions options)
    {
        _evaluator = new StreamingEvaluator(expression);
        _allowMultipleValues = options.AllowMultipleValues;

        // Comments are not values, and a JsonNode cannot be parsed from a reader that returns them
        if (options.CommentHandling is JsonCommentHandling.Allow)
        {
            options.CommentHandling = JsonCommentHandling.Skip;
        }

        _state = new JsonReaderState(options);
    }

    private bool IsDone => _isTrailingDataRead;


    public static IEnumerable<JsonPathMatch> Evaluate(JsonPathExpression expression, Stream utf8Json, JsonReaderOptions options)
    {
        using var evaluation = new StreamingEvaluation(expression, options);
        while (true)
        {
            var needsData = evaluation.Process();
            foreach (var match in evaluation.TakeMatches())
            {
                yield return match;
            }

            if (evaluation.IsDone)
                yield break;

            if (!needsData)
                continue;

            do
            {
                evaluation.PrepareRead();
                evaluation.Complete(utf8Json.Read(evaluation._buffer, evaluation._end, evaluation._buffer.Length - evaluation._end));
            }
            while (evaluation.ShouldReadMore());
        }
    }

    public static async IAsyncEnumerable<JsonPathMatch> EvaluateAsync(JsonPathExpression expression, Stream utf8Json, JsonReaderOptions options, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var evaluation = new StreamingEvaluation(expression, options);
        while (true)
        {
            var needsData = evaluation.Process();
            foreach (var match in evaluation.TakeMatches())
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return match;
            }

            if (evaluation.IsDone)
                yield break;

            if (!needsData)
                continue;

            do
            {
                evaluation.PrepareRead();
                evaluation.Complete(await utf8Json.ReadAsync(evaluation._buffer.AsMemory(evaluation._end), cancellationToken).ConfigureAwait(false));
            }
            while (evaluation.ShouldReadMore());
        }
    }

    public void Dispose()
    {
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = [];
    }

    private JsonPathMatch[] TakeMatches()
    {
        if (_evaluator.Matches.Count is 0)
            return [];

        var matches = _evaluator.Matches.ToArray();
        _evaluator.Matches.Clear();
        return matches;
    }

    /// <summary>Processes the data read so far.</summary>
    /// <returns><see langword="true"/> when more data must be read; <see langword="false"/> when matches were found and the evaluation can go on.</returns>
    private bool Process()
    {
        if (_isFirstBlock)
        {
            // Skip the byte order mark, as JsonDocument.Parse does. Wait for 3 bytes before deciding, unless the stream ends.
            if (_end - _start < 3 && !_isFinalBlock)
                return true;

            if (_buffer.AsSpan(_start, _end - _start).StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
            {
                _start += 3;
            }

            _isFirstBlock = false;
        }

        var reader = new Utf8JsonReader(_buffer.AsSpan(_start, _end - _start), _isFinalBlock, _state);
        var needsData = true;
        var needsWholeValue = false;
        if (!_evaluator.IsCompleted)
        {
            var status = _evaluator.Process(ref reader);
            if (status is StreamingStatus.MatchesFound)
            {
                needsData = false;
            }
            else if (status is not StreamingStatus.Completed)
            {
                // The reader throws on invalid data, so the data ended in the middle of a value
                if (_isFinalBlock)
                    throw new JsonException("The JSON value is incomplete.");

                needsWholeValue = status is StreamingStatus.NeedsWholeValue;
            }
        }

        if (_evaluator.IsCompleted)
        {
            // What follows the value must be whitespace, as JsonDocument.Parse requires. Reading the next token throws
            // otherwise, unless the options allow several values: the evaluation then stops after the first one.
            if (_allowMultipleValues || reader.Read() || _isFinalBlock)
            {
                _isTrailingDataRead = true;
            }
        }

        _start += (int)reader.BytesConsumed;
        _state = reader.CurrentState;
        if (needsWholeValue)
        {
            // The value starts with the first token after the processed data
            _isProbing = true;
            _probeOffset = 0;
            _probeDepth = -1;
            _probeState = _state;
        }

        return needsData && !IsDone;
    }

    /// <summary>
    /// Gets whether to read more data before processing again. While a value to parse is incomplete, its tokens are read
    /// as they come, from where the previous read stopped, until the value ends. Each byte of the value is then scanned
    /// once, and the value is parsed as soon as it is complete, without waiting for data that may only come later.
    /// </summary>
    private bool ShouldReadMore()
    {
        if (!_isProbing)
            return false;

        // At the end of the stream, processing reports the incomplete value
        if (_isFinalBlock)
        {
            _isProbing = false;
            return false;
        }

        var reader = new Utf8JsonReader(_buffer.AsSpan(_start + _probeOffset, _end - _start - _probeOffset), isFinalBlock: false, _probeState);
        while (reader.Read())
        {
            if (_probeDepth < 0)
            {
                // The start of the value: the value ends with the end token at the same depth
                _probeDepth = reader.CurrentDepth;
            }
            else if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray && reader.CurrentDepth == _probeDepth)
            {
                _isProbing = false;
                return false;
            }
        }

        _probeOffset += (int)reader.BytesConsumed;
        _probeState = reader.CurrentState;
        return true;
    }

    // Makes room for more data: moves the unprocessed bytes to the start, and grows the buffer when they fill it
    private void PrepareRead()
    {
        var length = _end - _start;
        if (_start is 0 && _end == _buffer.Length)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(checked(_buffer.Length * 2));
            _buffer.AsSpan(0, length).CopyTo(buffer);
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = buffer;
        }
        else if (_start > 0)
        {
            _buffer.AsSpan(_start, length).CopyTo(_buffer);
        }

        _start = 0;
        _end = length;
    }

    private void Complete(int bytesRead)
    {
        _end += bytesRead;
        _isFinalBlock = bytesRead is 0;
    }
}
