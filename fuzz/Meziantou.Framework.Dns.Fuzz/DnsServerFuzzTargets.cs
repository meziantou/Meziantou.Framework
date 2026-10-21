using System.Buffers.Binary;
using System.Net;
using Meziantou.Framework.DnsServer.Handler;
using Meziantou.Framework.DnsServer.Hosting;
using Meziantou.Framework.DnsServer.Protocol;
using Meziantou.Framework.DnsServer.Protocol.Records;
using Meziantou.Framework.DnsServer.Protocol.Wire;
using Meziantou.Framework.Fuzzing;
using Microsoft.Extensions.Logging.Abstractions;
using ClientProtocol = Meziantou.Framework.DnsClient.Protocol;
using ClientRecords = Meziantou.Framework.DnsClient.Response.Records;
using ClientResponse = Meziantou.Framework.DnsClient.Response;
using DnsResponseCode = Meziantou.Framework.DnsServer.Protocol.DnsResponseCode;

namespace Meziantou.Framework.DnsFuzz;

/// <summary>
/// Targets of Meziantou.Framework.DnsServer, which decodes the queries it receives from the network: decoding only throws
/// <see cref="DnsProtocolException"/>, a decoded message encodes again unless it holds a value the encoder cannot write,
/// and the request pipeline answers every query with a well-formed response.
/// </summary>
internal static class DnsServerFuzzTargets
{
    // An input this short cannot hold record data that grows past 65535 bytes once encoded again: names behind
    // compression pointers are written in full in the data of the types defined after RFC 1035 (at most two names per
    // record), and invalid UTF-8 grows to three bytes per byte in TXT, NAPTR and URI records
    private const int MaxInputLengthWithoutRecordDataOverflow = 20_000;

    private static readonly int[] ResponseSizes = [512, 1232, 4096, ushort.MaxValue];

    private static readonly DnsServerProtocol[] Protocols = [DnsServerProtocol.Udp, DnsServerProtocol.Tcp, DnsServerProtocol.Tls, DnsServerProtocol.Https, DnsServerProtocol.Quic];

    private static readonly IPEndPoint RemoteEndPoint = new(IPAddress.Loopback, 50000);

