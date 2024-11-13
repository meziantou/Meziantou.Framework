using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

namespace Meziantou.Framework.HumanReadable.Converters;

public static class HumanReadableHttpExtensions
{
    public static HumanReadableSerializerOptions AddHttpConverters(this HumanReadableSerializerOptions options, HumanReadableHttpOptions httpOptions)
    {
        if (httpOptions.RequestMessageOptions is { } requestOptions)
        {
            if (requestOptions.ExcludedHeaderNames is not null || requestOptions.HeaderValueTransformer is not null)
                options.Converters.Add(new HttpHeadersConverter<HttpRequestHeaders>(requestOptions.ExcludedHeaderNames, requestOptions.HeaderValueTransformer));
        }

        if (httpOptions.ResponseMessageOptions is { } responseOptions)
        {
            IEnumerable<HttpHeaderValueFormatter> headerFormatters = responseOptions.HeaderValueTransformer;
            if (responseOptions.RedactContentSecurityPolicyNonce)
            {
                headerFormatters = headerFormatters.Prepend(new ContentSecurityPolicyFormatter());
            }

            if (responseOptions.ExcludedHeaderNames is not null || headerFormatters.Any())
            {
                options.Converters.Add(new HttpHeadersConverter<HttpResponseHeaders>(responseOptions.ExcludedHeaderNames, headerFormatters));
            }
        }

        ConfigureHttpRequestMessage(options, httpOptions.RequestMessageOptions);
        ConfigureHttpResponseMessage(options, httpOptions.ResponseMessageOptions);

        return options;
    }

    private static void ConfigureHttpRequestMessage(HumanReadableSerializerOptions options, HumanReadableHttpRequestMessageOptions? requestOptions)
    {
        // Set order
        options.AddAttribute(typeof(HttpRequestMessage), nameof(HttpRequestMessage.Method), new HumanReadablePropertyOrderAttribute(0));
        options.AddAttribute(typeof(HttpRequestMessage), nameof(HttpRequestMessage.RequestUri), new HumanReadablePropertyOrderAttribute(1));
        options.AddAttribute(typeof(HttpRequestMessage), nameof(HttpRequestMessage.Version), new HumanReadablePropertyOrderAttribute(2));
#if NET5_0_OR_GREATER
        options.AddAttribute(typeof(HttpRequestMessage), nameof(HttpRequestMessage.VersionPolicy), new HumanReadablePropertyOrderAttribute(3));
#endif
        options.AddAttribute(typeof(HttpRequestMessage), nameof(HttpRequestMessage.Headers), new HumanReadablePropertyOrderAttribute(4));
        options.AddAttribute(typeof(HttpRequestMessage), nameof(HttpRequestMessage.Content), new HumanReadablePropertyOrderAttribute(5));

        // ignore properties
        options.AddAttribute(typeof(HttpRequestMessage), nameof(HttpRequestMessage.Version), new HumanReadableDefaultValueAttribute(HttpVersion.Version11));
        options.AddAttribute(typeof(HttpRequestMessage), nameof(HttpRequestMessage.Headers), new HumanReadableIgnoreAttribute()
        {
            Condition = HumanReadableIgnoreCondition.Custom,
            CustomCondition = data => FilterHeaders(data.Value, requestOptions?.ExcludedHeaderNames),
        });

        if (requestOptions.OmitProtocolVersion)
        {
            options.AddAttribute(typeof(HttpResponseMessage), nameof(HttpResponseMessage.Version), new HumanReadableIgnoreAttribute { Condition = HumanReadableIgnoreCondition.Always });
        }

#pragma warning disable CS0618 // Type or member is obsolete
        options.AddAttribute(typeof(HttpRequestMessage), nameof(HttpRequestMessage.Properties), new HumanReadableIgnoreAttribute() { Condition = HumanReadableIgnoreCondition.WhenWritingDefaultOrEmptyCollection });
#pragma warning restore CS0618

#if NET5_0_OR_GREATER
        options.AddAttribute(typeof(HttpRequestMessage), nameof(HttpRequestMessage.Options), new HumanReadableIgnoreAttribute() { Condition = HumanReadableIgnoreCondition.WhenWritingDefaultOrEmptyCollection });
#endif
    }

