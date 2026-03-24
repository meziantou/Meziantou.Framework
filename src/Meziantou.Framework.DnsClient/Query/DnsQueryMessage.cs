namespace Meziantou.Framework.DnsClient.Query;

/// <summary>Represents a DNS query message to be sent to a DNS server.</summary>
public sealed class DnsQueryMessage
{
    /// <summary>Gets or sets the query identifier. A random value is generated if not set.</summary>
    public ushort? Id { get; set; }

    /// <summary>Gets or sets the operation code. Default is <see cref="DnsOpCode.Query"/>.</summary>
    public DnsOpCode OpCode { get; set; } = DnsOpCode.Query;

    /// <summary>Gets or sets a value indicating whether recursion is desired. Default is <see langword="true"/>.</summary>
    public bool RecursionDesired { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether checking is disabled (DNSSEC). Default is <see langword="false"/>.</summary>
    public bool CheckingDisabled { get; set; }

    /// <summary>Gets the list of questions in this query.</summary>
    [SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public List<DnsQuestion> Questions { get; } = [];

    /// <summary>Gets or sets the EDNS(0) options. Set to <see langword="null"/> to disable EDNS.</summary>
    public DnsEdnsOptions? EdnsOptions { get; set; }
}
