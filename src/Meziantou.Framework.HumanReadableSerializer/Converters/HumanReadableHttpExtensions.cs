using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

namespace Meziantou.Framework.HumanReadable.Converters;

public static class HumanReadableHttpExtensions
{
    private static readonly object RegistrationOwner = new();

    /// <summary>Configures how HTTP messages are serialized.</summary>
    /// <param name="options">The serialization options.</param>
    /// <param name="httpOptions">The HTTP options.</param>
    /// <returns>The serialization options.</returns>
    /// <remarks>
    /// A later call replaces the configuration of the previous calls, so the defaults of a snapshot serializer can be customized.
    /// The options are copied, so changing <paramref name="httpOptions"/> after this call has no effect.
    /// </remarks>
    public static HumanReadableSerializerOptions AddHttpConverters(this HumanReadableSerializerOptions options, HumanReadableHttpOptions httpOptions)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpOptions);

        options.VerifyMutable();
        options.RemoveMemberAttributes(RegistrationOwner);
        for (var i = options.Converters.Count - 1; i >= 0; i--)
        {
            // These converter types are only created by this method
            if (options.Converters[i] is HttpHeadersConverter<HttpRequestHeaders> or HttpHeadersConverter<HttpResponseHeaders>)
            {
                options.Converters.RemoveAt(i);
            }
        }

        var requestOptions = httpOptions.RequestMessageOptions;
        HttpHeaderSettings? requestHeaders = null;
        if (requestOptions is not null)
        {
            requestHeaders = new HttpHeaderSettings(requestOptions.ExcludedHeaderNames, requestOptions.HeaderValueTransformer);
            if (!requestHeaders.IsEmpty)
            {
                options.Converters.Add(new HttpHeadersConverter<HttpRequestHeaders>(requestHeaders));
            }
        }

        var responseOptions = httpOptions.ResponseMessageOptions;
        HttpHeaderSettings? responseHeaders = null;
        if (responseOptions is not null)
        {
            IEnumerable<HttpHeaderValueFormatter> headerFormatters = responseOptions.HeaderValueTransformer;
            if (responseOptions.RedactContentSecurityPolicyNonce)
            {
                headerFormatters = headerFormatters.Prepend(new ContentSecurityPolicyFormatter());
            }

            responseHeaders = new HttpHeaderSettings(responseOptions.ExcludedHeaderNames, headerFormatters);
            if (!responseHeaders.IsEmpty)
            {
                options.Converters.Add(new HttpHeadersConverter<HttpResponseHeaders>(responseHeaders));
            }
        }

        ConfigureHttpRequestMessage(options, requestOptions, requestHeaders);
        ConfigureHttpResponseMessage(options, responseOptions, responseHeaders);

        return options;
    }

    private static void AddAttribute(HumanReadableSerializerOptions options, Type type, string memberName, HumanReadableAttribute attribute)
        => options.AddAttribute(type, memberName, attribute, RegistrationOwner);

    private static void ConfigureHttpRequestMessage(HumanReadableSerializerOptions options, HumanReadableHttpRequestMessageOptions? requestOptions, HttpHeaderSettings? headers)
    {
        // Set order
        AddAttribute(options, typeof(HttpRequestMessage), nameof(HttpRequestMessage.Method), new HumanReadablePropertyOrderAttribute(0));
        AddAttribute(options, typeof(HttpRequestMessage), nameof(HttpRequestMessage.RequestUri), new HumanReadablePropertyOrderAttribute(1));
        AddAttribute(options, typeof(HttpRequestMessage), nameof(HttpRequestMessage.Version), new HumanReadablePropertyOrderAttribute(2));
        AddAttribute(options, typeof(HttpRequestMessage), nameof(HttpRequestMessage.VersionPolicy), new HumanReadablePropertyOrderAttribute(3));
        AddAttribute(options, typeof(HttpRequestMessage), nameof(HttpRequestMessage.Headers), new HumanReadablePropertyOrderAttribute(4));
        AddAttribute(options, typeof(HttpRequestMessage), nameof(HttpRequestMessage.Content), new HumanReadablePropertyOrderAttribute(5));

        // ignore properties
        AddAttribute(options, typeof(HttpRequestMessage), nameof(HttpRequestMessage.Headers), new HumanReadableIgnoreAttribute()
        {
            Condition = HumanReadableIgnoreCondition.Custom,
            CustomCondition = data => IsEmptyAfterFiltering(data.Value, headers),
        });

        if (headers is not null && !headers.IsEmpty)
        {
            AddAttribute(options, typeof(HttpRequestMessage), nameof(HttpRequestMessage.Content), new HumanReadableConverterAttribute(new HttpContentConverter(headers)));
        }

        if (requestOptions is not null && requestOptions.OmitProtocolVersion)
        {
            AddAttribute(options, typeof(HttpRequestMessage), nameof(HttpRequestMessage.Version), new HumanReadableDefaultValueAttribute(HttpVersion.Version11));
            AddAttribute(options, typeof(HttpRequestMessage), nameof(HttpRequestMessage.Version), new HumanReadableIgnoreAttribute { Condition = HumanReadableIgnoreCondition.WhenWritingDefault });
            AddAttribute(options, typeof(HttpRequestMessage), nameof(HttpRequestMessage.VersionPolicy), new HumanReadableIgnoreAttribute { Condition = HumanReadableIgnoreCondition.WhenWritingDefault });
        }

#pragma warning disable CS0618 // Type or member is obsolete
        AddAttribute(options, typeof(HttpRequestMessage), nameof(HttpRequestMessage.Properties), new HumanReadableIgnoreAttribute() { Condition = HumanReadableIgnoreCondition.WhenWritingDefaultOrEmptyCollection });
#pragma warning restore CS0618

        AddAttribute(options, typeof(HttpRequestMessage), nameof(HttpRequestMessage.Options), new HumanReadableIgnoreAttribute() { Condition = HumanReadableIgnoreCondition.WhenWritingDefaultOrEmptyCollection });
#if NET11_0_OR_GREATER
        AddAttribute(options, typeof(HttpRequestMessage), "ConnectionId", new HumanReadableIgnoreAttribute());
#endif
    }

    private static void ConfigureHttpResponseMessage(HumanReadableSerializerOptions options, HumanReadableHttpResponseMessageOptions? responseOptions, HttpHeaderSettings? headers)
    {
        // Ignore properties
        if (responseOptions is not null)
        {
            switch (responseOptions.RequestMessageFormat)
            {
                case HttpRequestMessageFormat.NotSerialized:
                    AddAttribute(options, typeof(HttpResponseMessage), nameof(HttpResponseMessage.RequestMessage), new HumanReadableIgnoreAttribute());
                    break;

                case HttpRequestMessageFormat.MethodAndUri:
                    AddAttribute(options, typeof(HttpResponseMessage), nameof(HttpResponseMessage.RequestMessage), new HumanReadableConverterAttribute(typeof(RequestMessageAsUriConverter)));
                    break;
            }
        }

        AddAttribute(options, typeof(HttpResponseMessage), nameof(HttpResponseMessage.IsSuccessStatusCode), new HumanReadableIgnoreAttribute());
        AddAttribute(options, typeof(HttpResponseMessage), nameof(HttpResponseMessage.ReasonPhrase), new HumanReadableIgnoreAttribute());
        AddAttribute(options, typeof(HttpResponseMessage), nameof(HttpResponseMessage.Headers), new HumanReadableIgnoreAttribute()
        {
            Condition = HumanReadableIgnoreCondition.Custom,
            CustomCondition = data => IsEmptyAfterFiltering(data.Value, headers),
        });

        AddAttribute(options, typeof(HttpResponseMessage), nameof(HttpResponseMessage.TrailingHeaders), new HumanReadableIgnoreAttribute()
        {
            Condition = HumanReadableIgnoreCondition.Custom,
            CustomCondition = data => IsEmptyAfterFiltering(data.Value, headers),
        });

        if (headers is not null && !headers.IsEmpty)
        {
            AddAttribute(options, typeof(HttpResponseMessage), nameof(HttpResponseMessage.Content), new HumanReadableConverterAttribute(new HttpContentConverter(headers)));
        }

        if (responseOptions is not null && responseOptions.OmitProtocolVersion)
        {
            AddAttribute(options, typeof(HttpResponseMessage), nameof(HttpResponseMessage.Version), new HumanReadableIgnoreAttribute { Condition = HumanReadableIgnoreCondition.WhenWritingDefault });
            AddAttribute(options, typeof(HttpResponseMessage), nameof(HttpResponseMessage.Version), new HumanReadableDefaultValueAttribute(HttpVersion.Version11));
        }

        // Set order
        AddAttribute(options, typeof(HttpResponseMessage), nameof(HttpResponseMessage.StatusCode), new HumanReadablePropertyOrderAttribute(0));
        AddAttribute(options, typeof(HttpResponseMessage), nameof(HttpResponseMessage.Version), new HumanReadablePropertyOrderAttribute(1));
        AddAttribute(options, typeof(HttpResponseMessage), nameof(HttpResponseMessage.Headers), new HumanReadablePropertyOrderAttribute(2));
        AddAttribute(options, typeof(HttpResponseMessage), nameof(HttpResponseMessage.TrailingHeaders), new HumanReadablePropertyOrderAttribute(3));
        AddAttribute(options, typeof(HttpResponseMessage), nameof(HttpResponseMessage.Content), new HumanReadablePropertyOrderAttribute(4));
    }

    private static bool IsEmptyAfterFiltering(object? value, HttpHeaderSettings? headers)
    {
        if (headers is not null)
            return headers.IsEmptyAfterFiltering(value);

        return value is not HttpHeaders httpHeaders || httpHeaders.NonValidated.Count is 0;
    }

    [SuppressMessage("Performance", "CA1812")]
    private sealed class RequestMessageAsUriConverter : HumanReadableConverter<HttpRequestMessage>
    {
        protected override void WriteValue(HumanReadableTextWriter writer, HttpRequestMessage? value, HumanReadableSerializerOptions options)
        {
            Debug.Assert(value is not null);

            // Uri.ToString() unescapes the URI, so use the original string as the Uri converter does
            if (value.RequestUri is null)
            {
                writer.WriteValue(value.Method.ToString());
            }
            else
            {
                writer.WriteValue(value.Method + " " + value.RequestUri.OriginalString);
            }
        }
    }
}
