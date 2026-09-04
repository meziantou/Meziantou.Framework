using System.Diagnostics;
using System.Net;
using Meziantou.DnsProxy.Filtering;
using Meziantou.DnsProxy.Forwarding;
using Meziantou.DnsProxy.History;
using Meziantou.Framework.DnsFilter;
using Meziantou.Framework.DnsServer.Handler;
using Meziantou.Framework.DnsServer.Protocol;
using Microsoft.Extensions.Logging;
using DnsResponseCode = Meziantou.Framework.DnsServer.Protocol.DnsResponseCode;

namespace Meziantou.DnsProxy.Proxy;

internal sealed class DnsProxyHandler
{
    /// <summary>The payload size the proxy advertises in its own OPT record. 1232 bytes avoids IP fragmentation on most paths.</summary>
    private const ushort ResponseUdpPayloadSize = 1232;

    /// <summary>Upper 8 bits of BADVERS (16), reported through the OPT record per RFC 6891.</summary>
    private const byte BadVersionExtendedRCode = 1;

    /// <summary>TTL of a record synthesized from a <c>$dnsrewrite</c> directive.</summary>
    private const uint RewriteTimeToLive = 60;

    private readonly FilterEngineProvider _filterEngineProvider;
    private readonly FilteringPauseState _filteringPauseState;
    private readonly CustomDnsRecordProvider _customDnsRecordProvider;
    private readonly IUpstreamDnsClientProvider _upstreamDnsClientProvider;
    private readonly DnsResponseCache _dnsResponseCache;
    private readonly ClientRateLimiter _clientRateLimiter;
    private readonly ClientAccessPolicy _clientAccessPolicy;
    private readonly RequestHistoryStore _requestHistoryStore;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DnsProxyHandler> _logger;