    /// <summary>
    /// The first byte selects a size limit, the rest is a message. A decoded message encodes again exactly when every
    /// name splits into valid labels and every character string fits in 255 bytes, and the encoding decodes to the same
    /// message (names compared ignoring case: the encoder compresses names case-insensitively). Encoding is a fixed point
    /// after one round, and the size-limited encoding fits in the limit, dropping the records and setting TC when needed.
    /// </summary>
    public static void Message(ReadOnlySpan<byte> data)
    {
        var maxSize = FuzzInput.Pick(ResponseSizes, FuzzInput.ReadByte(ref data));
        DnsMessage message;
        try
        {
            message = DnsMessageEncoder.DecodeQuery(data);
        }
        catch (DnsProtocolException)
        {
            return;
        }

        CheckDecodedMessage(message, data);

        var isRepresentable = IsRepresentable(message, includeRecords: true);
        byte[] encoded;
        try
        {
            encoded = DnsMessageEncoder.EncodeResponse(message);
        }
        catch (DnsProtocolException ex)
        {
            FuzzAssert.True(!isRepresentable || data.Length > MaxInputLengthWithoutRecordDataOverflow, $"A decoded message must encode again: {ex.Message}");
            return;
        }

        FuzzAssert.True(isRepresentable, "The encoder must reject a name with an empty label or a character string longer than 255 bytes");

        var expected = DnsMessageText.Format(message, normalizeNames: true);
        DnsMessage decoded;
        try
        {
            decoded = DnsMessageEncoder.DecodeQuery(encoded);
        }
        catch (DnsProtocolException ex)
        {
            throw new FuzzAssertionException("The server cannot decode a message it encoded", ex);
        }

        FuzzAssert.Equal(expected, DnsMessageText.Format(decoded, normalizeNames: true), "Encoding then decoding must give the same message");
        FuzzAssert.True(encoded.AsSpan().SequenceEqual(DnsMessageEncoder.EncodeResponse(decoded)), "Encoding must be a fixed point after one round");

        // The client reads the labels as bytes: the server must write the names it read, not only names with the same text
        if (TryDecodeWithClient(data) is { } original)
        {
            var includeResponseCode = original.AdditionalRecords.Count(record => record is ClientRecords.DnsOptRecord) <= 1;
            var reencoded = TryDecodeWithClient(encoded);
            FuzzAssert.True(reencoded is not null, "The client must decode the encoded message");
            FuzzAssert.Equal(DnsMessageText.Format(original, normalizeNames: true, includeResponseCode, presentationNames: true), DnsMessageText.Format(reencoded, normalizeNames: true, includeResponseCode, presentationNames: true), "The client must read the same message from the decoded and encoded message");
        }

        // EncodeResponse(message, maxSize) updates the message to match what it wrote
        var limitedMessage = DnsMessageEncoder.DecodeQuery(encoded);
        var limited = DnsMessageEncoder.EncodeResponse(limitedMessage, maxSize);
        FuzzAssert.True(limited.Length <= maxSize, $"The response is {limited.Length} bytes, more than the {maxSize}-byte limit");
        if (encoded.Length <= maxSize)
        {
            FuzzAssert.True(limited.AsSpan().SequenceEqual(encoded), "A response within the limit must not change");
            return;
        }

        var truncated = DnsMessageEncoder.DecodeQuery(limited);
        FuzzAssert.Equal(DnsMessageText.Format(limitedMessage, normalizeNames: true), DnsMessageText.Format(truncated, normalizeNames: true), "The truncated message must describe the bytes that were written");
        FuzzAssert.True(truncated.IsTruncated, "A response that does not fit must have the TC bit");
        FuzzAssert.True(truncated.Answers.Count is 0 && truncated.Authorities.Count is 0 && truncated.AdditionalRecords.Count is 0, "A truncated response must not keep a partial section");
        FuzzAssert.True(truncated.Questions.Count is 0 || truncated.Questions.Count == decoded.Questions.Count, "A truncated response keeps all the questions or none");
        FuzzAssert.Equal(decoded.EdnsOptions is null, truncated.EdnsOptions is null, "Truncation must keep the OPT record");
    }

