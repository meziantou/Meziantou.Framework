using System.Diagnostics;
using Meziantou.Framework.DnsClient.Query;
using Meziantou.Framework.DnsClient.Response;
using Meziantou.Framework.DnsClient.Transport;

namespace Meziantou.Framework.DnsClient.Tests;

public sealed class DnsClientTelemetryTests
{
    [Fact]
    public async Task QueryAsync_WhenResponseCodeIsNoError_SetsActivityStatusToOk()
    {
        const string QuestionName = "success.example.com";

        var activityTask = new TaskCompletionSource<Activity>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = CreateDnsClientActivityListener(QuestionName, activity => activityTask.TrySetResult(activity));
        ActivitySource.AddActivityListener(listener);
        using var transport = new ResponseTransport(DnsResponseCode.NoError);
        using var client = new DnsClient(transport, DnsClientProtocol.Udp, options: null);

        await client.QueryAsync(QuestionName, DnsQueryType.A, TestContext.Current.CancellationToken);

        var activity = await activityTask.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal("dns.query", activity.OperationName);
        Assert.Equal(ActivityKind.Client, activity.Kind);
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
        Assert.Equal(QuestionName, activity.GetTagItem("dns.question.name"));
        Assert.Equal(nameof(DnsQueryType.A), activity.GetTagItem("dns.question.type"));
        Assert.Equal(nameof(DnsQueryClass.IN), activity.GetTagItem("dns.question.class"));
        Assert.Equal("udp", activity.GetTagItem("network.transport"));
        Assert.Equal(nameof(DnsResponseCode.NoError), activity.GetTagItem("dns.response.code"));
    }

    [Fact]
    public async Task QueryAsync_WhenResponseCodeIsFailure_SetsActivityStatusToError()
    {
        const string QuestionName = "missing.example.com";

        var activityTask = new TaskCompletionSource<Activity>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = CreateDnsClientActivityListener(QuestionName, activity => activityTask.TrySetResult(activity));
        ActivitySource.AddActivityListener(listener);
        using var transport = new ResponseTransport(DnsResponseCode.NameError);
        using var client = new DnsClient(transport, DnsClientProtocol.Udp, options: null);

        await client.QueryAsync(QuestionName, DnsQueryType.A, TestContext.Current.CancellationToken);

        var activity = await activityTask.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("DNS response code: NameError", activity.StatusDescription);
        Assert.Equal(nameof(DnsResponseCode.NameError), activity.GetTagItem("dns.response.code"));
    }

    [Fact]
    public async Task QueryAsync_WhenTransportThrows_SetsActivityStatusToError()
    {
        const string QuestionName = "exception.example.com";

        var activityTask = new TaskCompletionSource<Activity>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = CreateDnsClientActivityListener(QuestionName, activity => activityTask.TrySetResult(activity));
        ActivitySource.AddActivityListener(listener);
        using var transport = new ThrowingTransport();
        using var client = new DnsClient(transport, DnsClientProtocol.Udp, options: null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.QueryAsync(QuestionName, DnsQueryType.A, TestContext.Current.CancellationToken));

        var activity = await activityTask.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal(exception.Message, activity.StatusDescription);
    }

    private static ActivityListener CreateDnsClientActivityListener(string questionName, Action<Activity> onActivityStopped)
    {
        return new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Meziantou.Framework.DnsClient",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (Equals(activity.GetTagItem("dns.question.name"), questionName))
                {
                    onActivityStopped(activity);
                }
            },
        };
    }

    private sealed class ResponseTransport : IDnsTransport
    {
        private readonly DnsResponseCode _responseCode;

        public ResponseTransport(DnsResponseCode responseCode)
        {
            _responseCode = responseCode;
        }

        public Task<byte[]> SendAsync(byte[] query, CancellationToken cancellationToken)
        {
            return Task.FromResult(CreateResponse(query, _responseCode));
        }

        public void Dispose()
        {
        }

        private static byte[] CreateResponse(byte[] query, DnsResponseCode responseCode)
        {
            return
            [
                query[0], query[1],
                0x81, (byte)(0x80 | (byte)responseCode),
                0x00, 0x00,
                0x00, 0x00,
                0x00, 0x00,
                0x00, 0x00,
            ];
        }
    }

    private sealed class ThrowingTransport : IDnsTransport
    {
        public Task<byte[]> SendAsync(byte[] query, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Could not send DNS query.");
        }

        public void Dispose()
        {
        }
    }
}
