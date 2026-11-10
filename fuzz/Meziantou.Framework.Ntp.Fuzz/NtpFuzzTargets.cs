extern alias NtpServerAssembly;

using System.Buffers.Binary;
using System.Net;
using Meziantou.Framework.Fuzzing;
using NtpServer = NtpServerAssembly::Meziantou.Framework.Ntp.NtpServer;
using NtpServerOptions = NtpServerAssembly::Meziantou.Framework.Ntp.NtpServerOptions;

namespace Meziantou.Framework.Ntp.Fuzz;

/// <summary>
/// The client accepts a reply only when it answers the request (mode, version, echoed transmit timestamp, no
/// Kiss-o'-Death or alarm) and, with a key, ends with a valid MAC. The server answers a request with a reply that the
/// client accepts, authenticated with the key of the request, a crypto-NAK when the MAC is not valid, or nothing. A MAC
/// written after a packet is found and verifies, and any change to the packet breaks it. The selection algorithm keeps a
/// majority of the candidates, whatever their order, and combines them into an offset within their range.
/// </summary>
public static class NtpFuzzTargets
{
    private const int HeaderSize = 48;
    private const int TransmitTimestampOffset = 40;
    private const int OriginateTimestampOffset = 24;
    private const int MaxCandidates = 16;

    private static readonly DateTimeOffset RequestTime = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly DateTimeOffset DestinationTime = RequestTime.AddMilliseconds(20);

    // Whole seconds, so that the NTP timestamp of the server decodes back to the same value
    private static readonly DateTimeOffset ServerTime = new(2026, 1, 2, 3, 4, 6, TimeSpan.Zero);

    private static readonly NtpAuthenticationKey[] Keys =
    [
        new(1, NtpAuthenticationAlgorithm.AesCmac, [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15]),
        new(2, NtpAuthenticationAlgorithm.Sha1, "sha1-key"u8),
        new(3, NtpAuthenticationAlgorithm.Md5, "md5-key"u8),
    ];

    private static readonly NtpAuthenticationKey?[] ClientKeys = [null, .. Keys];

    private static readonly NtpServer OpenServer = CreateServer(requireAuthentication: false);
    private static readonly NtpServer AuthenticatedServer = CreateServer(requireAuthentication: true);

    private static readonly string[] Tokens =
    [
        Token(0x23), Token(0x24), Token(0x1B), Token(0x1C), Token(0xE4), Token(0x00, 0x00, 0x00, 0x00),
        Token(0x00, 0x00, 0x00, 0x01), Token(0x00, 0x00, 0x00, 0x02), Token(0x00, 0x00, 0x00, 0x03),
        Token(0x01, 0x04, 0x00, 0x1C), Token(0x02, 0x04, 0x00, 0x10), Token(0x00, 0x18), Token(0x00, 0x1C), Token(0xFF, 0xFC),
        "RATE", "DENY", "RSTR", "LOCL", "GPS",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("ntp-client-response", ClientResponse) { Seeds = GetClientSeeds, Dictionary = Tokens },
        new("ntp-server-request", ServerRequest) { Seeds = GetServerSeeds, Dictionary = Tokens },
        new("ntp-mac", Mac) { Seeds = GetMacSeeds, Dictionary = Tokens },
        new("ntp-clock-selection", ClockSelection) { Seeds = GetSelectionSeeds },
    ];

