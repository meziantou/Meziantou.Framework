using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Meziantou.Framework.DnsServer.Handler;
using Meziantou.Framework.DnsServer.Hosting;
using Meziantou.Framework.DnsServer.Protocol;
using Meziantou.Framework.DnsServer.Protocol.Records;
using Meziantou.Framework.DnsServer.Protocol.Wire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ClientDns = Meziantou.Framework.DnsClient;

namespace Meziantou.Framework.DnsServer.Tests;

public sealed class DnsServerIntegrationTests
{
    [Fact]
    public async Task Udp_SimpleQuery_ReturnsARecord()
    {
        var port = GetAvailableUdpPort();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddDnsServer(options =>
        {
            options.AddUdpListener(port, IPAddress.Loopback);
        });

        await using var app = builder.Build();
        app.MapDnsHandler(async (context, ct) =>
        {
            var response = context.CreateResponse();
            if (context.Query.Questions.Count > 0 && context.Query.Questions[0].Type == DnsQueryType.A)
            {
                response.Answers.Add(new DnsResourceRecord
                {
                    Name = context.Query.Questions[0].Name,
                    Type = DnsQueryType.A,
                    Class = DnsQueryClass.IN,
                    TimeToLive = 300,
                    Data = new DnsARecordData { Address = IPAddress.Parse("10.0.0.1") },
                });
            }

            return response;
        });

        await app.StartAsync();

        try
        {
            using var client = new ClientDns.DnsClient($"127.0.0.1:{port}", ClientDns.DnsClientProtocol.Udp);
            var response = await client.QueryAsync("test.example.com", ClientDns.Query.DnsQueryType.A);

            Assert.True(response.Header.IsResponse);
            Assert.Equal(ClientDns.Response.DnsResponseCode.NoError, response.Header.ResponseCode);
            Assert.Single(response.Answers);

            var aRecord = Assert.IsType<ClientDns.Response.Records.DnsARecord>(response.Answers[0]);
            Assert.Equal(IPAddress.Parse("10.0.0.1"), aRecord.Address);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Tcp_SimpleQuery_ReturnsARecord()
    {
        var port = GetAvailableTcpPort();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddDnsServer(options =>
        {
            options.AddTcpListener(port, IPAddress.Loopback);
        });

        await using var app = builder.Build();
        app.MapDnsHandler(async (context, ct) =>
        {
            var response = context.CreateResponse();
            if (context.Query.Questions.Count > 0 && context.Query.Questions[0].Type == DnsQueryType.AAAA)
            {
                response.Answers.Add(new DnsResourceRecord
                {
                    Name = context.Query.Questions[0].Name,
                    Type = DnsQueryType.AAAA,
                    Class = DnsQueryClass.IN,
                    TimeToLive = 600,
                    Data = new DnsAaaaRecordData { Address = IPAddress.Parse("::1") },
                });
            }

            return response;
        });

        await app.StartAsync();

        try
        {
            using var client = new ClientDns.DnsClient($"127.0.0.1:{port}", ClientDns.DnsClientProtocol.Tcp);
            var response = await client.QueryAsync("test.example.com", ClientDns.Query.DnsQueryType.AAAA);

            Assert.True(response.Header.IsResponse);
            Assert.Equal(ClientDns.Response.DnsResponseCode.NoError, response.Header.ResponseCode);
            Assert.Single(response.Answers);

            var aaaaRecord = Assert.IsType<ClientDns.Response.Records.DnsAaaaRecord>(response.Answers[0]);
            Assert.Equal(IPAddress.Parse("::1"), aaaaRecord.Address);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task DoH_PostQuery_ReturnsResponse()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddDnsServer(_ => { });

        await using var app = builder.Build();
        app.MapDnsHandler(async (context, ct) =>
        {
            var response = context.CreateResponse();
            response.Answers.Add(new DnsResourceRecord
            {
                Name = "example.com",
                Type = DnsQueryType.A,
                Class = DnsQueryClass.IN,
                TimeToLive = 300,
                Data = new DnsARecordData { Address = IPAddress.Parse("1.2.3.4") },
            });

            return response;
        });
        app.MapDnsOverHttps("/dns-query");

        await app.StartAsync();

        try
        {
            var httpAddress = app.Urls.First(u => u.StartsWith("http://", StringComparison.Ordinal));
            using var httpClient = new HttpClient { BaseAddress = new Uri(httpAddress) };
            var queryBytes = CreateQueryBytes("example.com", DnsQueryType.A);

            using var content = new ByteArrayContent(queryBytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/dns-message");

            using var httpResponse = await httpClient.PostAsync("/dns-query", content);
            httpResponse.EnsureSuccessStatusCode();

            Assert.Equal("application/dns-message", httpResponse.Content.Headers.ContentType?.MediaType);

            var responseBytes = await httpResponse.Content.ReadAsByteArrayAsync();
            var dnsResponse = DnsMessageEncoder.DecodeQuery(responseBytes);

            Assert.True(dnsResponse.IsResponse);
            Assert.Single(dnsResponse.Answers);
            var aRecord = Assert.IsType<DnsARecordData>(dnsResponse.Answers[0].Data);
            Assert.Equal(IPAddress.Parse("1.2.3.4"), aRecord.Address);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task DoH_GetQuery_ReturnsResponse()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddDnsServer(_ => { });

        await using var app = builder.Build();
        app.MapDnsHandler(async (context, ct) =>
        {
            var response = context.CreateResponse();
            response.Answers.Add(new DnsResourceRecord
            {
                Name = "example.com",
                Type = DnsQueryType.MX,
                Class = DnsQueryClass.IN,
                TimeToLive = 300,
                Data = new DnsMxRecordData { Preference = 10, Exchange = "mail.example.com" },
            });

            return response;
        });
        app.MapDnsOverHttps("/dns-query");

        await app.StartAsync();

        try
        {
            var httpAddress = app.Urls.First(u => u.StartsWith("http://", StringComparison.Ordinal));
            using var httpClient = new HttpClient { BaseAddress = new Uri(httpAddress) };
            var queryBytes = CreateQueryBytes("example.com", DnsQueryType.MX);

            // Base64url encode (RFC 8484)
            var base64 = Convert.ToBase64String(queryBytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');

            using var httpResponse = await httpClient.GetAsync($"/dns-query?dns={base64}");
            httpResponse.EnsureSuccessStatusCode();

            var responseBytes = await httpResponse.Content.ReadAsByteArrayAsync();
            var dnsResponse = DnsMessageEncoder.DecodeQuery(responseBytes);

            Assert.True(dnsResponse.IsResponse);
            Assert.Single(dnsResponse.Answers);
            var mxRecord = Assert.IsType<DnsMxRecordData>(dnsResponse.Answers[0].Data);
            Assert.Equal(10, mxRecord.Preference);
            Assert.Equal("mail.example.com", mxRecord.Exchange);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Udp_ServerFailure_WhenNoHandlerConfigured()
    {
        var port = GetAvailableUdpPort();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddDnsServer(options =>
        {
            options.AddUdpListener(port, IPAddress.Loopback);
        });

        await using var app = builder.Build();

        // Do NOT call MapDnsHandler - the default holder should return ServerFailure
        await app.StartAsync();

        try
        {
            using var client = new ClientDns.DnsClient($"127.0.0.1:{port}", ClientDns.DnsClientProtocol.Udp);
            var response = await client.QueryAsync("test.example.com", ClientDns.Query.DnsQueryType.A);

            Assert.True(response.Header.IsResponse);
            Assert.Equal(ClientDns.Response.DnsResponseCode.ServerFailure, response.Header.ResponseCode);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Udp_ProtocolIsUdp()
    {
        var port = GetAvailableUdpPort();
        DnsServerProtocol? capturedProtocol = null;

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddDnsServer(options =>
        {
            options.AddUdpListener(port, IPAddress.Loopback);
        });

        await using var app = builder.Build();
        app.MapDnsHandler(async (context, ct) =>
        {
            capturedProtocol = context.Protocol;

            return context.CreateResponse();
        });

        await app.StartAsync();

        try
        {
            using var client = new ClientDns.DnsClient($"127.0.0.1:{port}", ClientDns.DnsClientProtocol.Udp);
            await client.QueryAsync("test.example.com", ClientDns.Query.DnsQueryType.A);

            Assert.Equal(DnsServerProtocol.Udp, capturedProtocol);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Tcp_ProtocolIsTcp()
    {
        var port = GetAvailableTcpPort();
        DnsServerProtocol? capturedProtocol = null;

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddDnsServer(options =>
        {
            options.AddTcpListener(port, IPAddress.Loopback);
        });

        await using var app = builder.Build();
        app.MapDnsHandler(async (context, ct) =>
        {
            capturedProtocol = context.Protocol;

            return context.CreateResponse();
        });

        await app.StartAsync();

        try
        {
            using var client = new ClientDns.DnsClient($"127.0.0.1:{port}", ClientDns.DnsClientProtocol.Tcp);
            await client.QueryAsync("test.example.com", ClientDns.Query.DnsQueryType.A);

            Assert.Equal(DnsServerProtocol.Tcp, capturedProtocol);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task AllProtocols_SingleServer_CanHandleUdpTcpDoTAndDoH()
    {
        using var certificate = CreateSelfSignedCertificate();
        var server = await StartAllProtocolsServerAsync(certificate, includeQuic: false);
        await using var app = server.App;

        try
        {
            // Query via UDP
            using var udpClient = new ClientDns.DnsClient($"127.0.0.1:{server.UdpPort}", ClientDns.DnsClientProtocol.Udp);
            var udpResponse = await udpClient.QueryAsync("udp.example.com", ClientDns.Query.DnsQueryType.A);

            Assert.True(udpResponse.Header.IsResponse);
            Assert.Equal(ClientDns.Response.DnsResponseCode.NoError, udpResponse.Header.ResponseCode);
            var udpRecord = Assert.IsType<ClientDns.Response.Records.DnsARecord>(Assert.Single(udpResponse.Answers));
            Assert.Equal(IPAddress.Parse("10.0.0.1"), udpRecord.Address);

            // Query via TCP
            using var tcpClient = new ClientDns.DnsClient($"127.0.0.1:{server.TcpPort}", ClientDns.DnsClientProtocol.Tcp);
            var tcpResponse = await tcpClient.QueryAsync("tcp.example.com", ClientDns.Query.DnsQueryType.A);

            Assert.True(tcpResponse.Header.IsResponse);
            Assert.Equal(ClientDns.Response.DnsResponseCode.NoError, tcpResponse.Header.ResponseCode);
            var tcpRecord = Assert.IsType<ClientDns.Response.Records.DnsARecord>(Assert.Single(tcpResponse.Answers));
            Assert.Equal(IPAddress.Parse("10.0.0.1"), tcpRecord.Address);

            // Query via DNS over TLS (raw SslStream to bypass cert validation with self-signed cert)
            await AssertDnsOverTls(server.TlsPort);

            // Query via DoH (POST)
            var httpAddress = app.Urls.First(u => u.StartsWith("http://", StringComparison.Ordinal));
            using var httpClient = new HttpClient { BaseAddress = new Uri(httpAddress) };
            var queryBytes = CreateQueryBytes("doh.example.com", DnsQueryType.A);

            using var content = new ByteArrayContent(queryBytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/dns-message");

            using var httpResponse = await httpClient.PostAsync("/dns-query", content);
            httpResponse.EnsureSuccessStatusCode();

            var responseBytes = await httpResponse.Content.ReadAsByteArrayAsync();
            var dohResponse = DnsMessageEncoder.DecodeQuery(responseBytes);

            Assert.True(dohResponse.IsResponse);
            var dohRecord = Assert.IsType<DnsARecordData>(Assert.Single(dohResponse.Answers).Data);
            Assert.Equal(IPAddress.Parse("10.0.0.1"), dohRecord.Address);
        }
        finally
        {
            await app.StopAsync();
        }
    }

#if NET9_0_OR_GREATER
    [Fact]
    public async Task AllProtocols_SingleServer_CanHandleUdpTcpDoTDoQAndDoH()
    {
        if (!System.Net.Quic.QuicListener.IsSupported)
            return;

        using var certificate = CreateSelfSignedCertificate();
        var server = await StartAllProtocolsServerAsync(certificate, includeQuic: true);
        await using var app = server.App;
        var quicPort = server.QuicPort ?? throw new InvalidOperationException("QUIC listener port was not configured.");

        try
        {
            // Query via UDP
            using var udpClient = new ClientDns.DnsClient($"127.0.0.1:{server.UdpPort}", ClientDns.DnsClientProtocol.Udp);
            var udpResponse = await udpClient.QueryAsync("udp.example.com", ClientDns.Query.DnsQueryType.A);

            Assert.True(udpResponse.Header.IsResponse);
            Assert.Equal(ClientDns.Response.DnsResponseCode.NoError, udpResponse.Header.ResponseCode);
            var udpRecord = Assert.IsType<ClientDns.Response.Records.DnsARecord>(Assert.Single(udpResponse.Answers));
            Assert.Equal(IPAddress.Parse("10.0.0.1"), udpRecord.Address);

            // Query via TCP
            using var tcpClient = new ClientDns.DnsClient($"127.0.0.1:{server.TcpPort}", ClientDns.DnsClientProtocol.Tcp);
            var tcpResponse = await tcpClient.QueryAsync("tcp.example.com", ClientDns.Query.DnsQueryType.A);

            Assert.True(tcpResponse.Header.IsResponse);
            Assert.Equal(ClientDns.Response.DnsResponseCode.NoError, tcpResponse.Header.ResponseCode);
            var tcpRecord = Assert.IsType<ClientDns.Response.Records.DnsARecord>(Assert.Single(tcpResponse.Answers));
            Assert.Equal(IPAddress.Parse("10.0.0.1"), tcpRecord.Address);

            // Query via DNS over TLS
            await AssertDnsOverTls(server.TlsPort);

            // Query via DNS over QUIC
            await AssertDnsOverQuic(quicPort);

            // Query via DoH (POST)
            var httpAddress = app.Urls.First(u => u.StartsWith("http://", StringComparison.Ordinal));
            using var httpClient = new HttpClient { BaseAddress = new Uri(httpAddress) };
            var queryBytes = CreateQueryBytes("doh.example.com", DnsQueryType.A);

            using var content = new ByteArrayContent(queryBytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/dns-message");

            using var httpResponse = await httpClient.PostAsync("/dns-query", content);
            httpResponse.EnsureSuccessStatusCode();

            var responseBytes = await httpResponse.Content.ReadAsByteArrayAsync();
            var dohResponse = DnsMessageEncoder.DecodeQuery(responseBytes);

            Assert.True(dohResponse.IsResponse);
            var dohRecord = Assert.IsType<DnsARecordData>(Assert.Single(dohResponse.Answers).Data);
            Assert.Equal(IPAddress.Parse("10.0.0.1"), dohRecord.Address);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [SuppressMessage("Security", "CA5359:Do Not Disable Certificate Validation")]
    private static async Task AssertDnsOverQuic(int quicPort)
    {
        var connectionOptions = new System.Net.Quic.QuicClientConnectionOptions
        {
            RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, quicPort),
            DefaultStreamErrorCode = 0,
            DefaultCloseErrorCode = 0,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions
            {
                TargetHost = "127.0.0.1",
                ApplicationProtocols = [new SslApplicationProtocol("doq")],
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            },
        };

        await using var connection = await System.Net.Quic.QuicConnection.ConnectAsync(connectionOptions);
        await using var stream = await connection.OpenOutboundStreamAsync(System.Net.Quic.QuicStreamType.Bidirectional);

        var queryBytes = CreateQueryBytes("quic.example.com", DnsQueryType.A);
        var lengthPrefix = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(lengthPrefix, (ushort)queryBytes.Length);
        await stream.WriteAsync(lengthPrefix);
        await stream.WriteAsync(queryBytes);
        stream.CompleteWrites();

        await stream.ReadExactlyAsync(lengthPrefix);
        var responseLength = BinaryPrimitives.ReadUInt16BigEndian(lengthPrefix);
        var responseBytes = new byte[responseLength];
        await stream.ReadExactlyAsync(responseBytes);

        var quicResponse = DnsMessageEncoder.DecodeQuery(responseBytes);
        Assert.True(quicResponse.IsResponse);
        var quicRecord = Assert.IsType<DnsARecordData>(Assert.Single(quicResponse.Answers).Data);
        Assert.Equal(IPAddress.Parse("10.0.0.1"), quicRecord.Address);
    }
#endif

    private static async Task<AllProtocolsServer> StartAllProtocolsServerAsync(X509Certificate2 certificate, bool includeQuic)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var udpPort = GetAvailableUdpPort();
            var tcpPort = GetAvailableTcpPort();
            var tlsPort = GetAvailableTcpPort();
            int? quicPort = includeQuic ? GetAvailableUdpPort() : null;

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.ConfigureKestrel(kestrel =>
            {
                // UseUrls is ignored when Kestrel has explicit Listen calls.
                kestrel.Listen(IPAddress.Loopback, 0);
            });
            builder.AddDnsServer(options =>
            {
                options.AddUdpListener(udpPort, IPAddress.Loopback);
                options.AddTcpListener(tcpPort, IPAddress.Loopback);
                options.AddTlsListener(tlsPort, certificate, IPAddress.Loopback);
                if (quicPort is int actualQuicPort)
                {
                    options.AddQuicListener(actualQuicPort, certificate, IPAddress.Loopback);
                }
            });

            var app = builder.Build();
            app.MapDnsHandler(async (context, ct) =>
            {
                var response = context.CreateResponse();
                if (context.Query.Questions.Count > 0)
                {
                    var question = context.Query.Questions[0];
                    response.Answers.Add(new DnsResourceRecord
                    {
                        Name = question.Name,
                        Type = DnsQueryType.A,
                        Class = DnsQueryClass.IN,
                        TimeToLive = 300,
                        Data = new DnsARecordData { Address = IPAddress.Parse("10.0.0.1") },
                    });
                }

                return response;
            });
            app.MapDnsOverHttps("/dns-query");

            try
            {
                await app.StartAsync();
                return new AllProtocolsServer(app, udpPort, tcpPort, tlsPort, quicPort);
            }
            catch (IOException exception) when (attempt < 4 && IsAddressAlreadyInUse(exception))
            {
                await app.DisposeAsync();
            }
        }

        throw new IOException("Failed to bind test listeners after multiple retries due to port conflicts.");
    }

    private static bool IsAddressAlreadyInUse(Exception exception)
    {
        if (exception is SocketException socketException && socketException.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            return true;
        }

        if (exception.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return exception.InnerException is not null && IsAddressAlreadyInUse(exception.InnerException);
    }

    [SuppressMessage("Security", "CA5359:Do Not Disable Certificate Validation")]
    private static async Task AssertDnsOverTls(int tlsPort)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, tlsPort);
        await using var sslStream = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false);
        var sslOptions = new SslClientAuthenticationOptions
        {
            TargetHost = "127.0.0.1",
            RemoteCertificateValidationCallback = (_, _, _, _) => true,
        };
        await sslStream.AuthenticateAsClientAsync(sslOptions);

        var queryBytes = CreateQueryBytes("tls.example.com", DnsQueryType.A);
        var lengthPrefix = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(lengthPrefix, (ushort)queryBytes.Length);
        await sslStream.WriteAsync(lengthPrefix);
        await sslStream.WriteAsync(queryBytes);
        await sslStream.FlushAsync();

        await sslStream.ReadExactlyAsync(lengthPrefix);
        var responseLength = BinaryPrimitives.ReadUInt16BigEndian(lengthPrefix);
        var responseBytes = new byte[responseLength];
        await sslStream.ReadExactlyAsync(responseBytes);

        var tlsResponse = DnsMessageEncoder.DecodeQuery(responseBytes);
        Assert.True(tlsResponse.IsResponse);
        var tlsRecord = Assert.IsType<DnsARecordData>(Assert.Single(tlsResponse.Answers).Data);
        Assert.Equal(IPAddress.Parse("10.0.0.1"), tlsRecord.Address);
    }

    private static byte[] CreateQueryBytes(string name, DnsQueryType type)
    {
        var query = new DnsMessage
        {
            Id = 1234,
            RecursionDesired = true,
        };
        query.Questions.Add(new DnsQuestion(name, type));

        return DnsMessageEncoder.EncodeResponse(query);
    }

    private static int GetAvailableUdpPort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));

        return ((IPEndPoint)socket.LocalEndPoint).Port;
    }

    private sealed record AllProtocolsServer(WebApplication App, int UdpPort, int TcpPort, int TlsPort, int? QuicPort);

    private static int GetAvailableTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return port;
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=localhost", ecdsa, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false));

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(sanBuilder.Build());

        var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));

        // Export and re-import to ensure the private key is available on all platforms
        var pfxBytes = cert.Export(X509ContentType.Pfx);
#if NET9_0_OR_GREATER
        return X509CertificateLoader.LoadPkcs12(pfxBytes, password: null);
#else
        return new X509Certificate2(pfxBytes);
#endif
    }
}
