using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Meziantou.Framework.MsQuic.Interop;

// Validates the certificate of the server as SslStream does: MsQuic is told not to validate it
// (QUIC_CREDENTIAL_FLAG_NO_CERTIFICATE_VALIDATION) and to report it (QUIC_CREDENTIAL_FLAG_INDICATE_CERTIFICATE_RECEIVED).
internal static class MsQuicCertificateValidation
{
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";

    public static bool Validate(MsQuicConnection connection, SslClientAuthenticationOptions options, string targetHost, X509Certificate2? certificate, X509Certificate2Collection? additionalCertificates, out Exception? error)
    {
        var errors = SslPolicyErrors.None;
        using var chain = new X509Chain();
        if (options.CertificateChainPolicy is { } policy)
        {
            chain.ChainPolicy = policy.Clone();
        }
        else
        {
            chain.ChainPolicy.RevocationMode = options.CertificateRevocationCheckMode;
            chain.ChainPolicy.ApplicationPolicy.Add(new Oid(ServerAuthenticationOid));
        }

        if (additionalCertificates is not null)
        {
            chain.ChainPolicy.ExtraStore.AddRange(additionalCertificates);
        }

        if (certificate is null)
        {
            errors |= SslPolicyErrors.RemoteCertificateNotAvailable;
        }
        else
        {
            if (!chain.Build(certificate))
            {
                errors |= SslPolicyErrors.RemoteCertificateChainErrors;
            }

            if (!string.IsNullOrEmpty(targetHost) && !certificate.MatchesHostname(targetHost))
            {
                errors |= SslPolicyErrors.RemoteCertificateNameMismatch;
            }
        }

        try
        {
            var callback = options.RemoteCertificateValidationCallback;
            var valid = callback is not null ? callback(connection, certificate, chain, errors) : errors is SslPolicyErrors.None;
            error = valid ? null : new AuthenticationException($"The remote certificate was rejected ({errors}).");
            return valid;
        }
        finally
        {
            for (var i = 0; i < chain.ChainElements.Count; i++)
            {
                var element = chain.ChainElements[i].Certificate;
                if (!ReferenceEquals(element, certificate))
                {
                    element.Dispose();
                }
            }
        }
    }
}
