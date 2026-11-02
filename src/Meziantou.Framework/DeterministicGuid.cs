#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
#pragma warning disable CA5351 // Do Not Use Broken Cryptographic Algorithms
using System.Buffers;
using System.Security.Cryptography;

namespace Meziantou.Framework;

/*
   https://www.ietf.org/rfc/rfc4122.txt

   The version 3 or 5 UUID is meant for generating UUIDs from "names"
   that are drawn from, and unique within, some "name space".  The
   concept of name and name space should be broadly construed, and not
   limited to textual names.  For example, some name spaces are the
   domain name system, URLs, ISO Object IDs (OIDs), X.500 Distinguished
   Names (DNs), and reserved words in a programming language.  The
   mechanisms or conventions used for allocating names and ensuring
   their uniqueness within their name spaces are beyond the scope of
   this specification.

   The requirements for these types of UUIDs are as follows:
   -  The UUIDs generated at different times from the same name in the
      same namespace MUST be equal.
   -  The UUIDs generated from two different names in the same namespace
      should be different (with very high probability).
   -  The UUIDs generated from the same name in two different namespaces
      should be different with (very high probability).
   -  If two UUIDs that were generated from names are equal, then they
      were generated from the same name in the same namespace (with very
      high probability).

   The algorithm for generating a UUID from a name and a name space are
   as follows:
   -  Allocate a UUID to use as a "name space ID" for all UUIDs
      generated from names in that name space; see Appendix C for some
      pre-defined values.
   -  Choose either MD5 [4] or SHA-1 [8] as the hash algorithm; If
      backward compatibility is not an issue, SHA-1 is preferred.
   -  Convert the name to a canonical sequence of octets (as defined by
      the standards or conventions of its name space); put the name
      space ID in network byte order.
   -  Compute the hash of the name space ID concatenated with the name.
   -  Set octets zero through 3 of the time_low field to octets zero
      through 3 of the hash.
   -  Set octets zero and one of the time_mid field to octets 4 and 5 of
      the hash.
   -  Set octets zero and one of the time_hi_and_version field to octets
      6 and 7 of the hash.
   -  Set the four most significant bits (bits 12 through 15) of the
      time_hi_and_version field to the appropriate 4-bit version number
      from Section 4.1.3.
   -  Set the clock_seq_hi_and_reserved field to octet 8 of the hash.
   -  Set the two most significant bits (bits 6 and 7) of the
      clock_seq_hi_and_reserved to zero and one, respectively.
   -  Set the clock_seq_low field to octet 9 of the hash.
   -  Set octets zero through five of the node field to octets 10
      through 15 of the hash.
   -  Convert the resulting UUID to local byte order.
 */

/// <summary>
/// Provides methods for generating deterministic (version 3 and version 5) GUIDs according to <see href="https://www.ietf.org/rfc/rfc4122.txt">RFC 4122</see>.
/// </summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Tests/DeterministicGuidTests.cs" ref:region="DeterministicGuid_Create" ref:dedent="true">
/// // Generate a GUID from a DNS name
/// var guid = DeterministicGuid.Create(DeterministicGuid.DnsNamespace, "example.com", DeterministicGuidVersion.Version5);
/// Assert.Equal(Guid.Parse("cfbff0d1-9375-5685-968c-48ce8b15ae17"), guid);
///
/// // The same input will always generate the same GUID
/// var guid2 = DeterministicGuid.Create(DeterministicGuid.DnsNamespace, "example.com", DeterministicGuidVersion.Version5);
/// Assert.Equal(guid, guid2);
/// </code>
/// </example>
public static class DeterministicGuid
{
    private const int NamespaceLength = 16;
    private const int StackAllocThreshold = 256;

    /// <summary>Gets the DNS namespace UUID as defined in RFC 4122.</summary>
    public static Guid DnsNamespace { get; } = new Guid(0x6ba7b810, 0x9dad, 0x11d1, 0x80, 0xb4, 0x0, 0xc0, 0x4f, 0xd4, 0x30, 0xc8) /* 6ba7b810-9dad-11d1-80b4-00c04fd430c8 */;

