namespace Meziantou.Framework.DnsClient;

/// <summary>Specifies the HMAC algorithm of a <see cref="DnsTsigKey"/> (RFC 8945 section 6).</summary>
public enum DnsTsigAlgorithm
{
    /// <summary>HMAC-SHA256 (<c>hmac-sha256</c>), the algorithm every implementation supports.</summary>
    HmacSha256,

    /// <summary>HMAC-SHA384 (<c>hmac-sha384</c>).</summary>
    HmacSha384,

    /// <summary>HMAC-SHA512 (<c>hmac-sha512</c>).</summary>
    HmacSha512,

    /// <summary>HMAC-SHA1 (<c>hmac-sha1</c>). Use it only for servers that do not support HMAC-SHA256.</summary>
    HmacSha1,

    /// <summary>HMAC-MD5 (<c>hmac-md5.sig-alg.reg.int</c>). Use it only for servers that do not support HMAC-SHA256.</summary>
    HmacMd5,
}
