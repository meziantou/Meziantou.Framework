using System.Buffers.Binary;
using System.Security.Cryptography;
using Meziantou.Framework.DnsClient.Protocol;
using Meziantou.Framework.DnsClient.Response;
using Meziantou.Framework.DnsClient.Response.Records;
using DnsResponseCode = Meziantou.Framework.DnsClient.Response.DnsResponseCode;

namespace Meziantou.Framework.DnsClient.Internal;

/// <summary>
/// Verifies the TSIG records of the responses to a signed query (RFC 8945 section 5.3): one response, or the messages of
/// a zone transfer, where the server may leave up to 99 messages in a row unsigned.
/// </summary>
internal sealed class DnsTsigVerifier : IDisposable
{
    private const int MaxUnsignedMessages = 99;

    private readonly DnsTsigKey _key;
    private readonly TimeProvider _timeProvider;
    private readonly IncrementalHash _hmac;
    private readonly int _macSize;
    private bool _first = true;
    private int _unsignedMessages;

    public DnsTsigVerifier(DnsTsigKey key, ReadOnlySpan<byte> requestMac, TimeProvider timeProvider)
    {
        _key = key;
        _timeProvider = timeProvider;
        _hmac = DnsTsig.CreateHmac(key);
        _macSize = _hmac.HashLengthInBytes;
        DnsTsig.AppendMac(_hmac, requestMac);
    }

    /// <summary>Gets a value indicating whether the last verified message was signed: the last message of a transfer must be.</summary>
    public bool IsLastMessageSigned { get; private set; }

    /// <summary>
    /// Verifies the signature of a message. A TSIG error in the first response (the server did not recognize the key or
    /// the signature) becomes the response code of the response, which is not authenticated.
    /// </summary>
    /// <exception cref="DnsProtocolException">The message is not signed when it must be, or its signature is not valid.</exception>
    public void Verify(ReadOnlySpan<byte> message, DnsResponseMessage response)
    {
        var tsig = GetTsigRecord(response);
        if (tsig is null)
        {
            if (_first)
                throw new DnsProtocolException("The response to a TSIG-signed query is not signed.");

            if (++_unsignedMessages > MaxUnsignedMessages)
                throw new DnsProtocolException($"The server sent more than {MaxUnsignedMessages} unsigned messages in a row (RFC 8945 section 5.3.1).");

            // The unsigned messages are covered by the MAC of the next signed message, as they were received
            _hmac.AppendData(message);
            IsLastMessageSigned = false;
            return;
        }

        if (!DnsNameComparer.Equals(tsig.Name, _key.CanonicalName))
            throw new DnsProtocolException($"The response is signed with the key '{tsig.Name}' instead of '{_key.Name}'.");

        if (!DnsNameComparer.Equals(tsig.Algorithm, DnsTsig.GetAlgorithmName(_key.Algorithm)))
            throw new DnsProtocolException($"The response is signed with the algorithm '{tsig.Algorithm}' instead of '{DnsTsig.GetAlgorithmName(_key.Algorithm)}'.");

        if (tsig.Error is not DnsResponseCode.NoError)
        {
            if (!_first)
                throw new DnsProtocolException($"The server reported the TSIG error {tsig.Error} during the transfer.");

            // RFC 8945 section 5.3.2: the server could not verify the query, and answers NOTAUTH with no data. Only the
            // TSIG errors are accepted, so a forged response cannot pass for another answer (NXDOMAIN, say).
            if (tsig.Error is not (DnsResponseCode.BadSignature or DnsResponseCode.BadKey or DnsResponseCode.BadTime or DnsResponseCode.BadTruncation))
                throw new DnsProtocolException($"The TSIG record of the response has the error {tsig.Error}, which is not a TSIG error.");

            if (response.Header.ResponseCode is not DnsResponseCode.NotAuthoritative)
                throw new DnsProtocolException($"The response reports the TSIG error {tsig.Error} with the response code {response.Header.ResponseCode} instead of NotAuthoritative.");

            if (response.Answers.Count > 0 || response.Authorities.Count > 0 || response.AdditionalRecords.Any(record => record is not DnsOptRecord and not DnsTsigRecord))
                throw new DnsProtocolException($"The response reports the TSIG error {tsig.Error}, but carries records.");

            if (tsig.Error is DnsResponseCode.BadSignature or DnsResponseCode.BadKey)
            {
                // The server could not compute a MAC with the key: the response cannot be authenticated, and carries
                // nothing but the error
                response.Header.ResponseCode = tsig.Error;
                _first = false;
                IsLastMessageSigned = true;
                return;
            }

            // A BADTIME or BADTRUNC response is signed with the key: it is verified below
        }

        if (tsig.Mac.Length != _macSize)
            throw new DnsProtocolException($"The TSIG MAC of the response has {tsig.Mac.Length} bytes instead of {_macSize}: truncated MACs are not accepted.");

        // The message as it was signed: without the TSIG record, and with its original identifier
        var signedMessage = message[..tsig.MessageOffset].ToArray();
        BinaryPrimitives.WriteUInt16BigEndian(signedMessage, tsig.OriginalId);
        BinaryPrimitives.WriteUInt16BigEndian(signedMessage.AsSpan(10), (ushort)(BinaryPrimitives.ReadUInt16BigEndian(signedMessage.AsSpan(10)) - 1));

        _hmac.AppendData(signedMessage);
        DnsTsig.AppendVariables(_hmac, _key, tsig.TimeSigned, tsig.Fudge, tsig.Error, tsig.OtherData, timersOnly: !_first);
        var expectedMac = _hmac.GetHashAndReset();
        if (!CryptographicOperations.FixedTimeEquals(expectedMac, tsig.Mac))
            throw new DnsProtocolException("The TSIG signature of the response is not valid.");

        // A BADTIME response says the clocks differ: its own time is not checked
        if (tsig.Error is not DnsResponseCode.BadTime)
        {
            var now = (ulong)Math.Max(0, _timeProvider.GetUtcNow().ToUnixTimeSeconds());
            var difference = now > tsig.TimeSigned ? now - tsig.TimeSigned : tsig.TimeSigned - now;
            if (difference > tsig.Fudge)
                throw new DnsProtocolException($"The response was signed {difference} seconds away from the current time, which is more than the {tsig.Fudge} seconds the server allows.");
        }

        if (tsig.Error is not DnsResponseCode.NoError)
        {
            response.Header.ResponseCode = tsig.Error;
        }

        DnsTsig.AppendMac(_hmac, tsig.Mac);
        _first = false;
        _unsignedMessages = 0;
        IsLastMessageSigned = true;
    }

    public void Dispose()
    {
        _hmac.Dispose();
    }

    /// <summary>Gets the TSIG record of a message, which must be the last record of the additional section (RFC 8945 section 5.1).</summary>
    private static DnsTsigRecord? GetTsigRecord(DnsResponseMessage response)
    {
        var tsigCount = response.Answers.Count(record => record is DnsTsigRecord)
            + response.Authorities.Count(record => record is DnsTsigRecord)
            + response.AdditionalRecords.Count(record => record is DnsTsigRecord);
        if (tsigCount is 0)
            return null;

        if (tsigCount > 1 || response.AdditionalRecords.Count is 0 || response.AdditionalRecords[^1] is not DnsTsigRecord tsig)
            throw new DnsProtocolException("A TSIG record must be the last record of the additional section, and a message has at most one.");

        return tsig;
    }
}
