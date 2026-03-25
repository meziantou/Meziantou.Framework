namespace Meziantou.Framework.DnsServer.Protocol;

/// <summary>Represents a DNS protocol exception.</summary>
public sealed class DnsProtocolException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="DnsProtocolException"/> class.</summary>
    public DnsProtocolException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DnsProtocolException"/> class with a specified error message.</summary>
    public DnsProtocolException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DnsProtocolException"/> class with a specified error message and inner exception.</summary>
    public DnsProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
