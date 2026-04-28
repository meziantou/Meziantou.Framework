using System.Security.Cryptography.X509Certificates;

namespace Meziantou.Framework.PostgreSql;

internal static class PostgreSqlServerCertificateLoader
{
    public static X509Certificate2? Load(PostgreSqlServerOptions options)
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
                throw new InvalidOperationException($"'{nameof(PostgreSqlServerOptions.RequireEncryption)}' requires either '{nameof(PostgreSqlServerOptions.TlsPfxPath)}' or both '{nameof(PostgreSqlServerOptions.TlsPemCertificatePath)}' and '{nameof(PostgreSqlServerOptions.TlsPemPrivateKeyPath)}'.");
            }

            return null;
        }

        if (hasPfxPath && (hasPemCertificatePath || hasPemPrivateKeyPath))
        {
            throw new InvalidOperationException($"Configure TLS using either '{nameof(PostgreSqlServerOptions.TlsPfxPath)}' or PEM paths ('{nameof(PostgreSqlServerOptions.TlsPemCertificatePath)}' and '{nameof(PostgreSqlServerOptions.TlsPemPrivateKeyPath)}'), but not both.");
        }

        if (!hasPfxPath && hasPfxPassword)
        {
            throw new InvalidOperationException($"'{nameof(PostgreSqlServerOptions.TlsPfxPassword)}' can only be set when '{nameof(PostgreSqlServerOptions.TlsPfxPath)}' is configured.");
        }

        if (hasPfxPath)
        {
            var pfxPath = EnsureFileExists(options.TlsPfxPath!, nameof(PostgreSqlServerOptions.TlsPfxPath));
            return ValidateLoadedCertificate(LoadPfxCertificate(pfxPath, options.TlsPfxPassword), configuration: nameof(PostgreSqlServerOptions.TlsPfxPath));
        }

        if (hasPemCertificatePath ^ hasPemPrivateKeyPath)
        {
            throw new InvalidOperationException($"Both '{nameof(PostgreSqlServerOptions.TlsPemCertificatePath)}' and '{nameof(PostgreSqlServerOptions.TlsPemPrivateKeyPath)}' must be configured when using PEM certificates.");
        }

        var certificatePath = EnsureFileExists(options.TlsPemCertificatePath!, nameof(PostgreSqlServerOptions.TlsPemCertificatePath));
        var privateKeyPath = EnsureFileExists(options.TlsPemPrivateKeyPath!, nameof(PostgreSqlServerOptions.TlsPemPrivateKeyPath));
        return ValidateLoadedCertificate(LoadPemCertificate(certificatePath, privateKeyPath), configuration: nameof(PostgreSqlServerOptions.TlsPemCertificatePath));
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
