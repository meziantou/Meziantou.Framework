using System.Net;
using Meziantou.DnsProxy;
using Meziantou.DnsProxy.Proxy;
using Meziantou.Framework.DnsServer.Protocol;
using Meziantou.Framework.DnsServer.Protocol.Records;
using Microsoft.Extensions.Options;

namespace Meziantou.Framework.DnsProxy.Tests;

public sealed class DnsResponseCacheTests
{
    [Theory]
    [InlineData(60, 300, 3600, 60)]
    [InlineData(600, 300, 3600, 300)]
    [InlineData(600, 1200, 300, 300)]
    public void Store_PositiveResponse_UsesMinimumCacheDuration(int recordTtl, int positiveCacheDuration, int maximumCacheDuration, uint expectedTtl)
    {
        var timeProvider = new ManualTimeProvider();
        var cache = CreateCache(timeProvider, positiveCacheDuration: TimeSpan.FromSeconds(positiveCacheDuration), maximumCacheDuration: TimeSpan.FromSeconds(maximumCacheDuration));
        var question = CreateQuestion();
        var response = CreateResponse(DnsResponseCode.NoError);
        response.Answers.Add(CreateARecord(recordTtl));

        Store(cache, question, response);

        var cachedResponse = CreateResponse();
        Assert.True(TryGet(cache, question, cachedResponse));
        Assert.Equal(expectedTtl, Assert.Single(cachedResponse.Answers).TimeToLive);
    }

    [Fact]
    public void Store_PositiveResponse_UsesLowestNonOptTtl()
    {
        var timeProvider = new ManualTimeProvider();
        var cache = CreateCache(timeProvider);
        var question = CreateQuestion();
        var response = CreateResponse(DnsResponseCode.NoError);
        response.Answers.Add(CreateARecord(300));
        response.Authorities.Add(CreateNsRecord(120));
        response.AdditionalRecords.Add(CreateOptRecord(1));

        Store(cache, question, response);

        var cachedResponse = CreateResponse();
        Assert.True(TryGet(cache, question, cachedResponse));
        Assert.Equal(120u, Assert.Single(cachedResponse.Answers).TimeToLive);
        Assert.Equal(120u, Assert.Single(cachedResponse.Authorities).TimeToLive);
        Assert.Equal(1u, Assert.Single(cachedResponse.AdditionalRecords).TimeToLive);
    }

    [Fact]
    public void TryGet_UsesNormalizedQuestionNameButPreservesTypeAndClass()
    {
        var timeProvider = new ManualTimeProvider();
        var cache = CreateCache(timeProvider);
        var response = CreateResponse(DnsResponseCode.NoError);
        response.Answers.Add(CreateARecord(300));

        Store(cache, new DnsQuestion("Example.COM.", DnsQueryType.A, DnsQueryClass.IN), response);

        Assert.True(TryGet(cache, new DnsQuestion("example.com", DnsQueryType.A, DnsQueryClass.IN), CreateResponse()));
        Assert.False(TryGet(cache, new DnsQuestion("example.com", DnsQueryType.AAAA, DnsQueryClass.IN), CreateResponse()));
        Assert.False(TryGet(cache, new DnsQuestion("example.com", DnsQueryType.A, DnsQueryClass.CH), CreateResponse()));
    }

    [Fact]
    public void Store_WhenEffectivePositiveDurationIsZero_DoesNotCache()
    {
        var timeProvider = new ManualTimeProvider();
        var cache = CreateCache(timeProvider, positiveCacheDuration: TimeSpan.Zero);
        var question = CreateQuestion();
        var response = CreateResponse(DnsResponseCode.NoError);
        response.Answers.Add(CreateARecord(300));

        Store(cache, question, response);

        Assert.False(TryGet(cache, question, CreateResponse()));
    }

