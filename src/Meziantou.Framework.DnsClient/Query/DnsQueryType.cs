namespace Meziantou.Framework.DnsClient.Query;

/// <summary>
/// Specifies DNS resource record types as defined in various RFCs.
/// Values correspond to the IANA DNS Parameters registry.
/// </summary>
public enum DnsQueryType : ushort
{
    /// <summary>IPv4 host address (RFC 1035).</summary>
    A = 1,

    /// <summary>Authoritative name server (RFC 1035).</summary>
    NS = 2,

    /// <summary>Mail destination, obsolete (RFC 1035).</summary>
    MD = 3,

    /// <summary>Mail forwarder, obsolete (RFC 1035).</summary>
    MF = 4,

    /// <summary>Canonical name for an alias (RFC 1035).</summary>
    CNAME = 5,

    /// <summary>Start of a zone of authority (RFC 1035).</summary>
    SOA = 6,

    /// <summary>Mailbox domain name, experimental (RFC 1035).</summary>
    MB = 7,

    /// <summary>Mail group member, experimental (RFC 1035).</summary>
    MG = 8,

    /// <summary>Mail rename domain name, experimental (RFC 1035).</summary>
    MR = 9,

    /// <summary>Null resource record, experimental (RFC 1035).</summary>
    NULL = 10,

    /// <summary>Well known service description (RFC 1035).</summary>
    WKS = 11,

    /// <summary>Domain name pointer (RFC 1035).</summary>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name")]
    PTR = 12,

    /// <summary>Host information (RFC 1035).</summary>
    HINFO = 13,

    /// <summary>Mailbox or mail list information (RFC 1035).</summary>
    MINFO = 14,

    /// <summary>Mail exchange (RFC 1035).</summary>
    MX = 15,

    /// <summary>Text strings (RFC 1035).</summary>
    TXT = 16,

    /// <summary>Responsible person (RFC 1183).</summary>
    RP = 17,

    /// <summary>AFS database location (RFC 1183).</summary>
    AFSDB = 18,

    /// <summary>X.25 PSDN address (RFC 1183).</summary>
    X25 = 19,

    /// <summary>ISDN address (RFC 1183).</summary>
    ISDN = 20,

    /// <summary>Route through (RFC 1183).</summary>
    RT = 21,

    /// <summary>NSAP address (RFC 1706).</summary>
    NSAP = 22,

    /// <summary>NSAP pointer, deprecated (RFC 1706).</summary>
    NSAP_PTR = 23,

    /// <summary>Security signature, deprecated (RFC 2535).</summary>
    SIG = 24,

    /// <summary>Security key, deprecated (RFC 2535).</summary>
    KEY = 25,

    /// <summary>X.400 mail mapping information (RFC 2163).</summary>
    PX = 26,

    /// <summary>Geographical position, deprecated (RFC 1712).</summary>
    GPOS = 27,

    /// <summary>IPv6 host address (RFC 3596).</summary>
    AAAA = 28,

    /// <summary>Location information (RFC 1876).</summary>
    LOC = 29,

    /// <summary>Service locator (RFC 2782).</summary>
    SRV = 33,

    /// <summary>Naming authority pointer (RFC 3403).</summary>
    NAPTR = 35,

    /// <summary>Key exchanger (RFC 2230).</summary>
    KX = 36,

    /// <summary>Certificate record (RFC 4398).</summary>
    CERT = 37,

    /// <summary>Delegation name (RFC 6672).</summary>
    DNAME = 39,

    /// <summary>EDNS(0) option, pseudo-record (RFC 6891).</summary>
    OPT = 41,

    /// <summary>Address prefix list (RFC 3123).</summary>
    APL = 42,

    /// <summary>Delegation signer, DNSSEC (RFC 4034).</summary>
    DS = 43,

    /// <summary>SSH key fingerprint (RFC 4255).</summary>
    SSHFP = 44,

    /// <summary>IPsec public key (RFC 4025).</summary>
    IPSECKEY = 45,

    /// <summary>DNSSEC signature (RFC 4034).</summary>
    RRSIG = 46,

    /// <summary>Next secure record, DNSSEC (RFC 4034).</summary>
    NSEC = 47,

    /// <summary>DNS public key, DNSSEC (RFC 4034).</summary>
    DNSKEY = 48,

    /// <summary>DHCP identifier (RFC 4701).</summary>
    DHCID = 49,

    /// <summary>Hashed next secure record, DNSSEC (RFC 5155).</summary>
    NSEC3 = 50,

    /// <summary>NSEC3 parameters, DNSSEC (RFC 5155).</summary>
    NSEC3PARAM = 51,

    /// <summary>TLSA certificate association, DANE (RFC 6698).</summary>
    TLSA = 52,

    /// <summary>S/MIME certificate association (RFC 8162).</summary>
    SMIMEA = 53,

    /// <summary>Host identity protocol (RFC 8005).</summary>
    HIP = 55,

    /// <summary>Child DS, DNSSEC (RFC 7344).</summary>
    CDS = 59,

    /// <summary>Child DNSKEY, DNSSEC (RFC 7344).</summary>
    CDNSKEY = 60,

    /// <summary>OpenPGP public key (RFC 7929).</summary>
    OPENPGPKEY = 61,

    /// <summary>Child-to-parent synchronization (RFC 7477).</summary>
    CSYNC = 62,

    /// <summary>Service binding (RFC 9460).</summary>
    SVCB = 64,

    /// <summary>HTTPS service binding (RFC 9460).</summary>
    HTTPS = 65,

    /// <summary>Sender policy framework, obsolete (RFC 7208).</summary>
    SPF = 99,

    /// <summary>Transaction key (RFC 2930).</summary>
    TKEY = 249,

    /// <summary>Transaction signature (RFC 2845).</summary>
    TSIG = 250,

    /// <summary>Incremental zone transfer (RFC 1995).</summary>
    IXFR = 251,

    /// <summary>Full zone transfer (RFC 5936).</summary>
    AXFR = 252,

    /// <summary>Mailbox-related records (RFC 1035).</summary>
    MAILB = 253,

    /// <summary>Mail agent RRs, obsolete (RFC 1035).</summary>
    MAILA = 254,

    /// <summary>Request for all records (RFC 8482).</summary>
    ANY = 255,

    /// <summary>URI record (RFC 7553).</summary>
    URI = 256,

    /// <summary>Certification authority authorization (RFC 8659).</summary>
    CAA = 257,

    /// <summary>DNSSEC trust authorities.</summary>
    TA = 32768,

    /// <summary>DNSSEC lookaside validation, obsolete (RFC 8749).</summary>
    DLV = 32769,
}
