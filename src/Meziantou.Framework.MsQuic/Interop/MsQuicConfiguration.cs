using System.Net.Security;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace Meziantou.Framework.MsQuic.Interop;

// Creates the MsQuic configuration of a connection: its settings, its application protocols and its TLS credential.
internal static class MsQuicConfiguration
{
    public static MsQuicSafeHandle CreateClient(MsQuicApi api, MsQuicClientConnectionOptions options)
    {
        var flags = QuicFlags.CredentialClient | QuicFlags.CredentialIndicateCertificateReceived | QuicFlags.CredentialNoCertificateValidation;
        if (!api.UsesSchannel)
        {
            flags |= QuicFlags.CredentialUsePortableCertificates;
        }

        var credential = new QuicCredentialConfig { Type = QuicFlags.CredentialTypeNone, Flags = flags };
        return Create(api, options, options.ClientAuthenticationOptions.ApplicationProtocols!, credential);
    }

    public static MsQuicSafeHandle CreateServer(MsQuicApi api, MsQuicServerConnectionOptions options, IList<SslApplicationProtocol> applicationProtocols)
    {
        var authentication = options.ServerAuthenticationOptions;
        X509Certificate2? ownedCertificate = null;
        try
        {
            X509Certificate2 certificate;
            X509Certificate2[] intermediates;
            if (authentication.ServerCertificateContext is { } context)
            {
                certificate = context.TargetCertificate;
                intermediates = [.. context.IntermediateCertificates];
            }
            else
            {
                if (authentication.ServerCertificate is X509Certificate2 certificate2)
                {
                    certificate = certificate2;
                }
                else
                {
                    ownedCertificate = new X509Certificate2(authentication.ServerCertificate!);
                    certificate = ownedCertificate;
                }

                intermediates = [];
            }

            if (api.UsesSchannel)
            {
                // Schannel references the certificate context, and duplicates it
                unsafe
                {
                    var credential = new QuicCredentialConfig { Type = QuicFlags.CredentialTypeCertificateContext, Certificate = (void*)certificate.Handle };
                    return Create(api, options, applicationProtocols, credential);
                }
            }

            // OpenSSL: the certificate, its private key and the intermediate certificates as a PKCS #12 blob
            byte[] pkcs12;
            try
            {
                if (intermediates.Length > 0)
                {
                    var collection = new X509Certificate2Collection(certificate);
                    collection.AddRange(intermediates);
                    pkcs12 = collection.Export(X509ContentType.Pkcs12)!;
                }
                else
                {
                    pkcs12 = certificate.Export(X509ContentType.Pkcs12);
                }
            }
            catch (CryptographicException ex)
            {
                throw new CryptographicException("MsQuic loads the server certificate from a PKCS #12 export: its private key must be exportable (X509KeyStorageFlags.Exportable).", ex);
            }

            unsafe
            {
                fixed (byte* blob = pkcs12)
                {
                    var pkcs12Config = new QuicCertificatePkcs12 { Asn1Blob = blob, Asn1BlobLength = (uint)pkcs12.Length };
                    var credential = new QuicCredentialConfig { Type = QuicFlags.CredentialTypeCertificatePkcs12, Flags = QuicFlags.CredentialUsePortableCertificates, Certificate = &pkcs12Config };
                    return Create(api, options, applicationProtocols, credential);
                }
            }
        }
        finally
        {
            ownedCertificate?.Dispose();
        }
    }

    private static MsQuicSafeHandle Create(MsQuicApi api, MsQuicConnectionOptions options, IList<SslApplicationProtocol> applicationProtocols, QuicCredentialConfig credential)
    {
        var settings = CreateSettings(options);
        using var alpn = new ApplicationProtocolBuffers(applicationProtocols);
        nint handle;
        MsQuicSafeHandle configuration;
        unsafe
        {
            MsQuicApi.ThrowIfFailure(api.Table->ConfigurationOpen(api.Registration, alpn.Buffers, (uint)alpn.Count, &settings, (uint)sizeof(QuicSettings), null, &handle), "ConfigurationOpen");
            configuration = new MsQuicSafeHandle(handle, api.Table->ConfigurationClose);
        }

        try
        {
            unsafe
            {
                MsQuicApi.ThrowIfFailure(api.Table->ConfigurationLoadCredential(handle, &credential), "ConfigurationLoadCredential");
            }
        }
        catch
        {
            configuration.Dispose();
            throw;
        }

        return configuration;
    }

