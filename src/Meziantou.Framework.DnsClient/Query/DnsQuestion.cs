using Meziantou.Framework.DnsClient.Query;

namespace Meziantou.Framework.DnsClient;

/// <summary>
/// Represents a DNS question in the question section of a DNS message.
/// </summary>
public sealed class DnsQuestion
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DnsQuestion"/> class.
    /// </summary>
    /// <param name="name">The domain name to query.</param>
    /// <param name="type">The DNS record type to query.</param>
    /// <param name="queryClass">The DNS query class.</param>
    public DnsQuestion(string name, DnsQueryType type, DnsQueryClass queryClass = DnsQueryClass.IN)
    {
        Name = name;
        Type = type;
        QueryClass = queryClass;
    }

    /// <summary>Gets the domain name being queried.</summary>
    public string Name { get; }

    /// <summary>Gets the DNS record type being queried.</summary>
    public DnsQueryType Type { get; }

    /// <summary>Gets the DNS class being queried.</summary>
    public DnsQueryClass QueryClass { get; }
}
