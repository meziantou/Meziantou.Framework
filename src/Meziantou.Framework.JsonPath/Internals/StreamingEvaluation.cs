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
            evaluation.Process();
            foreach (var match in evaluation.TakeMatches())
            {
                yield return match;
            }

            if (evaluation.IsDone)
                yield break;

            evaluation.PrepareRead();
            evaluation.Complete(utf8Json.Read(evaluation._buffer, evaluation._end, evaluation._buffer.Length - evaluation._end));
        }
    }

    public static async IAsyncEnumerable<JsonPathMatch> EvaluateAsync(JsonPathExpression expression, Stream utf8Json, JsonReaderOptions options, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var evaluation = new StreamingEvaluation(expression, options);
        while (true)
        {
            evaluation.Process();
            foreach (var match in evaluation.TakeMatches())
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return match;
            }

            if (evaluation.IsDone)
                yield break;

            evaluation.PrepareRead();
            evaluation.Complete(await utf8Json.ReadAsync(evaluation._buffer.AsMemory(evaluation._end), cancellationToken).ConfigureAwait(false));
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

    private void Process()
    {
        if (_isFirstBlock)
        {
            // Skip the byte order mark, as JsonDocument.Parse does. Wait for 3 bytes before deciding, unless the stream ends.
            if (_end - _start < 3 && !_isFinalBlock)
                return;

            if (_buffer.AsSpan(_start, _end - _start).StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
            {
                _start += 3;
            }

            _isFirstBlock = false;
        }

        var reader = new Utf8JsonReader(_buffer.AsSpan(_start, _end - _start), _isFinalBlock, _state);
        if (!_evaluator.IsCompleted)
        {
            _evaluator.Process(ref reader);
            if (!_evaluator.IsCompleted && _isFinalBlock)
            {
                // The reader throws on invalid data, so the data ended in the middle of a value
                throw new JsonException("The JSON value is incomplete.");
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