    private static QuicSettings CreateSettings(MsQuicConnectionOptions options)
    {
        var settings = default(QuicSettings);
        settings.IsSetFlags = QuicSettings.IsSetPeerBidiStreamCount | QuicSettings.IsSetPeerUnidiStreamCount | QuicSettings.IsSetDatagramReceiveEnabled;
        settings.PeerBidiStreamCount = (ushort)options.MaxInboundBidirectionalStreams;
        settings.PeerUnidiStreamCount = (ushort)options.MaxInboundUnidirectionalStreams;
        if (options.EnableDatagrams)
        {
            settings.Flags |= QuicSettings.DatagramReceiveEnabledBit;
        }

        if (options.IdleTimeout > TimeSpan.Zero)
        {
            settings.IsSetFlags |= QuicSettings.IsSetIdleTimeoutMs;
            settings.IdleTimeoutMs = (ulong)options.IdleTimeout.TotalMilliseconds;
        }

        if (options.HandshakeTimeout > TimeSpan.Zero)
        {
            settings.IsSetFlags |= QuicSettings.IsSetHandshakeIdleTimeoutMs;
            settings.HandshakeIdleTimeoutMs = (ulong)options.HandshakeTimeout.TotalMilliseconds;
        }

        if (options.KeepAliveInterval > TimeSpan.Zero)
        {
            settings.IsSetFlags |= QuicSettings.IsSetKeepAliveIntervalMs;
            settings.KeepAliveIntervalMs = (uint)Math.Min(options.KeepAliveInterval.TotalMilliseconds, uint.MaxValue);
        }

        if (options.CongestionControlAlgorithm is not MsQuicCongestionControlAlgorithm.Default)
        {
            settings.IsSetFlags |= QuicSettings.IsSetCongestionControlAlgorithm;
            settings.CongestionControlAlgorithm = options.CongestionControlAlgorithm switch
            {
                MsQuicCongestionControlAlgorithm.Bbr => 1, // QUIC_CONGESTION_CONTROL_ALGORITHM_BBR
                _ => 0, // QUIC_CONGESTION_CONTROL_ALGORITHM_CUBIC
            };
        }

        return settings;
    }

    // The QUIC_BUFFER array of the application protocols, in one native block.
    internal sealed class ApplicationProtocolBuffers : IDisposable
    {
        private nint _block;

        public ApplicationProtocolBuffers(IList<SslApplicationProtocol> protocols)
        {
            Count = protocols.Count;
            var total = 0;
            foreach (var protocol in protocols)
            {
                total += protocol.Protocol.Length;
            }

            unsafe
            {
                var block = (byte*)NativeMemory.Alloc((nuint)((sizeof(QuicBuffer) * Count) + total));
                _block = (nint)block;
                var buffers = (QuicBuffer*)block;
                var data = block + (sizeof(QuicBuffer) * Count);
                for (var i = 0; i < Count; i++)
                {
                    var bytes = protocols[i].Protocol.Span;
                    bytes.CopyTo(new Span<byte>(data, bytes.Length));
                    buffers[i] = new QuicBuffer { Length = (uint)bytes.Length, Buffer = data };
                    data += bytes.Length;
                }
            }
        }

        public QuicBuffer* Buffers => (QuicBuffer*)_block;

        public int Count { get; }

        public void Dispose()
        {
            unsafe
            {
                NativeMemory.Free((void*)_block);
            }

            _block = 0;
        }
    }
}
