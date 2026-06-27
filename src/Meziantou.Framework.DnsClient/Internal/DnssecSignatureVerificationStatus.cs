namespace Meziantou.Framework.DnsClient.Internal;

internal enum DnssecSignatureVerificationStatus
{
    Valid,
    Invalid,
    UnsupportedAlgorithm,
    InvalidKey,
}
