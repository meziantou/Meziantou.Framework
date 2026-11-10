using System.Buffers.Binary;
using Meziantou.Framework.DnsClient;
using Meziantou.Framework.DnsClient.Internal;
using Meziantou.Framework.DnsClient.Protocol;
using Meziantou.Framework.DnsClient.Query;
using Meziantou.Framework.DnsClient.Response;
using Meziantou.Framework.DnsClient.Response.Records;
using Meziantou.Framework.DnsClient.Transport;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Cryptography;
using DnsResponseCode = Meziantou.Framework.DnsClient.Response.DnsResponseCode;

namespace Meziantou.Framework.DnsFuzz;

/// <summary>
/// Targets of Meziantou.Framework.DnsClient, which decodes the responses it receives from the network (or from an
/// attacker spoofing them): decoding only throws <see cref="DnsProtocolException"/>, the names it returns encode back
/// to the same wire names, and DNSSEC validation of a forged chain never reports Secure or Insecure.
/// </summary>
internal static class DnsClientFuzzTargets
{
    private const int MaxDnssecResponses = 64;
    private const int MaxTransferMessages = 16;

    // The time the TSIG messages of the harness are signed and verified at
    private const ulong TsigTime = 1_700_000_000;

    private static readonly DnsTsigAlgorithm[] TsigAlgorithms = Enum.GetValues<DnsTsigAlgorithm>();
    private static readonly byte[] TsigSecret = [.. Enumerable.Range(0, 32).Select(value => (byte)value)];
    private static readonly byte[] TsigRequestMac = [.. Enumerable.Range(0x40, 32).Select(value => (byte)value)];

    private static readonly uint[] Ages = [0, 1, 60, 3600, uint.MaxValue];

    /// <summary>
    /// The first byte selects the decoding options (raw record data, age of a cached response), the rest is a message.
    /// The options do not change whether a message decodes, nor anything but the TTLs (reduced by the age) and the raw
    /// data. The counts match the header, the response code takes its extended bits from the first OPT record, and every
    /// name is a valid presentation-format name that encodes back to a wire name that decodes to the same text. The
    /// canonical order of these names (<c>DnsNameComparer</c>) is a total order that agrees with its equality.
    /// </summary>
    public static void Response(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        var preserveRawRecordData = (options & 1) is not 0;
        var age = FuzzInput.Pick(Ages, (byte)(options >> 1));

        DnsResponseMessage plain;
        try
        {
            plain = DnsMessageEncoder.DecodeResponse(data);
        }
        catch (DnsProtocolException)
        {
            return;
        }

        DnsResponseMessage response;
        try
        {
            response = DnsMessageEncoder.DecodeResponse(data, preserveRawRecordData, age);
        }
        catch (DnsProtocolException ex)
        {
            throw new FuzzAssertionException("Whether a message decodes must not depend on the decoding options", ex);
        }

        var header = response.Header;
        FuzzAssert.Equal(BinaryPrimitives.ReadUInt16BigEndian(data), header.Id, "Wrong message ID");
        FuzzAssert.Equal(header.QuestionCount, response.Questions.Count, "Wrong number of questions");
        FuzzAssert.Equal(header.AnswerCount, response.Answers.Count, "Wrong number of answers");
        FuzzAssert.Equal(header.AuthorityCount, response.Authorities.Count, "Wrong number of authority records");
        FuzzAssert.Equal(header.AdditionalCount, response.AdditionalRecords.Count, "Wrong number of additional records");

        var opt = response.AdditionalRecords.OfType<DnsOptRecord>().FirstOrDefault();
        var expectedResponseCode = opt is { ExtendedRCode: not 0 } ? (opt.ExtendedRCode << 4) | (data[3] & 0x0F) : data[3] & 0x0F;
        FuzzAssert.Equal(expectedResponseCode, (int)header.ResponseCode, "Wrong response code");

        var records = response.Answers.Concat(response.Authorities).Concat(response.AdditionalRecords).ToArray();
        var plainRecords = plain.Answers.Concat(plain.Authorities).Concat(plain.AdditionalRecords).ToArray();
        for (var i = 0; i < records.Length; i++)
        {
            var record = records[i];
            var expectedTimeToLive = record is DnsOptRecord ? plainRecords[i].TimeToLive : plainRecords[i].TimeToLive - Math.Min(plainRecords[i].TimeToLive, age);
            FuzzAssert.Equal(expectedTimeToLive, record.TimeToLive, "The TTL must be reduced by the age of the response, except for OPT");
            FuzzAssert.Equal(preserveRawRecordData ? record.DataLength : 0, record.RawData.Length, "The raw data must be the RDATA of the record");
        }

        if (age is 0)
        {
            FuzzAssert.Equal(DnsMessageText.Format(plain, normalizeNames: false, includeResponseCode: true, presentationNames: true, parseCdsAsUnknown: false), DnsMessageText.Format(response, normalizeNames: false, includeResponseCode: true, presentationNames: true, parseCdsAsUnknown: false), "Keeping the raw data must not change the decoded message");
        }

        var names = new List<string>();
        foreach (var question in response.Questions)
        {
            names.Add(question.Name);
        }

        foreach (var record in records)
        {
            names.Add(record.Name);
            _ = DnsRecordFormatter.Format(record, name =>
            {
                names.Add(name);
                return name;
            }, sortTypeBitMaps: false);
        }

        var wireNames = new List<byte[]>(names.Count);
        foreach (var name in names)
        {
            wireNames.Add(CheckName(name));
        }

        CheckComparer(names, wireNames);
    }

