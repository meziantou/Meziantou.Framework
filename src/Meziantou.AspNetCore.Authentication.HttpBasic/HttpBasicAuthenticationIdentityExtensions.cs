using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace Meziantou.AspNetCore.Authentication.HttpBasic;

/// <summary>Extension methods to register HTTP Basic authentication with ASP.NET Core Identity.</summary>
public static class HttpBasicAuthenticationIdentityExtensions
{
    /// <summary>Adds HTTP Basic authentication using ASP.NET Core Identity to validate credentials and build user principals.</summary>
    public static AuthenticationBuilder AddHttpBasicIdentity<TUser>(this AuthenticationBuilder builder)
        where TUser : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddHttpBasicIdentity<TUser>(_ => { });
    }

    /// <summary>Adds HTTP Basic authentication using ASP.NET Core Identity to validate credentials and build user principals.</summary>
    public static AuthenticationBuilder AddHttpBasicIdentity<TUser>(this AuthenticationBuilder builder, Action<HttpBasicAuthenticationOptions> configureOptions)
        where TUser : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configureOptions);

        return builder.AddHttpBasicIdentity<TUser>(HttpBasicAuthenticationDefaults.AuthenticationScheme, displayName: null, configureOptions, lockoutOnFailure: false);
    }

    /// <summary>Adds HTTP Basic authentication using ASP.NET Core Identity to validate credentials and build user principals.</summary>
    public static AuthenticationBuilder AddHttpBasicIdentity<TUser>(this AuthenticationBuilder builder, Action<HttpBasicAuthenticationOptions> configureOptions, bool lockoutOnFailure)
        where TUser : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configureOptions);

        return builder.AddHttpBasicIdentity<TUser>(HttpBasicAuthenticationDefaults.AuthenticationScheme, displayName: null, configureOptions, lockoutOnFailure);
    }

    /// <summary>Adds HTTP Basic authentication using ASP.NET Core Identity to validate credentials and build user principals.</summary>
    public static AuthenticationBuilder AddHttpBasicIdentity<TUser>(this AuthenticationBuilder builder, string authenticationScheme, Action<HttpBasicAuthenticationOptions> configureOptions, bool lockoutOnFailure)
        where TUser : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(authenticationScheme);
        ArgumentNullException.ThrowIfNull(configureOptions);

        return builder.AddHttpBasicIdentity<TUser>(authenticationScheme, displayName: null, configureOptions, lockoutOnFailure);
    }

    /// <summary>Adds HTTP Basic authentication using ASP.NET Core Identity to validate credentials and build user principals.</summary>
    public static AuthenticationBuilder AddHttpBasicIdentity<TUser>(this AuthenticationBuilder builder, string authenticationScheme, string? displayName, Action<HttpBasicAuthenticationOptions> configureOptions, bool lockoutOnFailure)
        where TUser : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(authenticationScheme);
        ArgumentNullException.ThrowIfNull(configureOptions);

        return builder.AddHttpBasic(authenticationScheme, displayName, options =>
        {
            configureOptions(options);
            ConfigureIdentityIntegration<TUser>(options, lockoutOnFailure);
        });
    }

    private static void ConfigureIdentityIntegration<TUser>(HttpBasicAuthenticationOptions options, bool lockoutOnFailure)
        where TUser : class
    {
        options.ValidateCredentials = (context, username, password) => ValidateCredentialsAsync<TUser>(context, username, password, lockoutOnFailure);
        options.CreatePrincipal = static (context, _, username) => CreatePrincipalAsync<TUser>(context, username);
    }

    private static async ValueTask<bool> ValidateCredentialsAsync<TUser>(HttpContext context, string username, string password, bool lockoutOnFailure)
        where TUser : class
    {
        var signInManager = context.RequestServices.GetRequiredService<SignInManager<TUser>>();
        var user = await signInManager.UserManager.FindByNameAsync(username).ConfigureAwait(false);
        if (user is null)
            return false;

        var result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure).ConfigureAwait(false);
        return result.Succeeded;
    }

    private static async ValueTask<ClaimsPrincipal?> CreatePrincipalAsync<TUser>(HttpContext context, string username)
        where TUser : class
    {
        var signInManager = context.RequestServices.GetRequiredService<SignInManager<TUser>>();
        var user = await signInManager.UserManager.FindByNameAsync(username).ConfigureAwait(false);
        if (user is null)
            return null;

        return await signInManager.CreateUserPrincipalAsync(user).ConfigureAwait(false);
    }
}
