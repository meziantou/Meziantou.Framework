namespace Meziantou.Framework.DnsClient;

/// <summary>Represents an error in the DNS wire protocol.</summary>
public sealed class DnsProtocolException : Exception
{
    public DnsProtocolException()
    {
    }

    public DnsProtocolException(string message)
        : base(message)
    {
    }

    public DnsProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
