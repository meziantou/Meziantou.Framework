using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
    public static AuthenticationBuilder AddHttpBasicIdentity<TUser>(this AuthenticationBuilder builder, string authenticationScheme, Action<HttpBasicAuthenticationOptions> configureOptions)
        where TUser : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(authenticationScheme);
        ArgumentNullException.ThrowIfNull(configureOptions);

        return builder.AddHttpBasicIdentity<TUser>(authenticationScheme, displayName: null, configureOptions, lockoutOnFailure: false);
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

        HttpBasicCredentialValidator identityValidator = (context, username, password) => ValidateCredentialsAsync<TUser>(context, username, password, authenticationScheme, lockoutOnFailure);

        // Identity owns credential validation for this scheme. Any other validator, whether set by configureOptions
        // or by a later Configure/PostConfigure call, would either be ignored or bypass the password check,
        // so the configuration is rejected instead of letting one of them win silently.
        builder.Services.AddOptions<HttpBasicAuthenticationOptions>(authenticationScheme)
            .Validate(options => ReferenceEquals(options.ValidateCredentials, identityValidator), $"The '{authenticationScheme}' scheme is registered with {nameof(AddHttpBasicIdentity)}, so ASP.NET Core Identity validates the credentials and {nameof(HttpBasicAuthenticationOptions)}.{nameof(HttpBasicAuthenticationOptions.ValidateCredentials)} must not be changed. Use {nameof(HttpBasicAuthenticationExtensions.AddHttpBasic)} to provide a custom validator, or use authorization policies to restrict which authenticated users can access a resource.")
            .ValidateOnStart();

        return builder.AddHttpBasic(authenticationScheme, displayName, options =>
        {
            options.ValidateCredentials = identityValidator;
            configureOptions(options);
        });
    }

    private static async ValueTask<ClaimsPrincipal?> ValidateCredentialsAsync<TUser>(HttpContext context, string username, string password, string authenticationScheme, bool lockoutOnFailure)
        where TUser : class
    {
        var signInManager = context.RequestServices.GetRequiredService<SignInManager<TUser>>();
        var user = await signInManager.UserManager.FindByNameAsync(username).ConfigureAwait(false);
        if (user is null)
            return null;

        var result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure).ConfigureAwait(false);
        if (!result.Succeeded)
            return null;

        // CheckPasswordSignInAsync only checks the password: Identity enforces the second factor later, in the interactive
        // sign-in flow, which a per-request Basic header cannot complete. Reject the account instead of letting the password
        // alone authenticate it, unless the application explicitly opted out. A remembered two-factor client is deliberately
        // not honored, so the result does not depend on an ambient browser cookie.
        // The option is read on each request so a value set by a Configure call registered after AddHttpBasicIdentity is honored.
        var allowTwoFactorEnabledAccounts = context.RequestServices.GetRequiredService<IOptionsMonitor<HttpBasicAuthenticationOptions>>().Get(authenticationScheme).AllowTwoFactorEnabledAccounts;
        if (!allowTwoFactorEnabledAccounts && await signInManager.IsTwoFactorEnabledAsync(user).ConfigureAwait(false))
            return null;

        return await signInManager.CreateUserPrincipalAsync(user).ConfigureAwait(false);
    }
}
