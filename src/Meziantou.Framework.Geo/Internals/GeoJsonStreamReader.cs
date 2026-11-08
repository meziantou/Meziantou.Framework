using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Meziantou.Framework.Geo.Internals;

/// <summary>
/// Reads the features of a GeoJSON document from a stream, one at a time: the items of the <c>features</c> array of a
/// <c>FeatureCollection</c> are parsed as soon as they are read, and dropped from the buffer. Another document, such as
/// a <c>Feature</c> or a geometry, is read whole, as <see cref="GeoJson.Read"/> does.
/// </summary>
internal sealed class GeoJsonStreamReader : IDisposable
{
    private const int InitialBufferLength = 64 * 1024;

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    private byte[] _buffer = ArrayPool<byte>.Shared.Rent(InitialBufferLength);
    private int _start;
    private int _end;
    private bool _isFinalBlock;
    private bool _isFirstBlock = true;
    private JsonReaderState _state;

    // The start of the document while it may have to be read whole: until the 'features' array of a FeatureCollection starts
    private int _anchor;
    private Mode _mode;
    private string? _type;
    private GeoFeature? _feature;
    private GeoFeatureCollection? _features;

    private GeoJsonStreamReader()
    {
    }

    private enum Mode
    {
        Start,
        Root,
        Features,
        Whole,
        End,
    }

    private enum Step
    {
        NeedMoreData,
        Feature,
        Features,
        End,
    }

    public static async IAsyncEnumerable<GeoFeature> ReadAsync(Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new GeoJsonStreamReader();
        await reader.FillAsync(stream, cancellationToken).ConfigureAwait(false);
        while (true)
        {
            switch (reader.Next())
            {
                case Step.NeedMoreData:
                    await reader.FillAsync(stream, cancellationToken).ConfigureAwait(false);
                    break;

                case Step.Feature:
                    yield return reader._feature!;
                    reader._feature = null;
                    break;

                case Step.Features:
                    foreach (var feature in reader._features!)
                    {
                        yield return feature;
                    }

                    yield break;

                default:
                    yield break;
            }
        }
    }

    public void Dispose()
    {
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = [];
    }

    private async ValueTask FillAsync(Stream stream, CancellationToken cancellationToken)
    {
        // Keep the bytes that are not consumed yet, and the whole document while it may be read whole
        var keep = _mode is Mode.Features or Mode.End ? _start : _anchor;
        if (keep > 0)
        {
            _buffer.AsSpan(keep, _end - keep).CopyTo(_buffer);
            _start -= keep;
            _anchor -= keep;
            _end -= keep;
        }

        // A feature larger than the buffer is read again from its start once more data is read: doubling the buffer keeps it linear
        if (_end == _buffer.Length)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(checked(_buffer.Length * 2));
            _buffer.AsSpan(0, _end).CopyTo(buffer);
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = buffer;
        }

        var minimum = _isFirstBlock ? Math.Min(Utf8Bom.Length, _buffer.Length - _end) : 1;
        var read = await stream.ReadAtLeastAsync(_buffer.AsMemory(_end), minimum, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
        _end += read;
        if (read < minimum)
        {
            _isFinalBlock = true;
        }

        if (_isFirstBlock)
        {
            _isFirstBlock = false;
            if (_buffer.AsSpan(0, _end).StartsWith(Utf8Bom))
            {
                _start = Utf8Bom.Length;
                _anchor = _start;
            }
        }
    }

    private Step Next()
    {
        try
        {
            return NextCore();
        }
        catch (JsonException ex)
        {
            throw new FormatException("The document is not valid JSON: " + ex.Message, ex);
        }
        catch (InvalidOperationException ex)
        {
            // A string with an escaped unpaired surrogate ("\ud800"), which a .NET string cannot be read from
            throw new FormatException("The JSON text contains a string that is not valid UTF-16: " + ex.Message, ex);
        }
    }

    private Step NextCore()
    {
        var reader = new Utf8JsonReader(_buffer.AsSpan(_start, _end - _start), _isFinalBlock, _state);
        while (true)
        {
            // The position to come back to when a token, a property or a feature is not complete yet
            var checkpoint = reader;
            if (!reader.Read())
            {
                if (!_isFinalBlock)
                    return Rewind(ref checkpoint);

                if (_mode is Mode.End)
                    return Step.End;

                // The document is complete, and is not a FeatureCollection whose features were streamed
                using var document = JsonDocument.Parse(_buffer.AsMemory(_anchor, _end - _anchor));
                _features = GeoJson.ReadFeatures(document.RootElement);
                _mode = Mode.End;
                return Step.Features;
            }

            switch (_mode)
            {
                case Mode.Start:
                    _mode = reader.TokenType is JsonTokenType.StartObject ? Mode.Root : Mode.Whole;
                    break;

                case Mode.Root when reader.TokenType is JsonTokenType.EndObject:
                    // The root object of a document without a 'features' array is read whole
                    _mode = Mode.Whole;
                    break;

                case Mode.Root when reader.TokenType is JsonTokenType.PropertyName && reader.ValueTextEquals("features"u8) && _type is null or "FeatureCollection":
                    if (!reader.Read())
                        return Rewind(ref checkpoint);

                    if (reader.TokenType is JsonTokenType.StartArray)
                    {
                        _mode = Mode.Features;
                    }
                    else if (!reader.TrySkip())
                    {
                        return Rewind(ref checkpoint);
                    }

                    break;

                case Mode.Root or Mode.End when reader.TokenType is JsonTokenType.PropertyName && reader.ValueTextEquals("type"u8):
                    var value = reader;
                    if (!value.TrySkip())
                        return Rewind(ref checkpoint);

                    _ = reader.Read();
                    if (reader.TokenType is JsonTokenType.String)
                    {
                        _type = reader.GetString();
                    }

                    reader = value;
                    break;

                case Mode.Root or Mode.End when reader.TokenType is JsonTokenType.PropertyName:
                    if (!reader.TrySkip())
                        return Rewind(ref checkpoint);

                    break;

                case Mode.Features when reader.TokenType is JsonTokenType.EndArray:
                    // The features were streamed: the rest of the root is checked and skipped
                    _mode = Mode.End;
                    break;

                case Mode.Features when reader.TokenType is JsonTokenType.StartObject:
                    var featureStart = _start + (int)reader.TokenStartIndex;
                    if (!reader.TrySkip())
                        return Rewind(ref checkpoint);

                    var featureEnd = _start + (int)reader.BytesConsumed;
                    using (var document = JsonDocument.Parse(_buffer.AsMemory(featureStart, featureEnd - featureStart)))
                    {
                        _feature = GeoJson.ReadFeature(document.RootElement);
                    }

                    Commit(ref reader);
                    return Step.Feature;

                case Mode.Features:
                    throw new FormatException("The items of a FeatureCollection must be features.");

                case Mode.End when reader.TokenType is JsonTokenType.EndObject && reader.CurrentDepth is 0:
                    if (_type is not "FeatureCollection")
                        throw new FormatException(_type is null ? "A GeoJSON object must have a 'type' member." : "A GeoJSON object with a 'features' array must be a FeatureCollection.");

                    break;
            }
        }
    }

    private Step Rewind(ref Utf8JsonReader checkpoint)
    {
        Commit(ref checkpoint);
        return Step.NeedMoreData;
    }

    private void Commit(ref Utf8JsonReader reader)
    {
        _start += (int)reader.BytesConsumed;
        _state = reader.CurrentState;
    }
}
