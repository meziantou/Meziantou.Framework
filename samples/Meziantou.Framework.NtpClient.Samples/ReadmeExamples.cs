namespace Meziantou.Framework.Ntp.Samples;

internal static class ReadmeExamples
{
    public static async Task QueryAsync()
    {
        #region NtpClient_Query
        // Query an NTP server using NTPv4 (default)
        var client = new NtpClient("pool.ntp.org");
        var response = await client.QueryAsync();

        Console.WriteLine($"Server time: {response.TransmitTimestamp}");
        Console.WriteLine($"Clock offset: {response.ClockOffset}");
        Console.WriteLine($"Round-trip delay: {response.RoundTripDelay}");
        Console.WriteLine($"Stratum: {response.Stratum}");

        // How much the server itself vouches for that reading
        Console.WriteLine($"Root dispersion: {response.RootDispersion}");
        #endregion
    }

    public static async Task<NtpResponse> QueryV3Async()
    {
        #region NtpClient_V3
        var client = new NtpClient("pool.ntp.org", new NtpClientOptions
        {
            Version = NtpVersion.V3,
        });
        var response = await client.QueryAsync();
        #endregion

        return response;
    }

    public static async Task<NtpResponse> QueryWithTimeoutAsync()
    {
        #region NtpClient_Cancellation
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var client = new NtpClient("time.google.com");
        var response = await client.QueryAsync(cts.Token);
        #endregion

        return response;
    }

    public static async Task<NtpResponse> QueryCustomPortAsync()
    {
        #region NtpClient_CustomPort
        var client = new NtpClient("localhost", new NtpClientOptions
        {
            Port = 12345,
        });
        var response = await client.QueryAsync();
        #endregion

        return response;
    }

    public static async Task QueryWithoutValidationAsync()
    {
        #region NtpClient_WithoutValidation
        var client = new NtpClient("pool.ntp.org", new NtpClientOptions { ValidateResponse = false });
        var response = await client.QueryAsync();
        if (response.IsKissOfDeath)
        {
            Console.WriteLine($"The server refused to answer: {response.KissCode}"); // RATE, DENY, RSTR, ...
        }
        #endregion
    }

    public static async Task<NtpResponse> QueryWithRetryAsync()
    {
        #region NtpClient_ReadmeRetry
        var client = new NtpClient("pool.ntp.org", new NtpClientOptions
        {
            Timeout = TimeSpan.FromSeconds(2),
            RetryCount = 2, // up to 3 requests per address
        });
        var response = await client.QueryAsync();
        #endregion

        return response;
    }

    public static async Task MeasureServersAsync()
    {
        #region NtpClient_ReadmeMeasureServers
        // A single server, 4 samples 2 seconds apart
        var measurement = await new NtpClient("time.google.com").MeasureAsync();
        Console.WriteLine($"Offset: {measurement.ClockOffset} ± {measurement.RootDistance}");

        // Several servers: the majority outvotes a server whose clock is wrong
        var result = await NtpClient.MeasureAsync(
        [
            new NtpClient("time.google.com"),
            new NtpClient("time.cloudflare.com"),
            new NtpClient("pool.ntp.org"),
        ]);

        Console.WriteLine($"Offset: {result.ClockOffset}, between {result.MinimumClockOffset} and {result.MaximumClockOffset}");
        foreach (var rejected in result.RejectedServers)
        {
            Console.WriteLine($"Ignored {rejected.Server}: offset {rejected.ClockOffset}");
        }
        #endregion
    }

    public static async Task<NtpResponse> QueryAuthenticatedAsync()
    {
        #region NtpClient_ReadmeAuthentication
        // The line "1 AES128CMAC 000102030405060708090a0b0c0d0e0f" of the keys file of the server
        var client = new NtpClient("ntp.example.com", new NtpClientOptions
        {
            AuthenticationKey = new NtpAuthenticationKey(
                keyId: 1,
                NtpAuthenticationAlgorithm.AesCmac,
                Convert.FromHexString("000102030405060708090a0b0c0d0e0f")),
        });
        var response = await client.QueryAsync();
        #endregion

        return response;
    }
}
