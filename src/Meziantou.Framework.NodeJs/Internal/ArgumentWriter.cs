using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meziantou.Framework.NodeJs.Internal;

/// <summary>
/// Writes the arguments of a call. Values that JSON cannot represent (<see cref="JSValue"/> and <see cref="JSReference"/>) are written as <see langword="null"/>,
/// and listed with their location in the <c>values</c> property, so the JSON provided by the caller never needs to be escaped.
/// </summary>
internal sealed class ArgumentWriter : IDisposable
{
    private readonly Utf8JsonWriter _writer;
    private readonly NodeJsChannel _channel;
    private readonly List<PathSegment> _path = [];
    private ArrayBufferWriter<byte>? _valuesBuffer;
    private Utf8JsonWriter? _valuesWriter;

    private readonly string _parameterName;

    private ArgumentWriter(Utf8JsonWriter writer, NodeJsChannel channel, string parameterName)
    {
        _writer = writer;
        _channel = channel;
        _parameterName = parameterName;
    }

    /// <summary>Writes the <c>args</c> and <c>values</c> properties of a message sent on <paramref name="channel"/>.</summary>
    /// <exception cref="ArgumentException">A <see cref="JSReference"/> belongs to another host, or to another worker thread.</exception>
    /// <exception cref="ObjectDisposedException">A <see cref="JSReference"/> is disposed.</exception>
    public static void Write(Utf8JsonWriter writer, NodeJsChannel channel, IReadOnlyList<JsonNode?>? arguments)
    {
        using var argumentWriter = new ArgumentWriter(writer, channel, nameof(arguments));
        argumentWriter.WriteArguments(arguments);
    }

    /// <summary>Gets the host of the <see cref="JSReference"/> instances contained in the arguments, or <see langword="null"/> when there is none.</summary>
    /// <exception cref="ArgumentException">The references belong to different hosts.</exception>
    public static NodeJsHost? FindReferenceHost(IReadOnlyList<JsonNode?>? arguments)
    {
        NodeJsHost? host = null;
        if (arguments is not null)
        {
            foreach (var argument in arguments)
            {
                VisitReferences(argument, reference => SetReferenceHost(reference, ref host, nameof(arguments)));
            }
        }

        return host;
    }

    /// <inheritdoc cref="FindReferenceHost(IReadOnlyList{JsonNode?}?)"/>
    public static NodeJsHost? FindReferenceHost(object?[]? arguments)
    {
        NodeJsHost? host = null;
        if (arguments is not null)
        {
            foreach (var argument in arguments)
            {
                switch (argument)
                {
                    case JSReference reference:
                        SetReferenceHost(reference, ref host, nameof(arguments));
                        break;
                    case JsonNode node:
                        VisitReferences(node, reference => SetReferenceHost(reference, ref host, nameof(arguments)));
                        break;
                }
            }
        }

        return host;
    }

    /// <summary>Gets the connection with the thread that keeps the values of <paramref name="target"/> and of the <see cref="JSReference"/> instances contained in the arguments, or <see langword="null"/> when there is none.</summary>
    /// <exception cref="ArgumentException">The references belong to another host, or to different worker threads.</exception>
    public static NodeJsChannel? FindReferenceChannel(NodeJsHost host, JSReference? target, IReadOnlyList<JsonNode?>? arguments)
    {
        NodeJsChannel? channel = target?.Channel;
        if (arguments is not null)
        {
            foreach (var argument in arguments)
            {
                VisitReferences(argument, reference =>
                {
                    if (reference.Host != host)
                        throw new ArgumentException("The reference belongs to another Node.js process.", nameof(arguments));

                    if (channel is not null && channel != reference.Channel)
                        throw new ArgumentException("The arguments contain references to values of different worker threads of the Node.js process.", nameof(arguments));

                    channel = reference.Channel;
                });
            }
        }

        return channel;
    }

    /// <summary>Converts arguments to JSON nodes using reflection. <see cref="JsonNode"/>, <see cref="JSValue"/>, and <see cref="JSReference"/> arguments are used as is.</summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="options">The serialization options. When <see langword="null"/>, the web defaults are used, so property names are camelCase like in JavaScript.</param>
    [RequiresUnreferencedCode("JSON serialization might require types that cannot be statically analyzed.")]
    [RequiresDynamicCode("JSON serialization might require types that cannot be statically analyzed and might need runtime code generation.")]
    public static JsonNode?[]? SerializeArguments(object?[]? arguments, JsonSerializerOptions? options)
    {
        if (arguments is null)
            return null;

        var nodes = new JsonNode?[arguments.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            nodes[i] = arguments[i] switch
            {
                null => null,
                JsonNode node => node,
                JSValue value => value.ToJsonNode(),
                JSReference reference => reference.ToJsonNode(),
                var argument => JsonSerializer.SerializeToNode(argument, argument.GetType(), options ?? NodeJsHost.DefaultArgumentSerializerOptions),
            };
        }

        return nodes;
    }