    /// <summary>
    /// The first 4 bytes are the current time (Unix seconds), then come messages, each prefixed with its length (2 bytes,
    /// big-endian): the response to validate, then the responses to the queries the validator sends for the chain of
    /// trust, in order. With the IANA root trust anchors, a forged chain cannot be signed by the root key, so validation
    /// must never report Secure or Insecure (an authenticated unsigned delegation), and it must not throw.
    /// </summary>
    public static void Dnssec(ReadOnlySpan<byte> data)
    {
        if (!TryReadDnssecInput(data, signRecords: false, out var now, out var messages))
            return;

        var result = Validate(messages, DnssecTrustAnchors.Root, now);
        FuzzAssert.True(result.Status is DnssecValidationStatus.Bogus or DnssecValidationStatus.Indeterminate, $"A chain the root key did not sign must not validate, but the status is {result.Status}");
    }

    /// <summary>
    /// The input of <see cref="Dnssec"/>, but the harness signs the messages with its own key, which the trust anchor
    /// names: every algorithm 13 DNSKEY gets its public key, every algorithm 13 DS its digest, and every algorithm 13 RRSIG
    /// a valid signature over the RRset it covers. So the fuzzer reaches what comes after the signatures: the chain of
    /// trust, the CNAME and DNAME chains, the wildcard expansions and the NSEC and NSEC3 denial proofs. Validation must not
    /// throw, must explain any status but Secure, and a Secure response is no longer Secure without its RRSIG records.
    /// </summary>
    public static void SignedDnssec(ReadOnlySpan<byte> data)
    {
        if (!TryReadDnssecInput(data, signRecords: true, out var now, out var messages))
            return;

        var result = Validate(messages, [DnssecSigningKey.TrustAnchor], now);
        if (result.Status is DnssecValidationStatus.Secure)
        {
            // A Secure verdict rests on the signatures of the response: without them, it cannot be Secure
            var response = messages[0]!;
            messages[0] = new DnsResponseMessage(response.Header)
            {
                Questions = response.Questions,
                Answers = [.. response.Answers.Where(record => record is not DnsRrsigRecord)],
                Authorities = [.. response.Authorities.Where(record => record is not DnsRrsigRecord)],
                AdditionalRecords = [.. response.AdditionalRecords.Where(record => record is not DnsRrsigRecord)],
            };

            var unsigned = Validate(messages, [DnssecSigningKey.TrustAnchor], now);
            FuzzAssert.True(unsigned.Status is not DnssecValidationStatus.Secure, "A response without its RRSIG records must not validate as Secure");
        }
    }