    /// <summary>
    /// The first two bytes select the transport, the handler, the size limits and how malformed messages are answered;
    /// the rest is a message, given to the request processor that every transport uses. It never throws for a handler
    /// that only answers the questions; it answers a malformed message with FORMERR, ignores a response, answers an
    /// EDNS version other than 0 with BADVERS, and its responses fit the size the transport and the client allow, echo
    /// the ID, the opcode and the questions, and decode with the client.
    /// </summary>
    public static void Request(ReadOnlySpan<byte> data)
    {
        var protocol = FuzzInput.Pick(Protocols, FuzzInput.ReadByte(ref data));
        var flags = FuzzInput.ReadByte(ref data);
        var handlerKind = (RequestHandlerKind)(flags & 0x03);
        var replyWithFormatError = (flags & 0x04) is 0;
        var options = new DnsServerOptions { MaxUdpResponseSize = ResponseSizes[(flags >> 3) & 0x03] };
        var transportMaxSize = protocol is DnsServerProtocol.Udp ? options.MaxUdpResponseSize : DnsMessageEncoder.MaxMessageSize;

        DnsMessage? query;
        try
        {
            query = DnsMessageEncoder.DecodeQuery(data);
        }
        catch (DnsProtocolException)
        {
            query = null;
        }

        var holder = new DnsRequestDelegateHolder();
        if (handlerKind is not RequestHandlerKind.Default)
        {
            holder.SetHandler((context, _) => ValueTask.FromResult(CreateResponse(context, handlerKind)));
        }

        var processor = new DnsRequestProcessor(holder, options, NullLogger<DnsRequestProcessor>.Instance);
        byte[]? response;
        try
        {
            response = processor.ProcessAsync(data.ToArray(), protocol, RemoteEndPoint, transportMaxSize, CancellationToken.None, replyWithFormatError).AsTask().GetAwaiter().GetResult();
        }
        catch (DnsProtocolException ex)
        {
            // Only the echo handler can return records the encoder rejects; the other handlers only answer the questions
            FuzzAssert.True(query is not null && !IsRepresentable(query, includeRecords: handlerKind is RequestHandlerKind.Echo), $"The request processor threw: {ex.Message}");
            return;
        }

        var hasHeader = data.Length >= 12;
        var queryFlags = hasHeader ? BinaryPrimitives.ReadUInt16BigEndian(data[2..]) : 0;
        var isResponse = (queryFlags & 0x8000) is not 0;
        if (query is null ? !hasHeader || isResponse || !replyWithFormatError : query.IsResponse)
        {
            FuzzAssert.True(response is null, "A response, or a malformed message that cannot get FORMERR, must be dropped");
            return;
        }

        FuzzAssert.True(response is not null, "A query must get a response");

        var maxSize = protocol is DnsServerProtocol.Udp ? Math.Clamp(query?.EdnsOptions?.UdpPayloadSize ?? 512, 512, options.MaxUdpResponseSize) : transportMaxSize;
        FuzzAssert.True(response.Length <= maxSize, $"The response is {response.Length} bytes, more than the {maxSize} bytes allowed");

        ClientResponse.DnsResponseMessage decoded;
        try
        {
            decoded = ClientProtocol.DnsMessageEncoder.DecodeResponse(response);
        }
        catch (DnsClient.DnsProtocolException ex)
        {
            throw new FuzzAssertionException("The client cannot decode a response of the server", ex);
        }

        var header = decoded.Header;
        FuzzAssert.Equal(BinaryPrimitives.ReadUInt16BigEndian(data), header.Id, "The response must echo the query ID");
        FuzzAssert.True(header.IsResponse, "The response must have the QR bit");
        FuzzAssert.Equal((queryFlags >> 11) & 0x0F, (int)header.OpCode, "The response must echo the opcode");
        FuzzAssert.Equal((queryFlags & 0x0100) is not 0, header.RecursionDesired, "The response must echo the RD bit");

        if (query is null)
        {
            FuzzAssert.Equal(DnsResponseCode.FormError, (DnsResponseCode)header.ResponseCode, "A malformed query must get FORMERR");
            FuzzAssert.True(decoded.Questions.Count is 0 && decoded.Answers.Count is 0 && decoded.Authorities.Count is 0 && decoded.AdditionalRecords.Count is 0, "A FORMERR response must be empty");
            return;
        }

        var expectedResponseCode = query.EdnsOptions is { Version: > 0 }
            ? DnsResponseCode.BadVersion
            : handlerKind switch
            {
                RequestHandlerKind.Default => DnsResponseCode.ServerFailure,
                RequestHandlerKind.Echo => query.ResponseCode,
                _ => DnsResponseCode.NoError,
            };

        FuzzAssert.Equal(expectedResponseCode, (DnsResponseCode)header.ResponseCode, "Unexpected response code");
        if (header.IsTruncated && decoded.Questions.Count is 0)
            return;

        // The names are compared as the client reads them, label bytes ignoring ASCII case
        var queryQuestionNames = ReadQuestionNames(data, query.Questions.Count);
        FuzzAssert.Equal(query.Questions.Count, decoded.Questions.Count, "The response must echo the questions");
        for (var i = 0; i < query.Questions.Count; i++)
        {
            var question = decoded.Questions[i];
            FuzzAssert.True(
                ClientProtocol.DnsNameComparer.Equals(queryQuestionNames[i], question.Name) &&
                (ushort)question.Type == (ushort)query.Questions[i].Type && (ushort)question.QueryClass == (ushort)query.Questions[i].QueryClass,
                $"The response must echo question {i}: {FuzzAssert.Format(queryQuestionNames[i])}, but it is {FuzzAssert.Format(question.Name)}");
        }

        if (header.IsTruncated)
        {
            FuzzAssert.True(decoded.Answers.Count is 0 && decoded.Authorities.Count is 0 && decoded.AdditionalRecords.All(record => record is ClientRecords.DnsOptRecord), "A truncated response must not keep a partial section");
        }
        else if (handlerKind is RequestHandlerKind.Answer && query.EdnsOptions is not { Version: > 0 })
        {
            FuzzAssert.Equal(query.Questions.Count * 2, decoded.Answers.Count, "The response must hold the answers of the handler");
        }
    }

