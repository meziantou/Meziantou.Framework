namespace Meziantou.Framework.DnsServer.Protocol;

/// <summary>Represents a DNS question in a query or response message.</summary>
public sealed class DnsQuestion
{
    /// <summary>Initializes a new instance of the <see cref="DnsQuestion"/> class.</summary>
    public DnsQuestion(string name, DnsQueryType type, DnsQueryClass queryClass = DnsQueryClass.IN)
    {
        Name = name;
        Type = type;
        QueryClass = queryClass;
    }

    /// <summary>Gets or sets the domain name being queried.</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets the DNS record type.</summary>
    public DnsQueryType Type { get; set; }

    /// <summary>Gets or sets the query class.</summary>
    public DnsQueryClass QueryClass { get; set; }
}
