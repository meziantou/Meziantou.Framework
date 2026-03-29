using System.Collections.Concurrent;
using Meziantou.Framework.DnsServer.Protocol;
using Meziantou.Framework.DnsServer.Protocol.Records;
using Microsoft.Extensions.Options;

namespace Meziantou.DnsProxy.History;

internal sealed class RequestHistoryStore
{
    private readonly ConcurrentQueue<RequestHistoryEntry> _entries = new();
    private readonly int _capacity;

    public RequestHistoryStore(IOptions<DnsProxyOptions> options)
    {
        _capacity = Math.Max(1, options.Value.DiagnosticsHistoryCapacity);
    }

    public void Add(RequestHistoryEntry entry)
    {
        _entries.Enqueue(entry);
        while (_entries.Count > _capacity && _entries.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyList<RequestHistoryEntry> GetSnapshot()
    {
        return _entries.ToArray()
            .OrderByDescending(item => item.TimestampUtc)
            .ToArray();
    }
}
