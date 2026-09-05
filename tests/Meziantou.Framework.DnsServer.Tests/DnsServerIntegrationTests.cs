using TestUtilities;
using System.Buffers.Binary;
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
using Meziantou.Framework.DnsServer.Listeners;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using ClientDns = Meziantou.Framework.DnsClient;
using DnsResponseCode = Meziantou.Framework.DnsServer.Protocol.DnsResponseCode;

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
            await Task.Yield();
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
            var response = await XUnitStaticHelpers.Retry(() => client.QueryAsync("test.example.com", ClientDns.Query.DnsQueryType.A, XunitCancellationToken));

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
            await Task.Yield();
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
            await Task.Yield();
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
            await Task.Yield();
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
            var response = await Retry(() => client.QueryAsync("test.example.com", ClientDns.Query.DnsQueryType.A, XunitCancellationToken));

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
            await Task.Yield();
            capturedProtocol = context.Protocol;

            return context.CreateResponse();
        });

        await app.StartAsync();

        try
        {
            using var client = new ClientDns.DnsClient($"127.0.0.1:{port}", ClientDns.DnsClientProtocol.Udp);
            await Retry(() => client.QueryAsync("test.example.com", ClientDns.Query.DnsQueryType.A, XunitCancellationToken));

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
            await Task.Yield();
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
                await Task.Yield();
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
        await sslStream.WriteAsync(lengthPrefix, XunitCancellationToken);
        await sslStream.WriteAsync(queryBytes, XunitCancellationToken);
        await sslStream.FlushAsync(XunitCancellationToken);

        await sslStream.ReadExactlyAsync(lengthPrefix, XunitCancellationToken);
        var responseLength = BinaryPrimitives.ReadUInt16BigEndian(lengthPrefix);
        var responseBytes = new byte[responseLength];
        await sslStream.ReadExactlyAsync(responseBytes);

        var tlsResponse = DnsMessageEncoder.DecodeQuery(responseBytes);
        Assert.True(tlsResponse.IsResponse);
        var tlsRecord = Assert.IsType<DnsARecordData>(Assert.Single(tlsResponse.Answers).Data);
        Assert.Equal(IPAddress.Parse("10.0.0.1"), tlsRecord.Address);
    }

    [Fact]
    [SuppressMessage("Security", "CA5359:Do Not Disable Certificate Validation")]
    public async Task DoT_AlpnIsNegotiatedAndProtocolIsTls()
    {
        using var certificate = CreateSelfSignedCertificate();
        var tlsPort = GetAvailableTcpPort();

        DnsServerProtocol? observedProtocol = null;

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        builder.AddDnsServer(options => options.AddTlsListener(tlsPort, certificate, IPAddress.Loopback));

        await using var app = builder.Build();
        app.MapDnsHandler((context, ct) =>
        {
            observedProtocol = context.Protocol;
            return ValueTask.FromResult(context.CreateResponse());
        });

        await app.StartAsync();
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(IPAddress.Loopback, tlsPort, XunitCancellationToken);
            await using var sslStream = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false);

            // RFC 7858 3.1: a DNS over TLS client indicates the "dot" application protocol.
            await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "127.0.0.1",
                ApplicationProtocols = [new SslApplicationProtocol("dot")],
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            }, XunitCancellationToken);

            Assert.Equal(new SslApplicationProtocol("dot"), sslStream.NegotiatedApplicationProtocol);

            await SendLengthPrefixedQueryAsync(sslStream, CreateQueryBytes("dot.example.com", DnsQueryType.A));

            Assert.Equal(DnsServerProtocol.Tls, observedProtocol);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Udp_ResponseNeverExceedsTheUdpSizeLimit()
    {
        var port = GetAvailableUdpPort();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddDnsServer(options => options.AddUdpListener(port, IPAddress.Loopback));

        await using var app = builder.Build();
        app.MapDnsHandler((context, ct) =>
        {
            var response = context.CreateResponse();
            for (var i = 0; i < 100; i++)
            {
                response.Answers.Add(new DnsResourceRecord
                {
                    Name = $"host{i}.example.com",
                    Type = DnsQueryType.A,
                    Class = DnsQueryClass.IN,
                    TimeToLive = 300,
                    Data = new DnsARecordData { Address = IPAddress.Loopback },
                });
            }

            return ValueTask.FromResult(response);
        });

        await app.StartAsync();
        try
        {
            // No EDNS in the query, so the classic 512-byte limit applies.
            var responseBytes = await SendUdpQueryAsync(port, CreateQueryBytes("example.com", DnsQueryType.A));

            Assert.NotNull(responseBytes);
            Assert.HasCountLessThanOrEqual(512, responseBytes);
            Assert.True(DnsMessageEncoder.DecodeQuery(responseBytes).IsTruncated);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Udp_MessageWithTheQrBitSet_IsDropped()
    {
        var port = GetAvailableUdpPort();
        var handlerInvoked = false;

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddDnsServer(options => options.AddUdpListener(port, IPAddress.Loopback));

        await using var app = builder.Build();
        app.MapDnsHandler((context, ct) =>
        {
            handlerInvoked = true;
            return ValueTask.FromResult(context.CreateResponse());
        });

        await app.StartAsync();
        try
        {
            // RFC 5625 4.4: answering a response lets two servers be pointed at each other.
            var query = CreateQueryBytes("example.com", DnsQueryType.A);
            query[2] |= 0x80; // set QR

            Assert.Null(await SendUdpQueryAsync(port, query, TimeSpan.FromSeconds(2)));
            Assert.False(handlerInvoked);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Udp_UnsupportedEdnsVersion_IsAnsweredWithBadVersion()
    {
        var port = GetAvailableUdpPort();
        var handlerInvoked = false;

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddDnsServer(options => options.AddUdpListener(port, IPAddress.Loopback));

        await using var app = builder.Build();
        app.MapDnsHandler((context, ct) =>
        {
            handlerInvoked = true;
            return ValueTask.FromResult(context.CreateResponse());
        });

        await app.StartAsync();
        try
        {
            var query = new DnsMessage { Id = 7, RecursionDesired = true, EdnsOptions = new DnsEdnsOptions { Version = 1 } };
            query.Questions.Add(new DnsQuestion("example.com", DnsQueryType.A));

            var responseBytes = await SendUdpQueryAsync(port, DnsMessageEncoder.EncodeResponse(query));

            Assert.NotNull(responseBytes);
            var response = DnsMessageEncoder.DecodeQuery(responseBytes);
            Assert.Equal(DnsResponseCode.BadVersion, response.ResponseCode);
            Assert.False(handlerInvoked);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Udp_MalformedQuery_IsAnsweredWithFormatError()
    {
        var port = GetAvailableUdpPort();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddDnsServer(options => options.AddUdpListener(port, IPAddress.Loopback));

        await using var app = builder.Build();
        app.MapDnsHandler((context, ct) => ValueTask.FromResult(context.CreateResponse()));

        await app.StartAsync();
        try
        {
            // A complete header that claims one question, with no question following it.
            var malformed = new byte[12];
            BinaryPrimitives.WriteUInt16BigEndian(malformed.AsSpan(0), 0x4242);
            BinaryPrimitives.WriteUInt16BigEndian(malformed.AsSpan(4), 1);

            var responseBytes = await SendUdpQueryAsync(port, malformed);

            Assert.NotNull(responseBytes);
            var response = DnsMessageEncoder.DecodeQuery(responseBytes);
            Assert.Equal(0x4242, response.Id);
            Assert.True(response.IsResponse);
            Assert.Equal(DnsResponseCode.FormError, response.ResponseCode);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task DoH_MalformedMessage_ReturnsBadRequest()
    {
        await using var app = await StartDohServerAsync();
        try
        {
            var address = app.Urls.First(u => u.StartsWith("http://", StringComparison.Ordinal));
            using var httpClient = new HttpClient { BaseAddress = new Uri(address) };

            // A CAA record whose tag length exceeds its RDLENGTH.
            var malformed = new List<byte>([0x12, 0x34, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00]);
            malformed.Add(0x00);
            malformed.AddRange([0x01, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x40]);
            malformed.AddRange(Enumerable.Repeat((byte)0x41, 64));

            using var content = new ByteArrayContent(malformed.ToArray());
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/dns-message");

            using var response = await httpClient.PostAsync("/dns-query", content, XunitCancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task DoH_OversizedBody_ReturnsPayloadTooLarge()
    {
        await using var app = await StartDohServerAsync();
        try
        {
            var address = app.Urls.First(u => u.StartsWith("http://", StringComparison.Ordinal));
            using var httpClient = new HttpClient { BaseAddress = new Uri(address) };

            using var content = new ByteArrayContent(new byte[70_000]);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/dns-message");

            using var response = await httpClient.PostAsync("/dns-query", content, XunitCancellationToken);

            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task DoH_Response_CarriesACacheControlHeader()
    {
        await using var app = await StartDohServerAsync();
        try
        {
            var address = app.Urls.First(u => u.StartsWith("http://", StringComparison.Ordinal));
            using var httpClient = new HttpClient { BaseAddress = new Uri(address) };

            using var content = new ByteArrayContent(CreateQueryBytes("example.com", DnsQueryType.A));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/dns-message");

            using var response = await httpClient.PostAsync("/dns-query", content, XunitCancellationToken);
            response.EnsureSuccessStatusCode();

            // RFC 8484 5.1: the freshness lifetime is the smallest TTL in the answer.
            Assert.Equal(TimeSpan.FromSeconds(300), response.Headers.CacheControl?.MaxAge);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task MapDnsHandler_CalledTwice_Throws()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddDnsServer(_ => { });

        await using var app = builder.Build();
        app.MapDnsHandler((context, ct) => ValueTask.FromResult(context.CreateResponse()));

        Assert.Throws<InvalidOperationException>(() => app.MapDnsHandler((context, ct) => ValueTask.FromResult(context.CreateResponse())));
    }

    [Fact]
    public void AddDnsServer_TcpListenerWithoutAWebHost_Throws()
    {
        var builder = Host.CreateApplicationBuilder();

        // Without Kestrel the TCP listener would silently never start.
        var exception = Assert.Throws<InvalidOperationException>(() => builder.AddDnsServer(options => options.AddTcpListener(5353, IPAddress.Loopback)));
        Assert.Contains("Kestrel", exception.Message);
    }

    [Fact]
    public async Task Udp_BindFailure_IsReportedByStartAsync()
    {
        // Occupy the port first. The failure has to surface here rather than faulting the background
        // service, which would silently stop the whole host a moment after a successful start.
        using var squatter = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        squatter.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)squatter.LocalEndPoint!).Port;

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddDnsServer(options => options.AddUdpListener(port, IPAddress.Loopback));

        await using var app = builder.Build();
        app.MapDnsHandler((context, ct) => ValueTask.FromResult(context.CreateResponse()));

        await Assert.ThrowsAsync<SocketException>(() => app.StartAsync(XunitCancellationToken));
        Assert.False(app.Lifetime.ApplicationStopping.IsCancellationRequested);
    }

    [Fact]
    public async Task Tcp_ResponseLargerThan64K_IsTruncatedInsteadOfCorruptingTheStream()
    {
        var port = GetAvailableTcpPort();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        builder.AddDnsServer(options => options.AddTcpListener(port, IPAddress.Loopback));

        await using var app = builder.Build();
        app.MapDnsHandler((context, ct) =>
        {
            var response = context.CreateResponse();
            for (var i = 0; i < 5000; i++)
            {
                response.Answers.Add(new DnsResourceRecord
                {
                    Name = $"host{i}.{new string('a', 60)}.example.com",
                    Type = DnsQueryType.A,
                    Class = DnsQueryClass.IN,
                    TimeToLive = 300,
                    Data = new DnsARecordData { Address = IPAddress.Loopback },
                });
            }

            return ValueTask.FromResult(response);
        });

        await app.StartAsync();
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(IPAddress.Loopback, port, XunitCancellationToken);
            await using var stream = tcp.GetStream();

            // Two queries on one connection: the second only arrives intact if the first response
            // framed its length correctly.
            for (var i = 0; i < 2; i++)
            {
                var query = CreateQueryBytes("example.com", DnsQueryType.A);
                var lengthPrefix = new byte[2];
                BinaryPrimitives.WriteUInt16BigEndian(lengthPrefix, (ushort)query.Length);
                await stream.WriteAsync(lengthPrefix, XunitCancellationToken);
                await stream.WriteAsync(query, XunitCancellationToken);
                await stream.FlushAsync(XunitCancellationToken);

                await stream.ReadExactlyAsync(lengthPrefix, XunitCancellationToken);
                var responseLength = BinaryPrimitives.ReadUInt16BigEndian(lengthPrefix);
                var responseBytes = new byte[responseLength];
                await stream.ReadExactlyAsync(responseBytes, XunitCancellationToken);

                var response = DnsMessageEncoder.DecodeQuery(responseBytes);
                Assert.True(response.IsResponse);
                Assert.True(response.IsTruncated);
                Assert.Equal(1234, response.Id);
            }
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Disposing the listener concurrently with StopAsync is what this test exercises")]
    public async Task UdpListener_ConcurrentStopAndDispose_IsSafe()
    {
        // A host that fails to start unwinds by stopping and disposing the services it already
        // started, and StopAsync waits for in-flight requests, so the two can overlap. This is a race,
        // so it is repeated: it can only fail when the listener is actually unsafe.
        for (var attempt = 0; attempt < 25; attempt++)
        {
            var options = new DnsServerOptions();
            foreach (var port in GetAvailableUdpPorts(20))
            {
                options.AddUdpListener(port, IPAddress.Loopback);
            }

            var processor = new DnsRequestProcessor(new DnsRequestDelegateHolder(), options, NullLogger<DnsRequestProcessor>.Instance);
            var listener = new DnsUdpListener(options, processor, NullLogger<DnsUdpListener>.Instance);

            await listener.StartAsync(XunitCancellationToken);

            await Task.WhenAll(
                Task.Run(() => listener.StopAsync(CancellationToken.None), XunitCancellationToken),
                Task.Run(listener.Dispose, XunitCancellationToken));
        }
    }

    [Fact]
    public async Task Udp_HostDisposedDuringAFailedStartup_DoesNotRaceTheListener()
    {
        // A hosted service registered after the DNS server fails once the UDP listener is already
        // running, so the host unwinds and disposes it while ExecuteAsync is still starting up.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.AddDnsServer(options =>
            {
                // The ports are reserved in one go: 4 separate calls can hand back the same port twice, and the
                // duplicate listener then fails the startup with a SocketException instead of the expected one.
                foreach (var port in GetAvailableUdpPorts(4))
                {
                    options.AddUdpListener(port, IPAddress.Loopback);
                }
            });
            builder.Services.AddHostedService<FailingHostedService>();

            await using var app = builder.Build();
            app.MapDnsHandler((context, ct) => ValueTask.FromResult(context.CreateResponse()));

            var exception = await Assert.ThrowsAnyAsync<Exception>(() => app.StartAsync(XunitCancellationToken));

            // The startup failure has to be the one the service reported, not a collection-modified
            // race inside the listener.
            Assert.IsType<InvalidTimeZoneException>(exception);
        }
    }

    private sealed class FailingHostedService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => throw new InvalidTimeZoneException("Simulated startup failure.");

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static async Task<WebApplication> StartDohServerAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddDnsServer(_ => { });

        var app = builder.Build();
        app.MapDnsHandler((context, ct) =>
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

            return ValueTask.FromResult(response);
        });
        app.MapDnsOverHttps("/dns-query");

        await app.StartAsync();
        return app;
    }

    private static async Task<byte[]?> SendUdpQueryAsync(int port, byte[] query, TimeSpan? timeout = null)
    {
        using var client = new UdpClient();
        await client.SendAsync(query, new IPEndPoint(IPAddress.Loopback, port));

        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
        try
        {
            var result = await client.ReceiveAsync(cts.Token);
            return result.Buffer;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task SendLengthPrefixedQueryAsync(Stream stream, byte[] query)
    {
        var lengthPrefix = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(lengthPrefix, (ushort)query.Length);
        await stream.WriteAsync(lengthPrefix);
        await stream.WriteAsync(query);
        await stream.FlushAsync();

        await stream.ReadExactlyAsync(lengthPrefix);
        var responseLength = BinaryPrimitives.ReadUInt16BigEndian(lengthPrefix);
        await stream.ReadExactlyAsync(new byte[responseLength]);
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

    /// <summary>Reserves several distinct ports at once; probing them one at a time tends to hand back the same port twice.</summary>
    private static List<int> GetAvailableUdpPorts(int count)
    {
        var sockets = new List<Socket>(count);
        try
        {
            var ports = new List<int>(count);
            for (var i = 0; i < count; i++)
            {
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                sockets.Add(socket);
                ports.Add(((IPEndPoint)socket.LocalEndPoint!).Port);
            }

            return ports;
        }
        finally
        {
            foreach (var socket in sockets)
            {
                socket.Dispose();
            }
        }
    }

    private static int GetAvailableUdpPort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));

        return ((IPEndPoint)socket.LocalEndPoint!).Port;
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
        return X509CertificateLoader.LoadPkcs12(pfxBytes, password: null);
    }
}