    /// <summary>
    /// The first byte selects the algorithm (bit 0: Ed25519 or Ed448) and the mode (bit 1). Mode 0 reads a public key, a
    /// signature and the signed data from the network: verification returns a status and never throws. Mode 1 reads a
    /// byte that picks the bit to flip, a private key and a message: the signature of the message verifies, and flipping
    /// one bit of the signature or of the message makes it invalid.
    /// </summary>
    public static void EdDsa(ReadOnlySpan<byte> data)
    {
        var selector = FuzzInput.ReadByte(ref data);
        var algorithm = (selector & 1) is 0 ? (byte)15 : (byte)16;
        var keySize = algorithm is 15 ? Ed25519.KeySize : Ed448.KeySize;
        var signatureSize = algorithm is 15 ? Ed25519.SignatureSize : Ed448.SignatureSize;
        if ((selector & 2) is 0)
        {
            var publicKey = data[..Math.Min(keySize, data.Length)];
            data = data[publicKey.Length..];
            var signature = data[..Math.Min(signatureSize, data.Length)];
            var key = new DnsDnskeyRecord { Flags = 257, Protocol = 3, Algorithm = algorithm, PublicKey = publicKey.ToArray() };
            var status = DnssecCrypto.VerifySignature(key, data[signature.Length..].ToArray(), signature.ToArray());
            FuzzAssert.True(status is DnssecSignatureVerificationStatus.Valid or DnssecSignatureVerificationStatus.Invalid or DnssecSignatureVerificationStatus.InvalidKey, $"Unexpected status {status}");
            return;
        }

        if (data.Length < keySize + 1)
            return;

        var flip = data[0];
        var privateKey = data.Slice(1, keySize);
        var message = data[(keySize + 1)..].ToArray();
        var signingKey = new DnsDnskeyRecord
        {
            Flags = 257,
            Protocol = 3,
            Algorithm = algorithm,
            PublicKey = algorithm is 15 ? Ed25519.GetPublicKey(privateKey) : Ed448.GetPublicKey(privateKey),
        };
        var validSignature = algorithm is 15 ? Ed25519.Sign(privateKey, message) : Ed448.Sign(privateKey, message, context: []);
        FuzzAssert.Equal(DnssecSignatureVerificationStatus.Valid, DnssecCrypto.VerifySignature(signingKey, message, validSignature), "A signature must verify");

        var tamperedSignature = validSignature.ToArray();
        tamperedSignature[flip % tamperedSignature.Length] ^= (byte)(1 << (flip % 8));
        FuzzAssert.Equal(DnssecSignatureVerificationStatus.Invalid, DnssecCrypto.VerifySignature(signingKey, message, tamperedSignature), "A signature with a flipped bit must not verify");

        if (message.Length > 0)
        {
            var tamperedMessage = message.ToArray();
            tamperedMessage[flip % tamperedMessage.Length] ^= (byte)(1 << (flip % 8));
            FuzzAssert.Equal(DnssecSignatureVerificationStatus.Invalid, DnssecCrypto.VerifySignature(signingKey, tamperedMessage, validSignature), "The signature of a message must not verify another message");
        }
    }

