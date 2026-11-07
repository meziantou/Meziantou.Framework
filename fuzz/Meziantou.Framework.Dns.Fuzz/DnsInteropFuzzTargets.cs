using Meziantou.Framework.Fuzzing;
using ClientProtocol = Meziantou.Framework.DnsClient.Protocol;
using ClientQuery = Meziantou.Framework.DnsClient.Query;
using ClientRecords = Meziantou.Framework.DnsClient.Response.Records;
using ClientResponse = Meziantou.Framework.DnsClient.Response;
using ServerProtocol = Meziantou.Framework.DnsServer.Protocol;
using ServerWire = Meziantou.Framework.DnsServer.Protocol.Wire;

namespace Meziantou.Framework.DnsFuzz;

/// <summary>
/// Differential targets between Meziantou.Framework.DnsServer and Meziantou.Framework.DnsClient, which each have their own
/// wire reader and encoder: a message one library encodes decodes to the same message with the other, and both decoders
/// read the same message from the same bytes.
/// </summary>
internal static class DnsInteropFuzzTargets
{
    /// <summary>
    /// The input is a message, decoded by the server (<c>DecodeQuery</c>) and by the client (<c>DecodeResponse</c>). The
    /// decoders differ in what they reject (the client checks the NSEC type bit map windows, the server reads CDS and CDNSKEY
    /// as unknown records, they bound the names behind a compression pointer differently), but when both decode a message,
    /// they agree on every field. The client writes names in presentation format, which is converted to the text the server
    /// uses; the response codes are only compared with at most one OPT record, as each decoder reads a different one.
    /// </summary>
    public static void Decoders(ReadOnlySpan<byte> data)
    {
        ServerProtocol.DnsMessage serverMessage;
        ClientResponse.DnsResponseMessage clientMessage;
        try
        {
            serverMessage = ServerWire.DnsMessageEncoder.DecodeQuery(data);
        }
        catch (ServerProtocol.DnsProtocolException)
        {
            return;
        }

        try
        {
            clientMessage = ClientProtocol.DnsMessageEncoder.DecodeResponse(data, preserveRawRecordData: true);
        }
        catch (DnsClient.DnsProtocolException)
        {
            return;
        }

        var includeResponseCode = clientMessage.AdditionalRecords.Count(record => record is ClientRecords.DnsOptRecord) <= 1;
        FuzzAssert.Equal(
            DnsMessageText.Format(serverMessage, normalizeNames: false, includeResponseCode),
            DnsMessageText.Format(clientMessage, normalizeNames: false, includeResponseCode),
            "The server and the client must decode the same message");
    }

    /// <summary>
    /// The input drives a generator that builds a message both libraries can encode (<see cref="DnsMessageGenerator"/>): every
    /// record type the libraries parse, names with any ASCII label, EDNS with an extended response code. The server encodes
    /// it; the server and the client decode the bytes to the same message (names compared ignoring case: the server compresses
    /// names case-insensitively). The client encodes a query with the same questions and EDNS options; it decodes back to the
    /// same presentation-format names with the client, and to the same names, exactly, with the server.
    /// </summary>
    public static void RoundTrip(ReadOnlySpan<byte> data)
    {
        var (message, generator) = DnsMessageGenerator.Generate(data);
        var expected = DnsMessageText.Format(message, normalizeNames: true);

        byte[] encoded;
        try
        {
            encoded = ServerWire.DnsMessageEncoder.EncodeResponse(message);
        }
        catch (ServerProtocol.DnsProtocolException ex)
        {
            throw new FuzzAssertionException("The server must encode a valid message", ex);
        }

        ServerProtocol.DnsMessage serverMessage;
        try
        {
            serverMessage = ServerWire.DnsMessageEncoder.DecodeQuery(encoded);
        }
        catch (ServerProtocol.DnsProtocolException ex)
        {
            throw new FuzzAssertionException("The server must decode the messages it encodes", ex);
        }

        FuzzAssert.Equal(expected, DnsMessageText.Format(serverMessage, normalizeNames: true), "The server must decode the message it encoded");

        ClientResponse.DnsResponseMessage clientMessage;
        try
        {
            clientMessage = ClientProtocol.DnsMessageEncoder.DecodeResponse(encoded, preserveRawRecordData: true);
        }
        catch (DnsClient.DnsProtocolException ex)
        {
            throw new FuzzAssertionException("The client must decode the messages the server encodes", ex);
        }

        FuzzAssert.Equal(expected, DnsMessageText.Format(clientMessage, normalizeNames: true, includeResponseCode: true), "The client must decode the message the server encoded");

        CheckQuery(message, generator);
    }

    private static void CheckQuery(ServerProtocol.DnsMessage message, DnsMessageGenerator generator)
    {
        var query = new ClientQuery.DnsQueryMessage
        {
            Id = message.Id,
            OpCode = (ClientQuery.DnsOpCode)message.OpCode,
            RecursionDesired = message.RecursionDesired,
            CheckingDisabled = message.CheckingDisabled,
        };

        var presentationNames = new List<string>();
        foreach (var question in message.Questions)
        {
            var name = string.Join('.', generator.Labels[question.Name].Select(label => DnsMessageText.ToPresentationLabel(label)));
            presentationNames.Add(name);
            query.Questions.Add(new DnsClient.DnsQuestion(name, (ClientQuery.DnsQueryType)question.Type, (ClientQuery.DnsQueryClass)question.QueryClass));
        }

        if (message.EdnsOptions is { } edns)
        {
            query.EdnsOptions = new ClientQuery.DnsEdnsOptions
            {
                UdpPayloadSize = edns.UdpPayloadSize,
                Version = edns.Version,
                DnssecOk = edns.DnssecOk,
                ExtendedRCode = edns.ExtendedRCode,
            };
        }

        byte[] encoded;
        try
        {
            encoded = ClientProtocol.DnsMessageEncoder.EncodeQuery(query, out var id);
            FuzzAssert.Equal(message.Id, id, "The query must use the ID of the message");
        }
        catch (DnsClient.DnsProtocolException ex)
        {
            throw new FuzzAssertionException("The client must encode a valid query", ex);
        }

        var clientMessage = ClientProtocol.DnsMessageEncoder.DecodeResponse(encoded);
        FuzzAssert.Equal(string.Join('\n', presentationNames), string.Join('\n', clientMessage.Questions.Select(question => question.Name)), "The client must decode the names it encoded");

        ServerProtocol.DnsMessage serverMessage;
        try
        {
            serverMessage = ServerWire.DnsMessageEncoder.DecodeQuery(encoded);
        }
        catch (ServerProtocol.DnsProtocolException ex)
        {
            throw new FuzzAssertionException("The server must decode the queries of the client", ex);
        }

        var expected = new ServerProtocol.DnsMessage
        {
            Id = message.Id,
            OpCode = message.OpCode,
            RecursionDesired = message.RecursionDesired,
            CheckingDisabled = message.CheckingDisabled,
            ResponseCode = (ServerProtocol.DnsResponseCode)((message.EdnsOptions?.ExtendedRCode ?? 0) << 4),
            EdnsOptions = message.EdnsOptions,
        };

        foreach (var question in message.Questions)
        {
            expected.Questions.Add(question);
        }

        FuzzAssert.Equal(DnsMessageText.Format(expected, normalizeNames: false), DnsMessageText.Format(serverMessage, normalizeNames: false), "The server must decode the query of the client");
    }
}