    [Fact]
    public void Store_WhenEffectiveNegativeDurationIsZero_DoesNotCache()
    {
        var timeProvider = new ManualTimeProvider();
        var cache = CreateCache(timeProvider, negativeCacheDuration: TimeSpan.Zero);
        var question = CreateQuestion();
        var response = CreateResponse(DnsResponseCode.NameError);
        response.Authorities.Add(CreateSoaRecord(300));

        Store(cache, question, response);

        Assert.False(TryGet(cache, question, CreateResponse()));
    }

    [Fact]
    public void TryGet_UpdatesTtlToRemainingLifetime()
    {
        var timeProvider = new ManualTimeProvider();
        var cache = CreateCache(timeProvider);
        var question = CreateQuestion();
        var response = CreateResponse(DnsResponseCode.NoError);
        response.Answers.Add(CreateARecord(300));
        Store(cache, question, response);
        timeProvider.Advance(TimeSpan.FromSeconds(42));

        var cachedResponse = CreateResponse();

        Assert.True(TryGet(cache, question, cachedResponse));
        Assert.Equal(258u, Assert.Single(cachedResponse.Answers).TimeToLive);
    }

    [Fact]
    public void Store_NameErrorResponse_UsesSoaTtl()
    {
        var timeProvider = new ManualTimeProvider();
        var cache = CreateCache(timeProvider, negativeCacheDuration: TimeSpan.FromMinutes(5), maximumCacheDuration: TimeSpan.FromHours(1));
        var question = CreateQuestion();
        var response = CreateResponse(DnsResponseCode.NameError);
        response.Authorities.Add(CreateSoaRecord(60));
        response.Authorities.Add(CreateSoaRecord(120));

        Store(cache, question, response);

        var cachedResponse = CreateResponse();
        Assert.True(TryGet(cache, question, cachedResponse));
        Assert.All(cachedResponse.Authorities, authority => Assert.Equal(60u, authority.TimeToLive));
    }

    [Fact]
    public void Store_NoDataResponseWithoutSoa_UsesNegativeCacheDuration()
    {
        var timeProvider = new ManualTimeProvider();
        var cache = CreateCache(timeProvider, negativeCacheDuration: TimeSpan.FromSeconds(90), maximumCacheDuration: TimeSpan.FromHours(1));
        var question = CreateQuestion();
        var response = CreateResponse(DnsResponseCode.NoError);
        response.Authorities.Add(CreateNsRecord(300));

        Store(cache, question, response);

        var cachedResponse = CreateResponse();
        Assert.True(TryGet(cache, question, cachedResponse));
        Assert.Equal(DnsResponseCode.NoError, cachedResponse.ResponseCode);
        Assert.Empty(cachedResponse.Answers);
        Assert.Equal(90u, Assert.Single(cachedResponse.Authorities).TimeToLive);
    }

    [Fact]
    public void TryGet_WhenEntryIsExpired_ReturnsFalse()
    {
        var timeProvider = new ManualTimeProvider();
        var cache = CreateCache(timeProvider);
        var question = CreateQuestion();
        var response = CreateResponse(DnsResponseCode.NoError);
        response.Answers.Add(CreateARecord(30));
        Store(cache, question, response);
        timeProvider.Advance(TimeSpan.FromSeconds(31));

        Assert.False(TryGet(cache, question, CreateResponse()));
    }

    [Fact]
    public void TryGet_UsesEdnsStateAsPartOfCacheKey()
    {
        var timeProvider = new ManualTimeProvider();
        var cache = CreateCache(timeProvider);
        var question = CreateQuestion();
        var response = CreateResponse(DnsResponseCode.NoError);
        response.Answers.Add(CreateARecord(300));
        var dnssecEdnsOptions = new DnsEdnsOptions { DnssecOk = true };

        Store(cache, question, response, dnssecEdnsOptions);

        Assert.True(TryGet(cache, question, CreateResponse(), dnssecEdnsOptions));
        Assert.False(TryGet(cache, question, CreateResponse()));
        Assert.False(TryGet(cache, question, CreateResponse(), new DnsEdnsOptions { DnssecOk = false }));
    }

