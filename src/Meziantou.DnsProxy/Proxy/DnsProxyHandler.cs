using System.Diagnostics;
using System.Net;
using Meziantou.DnsProxy.Filtering;
using Meziantou.DnsProxy.Forwarding;
using Meziantou.DnsProxy.History;
using Meziantou.Framework.DnsFilter;
using Meziantou.Framework.DnsServer.Handler;
using Meziantou.Framework.DnsServer.Protocol;
using Microsoft.Extensions.Logging;

namespace Meziantou.DnsProxy.Proxy;

internal sealed class DnsProxyHandler
{
    private readonly FilterEngineProvider _filterEngineProvider;
    private readonly FilteringPauseState _filteringPauseState;
    private readonly CustomDnsRecordProvider _customDnsRecordProvider;
    private readonly UpstreamDnsClientFactory _upstreamDnsClientFactory;
    private readonly DnsResponseCache _dnsResponseCache;
    private readonly RequestHistoryStore _requestHistoryStore;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DnsProxyHandler> _logger;

    public DnsProxyHandler(FilterEngineProvider filterEngineProvider, FilteringPauseState filteringPauseState, CustomDnsRecordProvider customDnsRecordProvider, UpstreamDnsClientFactory upstreamDnsClientFactory, DnsResponseCache dnsResponseCache, RequestHistoryStore requestHistoryStore, TimeProvider timeProvider, ILogger<DnsProxyHandler> logger)
    {
        _filterEngineProvider = filterEngineProvider;
        _filteringPauseState = filteringPauseState;
        _customDnsRecordProvider = customDnsRecordProvider;
        _upstreamDnsClientFactory = upstreamDnsClientFactory;
        _dnsResponseCache = dnsResponseCache;
        _requestHistoryStore = requestHistoryStore;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async ValueTask<DnsMessage> HandleAsync(DnsRequestContext context, CancellationToken cancellationToken)
    {
        var response = context.CreateResponse();
        response.ResponseCode = DnsResponseCode.NoError;

        foreach (var question in context.Query.Questions)
        {
            var historyEntryBuilder = new RequestHistoryEntryBuilder
            {
                TimestampUtc = _timeProvider.GetUtcNow(),
                Client = context.RemoteEndPoint is IPEndPoint ipEndPoint ? ipEndPoint.Address.ToString() : context.RemoteEndPoint.ToString() ?? "unknown",
                Protocol = context.Protocol.ToString(),
                QuestionName = question.Name,
                QuestionType = question.Type.ToString(),
                Result = "Forwarded",
                Upstream = "-",
            };

            if (_customDnsRecordProvider.TryApply(question, response))
            {
                ApplyQueryEdnsOptions(context, response);
                historyEntryBuilder.Result = "CustomRecord";
                historyEntryBuilder.ResponseCode = response.ResponseCode.ToString();
                _requestHistoryStore.Add(historyEntryBuilder.Build(response));
                continue;
            }

            if (!_filteringPauseState.IsDisabled)
            {
                var filterResult = _filterEngineProvider.Engine.Evaluate(
                    question.Name,
                    ConvertToFilterQueryType(question.Type),
                    new DnsClientInfo
                    {
                        Address = context.RemoteEndPoint is IPEndPoint endpoint ? endpoint.Address : null,
                    });

                if (filterResult.IsMatched && filterResult.Action == DnsFilterAction.Block)
                {
                    response.ResponseCode = DnsResponseCode.NameError;
                    historyEntryBuilder.Result = "Blocked";
                    historyEntryBuilder.ResponseCode = response.ResponseCode.ToString();
                    _requestHistoryStore.Add(historyEntryBuilder.Build(response));
                    continue;
                }
            }

            if (_dnsResponseCache.TryGet(question, response))
            {
                ApplyQueryEdnsOptions(context, response);
                historyEntryBuilder.Result = "CacheHit";
                historyEntryBuilder.Upstream = "Cache";
                historyEntryBuilder.ResponseCode = response.ResponseCode.ToString();
                _requestHistoryStore.Add(historyEntryBuilder.Build(response));
                continue;
            }

            var forwardResult = await ForwardToFastestUpstreamAsync(question, cancellationToken).ConfigureAwait(false);
            if (forwardResult.IsSuccess)
            {
                var upstreamResponse = forwardResult.Response!;
                var forwardedResponse = context.CreateResponse();
                ApplyUpstreamResponse(context, upstreamResponse, forwardedResponse);
                _dnsResponseCache.Store(question, forwardedResponse);
                AppendResponse(forwardedResponse, response);

                historyEntryBuilder.Upstream = forwardResult.UpstreamEndpoint;
                historyEntryBuilder.LatencyMs = forwardResult.LatencyMs;
                historyEntryBuilder.ResponseCode = response.ResponseCode.ToString();
            }
            else
            {
                response.ResponseCode = DnsResponseCode.ServerFailure;
                historyEntryBuilder.Result = "UpstreamFailure";
                historyEntryBuilder.ResponseCode = response.ResponseCode.ToString();
            }

            _requestHistoryStore.Add(historyEntryBuilder.Build(response));
        }

        return response;
    }

    private static void ApplyUpstreamResponse(DnsRequestContext context, Meziantou.Framework.DnsClient.Response.DnsResponseMessage upstreamResponse, DnsMessage response)
    {
        response.ResponseCode = (DnsResponseCode)upstreamResponse.Header.ResponseCode;
        response.RecursionAvailable = upstreamResponse.Header.RecursionAvailable;

        foreach (var answer in upstreamResponse.Answers)
        {
            response.Answers.Add(DnsRecordConverter.ConvertToServerRecord(answer));
        }

        foreach (var authority in upstreamResponse.Authorities)
        {
            response.Authorities.Add(DnsRecordConverter.ConvertToServerRecord(authority));
        }

        foreach (var additionalRecord in upstreamResponse.AdditionalRecords)
        {
            response.AdditionalRecords.Add(DnsRecordConverter.ConvertToServerRecord(additionalRecord));
        }

        ApplyQueryEdnsOptions(context, response);
    }

    private static void ApplyQueryEdnsOptions(DnsRequestContext context, DnsMessage response)
    {
        if (context.Query.EdnsOptions is not { } queryEdns)
        {
            return;
        }

        response.EdnsOptions = new DnsEdnsOptions
        {
            UdpPayloadSize = queryEdns.UdpPayloadSize,
            Version = queryEdns.Version,
            DnssecOk = queryEdns.DnssecOk,
            ExtendedRCode = queryEdns.ExtendedRCode,
        };
    }

    private static void AppendResponse(DnsMessage source, DnsMessage target)
    {
        target.IsAuthoritative = source.IsAuthoritative;
        target.IsTruncated = source.IsTruncated;
        target.RecursionAvailable = source.RecursionAvailable;
        target.AuthenticatedData = source.AuthenticatedData;
        target.CheckingDisabled = source.CheckingDisabled;
        target.ResponseCode = source.ResponseCode;
        target.EdnsOptions = source.EdnsOptions;

        foreach (var answer in source.Answers)
        {
            target.Answers.Add(answer);
        }

        foreach (var authority in source.Authorities)
        {
            target.Authorities.Add(authority);
        }

        foreach (var additionalRecord in source.AdditionalRecords)
        {
            target.AdditionalRecords.Add(additionalRecord);
        }
    }

    private static DnsFilterQueryType ConvertToFilterQueryType(DnsQueryType queryType)
    {
        return Enum.IsDefined(typeof(DnsFilterQueryType), (ushort)queryType)
            ? (DnsFilterQueryType)queryType
            : DnsFilterQueryType.ANY;
    }

    private async Task<ForwardResult> ForwardToFastestUpstreamAsync(Meziantou.Framework.DnsServer.Protocol.DnsQuestion question, CancellationToken cancellationToken)
    {
        var upstreams = _upstreamDnsClientFactory.GetUpstreams();
        if (upstreams.Count == 0)
        {
            return ForwardResult.Failure();
        }

        using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var queryTasks = upstreams.Select(upstream => QueryUpstreamAsync(upstream, question, linkedCancellationTokenSource.Token)).ToList();
        while (queryTasks.Count > 0)
        {
            var completedTask = await Task.WhenAny(queryTasks).ConfigureAwait(false);
            queryTasks.Remove(completedTask);

            ForwardResult result;
            try
            {
                result = await completedTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linkedCancellationTokenSource.IsCancellationRequested)
            {
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "An upstream query failed unexpectedly");
                continue;
            }

            if (result.IsSuccess)
            {
                linkedCancellationTokenSource.Cancel();
                return result;
            }
        }

        return ForwardResult.Failure();
    }

    private static async Task<ForwardResult> QueryUpstreamAsync(UpstreamDnsClientInfo upstream, Meziantou.Framework.DnsServer.Protocol.DnsQuestion question, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var query = new Meziantou.Framework.DnsClient.Query.DnsQueryMessage
            {
                RecursionDesired = true,
            };
            query.Questions.Add(new Meziantou.Framework.DnsClient.DnsQuestion(
                question.Name,
                (Meziantou.Framework.DnsClient.Query.DnsQueryType)question.Type,
                (Meziantou.Framework.DnsClient.Query.DnsQueryClass)question.QueryClass));

            var response = await upstream.Client.SendAsync(query, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            return ForwardResult.Success(upstream.DisplayName, response, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return ForwardResult.Failure();
        }
        catch (Meziantou.Framework.DnsClient.DnsProtocolException)
        {
            stopwatch.Stop();
            return ForwardResult.Failure();
        }
        catch (HttpRequestException)
        {
            stopwatch.Stop();
            return ForwardResult.Failure();
        }
    }
}