    public DnsProxyHandler(FilterEngineProvider filterEngineProvider, FilteringPauseState filteringPauseState, CustomDnsRecordProvider customDnsRecordProvider, IUpstreamDnsClientProvider upstreamDnsClientProvider, DnsResponseCache dnsResponseCache, ClientRateLimiter clientRateLimiter, ClientAccessPolicy clientAccessPolicy, RequestHistoryStore requestHistoryStore, TimeProvider timeProvider, ILogger<DnsProxyHandler> logger)
    {
        _filterEngineProvider = filterEngineProvider;
        _filteringPauseState = filteringPauseState;
        _customDnsRecordProvider = customDnsRecordProvider;
        _upstreamDnsClientProvider = upstreamDnsClientProvider;
        _dnsResponseCache = dnsResponseCache;
        _clientRateLimiter = clientRateLimiter;
        _clientAccessPolicy = clientAccessPolicy;
        _requestHistoryStore = requestHistoryStore;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async ValueTask<DnsMessage> HandleAsync(DnsRequestContext context, CancellationToken cancellationToken)
    {
        var response = context.CreateResponse();
        response.ResponseCode = DnsResponseCode.NoError;
        response.RecursionAvailable = true;

        var queryEdnsOptions = context.Query.EdnsOptions;
        ApplyResponseEdnsOptions(queryEdnsOptions, response);

        if (context.Query.OpCode is not DnsOpCode.Query)
        {
            response.ResponseCode = DnsResponseCode.NotImplemented;
            return response;
        }

        // An unsupported EDNS version must be answered with BADVERS and the highest version supported (RFC 6891, 6.1.3).
        if (queryEdnsOptions is { Version: > 0 } && response.EdnsOptions is { } responseEdns)
        {
            responseEdns.ExtendedRCode = BadVersionExtendedRCode;
            return response;
        }

        // A query carrying anything other than a single question has no defined merge semantics for the response code,
        // so it is rejected rather than answered ambiguously.
        if (context.Query.Questions.Count is not 1)
        {
            response.ResponseCode = DnsResponseCode.FormError;
            return response;
        }

        var question = context.Query.Questions[0];
        var clientAddress = context.RemoteEndPoint is IPEndPoint ipEndPoint ? ipEndPoint.Address : null;
        var historyEntryBuilder = new RequestHistoryEntryBuilder
        {
            TimestampUtc = _timeProvider.GetUtcNow(),
            Client = clientAddress?.ToString() ?? context.RemoteEndPoint.ToString() ?? "unknown",
            Protocol = context.Protocol.ToString(),
            QuestionName = question.Name,
            QuestionType = question.Type.ToString(),
            Result = "Forwarded",
            Upstream = "-",
        };

        if (!_clientAccessPolicy.IsAllowed(clientAddress))
        {
            return Complete(response, historyEntryBuilder, DnsResponseCode.Refused, "NotAllowed");
        }

        if (!_clientRateLimiter.TryAcquire(clientAddress))
        {
            return Complete(response, historyEntryBuilder, DnsResponseCode.Refused, "RateLimited");
        }

        if (_customDnsRecordProvider.TryApply(question, response))
        {
            return Complete(response, historyEntryBuilder, response.ResponseCode, "CustomRecord");
        }

        if (!_filteringPauseState.IsDisabled)
        {
            var filterResult = _filterEngineProvider.Engine.Evaluate(
                question.Name,
                ConvertToFilterQueryType(question.Type),
                new DnsClientInfo
                {
                    Address = clientAddress,
                });

            if (filterResult.Action is DnsFilterAction.Rewrite && TryApplyRewrite(filterResult.Rewrite!, question, response))
            {
                return Complete(response, historyEntryBuilder, response.ResponseCode, "Rewritten");
            }

            if (filterResult.Action is DnsFilterAction.Block or DnsFilterAction.Rewrite)
            {
                return Complete(response, historyEntryBuilder, DnsResponseCode.NameError, "Blocked");
            }
        }

        if (_dnsResponseCache.TryGet(question, queryEdnsOptions, response))
        {
            historyEntryBuilder.Upstream = "Cache";
            return Complete(response, historyEntryBuilder, response.ResponseCode, "CacheHit");
        }

        var forwardResult = await ForwardToUpstreamAsync(question, queryEdnsOptions, cancellationToken).ConfigureAwait(false);
        if (!forwardResult.IsSuccess)
        {
            return Complete(response, historyEntryBuilder, DnsResponseCode.ServerFailure, "UpstreamFailure");
        }

        ApplyUpstreamResponse(forwardResult.Response!, response);
        _dnsResponseCache.Store(question, queryEdnsOptions, response);

        historyEntryBuilder.Upstream = forwardResult.UpstreamEndpoint;
        historyEntryBuilder.LatencyMs = forwardResult.LatencyMs;
        var result = response.ResponseCode is DnsResponseCode.ServerFailure or DnsResponseCode.Refused ? "UpstreamFailure" : "Forwarded";
        return Complete(response, historyEntryBuilder, response.ResponseCode, result);
    }

    private DnsMessage Complete(DnsMessage response, RequestHistoryEntryBuilder historyEntryBuilder, DnsResponseCode responseCode, string result)
    {
        response.ResponseCode = responseCode;
        historyEntryBuilder.Result = result;
        historyEntryBuilder.ResponseCode = responseCode.ToString();
        _requestHistoryStore.Add(historyEntryBuilder.Build(response));

        return response;
    }

    private static void ApplyUpstreamResponse(Meziantou.Framework.DnsClient.Response.DnsResponseMessage upstreamResponse, DnsMessage response)
    {
        response.ResponseCode = (DnsResponseCode)upstreamResponse.Header.ResponseCode;
        response.RecursionAvailable = upstreamResponse.Header.RecursionAvailable;

        // The AD bit is deliberately not forwarded: the proxy does not validate signatures itself, so it must not
        // claim the answer is authenticated. DNSSEC records are forwarded, so a client can validate for itself.

        AppendRecords(response.Answers, upstreamResponse.Answers);
        AppendRecords(response.Authorities, upstreamResponse.Authorities);
        AppendRecords(response.AdditionalRecords, upstreamResponse.AdditionalRecords);
    }

    private static void AppendRecords(ICollection<DnsResourceRecord> target, IEnumerable<Meziantou.Framework.DnsClient.Response.DnsRecord> records)
    {
        foreach (var record in records)
        {
            // The proxy emits its own OPT record from DnsMessage.EdnsOptions; copying the upstream's would
            // produce a second OPT record, which RFC 6891 forbids.
            if (record.RecordType is Meziantou.Framework.DnsClient.Query.DnsQueryType.OPT)
            {
                continue;
            }

            target.Add(DnsRecordConverter.ConvertToServerRecord(record));
        }
    }

    /// <summary>
    /// Builds the OPT record the proxy sends back. The payload size and version describe the proxy, not the client,
    /// so they are not echoed from the query.
    /// </summary>
    private static void ApplyResponseEdnsOptions(DnsEdnsOptions? queryEdnsOptions, DnsMessage response)
    {
        if (queryEdnsOptions is null)
        {
            response.EdnsOptions = null;
            return;
        }

        response.EdnsOptions = new DnsEdnsOptions
        {
            UdpPayloadSize = ResponseUdpPayloadSize,
            Version = 0,
            DnssecOk = queryEdnsOptions.DnssecOk,
            ExtendedRCode = 0,
        };
    }

    private static DnsFilterQueryType ConvertToFilterQueryType(DnsQueryType queryType)
    {
        return Enum.IsDefined((DnsFilterQueryType)queryType)
            ? (DnsFilterQueryType)queryType
            : DnsFilterQueryType.ANY;
    }

    /// <summary>
    /// Applies a <c>$dnsrewrite</c> directive to the response. Returns <see langword="false"/> when
    /// the directive cannot be represented, in which case the caller falls back to blocking.
    /// </summary>
    private static bool TryApplyRewrite(DnsFilterRewriteRule rewrite, DnsQuestion question, DnsMessage response)
    {
        response.ResponseCode = rewrite.ResponseCode switch
        {
            DnsFilterRewriteResponseCode.NoError => DnsResponseCode.NoError,
            DnsFilterRewriteResponseCode.NameError => DnsResponseCode.NameError,
            DnsFilterRewriteResponseCode.Refused => DnsResponseCode.Refused,
            DnsFilterRewriteResponseCode.ServerFailure => DnsResponseCode.ServerFailure,
            _ => DnsResponseCode.NameError,
        };

        if (rewrite.RecordType is not { } recordType || rewrite.Value is not { } value)
            return true;

        var answerType = (DnsQueryType)recordType;
        if (question.Type is not DnsQueryType.ANY && question.Type != answerType)
        {
            // The rewrite targets a different record type than the one asked for; answer the
            // question with an empty NOERROR rather than a record the client did not request.
            return true;
        }

        if (!CustomDnsRecordProvider.TryCreateRecordData(answerType, value, out var data))
            return false;

        response.Answers.Add(new DnsResourceRecord
        {
            Name = question.Name,
            Type = answerType,
            Class = DnsQueryClass.IN,
            TimeToLive = RewriteTimeToLive,
            Data = data,
        });

        return true;
    }

    private async Task<ForwardResult> ForwardToUpstreamAsync(DnsQuestion question, DnsEdnsOptions? queryEdnsOptions, CancellationToken cancellationToken)
    {
        var upstreams = _upstreamDnsClientProvider.GetUpstreams();
        if (upstreams.Count == 0)
        {
            return ForwardResult.Failure();
        }

        // A SERVFAIL or REFUSED is an upstream problem rather than an answer, so the next upstream is tried. The last
        // such response is kept so the client sees the real response code when every upstream is unhealthy.
        var lastUnhealthyResult = ForwardResult.Failure();
        foreach (var upstream in upstreams)
        {
            try
            {
                var result = await QueryUpstreamAsync(upstream, question, queryEdnsOptions, cancellationToken).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    continue;
                }

                if (IsUpstreamFailure(result.Response!))
                {
                    lastUnhealthyResult = result;
                    continue;
                }

                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "An upstream query failed unexpectedly");
                continue;
            }
        }