    [Fact]
    public void Store_WhenMaxCacheEntriesIsExceeded_EvictsOldestEntry()
    {
        var timeProvider = new ManualTimeProvider();
        var cache = CreateCache(timeProvider, maxCacheEntries: 1);
        var firstQuestion = new DnsQuestion("first.example.com", DnsQueryType.A);
        var secondQuestion = new DnsQuestion("second.example.com", DnsQueryType.A);
        var response = CreateResponse(DnsResponseCode.NoError);
        response.Answers.Add(CreateARecord(300));

        Store(cache, firstQuestion, response);
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        Store(cache, secondQuestion, response);

        Assert.False(TryGet(cache, firstQuestion, CreateResponse()));
        Assert.True(TryGet(cache, secondQuestion, CreateResponse()));
    }

    private static DnsResponseCache CreateCache(
        ManualTimeProvider timeProvider,
        TimeSpan? positiveCacheDuration = null,
        TimeSpan? negativeCacheDuration = null,
        TimeSpan? maximumCacheDuration = null,
        int maxCacheEntries = 10_000)
    {
        return new DnsResponseCache(
            Options.Create(new DnsProxyOptions
            {
                PositiveCacheDuration = positiveCacheDuration ?? TimeSpan.FromMinutes(5),
                NegativeCacheDuration = negativeCacheDuration ?? TimeSpan.FromMinutes(5),
                MaximumCacheDuration = maximumCacheDuration ?? TimeSpan.FromHours(1),
                MaxCacheEntries = maxCacheEntries,
            }),
            timeProvider);
    }

    private static void Store(DnsResponseCache cache, DnsQuestion question, DnsMessage response, DnsEdnsOptions? ednsOptions = null)
    {
        cache.Store(question, ednsOptions, response);
    }

    private static bool TryGet(DnsResponseCache cache, DnsQuestion question, DnsMessage response, DnsEdnsOptions? ednsOptions = null)
    {
        return cache.TryGet(question, ednsOptions, response);
    }

    private static DnsQuestion CreateQuestion()
    {
        return new DnsQuestion("Example.COM.", DnsQueryType.A);
    }

    private static DnsMessage CreateResponse(DnsResponseCode responseCode = DnsResponseCode.NoError)
    {
        return new DnsMessage
        {
            IsResponse = true,
            RecursionAvailable = true,
            ResponseCode = responseCode,
        };
    }

    private static DnsResourceRecord CreateARecord(int ttl)
    {
        return new DnsResourceRecord
        {
            Name = "example.com",
            Type = DnsQueryType.A,
            Class = DnsQueryClass.IN,
            TimeToLive = (uint)ttl,
            Data = new DnsARecordData { Address = IPAddress.Parse("192.0.2.1") },
        };
    }

    private static DnsResourceRecord CreateNsRecord(int ttl)
    {
        return new DnsResourceRecord
        {
            Name = "example.com",
            Type = DnsQueryType.NS,
            Class = DnsQueryClass.IN,
            TimeToLive = (uint)ttl,
            Data = new DnsNsRecordData { NameServer = "ns1.example.com" },
        };
    }

    private static DnsResourceRecord CreateSoaRecord(int ttl)
    {
        return new DnsResourceRecord
        {
            Name = "example.com",
            Type = DnsQueryType.SOA,
            Class = DnsQueryClass.IN,
            TimeToLive = (uint)ttl,
            Data = new DnsSoaRecordData
            {
                PrimaryNameServer = "ns1.example.com",
                ResponsibleMailbox = "hostmaster.example.com",
                Serial = 1,
                Refresh = 3600,
                Retry = 600,
                Expire = 86400,
                Minimum = 300,
            },
        };
    }

    private static DnsResourceRecord CreateOptRecord(int ttl)
    {
        return new DnsResourceRecord
        {
            Name = "",
            Type = DnsQueryType.OPT,
            Class = (DnsQueryClass)4096,
            TimeToLive = (uint)ttl,
            Data = new DnsOptRecordData(),
        };
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 6, 28, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }

        public void Advance(TimeSpan duration)
        {
            _utcNow += duration;
        }
    }
}
