using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.DevTools.Protocol;

namespace Meziantou.Framework.WebPlatform.DevTools.Browser;

/// <summary>The streams of a browser (the <c>IO</c> domain): the PDF printed with <c>transferMode: "ReturnAsStream"</c>.</summary>
internal sealed class StreamRegistry
{
    private const int DefaultChunkSize = 1024 * 1024;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, (byte[] Data, int Position)> _streams = new(StringComparer.Ordinal);
    private int _nextId;

    public string Add(byte[] data)
    {
        lock (_lock)
        {
            var handle = (++_nextId).ToString(CultureInfo.InvariantCulture);
            _streams.Add(handle, (data, 0));
            return handle;
        }
    }

    public JsonObject? Invoke(string method, JsonElement parameters)
    {
        var handle = parameters.GetRequiredString("handle");
        lock (_lock)
        {
            if (!_streams.TryGetValue(handle, out var stream))
                throw new ProtocolException("Invalid stream handle");

            switch (method)
            {
                case "IO.read":
                    var position = parameters.GetInt32("offset") is { } offset ? Math.Clamp(offset, 0, stream.Data.Length) : stream.Position;
                    var size = parameters.GetInt32("size") is > 0 and var requestedSize ? requestedSize : DefaultChunkSize;
                    var count = Math.Min(size, stream.Data.Length - position);
                    _streams[handle] = (stream.Data, position + count);
                    return new JsonObject
                    {
                        ["base64Encoded"] = true,
                        ["data"] = Convert.ToBase64String(stream.Data, position, count),
                        ["eof"] = position + count >= stream.Data.Length,
                    };

                case "IO.close":
                    _streams.Remove(handle);
                    return null;

                default:
                    throw ProtocolException.NotFound(method);
            }
        }
    }
}