        return lastUnhealthyResult;
    }

    private static bool IsUpstreamFailure(Meziantou.Framework.DnsClient.Response.DnsResponseMessage response)
    {
        // NXDOMAIN and an empty NOERROR are real answers and must not trigger failover.
        return response.Header.ResponseCode is Meziantou.Framework.DnsClient.Response.DnsResponseCode.ServerFailure
            or Meziantou.Framework.DnsClient.Response.DnsResponseCode.Refused;
    }

    private static async Task<ForwardResult> QueryUpstreamAsync(IUpstreamDnsClient upstream, DnsQuestion question, DnsEdnsOptions? queryEdnsOptions, CancellationToken cancellationToken)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
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

            // The client's DNSSEC-OK bit must reach the upstream, otherwise the proxy would answer a DNSSEC-aware
            // client with unsigned data while still reporting DO in its own OPT record.
            if (queryEdnsOptions is not null)
            {
                query.EdnsOptions = new Meziantou.Framework.DnsClient.Query.DnsEdnsOptions
                {
                    UdpPayloadSize = ResponseUdpPayloadSize,
                    Version = 0,
                    DnssecOk = queryEdnsOptions.DnssecOk,
                };
            }

            var response = await upstream.SendAsync(query, cancellationToken).ConfigureAwait(false);
            return ForwardResult.Success(upstream.DisplayName, response, (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ForwardResult.Failure();
        }
        catch (Meziantou.Framework.DnsClient.DnsProtocolException)
        {
            return ForwardResult.Failure();
        }
        catch (HttpRequestException)
        {
            return ForwardResult.Failure();
        }
    }
}
