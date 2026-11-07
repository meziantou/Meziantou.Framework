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
}
