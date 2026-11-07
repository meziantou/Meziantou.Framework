using System.Buffers.Binary;
using Meziantou.Framework.DnsClient;
using Meziantou.Framework.DnsClient.Internal;
using Meziantou.Framework.DnsClient.Protocol;
using Meziantou.Framework.DnsClient.Query;
using Meziantou.Framework.DnsClient.Response;
using Meziantou.Framework.DnsClient.Response.Records;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.DnsFuzz;

/// <summary>
/// Targets of Meziantou.Framework.DnsClient, which decodes the responses it receives from the network (or from an
/// attacker spoofing them): decoding only throws <see cref="DnsProtocolException"/>, the names it returns encode back
/// to the same wire names, and DNSSEC validation of a forged chain never reports Secure or Insecure.
/// </summary>
internal static class DnsClientFuzzTargets
{
    private const int MaxDnssecResponses = 64;

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
}