    private static ClientResponse.DnsResponseMessage? TryDecodeWithClient(ReadOnlySpan<byte> data)
    {
        try
        {
            return ClientProtocol.DnsMessageEncoder.DecodeResponse(data, preserveRawRecordData: true);
        }
        catch (DnsClient.DnsProtocolException)
        {
            return null;
        }
    }

    /// <summary>Reads the question names of a message the server decoded, in presentation format (client).</summary>
    private static string[] ReadQuestionNames(ReadOnlySpan<byte> data, int count)
    {
        var reader = new ClientProtocol.DnsWireReader(data);
        reader.Skip(12);
        var names = new string[count];
        for (var i = 0; i < count; i++)
        {
            names[i] = reader.ReadDomainName();
            reader.Skip(4);
        }

        return names;
    }

    /// <summary>Gets whether the server can encode a decoded message: the names split into valid labels, and the character strings fit in 255 bytes.</summary>
    public static bool IsRepresentable(DnsMessage message, bool includeRecords)
    {
        var isRepresentable = message.Questions.All(question => IsRepresentableName(question.Name));
        if (!includeRecords)
            return isRepresentable;

        foreach (var record in message.Answers.Concat(message.Authorities).Concat(message.AdditionalRecords))
        {
            isRepresentable &= IsRepresentableName(record.Name);
            _ = DnsRecordFormatter.Format(record.Data, name =>
            {
                isRepresentable &= IsRepresentableName(name);
                return name;
            }, sortTypeBitMaps: false);

            isRepresentable &= record.Data switch
            {
                DnsTxtRecordData txt => txt.Text.All(text => Encoding.UTF8.GetByteCount(text) <= 255),
                DnsNaptrRecordData naptr => Encoding.UTF8.GetByteCount(naptr.Regexp) <= 255,
                _ => true,
            };
        }

        return isRepresentable;
    }

    /// <summary>The encoder drops one trailing dot, then splits the name on the dots: a decoded label holding a dot can leave an empty label.</summary>
    private static bool IsRepresentableName(string name)
    {
        var span = name.AsSpan();
        if (span.EndsWith('.'))
        {
            span = span[..^1];
        }

        if (span.IsEmpty)
            return true;

        foreach (var range in span.Split('.'))
        {
            if (range.Start.Equals(range.End))
                return false;
        }

        return true;
    }

    private static void CheckDecodedMessage(DnsMessage message, ReadOnlySpan<byte> data)
    {
        FuzzAssert.Equal(BinaryPrimitives.ReadUInt16BigEndian(data), message.Id, "Wrong message ID");
        FuzzAssert.Equal(BinaryPrimitives.ReadUInt16BigEndian(data[4..]), message.Questions.Count, "Wrong number of questions");
        FuzzAssert.Equal(BinaryPrimitives.ReadUInt16BigEndian(data[6..]), message.Answers.Count, "Wrong number of answers");
        FuzzAssert.Equal(BinaryPrimitives.ReadUInt16BigEndian(data[8..]), message.Authorities.Count, "Wrong number of authority records");
        FuzzAssert.Equal(BinaryPrimitives.ReadUInt16BigEndian(data[10..]), message.AdditionalRecords.Count + (message.EdnsOptions is null ? 0 : 1), "Wrong number of additional records");
        FuzzAssert.True((int)message.ResponseCode <= 0xFFF, "A response code has 12 bits");
        FuzzAssert.True(message.EdnsOptions is not null || (int)message.ResponseCode <= 0x0F, "Only the OPT record carries the extended response code bits");
        FuzzAssert.False(message.AdditionalRecords.Any(record => record.Type is DnsQueryType.OPT) && message.EdnsOptions is null, "An OPT record must give the EDNS options");

        foreach (var question in message.Questions)
        {
            CheckName(question.Name);
        }

        foreach (var record in message.Answers.Concat(message.Authorities).Concat(message.AdditionalRecords))
        {
            CheckName(record.Name);
            FuzzAssert.True(record.Data is not null, "A decoded record must have data");
            _ = DnsRecordFormatter.Format(record.Data, name =>
            {
                CheckName(name);
                return name;
            }, sortTypeBitMaps: false);
        }
    }