    /// <summary>
    /// The first byte selects the TSIG algorithm, the second which messages the harness leaves unsigned (bit i for
    /// message i; the first and the last messages are always signed), the third which byte to change, then come messages,
    /// each prefixed with its length (2 bytes, big-endian), the responses of a transfer. As received, their signatures are
    /// rejected with <see cref="DnsProtocolException"/> only. Signed by the harness as a server does (RFC 8945 section
    /// 5.3.1), they verify, and changing one byte of one of them (after its identifier, before its TSIG record) makes
    /// the verification fail.
    /// </summary>
    public static void Tsig(ReadOnlySpan<byte> data)
    {
        var algorithm = FuzzInput.Pick(TsigAlgorithms, FuzzInput.ReadByte(ref data));
        var unsignedMask = FuzzInput.ReadByte(ref data);
        var tamper = FuzzInput.ReadByte(ref data);
        var messages = ReadMessages(data, MaxTransferMessages);
        if (messages.Count is 0)
            return;

        var key = new DnsTsigKey("tsig-key", algorithm, TsigSecret);
        _ = VerifyTsig(messages, key);

        var signedMessages = new List<byte[]>(messages.Count);
        var signedOffsets = new List<int>(messages.Count);
        var priorMac = TsigRequestMac;
        var unsignedMessages = new List<byte>();
        for (var i = 0; i < messages.Count; i++)
        {
            try
            {
                var original = DnsMessageEncoder.DecodeResponse(messages[i]);
                if (original.Answers.Concat(original.Authorities).Concat(original.AdditionalRecords).Any(record => record is DnsTsigRecord))
                    return;
            }
            catch (DnsProtocolException)
            {
                return;
            }

            var isSigned = i is 0 || i == messages.Count - 1 || i >= 8 || (unsignedMask & (1 << i)) is 0;
            if (!isSigned)
            {
                signedMessages.Add(messages[i]);
                signedOffsets.Add(messages[i].Length);
                unsignedMessages.AddRange(messages[i]);
                continue;
            }

            var signed = DnsTsig.Sign(messages[i], key, TsigTime, priorMac, [.. unsignedMessages], timersOnly: i > 0, DnsResponseCode.NoError, out priorMac);
            unsignedMessages.Clear();

            // Bytes after the last record of the input are read as the start of the TSIG record
            IReadOnlyList<DnsRecord> additionalRecords;
            try
            {
                additionalRecords = DnsMessageEncoder.DecodeResponse(signed).AdditionalRecords;
            }
            catch (DnsProtocolException)
            {
                return;
            }

            if (additionalRecords.Count is 0 || additionalRecords[^1] is not DnsTsigRecord tsig || !tsig.Mac.AsSpan().SequenceEqual(priorMac))
                return;

            signedMessages.Add(signed);
            signedOffsets.Add(tsig.MessageOffset);
        }

        var error = VerifyTsig(signedMessages, key);
        if (error is not null)
            throw new FuzzAssertionException("Messages signed with the key must verify", error);

        var index = tamper % signedMessages.Count;
        var end = signedOffsets[index];
        if (end <= 2)
            return;

        var tampered = signedMessages.ToList();
        tampered[index] = tampered[index].ToArray();
        tampered[index][2 + (tamper % (end - 2))] ^= 0x01;
        FuzzAssert.True(VerifyTsig(tampered, key) is not null, $"Changing a byte of the message {index} must make the verification fail");
    }

