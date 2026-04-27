using System.Security.Cryptography.X509Certificates;

namespace Meziantou.Framework;

/// <summary>
/// Provides extension methods for <see cref="X509Certificate2"/>.
/// </summary>
public static class X509CertificateExtensions
{
    extension(X509Certificate2 certificate)
    {
        /// <summary>Gets the certificate validity end date in UTC.</summary>
        public DateTimeOffset NotAfterUtc => new(certificate.NotAfter.ToUniversalTime());

        /// <summary>Gets the certificate validity start date in UTC.</summary>
        public DateTimeOffset NotBeforeUtc => new(certificate.NotBefore.ToUniversalTime());
    }
}
