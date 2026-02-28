using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace Meziantou.AspNetCore.Authentication.HttpBasic;

/// <summary>Extension methods to register HTTP Basic authentication.</summary>
public static class HttpBasicAuthenticationExtensions
{
    /// <summary>Adds HTTP Basic authentication.</summary>
    public static AuthenticationBuilder AddHttpBasic(this AuthenticationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddHttpBasic(HttpBasicAuthenticationDefaults.AuthenticationScheme, _ => { });
    }

    /// <summary>Adds HTTP Basic authentication.</summary>
    public static AuthenticationBuilder AddHttpBasic(this AuthenticationBuilder builder, Action<HttpBasicAuthenticationOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configureOptions);

        return builder.AddHttpBasic(HttpBasicAuthenticationDefaults.AuthenticationScheme, configureOptions);
    }

    /// <summary>Adds HTTP Basic authentication.</summary>
    public static AuthenticationBuilder AddHttpBasic(this AuthenticationBuilder builder, string authenticationScheme, Action<HttpBasicAuthenticationOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(authenticationScheme);
        ArgumentNullException.ThrowIfNull(configureOptions);

        return builder.AddHttpBasic(authenticationScheme, displayName: null, configureOptions);
    }

    /// <summary>Adds HTTP Basic authentication.</summary>
    public static AuthenticationBuilder AddHttpBasic(this AuthenticationBuilder builder, string authenticationScheme, string? displayName, Action<HttpBasicAuthenticationOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(authenticationScheme);
        ArgumentNullException.ThrowIfNull(configureOptions);

        return builder.AddScheme<HttpBasicAuthenticationOptions, HttpBasicAuthenticationHandler>(authenticationScheme, displayName, configureOptions);
    }
}