    /// <summary>Gets the URL namespace UUID as defined in RFC 4122.</summary>
    public static Guid UrlNamespace { get; } = new Guid(0x6ba7b811, 0x9dad, 0x11d1, 0x80, 0xb4, 0x0, 0xc0, 0x4f, 0xd4, 0x30, 0xc8) /* 6ba7b811-9dad-11d1-80b4-00c04fd430c8 */;

    /// <summary>Gets the ISO OID namespace UUID as defined in RFC 4122.</summary>
    public static Guid OidNamespace { get; } = new Guid(0x6ba7b812, 0x9dad, 0x11d1, 0x80, 0xb4, 0x0, 0xc0, 0x4f, 0xd4, 0x30, 0xc8) /* 6ba7b812-9dad-11d1-80b4-00c04fd430c8 */;

    /// <summary>Gets the X.500 Distinguished Name namespace UUID as defined in RFC 4122.</summary>
    public static Guid X500Namespace { get; } = new Guid(0x6ba7b814, 0x9dad, 0x11d1, 0x80, 0xb4, 0x0, 0xc0, 0x4f, 0xd4, 0x30, 0xc8) /* 6ba7b814-9dad-11d1-80b4-00c04fd430c8 */;

    /// <summary>Creates a deterministic GUID from a namespace and name using the specified version.</summary>
    public static Guid Create(Guid @namespace, string name, DeterministicGuidVersion version)
    {
        ArgumentNullException.ThrowIfNull(name);
        ValidateVersion(version);

        // Encode the name right after the namespace bytes, in a single buffer
        var maxLength = NamespaceLength + Encoding.UTF8.GetMaxByteCount(name.Length);
        byte[]? rentedBuffer = null;
        var buffer = maxLength <= StackAllocThreshold
            ? stackalloc byte[StackAllocThreshold]
            : (rentedBuffer = ArrayPool<byte>.Shared.Rent(maxLength));
        try
        {
            var nameLength = Encoding.UTF8.GetBytes(name, buffer[NamespaceLength..]);
            return CreateCore(@namespace, buffer[..(NamespaceLength + nameLength)], version);
        }
        finally
        {
            if (rentedBuffer is not null)
                ArrayPool<byte>.Shared.Return(rentedBuffer);
        }
    }

    /// <summary>Creates a deterministic GUID from a namespace and name bytes using the specified version.</summary>
    public static Guid Create(Guid @namespace, ReadOnlySpan<byte> name, DeterministicGuidVersion version)
    {
        ValidateVersion(version);

        var length = NamespaceLength + name.Length;
        byte[]? rentedBuffer = null;
        var buffer = length <= StackAllocThreshold
            ? stackalloc byte[StackAllocThreshold]
            : (rentedBuffer = ArrayPool<byte>.Shared.Rent(length));
        try
        {
            name.CopyTo(buffer[NamespaceLength..]);
            return CreateCore(@namespace, buffer[..length], version);
        }
        finally
        {
            if (rentedBuffer is not null)
                ArrayPool<byte>.Shared.Return(rentedBuffer);
        }
    }

    private static void ValidateVersion(DeterministicGuidVersion version)
    {
        if (version is not DeterministicGuidVersion.Version3 and not DeterministicGuidVersion.Version5)
            throw new ArgumentOutOfRangeException(nameof(version), $"Version '{version}' is not valid.");
    }

    // data starts with NamespaceLength free bytes for the namespace, followed by the name
    private static Guid CreateCore(Guid @namespace, Span<byte> data, DeterministicGuidVersion version)
    {
        // put the namespace ID in network byte order (step 3)
        if (!@namespace.TryWriteBytes(data[..NamespaceLength], bigEndian: true, out _))
            throw new InvalidOperationException("Cannot convert Guid to byte array");

        Span<byte> hash = stackalloc byte[20];
        if (version is DeterministicGuidVersion.Version3)
        {
            if (!MD5.TryHashData(data, hash, out _))
                throw new InvalidOperationException("Cannot compute MD5 hash");
        }
        else
        {
            if (!SHA1.TryHashData(data, hash, out _))
                throw new InvalidOperationException("Cannot compute SHA1 hash");
        }

        var newGuid = hash[..16];
        newGuid[6] = (byte)((newGuid[6] & 0x0F) | ((int)version << 4));
        newGuid[8] = (byte)((newGuid[8] & 0x3F) | 0x80);

        return new Guid(newGuid, bigEndian: true);
    }
}
