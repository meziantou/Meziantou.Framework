using Meziantou.Framework.DnsClient.Internal;
using Meziantou.Framework.DnsClient.Protocol;

namespace Meziantou.Framework.DnsClient;

/// <summary>
/// A secret shared with a DNS server, used to sign the queries and to authenticate the responses with TSIG
/// (RFC 8945). Set it in <see cref="DnsClientOptions.TsigKey"/>.
/// </summary>
public sealed class DnsTsigKey
{
    private readonly byte[] _secret;
    private readonly TimeSpan _fudge = TimeSpan.FromSeconds(300);

    /// <summary>Initializes a new instance of the <see cref="DnsTsigKey"/> class.</summary>
    /// <param name="name">The name of the key, as configured on the server (for instance <c>transfer-key</c>).</param>
    /// <param name="algorithm">The HMAC algorithm of the key.</param>
    /// <param name="secret">The secret. Key files store it in base64: decode it with <see cref="Convert.FromBase64String(string)"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not a valid domain name, or <paramref name="secret"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="algorithm"/> is not a defined value.</exception>
    public DnsTsigKey(string name, DnsTsigAlgorithm algorithm, ReadOnlySpan<byte> secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (!Enum.IsDefined(algorithm))
            throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported TSIG algorithm.");

        if (secret.IsEmpty)
            throw new ArgumentException("The secret must not be empty.", nameof(secret));

        CanonicalName = DnssecCanonicalizer.NormalizeName(name);
        try
        {
            var writer = new DnsWireWriter(64);
            writer.WriteDomainName(CanonicalName);
        }
        catch (DnsProtocolException ex)
        {
            throw new ArgumentException($"The key name '{name}' is not a valid domain name.", nameof(name), ex);
        }

        Name = name;
        Algorithm = algorithm;
        _secret = secret.ToArray();
    }

    /// <summary>Gets the name of the key.</summary>
    public string Name { get; }

    /// <summary>Gets the HMAC algorithm of the key.</summary>
    public DnsTsigAlgorithm Algorithm { get; }

    /// <summary>
    /// Gets the clock difference allowed between the client and the server. A response signed further from the current
    /// time is rejected, and the server applies the same window to the queries. Default is 5 minutes, the value
    /// RFC 8945 recommends.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative, longer than 65535 seconds, or not a whole number of seconds.</exception>
    public TimeSpan Fudge
    {
        get => _fudge;
        init
        {
            if (value < TimeSpan.Zero || value > TimeSpan.FromSeconds(ushort.MaxValue) || value.Ticks % TimeSpan.TicksPerSecond is not 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The fudge must be a whole number of seconds between 0 and 65535.");

            _fudge = value;
        }
    }

    internal string CanonicalName { get; }

    internal ReadOnlySpan<byte> Secret => _secret;
}