    private static void VisitReferences(JsonNode? node, Action<JSReference> visitor)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (_, child) in obj)
                {
                    VisitReferences(child, visitor);
                }

                break;

            case JsonArray array:
                foreach (var child in array)
                {
                    VisitReferences(child, visitor);
                }

                break;

            case JsonValue value when value.TryGetValue(out JSReference? reference):
                visitor(reference);
                break;
        }
    }

    private static void SetReferenceHost(JSReference reference, ref NodeJsHost? host, string parameterName)
    {
        if (host is not null && host != reference.Host)
            throw new ArgumentException("The arguments contain references to values of different Node.js processes.", parameterName);

        host = reference.Host;
    }

    public void Dispose()
    {
        _valuesWriter?.Dispose();
    }

    private void WriteArguments(IReadOnlyList<JsonNode?>? arguments)
    {
        _writer.WriteStartArray("args");
        if (arguments is not null)
        {
            for (var i = 0; i < arguments.Count; i++)
            {
                _path.Add(new PathSegment(i, name: null));
                WriteNode(arguments[i]);
                _path.RemoveAt(_path.Count - 1);
            }
        }

        _writer.WriteEndArray();

        if (_valuesWriter is not null)
        {
            _valuesWriter.WriteEndArray();
            _valuesWriter.Flush();
            _writer.WritePropertyName("values");
            _writer.WriteRawValue(_valuesBuffer!.WrittenSpan, skipInputValidation: true);
        }
    }

    private void WriteNode(JsonNode? node)
    {
        switch (node)
        {
            case null:
                _writer.WriteNullValue();
                break;

            case JsonObject obj:
                _writer.WriteStartObject();
                foreach (var (name, child) in obj)
                {
                    _writer.WritePropertyName(name);
                    _path.Add(new PathSegment(index: 0, name));
                    WriteNode(child);
                    _path.RemoveAt(_path.Count - 1);
                }

                _writer.WriteEndObject();
                break;

            case JsonArray array:
                _writer.WriteStartArray();
                for (var i = 0; i < array.Count; i++)
                {
                    _path.Add(new PathSegment(i, name: null));
                    WriteNode(array[i]);
                    _path.RemoveAt(_path.Count - 1);
                }

                _writer.WriteEndArray();
                break;

            case JsonValue value when value.TryGetValue(out JSValue? jsValue):
                WriteValue(jsValue);
                break;

            case JsonValue value when value.TryGetValue(out JSReference? reference):
                WriteReference(reference);
                break;

            default:
                node.WriteTo(_writer);
                break;
        }
    }

    private void WriteValue(JSValue value)
    {
        switch (value.Kind)
        {
            case JSValueKind.Null:
                _writer.WriteNullValue();
                return;

            case JSValueKind.String:
                _writer.WriteStringValue((string)value.Value!);
                return;

            case JSValueKind.Boolean:
                _writer.WriteBooleanValue((bool)value.Value!);
                return;

            case JSValueKind.Undefined:
                StartValue("undefined");
                break;

            case JSValueKind.BigInt:
                StartValue("bigint").WriteString("value", ((BigInteger)value.Value!).ToString(CultureInfo.InvariantCulture));
                break;

            case JSValueKind.Number:
                var number = (double)value.Value!;
                var specialNumber = JSValue.GetSpecialNumberRepresentation(number);
                if (specialNumber is null)
                {
                    _writer.WriteNumberValue(number);
                    return;
                }

                StartValue("number").WriteString("value", specialNumber);
                break;

            case JSValueKind.NumberLiteral:
                // JSON.parse converts the literal to the nearest double-precision value
                _writer.WriteRawValue((string)value.Value!, skipInputValidation: true);
                return;

            case JSValueKind.Date:
                StartValue("date").WriteNumber("value", ((DateTimeOffset)value.Value!).ToUnixTimeMilliseconds());
                break;

            case JSValueKind.Uint8Array:
                StartValue("uint8Array").WriteBase64String("value", ((ReadOnlyMemory<byte>)value.Value!).Span);
                break;

            default:
                throw new InvalidOperationException($"Unknown kind '{value.Kind}'");
        }

        EndValue();
    }

    private void WriteReference(JSReference reference)
    {
        ObjectDisposedException.ThrowIf(reference.IsDisposed, reference);
        if (reference.Host != _channel.Host)
            throw new ArgumentException("The reference belongs to another Node.js process.", _parameterName);

        if (reference.Channel != _channel)
            throw new ArgumentException("The reference belongs to another worker thread of the Node.js process.", _parameterName);

        StartValue("reference").WriteNumber("value", reference.Id);
        EndValue();
    }

    private Utf8JsonWriter StartValue(string type)
    {
        _writer.WriteNullValue();

        if (_valuesWriter is null)
        {
            _valuesBuffer = new ArrayBufferWriter<byte>();
            _valuesWriter = new Utf8JsonWriter(_valuesBuffer, NodeJsHost.MessageWriterOptions);
            _valuesWriter.WriteStartArray();
        }

        _valuesWriter.WriteStartObject();
        _valuesWriter.WriteStartArray("path");
        foreach (var segment in _path)
        {
            if (segment.Name is null)
            {
                _valuesWriter.WriteNumberValue(segment.Index);
            }
            else
            {
                _valuesWriter.WriteStringValue(segment.Name);
            }
        }

        _valuesWriter.WriteEndArray();
        _valuesWriter.WriteString("type", type);
        return _valuesWriter;
    }

    private void EndValue()
    {
        _valuesWriter!.WriteEndObject();
    }

    private readonly struct PathSegment(int index, string? name)
    {
        public int Index { get; } = index;
        public string? Name { get; } = name;
    }
}
