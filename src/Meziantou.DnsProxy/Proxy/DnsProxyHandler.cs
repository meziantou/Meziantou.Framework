using System.Diagnostics;
using System.Net;
using Meziantou.DnsProxy.Filtering;
using Meziantou.DnsProxy.Forwarding;
using Meziantou.DnsProxy.History;
using Meziantou.Framework.DnsFilter;
using Meziantou.Framework.DnsServer.Handler;
using Meziantou.Framework.DnsServer.Protocol;
using Meziantou.Framework.DnsServer.Protocol.Records;
using Microsoft.Extensions.Logging;

namespace Meziantou.DnsProxy.Proxy;

internal sealed class DnsProxyHandler
{
    private readonly FilterEngineProvider _filterEngineProvider;
    private readonly UpstreamDnsClientFactory _upstreamDnsClientFactory;
    private readonly RequestHistoryStore _requestHistoryStore;
    private readonly ILogger<DnsProxyHandler> _logger;

    public DnsProxyHandler(FilterEngineProvider filterEngineProvider, UpstreamDnsClientFactory upstreamDnsClientFactory, RequestHistoryStore requestHistoryStore, ILogger<DnsProxyHandler> logger)
    {
        _filterEngineProvider = filterEngineProvider;
        _upstreamDnsClientFactory = upstreamDnsClientFactory;
        _requestHistoryStore = requestHistoryStore;
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
                TimestampUtc = DateTimeOffset.UtcNow,
                Client = context.RemoteEndPoint is IPEndPoint ipEndPoint ? ipEndPoint.Address.ToString() : context.RemoteEndPoint.ToString() ?? "unknown",
                Protocol = context.Protocol.ToString(),
                QuestionName = question.Name,
                QuestionType = question.Type.ToString(),
                Result = "Forwarded",
                Upstream = "-",
            };

            var filterResult = _filterEngineProvider.Engine.Evaluate(
                question.Name,
                ConvertToFilterQueryType(question.Type),
                new DnsClientInfo
                {
                    Address = context.RemoteEndPoint is IPEndPoint endpoint ? endpoint.Address : null,
                });

            if (TryApplyRewrite(question, filterResult, response, historyEntryBuilder))
            {
                _requestHistoryStore.Add(historyEntryBuilder.Build(response));
                continue;
            }

            if (filterResult.IsMatched && filterResult.Action == DnsFilterAction.Block)
            {
                response.ResponseCode = DnsResponseCode.NameError;
                historyEntryBuilder.Result = "Blocked";
                historyEntryBuilder.ResponseCode = response.ResponseCode.ToString();
                _requestHistoryStore.Add(historyEntryBuilder.Build(response));
                continue;
            }

            var forwardResult = await ForwardToFastestUpstreamAsync(question, cancellationToken).ConfigureAwait(false);
            if (forwardResult.IsSuccess)
            {
                var upstreamResponse = forwardResult.Response!;
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

                if (context.Query.EdnsOptions is { } queryEdns)
                {
                    response.EdnsOptions = new DnsEdnsOptions
                    {
                        UdpPayloadSize = queryEdns.UdpPayloadSize,
                        Version = queryEdns.Version,
                        DnssecOk = queryEdns.DnssecOk,
                        ExtendedRCode = queryEdns.ExtendedRCode,
                    };
                }

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

    private static DnsFilterQueryType ConvertToFilterQueryType(DnsQueryType queryType)
    {
        return Enum.IsDefined(typeof(DnsFilterQueryType), (ushort)queryType)
            ? (DnsFilterQueryType)queryType
            : DnsFilterQueryType.ANY;
    }

    private static bool TryApplyRewrite(Meziantou.Framework.DnsServer.Protocol.DnsQuestion question, DnsFilterResult filterResult, DnsMessage response, RequestHistoryEntryBuilder historyEntryBuilder)
    {
        if (!filterResult.IsMatched || filterResult.Rewrite is null)
        {
            return false;
        }

        response.ResponseCode = filterResult.Rewrite.ResponseCode switch
        {
            DnsFilterRewriteResponseCode.NoError => DnsResponseCode.NoError,
            DnsFilterRewriteResponseCode.NameError => DnsResponseCode.NameError,
            DnsFilterRewriteResponseCode.Refused => DnsResponseCode.Refused,
            _ => DnsResponseCode.ServerFailure,
        };

        if (filterResult.Rewrite.RecordType is null || string.IsNullOrWhiteSpace(filterResult.Rewrite.Value))
        {
            historyEntryBuilder.Result = "RewriteCodeOnly";
            historyEntryBuilder.ResponseCode = response.ResponseCode.ToString();
            return true;
        }

        var rewriteType = (DnsQueryType)filterResult.Rewrite.RecordType.Value;
        var rewriteData = CreateRewriteData(rewriteType, filterResult.Rewrite.Value);
        if (rewriteData is null)
        {
            historyEntryBuilder.Result = "RewriteUnsupported";
            historyEntryBuilder.ResponseCode = response.ResponseCode.ToString();
            return true;
        }

        response.Answers.Add(new DnsResourceRecord
        {
            Name = question.Name,
            Type = rewriteType,
            Class = DnsQueryClass.IN,
            TimeToLive = 60,
            Data = rewriteData,
        });

        historyEntryBuilder.Result = "Rewritten";
        historyEntryBuilder.ResponseCode = response.ResponseCode.ToString();
        return true;
    }

    private static DnsResourceRecordData? CreateRewriteData(DnsQueryType recordType, string value)
    {
        return recordType switch
        {
            DnsQueryType.A when IPAddress.TryParse(value, out var ipAddress) && ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                => new DnsARecordData { Address = ipAddress },
            DnsQueryType.AAAA when IPAddress.TryParse(value, out var ipAddress) && ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                => new DnsAaaaRecordData { Address = ipAddress },
            DnsQueryType.CNAME => new DnsCnameRecordData { CanonicalName = value },
            _ => null,
        };
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