    /// <summary>
    /// <c>options, reply</c>: bit 0 of <c>options</c> disables the validation, the other bits pick the key of the client.
    /// The reply answers a fixed request. An accepted reply is in server mode and, unless validation is off, has
    /// version 3 or 4, is not a Kiss-o'-Death packet, reports no alarm and echoes the transmit timestamp of the request.
    /// With a key, it ends with a valid MAC.
    /// </summary>
    private static void ClientResponse(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        var clientOptions = CreateClientOptions(options);
        var request = NtpClient.CreateRequest(clientOptions, RequestTime);

        if (!NtpClient.TryReadResponse(clientOptions, data, request, DestinationTime, out var response, out var rejection))
        {
            FuzzAssert.False(string.IsNullOrEmpty(rejection), "A rejected reply must have a reason");
            return;
        }

        FuzzAssert.True(data.Length >= HeaderSize, "A reply shorter than the header was accepted");
        FuzzAssert.Equal(4, data[0] & 0x07, "A reply that is not in server mode was accepted");

        if (clientOptions.ValidateResponse)
        {
            FuzzAssert.True(data.Slice(OriginateTimestampOffset, 8).SequenceEqual(request.AsSpan(TransmitTimestampOffset, 8)), "A reply that does not echo the request was accepted");
            FuzzAssert.True(response.Version is NtpVersion.V3 or NtpVersion.V4, "A reply with an unsupported version was accepted");
            FuzzAssert.False(response.IsKissOfDeath, "A Kiss-o'-Death reply was accepted");
            FuzzAssert.True(response.LeapIndicator is not NtpLeapIndicator.AlarmCondition, "A reply reporting an alarm was accepted");
        }

        if (clientOptions.AuthenticationKey is { } key)
        {
            FuzzAssert.True(NtpMac.TryGetMacOffset(data, out var macOffset) && NtpMac.Verify(key, data, macOffset), "A reply without a valid MAC was accepted");
        }

        // The computed values never throw, whatever the timestamps
        _ = response.ClockOffset;
        _ = response.RoundTripDelay;
        _ = response.ReferenceIdentifierText;
    }

    /// <summary>
    /// <c>options, request</c>: bit 0 of <c>options</c> requires authentication. A reply echoes the transmit timestamp of
    /// the request. It is a crypto-NAK only for a request whose MAC is not valid for any key, is authenticated with the
    /// key of the request otherwise, and is accepted by a client that sent the request with that key.
    /// </summary>
    private static void ServerRequest(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        var requireAuthentication = (options & 1) is not 0;
        var server = requireAuthentication ? AuthenticatedServer : OpenServer;

        var reply = server.CreateResponse(data, IPAddress.Loopback, activity: null);
        if (reply is null)
            return;

        FuzzAssert.True(data.Length >= HeaderSize, "A request shorter than the header was answered");
        FuzzAssert.Equal(3, data[0] & 0x07, "A request that is not in client mode was answered");
        FuzzAssert.True(((data[0] >> 3) & 0x07) is 3 or 4, "A request with an unsupported version was answered");
        FuzzAssert.Equal(4, reply[0] & 0x07, "The reply is not in server mode");
        FuzzAssert.Equal(data[0] & 0x38, reply[0] & 0x38, "The reply does not mirror the version of the request");
        FuzzAssert.True(reply.AsSpan(OriginateTimestampOffset, 8).SequenceEqual(data.Slice(TransmitTimestampOffset, 8)), "The reply does not echo the transmit timestamp of the request");
        FuzzAssert.True(NtpMac.TryGetMacOffset(data, out var macOffset), "A request with malformed extension fields was answered");

        var requestMacSize = data.Length - macOffset;
        NtpAuthenticationKey? key = null;
        if (requestMacSize is 0)
        {
            FuzzAssert.False(requireAuthentication, "An unauthenticated request was answered while authentication is required");
            FuzzAssert.Equal(HeaderSize, reply.Length, "The reply to an unauthenticated request has a MAC");
        }
        else
        {
            FuzzAssert.True(requestMacSize is not 4, "A request ending with a crypto-NAK was answered");
            var keyId = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(macOffset, 4));
            key = Array.Find(Keys, k => k.KeyId == keyId);
            if (key is null || !NtpMac.Verify(key, data, macOffset))
            {
                FuzzAssert.Equal(HeaderSize + 4, reply.Length, "A request with an invalid MAC did not get a crypto-NAK");
                FuzzAssert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(reply.AsSpan(HeaderSize)), "A crypto-NAK has a key identifier");
                FuzzAssert.True(reply.AsSpan(32, 16).IndexOfAnyExcept((byte)0) < 0, "A crypto-NAK carries the time");
                return;
            }

