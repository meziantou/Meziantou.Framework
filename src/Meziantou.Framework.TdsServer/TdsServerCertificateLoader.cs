using System.Security.Cryptography.X509Certificates;

namespace Meziantou.Framework.Tds;

internal static class TdsServerCertificateLoader
{
    public static X509Certificate2? Load(TdsServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var hasPfxPath = !string.IsNullOrWhiteSpace(options.TlsPfxPath);
        var hasPfxPassword = options.TlsPfxPassword is not null;
        var hasPemCertificatePath = !string.IsNullOrWhiteSpace(options.TlsPemCertificatePath);
        var hasPemPrivateKeyPath = !string.IsNullOrWhiteSpace(options.TlsPemPrivateKeyPath);

        if (!hasPfxPath && !hasPemCertificatePath && !hasPemPrivateKeyPath)
        {
            if (options.RequireEncryption)
            {
                throw new InvalidOperationException($"'{nameof(TdsServerOptions.RequireEncryption)}' requires either '{nameof(TdsServerOptions.TlsPfxPath)}' or both '{nameof(TdsServerOptions.TlsPemCertificatePath)}' and '{nameof(TdsServerOptions.TlsPemPrivateKeyPath)}'.");
            }

            return null;
        }

        if (hasPfxPath && (hasPemCertificatePath || hasPemPrivateKeyPath))
        {
            throw new InvalidOperationException($"Configure TLS using either '{nameof(TdsServerOptions.TlsPfxPath)}' or PEM paths ('{nameof(TdsServerOptions.TlsPemCertificatePath)}' and '{nameof(TdsServerOptions.TlsPemPrivateKeyPath)}'), but not both.");
        }

        if (!hasPfxPath && hasPfxPassword)
        {
            throw new InvalidOperationException($"'{nameof(TdsServerOptions.TlsPfxPassword)}' can only be set when '{nameof(TdsServerOptions.TlsPfxPath)}' is configured.");
        }

        if (hasPfxPath)
        {
            var pfxPath = EnsureFileExists(options.TlsPfxPath!, nameof(TdsServerOptions.TlsPfxPath));
            return ValidateLoadedCertificate(LoadPfxCertificate(pfxPath, options.TlsPfxPassword), configuration: nameof(TdsServerOptions.TlsPfxPath));
        }

        if (hasPemCertificatePath ^ hasPemPrivateKeyPath)
        {
            throw new InvalidOperationException($"Both '{nameof(TdsServerOptions.TlsPemCertificatePath)}' and '{nameof(TdsServerOptions.TlsPemPrivateKeyPath)}' must be configured when using PEM certificates.");
        }

        var certificatePath = EnsureFileExists(options.TlsPemCertificatePath!, nameof(TdsServerOptions.TlsPemCertificatePath));
        var privateKeyPath = EnsureFileExists(options.TlsPemPrivateKeyPath!, nameof(TdsServerOptions.TlsPemPrivateKeyPath));
        return ValidateLoadedCertificate(LoadPemCertificate(certificatePath, privateKeyPath), configuration: nameof(TdsServerOptions.TlsPemCertificatePath));
    }

    private static string EnsureFileExists(string path, string optionName)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"File configured in '{optionName}' does not exist: '{fullPath}'.", fullPath);
        }

        return fullPath;
    }

    private static X509Certificate2 ValidateLoadedCertificate(X509Certificate2 certificate, string configuration)
    {
        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();
            throw new InvalidOperationException($"Certificate loaded from '{configuration}' must contain a private key.");
        }

        return certificate;
    }

    private static X509Certificate2 LoadPfxCertificate(string path, string? password)
    {
#if NET9_0_OR_GREATER
        return X509CertificateLoader.LoadPkcs12(File.ReadAllBytes(path), password);
#else
        return new X509Certificate2(path, password);
#endif
    }

    private static X509Certificate2 LoadPemCertificate(string certificatePath, string privateKeyPath)
    {
        var certificate = X509Certificate2.CreateFromPemFile(certificatePath, privateKeyPath);
        if (!OperatingSystem.IsWindows())
        {
            return certificate;
        }

        // Re-import as PKCS12 to ensure the private key is usable with SslStream on Schannel.
        var exportedCertificate = certificate.Export(X509ContentType.Pkcs12);
        certificate.Dispose();
        try
        {
#if NET9_0_OR_GREATER
            return X509CertificateLoader.LoadPkcs12(exportedCertificate, password: null, keyStorageFlags: X509KeyStorageFlags.UserKeySet);
#else
            return new X509Certificate2(exportedCertificate, (string?)null, X509KeyStorageFlags.UserKeySet);
#endif
        }
        finally
        {
            Array.Clear(exportedCertificate);
        }
    }
}