    /// <summary>
    /// The first byte selects AXFR or IXFR (bit 0), then come the serial of the client (4 bytes, big-endian) and the
    /// messages the server sends, each prefixed with its length (2 bytes, big-endian), whose identifier the harness sets
    /// to the one of the query. The transfer only fails with <see cref="DnsProtocolException"/>; when it succeeds, a
    /// refused transfer is one message, and an accepted one starts with an SOA record and ends with the SOA record of the
    /// same version, or is the SOA record alone for an IXFR client that is up to date.
    /// </summary>
    public static void ZoneTransfer(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        if (data.Length < 4)
            return;

        uint? serial = (options & 1) is 0 ? null : BinaryPrimitives.ReadUInt32BigEndian(data);
        var messages = ReadMessages(data[4..], MaxTransferMessages);

        using var transport = new ScriptedStreamTransport(messages);
        using var client = new DnsClient.DnsClient(transport, DnsClientProtocol.Tcp, new DnsClientOptions { Timeout = Timeout.InfiniteTimeSpan });
        var received = new List<DnsResponseMessage>();
        try
        {
            var transfer = serial is { } currentSerial ? client.TransferZoneAsync("example.com", currentSerial) : client.TransferZoneAsync("example.com");
            var enumerator = transfer.GetAsyncEnumerator();
            try
            {
                while (enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
                {
                    received.Add(enumerator.Current);
                }
            }
            finally
            {
                enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        catch (DnsProtocolException)
        {
            return;
        }

        FuzzAssert.True(received.Count >= 1 && received.Count <= messages.Count, $"The transfer returned {received.Count} messages out of {messages.Count}");
        if (received[0].Header.ResponseCode is not DnsResponseCode.NoError)
        {
            FuzzAssert.Equal(1, received.Count, "A refused transfer has one message");
            return;
        }

        FuzzAssert.True(received.All(message => message.Header.ResponseCode is DnsResponseCode.NoError), "Every message of a transfer must succeed");
        var records = received.SelectMany(message => message.Answers).ToList();
        if (records.Count is 0 || records[0] is not DnsSoaRecord firstSoa)
            throw new FuzzAssertionException("A transfer must start with an SOA record");

        if (records.Count is 1)
        {
            FuzzAssert.True(serial is not null, "Only an IXFR can be the SOA record alone");
        }
        else
        {
            FuzzAssert.True(records[^1] is DnsSoaRecord lastSoa && lastSoa.Serial == firstSoa.Serial, "A transfer must end with the SOA record it starts with");
        }
    }

    /// <summary>
    /// The first 8 bytes are the client cookie, the rest is a response. Reading its server cookie only fails with
    /// <see cref="DnsProtocolException"/>, exactly when the COOKIE option is repeated, has an invalid length or does not
    /// echo the client cookie (RFC 7873); otherwise the server cookie is the end of the option.
    /// </summary>
    public static void Cookie(ReadOnlySpan<byte> data)
    {
        if (data.Length < DnsCookies.ClientCookieLength)
            return;

        var clientCookie = data[..DnsCookies.ClientCookieLength].ToArray();
        DnsResponseMessage response;
        try
        {
            response = DnsMessageEncoder.DecodeResponse(data[DnsCookies.ClientCookieLength..]);
        }
        catch (DnsProtocolException)
        {
            return;
        }

        var cookies = response.AdditionalRecords.OfType<DnsOptRecord>().SelectMany(opt => opt.Options).Where(option => option.Code is DnsCookies.OptionCode).Select(option => option.Data).ToList();
        var isValid = cookies.Count is 0 || (cookies.Count is 1 && cookies[0].Length is 8 or (>= 16 and <= 40) && cookies[0].AsSpan(0, 8).SequenceEqual(clientCookie));
        byte[]? serverCookie;
        try
        {
            serverCookie = DnsCookies.ReadServerCookie(response, clientCookie);
        }
        catch (DnsProtocolException)
        {
            FuzzAssert.False(isValid, "A valid COOKIE option must be accepted");
            return;
        }

        FuzzAssert.True(isValid, "An invalid COOKIE option must be rejected");
        var expected = cookies.Count is 1 && cookies[0].Length > 8 ? cookies[0][8..] : null;
        FuzzAssert.True(serverCookie is null ? expected is null : expected is not null && serverCookie.AsSpan().SequenceEqual(expected), "The server cookie must be the end of the COOKIE option");
    }

    /// <summary>Verifies the TSIG records of the responses to a query, and returns the error, or null when they are valid.</summary>
    private static DnsProtocolException? VerifyTsig(List<byte[]> messages, DnsTsigKey key)
    {
        using var verifier = new DnsTsigVerifier(key, TsigRequestMac, new FixedTimeProvider(DateTimeOffset.FromUnixTimeSeconds((long)TsigTime)));
        try
        {
            foreach (var message in messages)
            {
                verifier.Verify(message, DnsMessageEncoder.DecodeResponse(message));
            }

            return verifier.IsLastMessageSigned ? null : new DnsProtocolException("The last message is not signed.");
        }
        catch (DnsProtocolException ex)
        {
            return ex;
        }
    }

    /// <summary>Reads messages, each prefixed with its length (2 bytes, big-endian, cut to the end of the input).</summary>
    private static List<byte[]> ReadMessages(ReadOnlySpan<byte> data, int maxCount)
    {
        var messages = new List<byte[]>();
        while (data.Length >= 2 && messages.Count < maxCount)
        {
            var length = Math.Min(BinaryPrimitives.ReadUInt16BigEndian(data), data.Length - 2);
            messages.Add(data.Slice(2, length).ToArray());
            data = data[(2 + length)..];
        }

        return messages;
    }

    private static bool TryReadDnssecInput(ReadOnlySpan<byte> data, bool signRecords, out DateTimeOffset now, out List<DnsResponseMessage?> messages)
    {
        now = DateTimeOffset.FromUnixTimeSeconds(data.Length >= 4 ? BinaryPrimitives.ReadUInt32BigEndian(data) : 0);
        data = data.Length >= 4 ? data[4..] : default;

        // A message the client cannot decode is a failed query, which the validator reports as Indeterminate
        messages = [];
        while (data.Length >= 2 && messages.Count < MaxDnssecResponses)
        {
            var length = Math.Min(BinaryPrimitives.ReadUInt16BigEndian(data), data.Length - 2);
            DnsResponseMessage? message;
            try
            {
                message = DnsMessageEncoder.DecodeResponse(data.Slice(2, length), preserveRawRecordData: true);
            }
            catch (DnsProtocolException)
            {
                message = null;
            }

            if (message is null && messages.Count is 0)
                return false;

            if (message is not null && signRecords)
            {
                DnssecSigningKey.Sign(message);
            }

            messages.Add(message);
            data = data[(2 + length)..];
        }

        return messages.Count > 0;
    }

    private static DnssecValidationResult Validate(List<DnsResponseMessage?> messages, IReadOnlyList<DnssecTrustAnchor> trustAnchors, DateTimeOffset now)
    {
        var next = 1;
        var queries = 0;
        var validator = new DnssecValidator(
            (query, _) =>
            {
                queries++;
                if (next >= messages.Count || messages[next++] is not { } response)
                    throw new DnsProtocolException("The input has no response for this query.");

                return Task.FromResult(response);
            },
            trustAnchors,
            new FixedTimeProvider(now),
            ednsUdpPayloadSize: 1232);

        var result = validator.ValidateAsync(messages[0]!, CancellationToken.None).GetAwaiter().GetResult();
        FuzzAssert.True(Enum.IsDefined(result.Status) && result.Status is not DnssecValidationStatus.NotValidated, $"Unexpected status {result.Status}");
        FuzzAssert.True(result.Status is DnssecValidationStatus.Secure || result.Issues.Count > 0, $"A {result.Status} validation must report why");
        FuzzAssert.True(queries <= 48, $"The validator sent {queries} queries, more than its budget");
        return result;
    }

    /// <summary>Encodes a name with the client's writer, checks the limits of the wire name, and that it decodes to the same text.</summary>
    private static byte[] CheckName(string name)
    {
        FuzzAssert.False(DnsName.EndsWithUnescapedDot(name), $"A decoded name must not end with a dot: {FuzzAssert.Format(name)}");
        byte[] encoded;
        try
        {
            var writer = new DnsWireWriter(64);
            writer.WriteDomainName(name);
            encoded = writer.ToArray();
        }
        catch (DnsProtocolException ex)
        {
            throw new FuzzAssertionException($"The decoded name {FuzzAssert.Format(name)} cannot be encoded again", ex);
        }

        FuzzAssert.True(encoded.Length <= DnsName.MaxLength, $"The name {FuzzAssert.Format(name)} is longer than 255 bytes");
        var reader = new DnsWireReader(encoded);
        FuzzAssert.Equal(name, reader.ReadDomainName(), "A name must decode to the text it was encoded from");
        FuzzAssert.Equal(encoded.Length, reader.Position, "The encoded name must be read entirely");
        return encoded;
    }

    /// <summary>Checks that the canonical order is a total order, and that names compare equal exactly when their wire forms are equal ignoring ASCII case.</summary>
    private static void CheckComparer(List<string> names, List<byte[]> wireNames)
    {
        var count = Math.Min(names.Count, 8);
        for (var i = 0; i < count; i++)
        {
            FuzzAssert.Equal(0, DnsNameComparer.Compare(names[i], names[i]), "A name must compare equal to itself");
            for (var j = 0; j < count; j++)
            {
                var comparison = Math.Sign(DnsNameComparer.Compare(names[i], names[j]));
                FuzzAssert.Equal(-comparison, Math.Sign(DnsNameComparer.Compare(names[j], names[i])), $"The comparison of {FuzzAssert.Format(names[i])} and {FuzzAssert.Format(names[j])} must be antisymmetric");
                FuzzAssert.Equal(comparison is 0, DnsNameComparer.Equals(names[i], names[j]), "Equals must agree with Compare");
                FuzzAssert.Equal(EqualsIgnoreAsciiCase(wireNames[i], wireNames[j]), comparison is 0, $"{FuzzAssert.Format(names[i])} and {FuzzAssert.Format(names[j])} must compare equal exactly when their wire names are equal ignoring ASCII case");
                for (var k = 0; k < count; k++)
                {
                    if (comparison <= 0 && DnsNameComparer.Compare(names[j], names[k]) <= 0)
                    {
                        FuzzAssert.True(DnsNameComparer.Compare(names[i], names[k]) <= 0, "The canonical order must be transitive");
                    }
                }
            }
        }
    }

    private static bool EqualsIgnoreAsciiCase(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
            return false;

        for (var i = 0; i < left.Length; i++)
        {
            if (ToLowerAscii(left[i]) != ToLowerAscii(right[i]))
                return false;
        }

        return true;
    }

    private static byte ToLowerAscii(byte value) => value is >= (byte)'A' and <= (byte)'Z' ? (byte)(value + ('a' - 'A')) : value;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>A stream transport whose connection answers the query with the messages of the input, with the identifier of the query.</summary>
    private sealed class ScriptedStreamTransport(List<byte[]> messages) : IDnsTransport, IDnsStreamTransport
    {
        public Task<DnsTransportResponse> SendAsync(byte[] query, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DnsStreamConnection> ConnectAsync(CancellationToken cancellationToken) => Task.FromResult(new DnsStreamConnection(new ScriptedStream(messages), owner: null));

        public void Dispose()
        {
        }
    }

    private sealed class ScriptedStream(List<byte[]> messages) : Stream
    {
        private MemoryStream? _responses;
        private byte[] _query = [];

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_responses is null)
            {
                // The messages go after the query is written: they get its identifier, which the client chose at random
                _responses = new MemoryStream();
                foreach (var message in messages)
                {
                    var copy = message.ToArray();
                    if (copy.Length >= 2 && _query.Length >= 4)
                    {
                        copy[0] = _query[2];
                        copy[1] = _query[3];
                    }

                    _responses.Write([(byte)(copy.Length >> 8), (byte)copy.Length]);
                    _responses.Write(copy);
                }

                _responses.Position = 0;
            }

            return _responses.Read(buffer);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(Read(buffer.Span));

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer) => _query = [.. _query, .. buffer];

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _responses?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