    private static void ConfigureHttpResponseMessage(HumanReadableSerializerOptions options, HumanReadableHttpResponseMessageOptions? responseOptions)
    {
        // Ignore properties
        switch (responseOptions.RequestMessageFormat)
        {
            case HttpRequestMessageFormat.NotSerialized:
                options.AddAttribute(typeof(HttpResponseMessage), nameof(HttpResponseMessage.RequestMessage), new HumanReadableIgnoreAttribute());
                break;

            case HttpRequestMessageFormat.MethodAndUri:
                options.AddAttribute(typeof(HttpResponseMessage), nameof(HttpResponseMessage.RequestMessage), new HumanReadableConverterAttribute(typeof(RequestMessageAsUriConverter)));
                break;
        }

        options.AddAttribute(typeof(HttpResponseMessage), nameof(HttpResponseMessage.IsSuccessStatusCode), new HumanReadableIgnoreAttribute());
        options.AddAttribute(typeof(HttpResponseMessage), nameof(HttpResponseMessage.ReasonPhrase), new HumanReadableIgnoreAttribute());
        options.AddAttribute(typeof(HttpResponseMessage), nameof(HttpResponseMessage.Headers), new HumanReadableIgnoreAttribute()
        {
            Condition = HumanReadableIgnoreCondition.Custom,
            CustomCondition = data => FilterHeaders(data.Value, responseOptions?.ExcludedHeaderNames),
        });

#if NETCOREAPP3_1_OR_GREATER
        options.AddAttribute(typeof(HttpResponseMessage), nameof(HttpResponseMessage.TrailingHeaders), new HumanReadableIgnoreAttribute()
        {
            Condition = HumanReadableIgnoreCondition.Custom,
            CustomCondition = data => FilterHeaders(data.Value, responseOptions?.ExcludedHeaderNames),
        });
#endif

        if (responseOptions.OmitProtocolVersion)
        {
            options.AddAttribute(typeof(HttpResponseMessage), nameof(HttpResponseMessage.Version), new HumanReadableIgnoreAttribute { Condition = HumanReadableIgnoreCondition.WhenWritingDefault });
            options.AddAttribute(typeof(HttpResponseMessage), nameof(HttpResponseMessage.Version), new HumanReadableDefaultValueAttribute(HttpVersion.Version11));
        }

        // Set order
        options.AddAttribute(typeof(HttpResponseMessage), nameof(HttpResponseMessage.StatusCode), new HumanReadablePropertyOrderAttribute(0));
        options.AddAttribute(typeof(HttpResponseMessage), nameof(HttpResponseMessage.Version), new HumanReadablePropertyOrderAttribute(1));
        options.AddAttribute(typeof(HttpResponseMessage), nameof(HttpResponseMessage.Headers), new HumanReadablePropertyOrderAttribute(2));
#if NETCOREAPP3_1_OR_GREATER
        options.AddAttribute(typeof(HttpResponseMessage), nameof(HttpResponseMessage.TrailingHeaders), new HumanReadablePropertyOrderAttribute(3));
#endif
        options.AddAttribute(typeof(HttpResponseMessage), nameof(HttpResponseMessage.Content), new HumanReadablePropertyOrderAttribute(4));

    }

    private static bool FilterHeaders(object? value, ISet<string>? excludedHeaders)
    {
        if (value is HttpHeaders headers)
        {
            foreach (var header in headers)
            {
                if (excludedHeaders is null || !excludedHeaders.Contains(header.Key))
                    return false;
            }
        }

        return true;
    }

    [SuppressMessage("Performance", "CA1812")]
    private sealed class RequestMessageAsUriConverter : HumanReadableConverter<HttpRequestMessage>
    {
        protected override void WriteValue(HumanReadableTextWriter writer, HttpRequestMessage? value, HumanReadableSerializerOptions options)
        {
            Debug.Assert(value is not null);
            writer.WriteValue(value.Method + " " + value.RequestUri);
        }
    }
}