    private static void CheckName(string name)
    {
        FuzzAssert.True(name.Length <= DnsMessageText.MaxNameTextLength, $"The name {FuzzAssert.Format(name)} is longer than a domain name can be");
        FuzzAssert.True(Ascii.IsValid(name), $"The name {FuzzAssert.Format(name)} must be ASCII");
    }

    private static DnsMessage CreateResponse(DnsRequestContext context, RequestHandlerKind handlerKind)
    {
        var response = context.CreateResponse();
        switch (handlerKind)
        {
            case RequestHandlerKind.Answer:
                foreach (var question in context.Query.Questions)
                {
                    response.Answers.Add(new DnsResourceRecord { Name = question.Name, Type = DnsQueryType.A, TimeToLive = 300, Data = new DnsARecordData { Address = IPAddress.Parse("192.0.2.1") } });
                    response.Answers.Add(new DnsResourceRecord { Name = question.Name, Type = DnsQueryType.TXT, TimeToLive = 300, Data = new DnsTxtRecordData { Text = ["v=spf1 -all"] } });
                }

                break;

            case RequestHandlerKind.Echo:
                // The records the server parsed, which it encodes from their fields: it copies the data of the other
                // types as is, which the client may reject (a CDS shorter than a DS). The OPT records are left out: the
                // client reads the extended response code from the first one.
                response.ResponseCode = context.Query.ResponseCode;
                response.IsAuthoritative = context.Query.IsAuthoritative;
                foreach (var record in context.Query.Answers.Where(IsEchoed))
                {
                    response.Answers.Add(record);
                }

                foreach (var record in context.Query.Authorities.Where(IsEchoed))
                {
                    response.Authorities.Add(record);
                }

                foreach (var record in context.Query.AdditionalRecords.Where(IsEchoed))
                {
                    response.AdditionalRecords.Add(record);
                }

                break;

            case RequestHandlerKind.Large:
                // More than the largest UDP response, so that the server has to truncate it
                var text = new string('x', 250);
                foreach (var question in context.Query.Questions)
                {
                    for (var i = 0; i < 20; i++)
                    {
                        response.Answers.Add(new DnsResourceRecord { Name = question.Name, Type = DnsQueryType.TXT, TimeToLive = 300, Data = new DnsTxtRecordData { Text = [text, text] } });
                    }
                }

                response.AdditionalRecords.Add(new DnsResourceRecord { Name = "ns.example.com", Type = DnsQueryType.AAAA, TimeToLive = 300, Data = new DnsAaaaRecordData { Address = IPAddress.IPv6Loopback } });
                break;
        }

        return response;
    }

    private static bool IsEchoed(DnsResourceRecord record) => record.Data is not DnsUnknownRecordData and not DnsOptRecordData;

    private enum RequestHandlerKind
    {
        /// <summary>No handler registered: SERVFAIL.</summary>
        Default,

        /// <summary>An A and a TXT record for each question.</summary>
        Answer,

        /// <summary>The records and the response code of the query.</summary>
        Echo,

        /// <summary>A response that does not fit in a UDP datagram.</summary>
        Large,
    }
}
