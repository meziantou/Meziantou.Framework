using System.Net;
using Meziantou.Framework.DnsServer.Protocol;

namespace Meziantou.Framework.DnsServer.Handler;

/// <summary>Provides context for a DNS request being handled.</summary>
public sealed class DnsRequestContext
{
    internal DnsRequestContext(DnsMessage query, DnsServerProtocol protocol, EndPoint remoteEndPoint)
    {
        Query = query;
        Protocol = protocol;
        RemoteEndPoint = remoteEndPoint;
    }

    /// <summary>Gets the incoming DNS query message.</summary>
    public DnsMessage Query { get; }

    /// <summary>Gets the transport protocol used for this request.</summary>
    public DnsServerProtocol Protocol { get; }

    /// <summary>Gets the remote endpoint of the client.</summary>
    public EndPoint RemoteEndPoint { get; }

    /// <summary>Creates a response message pre-populated with the query's ID, questions, and standard response flags.</summary>
    public DnsMessage CreateResponse()
    {
        var response = new DnsMessage
        {
            Id = Query.Id,
            IsResponse = true,
            OpCode = Query.OpCode,
            RecursionDesired = Query.RecursionDesired,
            ResponseCode = DnsResponseCode.NoError,
        };

        foreach (var question in Query.Questions)
        {
            response.Questions.Add(question);
        }

        if (Query.EdnsOptions is not null)
        {
            response.EdnsOptions = new DnsEdnsOptions
            {
                UdpPayloadSize = 4096,
            };
        }

        return response;
    }
}