            FuzzAssert.Equal(HeaderSize + NtpMac.GetMacSize(key), reply.Length, "The reply to an authenticated request has no MAC");
            FuzzAssert.True(NtpMac.Verify(key, reply, HeaderSize), "The MAC of the reply is not valid");
        }

        // A request without a transmit timestamp gets a reply without an originate timestamp, which the client rejects
        if (data.Slice(TransmitTimestampOffset, 8).IndexOfAnyExcept((byte)0) < 0)
            return;

        var clientOptions = new NtpClientOptions { AuthenticationKey = key };
        FuzzAssert.True(NtpClient.TryReadResponse(clientOptions, reply, data, DestinationTime, out var response, out var rejection), "The client rejects the reply of the server: " + rejection);
        FuzzAssert.Equal(ServerTime, response.TransmitTimestamp, "The reply does not carry the time of the server");
    }

    /// <summary>
    /// <c>options, packet</c>: <c>options</c> picks a key. The MAC of a packet is found after its extension fields; a MAC
    /// written after a packet without one is found there and verifies, and flipping any bit breaks it.
    /// </summary>
    private static void Mac(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        var flip = FuzzInput.ReadByte(ref data);
        var key = FuzzInput.Pick(Keys, options);

        if (!NtpMac.TryGetMacOffset(data, out var macOffset))
            return;

        FuzzAssert.True(macOffset >= HeaderSize && macOffset <= data.Length, "The MAC offset is out of the packet");
        FuzzAssert.True((data.Length - macOffset) is 0 or 4 or 20 or 24, "What follows the extension fields is not a MAC");
        if (data.Length != macOffset)
            return;

        var packet = new byte[data.Length + NtpMac.GetMacSize(key)];
        data.CopyTo(packet);
        NtpMac.Write(key, packet, data.Length);

        FuzzAssert.True(NtpMac.TryGetMacOffset(packet, out var writtenOffset), "A packet with a MAC is malformed");
        FuzzAssert.Equal(data.Length, writtenOffset, "The MAC is not found where it was written");
        FuzzAssert.True(NtpMac.Verify(key, packet, writtenOffset), "The MAC does not verify");

        var index = (int)((uint)(flip * 2654435761) % (uint)packet.Length);
        packet[index] ^= (byte)(1 << (flip & 7));
        FuzzAssert.False(NtpMac.Verify(key, packet, writtenOffset), $"The MAC still verifies after flipping a bit of byte {index}");
    }

    /// <summary>
    /// Up to 16 candidates of 4 bytes: a signed offset and a root distance. A selection keeps more than half of the
    /// candidates, all of which intersect its bounds, does not depend on the order of the candidates, and combines them
    /// into an offset within the range of their offsets. A single candidate is always selected.
    /// </summary>
    private static void ClockSelection(ReadOnlySpan<byte> data)
    {
        var count = Math.Min(data.Length / 4, MaxCandidates);
        var offsets = new long[count];
        var rootDistances = new long[count];
        for (var i = 0; i < count; i++)
        {
            offsets[i] = BinaryPrimitives.ReadInt16BigEndian(data.Slice(i * 4, 2)) * 1000L;
            rootDistances[i] = BinaryPrimitives.ReadUInt16BigEndian(data.Slice((i * 4) + 2, 2)) * 100L;
        }

        var selected = new bool[count];
        var isSelected = NtpClockSelection.TrySelect(offsets, rootDistances, selected, out var low, out var high);
        if (count is 0)
        {
            FuzzAssert.False(isSelected, "An empty list has a selection");
            return;
        }

        if (count is 1)
        {
            FuzzAssert.True(isSelected && selected[0], "A single candidate is not selected");
        }

        var reversedSelected = new bool[count];
        var isReversedSelected = NtpClockSelection.TrySelect(Enumerable.Reverse(offsets).ToArray(), Enumerable.Reverse(rootDistances).ToArray(), reversedSelected, out var reversedLow, out var reversedHigh);
        FuzzAssert.Equal(isSelected, isReversedSelected, "The selection depends on the order of the candidates");
        if (!isSelected)
            return;

        FuzzAssert.Equal(low, reversedLow, "The lower bound depends on the order of the candidates");
        FuzzAssert.Equal(high, reversedHigh, "The upper bound depends on the order of the candidates");
        FuzzAssert.True(Enumerable.Reverse(reversedSelected).SequenceEqual(selected), "The selected candidates depend on their order");
        FuzzAssert.True(low <= high, "The bounds are reversed");

        var selectedCount = 0;
        var minOffset = long.MaxValue;
        var maxOffset = long.MinValue;
        for (var i = 0; i < count; i++)
        {
            if (!selected[i])
                continue;

            selectedCount++;
            minOffset = Math.Min(minOffset, offsets[i]);
            maxOffset = Math.Max(maxOffset, offsets[i]);
            FuzzAssert.True(offsets[i] - rootDistances[i] <= high && offsets[i] + rootDistances[i] >= low, "A selected candidate does not intersect the bounds");
        }

        FuzzAssert.True(selectedCount * 2 > count, "The selection is not a majority");

        var (offset, jitter) = NtpClockSelection.Combine(offsets, rootDistances, selected);
        FuzzAssert.True(offset >= minOffset && offset <= maxOffset, "The combined offset is out of the range of the selected offsets");
        FuzzAssert.True(jitter >= 0 && jitter <= maxOffset - minOffset, "The jitter is out of range");
    }

    private static NtpClientOptions CreateClientOptions(byte options) => new()
    {
        ValidateResponse = (options & 1) is 0,
        AuthenticationKey = FuzzInput.Pick(ClientKeys, (byte)(options >> 1)),
    };

    private static NtpServer CreateServer(bool requireAuthentication)
    {
        var options = new NtpServerOptions
        {
            TimeProvider = new FixedTimeProvider(ServerTime),
            MaxRequestsPerSecond = 0,
            RequireAuthentication = requireAuthentication,
        };

        foreach (var key in Keys)
        {
            options.AuthenticationKeys.Add(key);
        }

        return new NtpServer(options);
    }

    /// <summary>A token made of bytes, as <see cref="FuzzText.Encode"/> writes them back.</summary>
    private static string Token(params byte[] bytes) => FuzzText.Decode(bytes);

    private static IEnumerable<FuzzSeed> GetClientSeeds()
    {
        for (var options = 0; options < ClientKeys.Length * 2; options++)
        {
            var clientOptions = CreateClientOptions((byte)options);
            var request = NtpClient.CreateRequest(clientOptions, RequestTime);
            var key = clientOptions.AuthenticationKey;

            yield return Seed($"valid-{options}", options, Sign(BuildReply(request), key));
            yield return Seed($"kiss-of-death-{options}", options, Sign(BuildReply(request, stratum: 0, referenceIdentifier: 0x52415445), key));
            yield return Seed($"alarm-{options}", options, Sign(BuildReply(request, leapIndicator: 3), key));
            yield return Seed($"client-mode-{options}", options, Sign(BuildReply(request, mode: 3), key));
            yield return Seed($"version-7-{options}", options, Sign(BuildReply(request, version: 7), key));
            yield return Seed($"no-echo-{options}", options, Sign(BuildReply(request, echo: false), key));
            yield return Seed($"version-3-{options}", options, Sign(BuildReply(request, version: 3), key));
            yield return Seed($"truncated-{options}", options, BuildReply(request)[..20]);
            yield return Seed($"crypto-nak-{options}", options, [.. BuildReply(request), 0, 0, 0, 0]);
            yield return Seed($"extension-field-{options}", options, Sign([.. BuildReply(request), .. ExtensionField(28)], key));
        }
    }

    private static IEnumerable<FuzzSeed> GetServerSeeds()
    {
        foreach (var options in new[] { 0, 1 })
        {
            yield return Seed($"v4-{options}", options, BuildRequest(4));
            yield return Seed($"v3-{options}", options, BuildRequest(3));
            yield return Seed($"server-mode-{options}", options, BuildRequest(4, mode: 4));
            yield return Seed($"version-5-{options}", options, BuildRequest(5));
            yield return Seed($"no-transmit-timestamp-{options}", options, BuildRequest(4, transmit: false));
            yield return Seed($"truncated-{options}", options, BuildRequest(4)[..30]);
            yield return Seed($"extension-field-{options}", options, [.. BuildRequest(4), .. ExtensionField(28)]);
            yield return Seed($"crypto-nak-{options}", options, [.. BuildRequest(4), 0, 0, 0, 0]);
            foreach (var key in Keys)
            {
                yield return Seed($"key-{key.KeyId}-{options}", options, Sign(BuildRequest(4), key));
                yield return Seed($"key-{key.KeyId}-extension-field-{options}", options, Sign([.. BuildRequest(4), .. ExtensionField(28)], key));

                var tampered = Sign(BuildRequest(4), key);
                tampered[^1] ^= 1;
                yield return Seed($"key-{key.KeyId}-tampered-{options}", options, tampered);

                var unknownKey = Sign(BuildRequest(4), key);
                unknownKey[HeaderSize + 3] = 9;
                yield return Seed($"key-{key.KeyId}-unknown-{options}", options, unknownKey);
            }
        }
    }

    private static IEnumerable<FuzzSeed> GetMacSeeds()
    {
        for (var options = 0; options < Keys.Length; options++)
        {
            yield return Seed($"header-{options}", [(byte)options, 0x5A], BuildRequest(4));
            yield return Seed($"extension-field-{options}", [(byte)options, 0x13], [.. BuildRequest(4), .. ExtensionField(28)]);
            yield return Seed($"extension-fields-{options}", [(byte)options, 0xC1], [.. BuildRequest(4), .. ExtensionField(16), .. ExtensionField(32)]);
            yield return Seed($"signed-{options}", [(byte)options, 0x07], Sign(BuildRequest(4), Keys[options]));
        }
    }

    private static IEnumerable<FuzzSeed> GetSelectionSeeds()
    {
        yield return new FuzzSeed("single", Candidates((0, 10)));
        yield return new FuzzSeed("agreeing", Candidates((100, 50), (110, 50), (95, 50)));
        yield return new FuzzSeed("one-falseticker", Candidates((100, 50), (110, 50), (95, 50), (20000, 50)));
        yield return new FuzzSeed("no-majority", Candidates((0, 10), (1000, 10)));
        yield return new FuzzSeed("touching", Candidates((0, 10), (20, 10), (40, 10)));
        yield return new FuzzSeed("nested", Candidates((0, 1000), (0, 1), (900, 100)));
        yield return new FuzzSeed("zero-distances", Candidates((5, 0), (5, 0), (6, 0)));
    }

    private static FuzzSeed Seed(string name, int options, byte[] data) => new(name, (byte[])[(byte)options, .. data]);

    private static FuzzSeed Seed(string name, byte[] prefix, byte[] data) => new(name, (byte[])[.. prefix, .. data]);

    private static byte[] Candidates(params (short Offset, ushort RootDistance)[] candidates)
    {
        var buffer = new byte[candidates.Length * 4];
        for (var i = 0; i < candidates.Length; i++)
        {
            BinaryPrimitives.WriteInt16BigEndian(buffer.AsSpan(i * 4), candidates[i].Offset);
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan((i * 4) + 2), candidates[i].RootDistance);
        }

        return buffer;
    }

    private static byte[] BuildRequest(int version, int mode = 3, bool transmit = true)
    {
        var buffer = new byte[HeaderSize];
        buffer[0] = (byte)((version << 3) | mode);
        if (transmit)
        {
            NtpTimestamp.Encode(RequestTime, buffer.AsSpan(TransmitTimestampOffset));
        }

        return buffer;
    }

    private static byte[] BuildReply(byte[] request, byte stratum = 2, int version = 4, int mode = 4, int leapIndicator = 0, bool echo = true, uint referenceIdentifier = 0)
    {
        var buffer = new byte[HeaderSize];
        buffer[0] = (byte)((leapIndicator << 6) | (version << 3) | mode);
        buffer[1] = stratum;
        buffer[3] = unchecked((byte)-20);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(12), referenceIdentifier);
        if (echo)
        {
            request.AsSpan(TransmitTimestampOffset, 8).CopyTo(buffer.AsSpan(OriginateTimestampOffset));
        }
        else
        {
            NtpTimestamp.Encode(ServerTime, buffer.AsSpan(OriginateTimestampOffset));
        }

        NtpTimestamp.Encode(ServerTime, buffer.AsSpan(16));
        NtpTimestamp.Encode(ServerTime, buffer.AsSpan(32));
        NtpTimestamp.Encode(ServerTime, buffer.AsSpan(TransmitTimestampOffset));
        return buffer;
    }

    private static byte[] ExtensionField(int length)
    {
        var buffer = new byte[length];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, 0x0104);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(2), (ushort)length);
        return buffer;
    }

    private static byte[] Sign(byte[] packet, NtpAuthenticationKey? key)
    {
        if (key is null)
            return packet;

        var buffer = new byte[packet.Length + NtpMac.GetMacSize(key)];
        packet.CopyTo(buffer, 0);
        NtpMac.Write(key, buffer, packet.Length);
        return buffer;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
