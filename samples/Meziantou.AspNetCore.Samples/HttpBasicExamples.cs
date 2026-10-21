using System.Security.Claims;
using Meziantou.AspNetCore.Authentication.HttpBasic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Meziantou.AspNetCore.Samples;

/// <summary>The examples of src/Meziantou.AspNetCore.Authentication.HttpBasic/readme.md.</summary>
public static class HttpBasicExamples
{
    public static void Usage(string[] args)
    {
        #region HttpBasic_Usage
        var builder = WebApplication.CreateBuilder(args);

        builder.Services
            .AddAuthentication(HttpBasicAuthenticationDefaults.AuthenticationScheme)
            .AddHttpBasic(options =>
            {
                options.Realm = "My application";
                options.MaxCredentialLength = 4096;
                options.ValidateCredentials = (context, username, password) =>
                {
                    if (!string.Equals(username, "admin", StringComparison.Ordinal) ||
                        !string.Equals(password, "secret", StringComparison.Ordinal))
                    {
                        return ValueTask.FromResult<ClaimsPrincipal?>(null);
                    }

                    var claims = new[]
                    {
                        new Claim(ClaimTypes.Name, username),
                        new Claim(ClaimTypes.NameIdentifier, username),
                    };
                    var identity = new ClaimsIdentity(claims, authenticationType: HttpBasicAuthenticationDefaults.AuthenticationScheme);
                    return ValueTask.FromResult<ClaimsPrincipal?>(new ClaimsPrincipal(identity));
                };
            });

        builder.Services.AddAuthorization();

        var app = builder.Build();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/", (ClaimsPrincipal user) => $"Hello {user.Identity?.Name}!")
            .RequireAuthorization();

        app.Run();
        #endregion
    }

    public static void IdentityIntegration(string[] args)
    {
        #region HttpBasic_IdentityIntegration
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddIdentityCore<IdentityUser>()
                        .AddSignInManager();

        builder.Services
            .AddAuthentication(HttpBasicAuthenticationDefaults.AuthenticationScheme)
            .AddHttpBasicIdentity<IdentityUser>(options =>
            {
                options.Realm = "My application";
            });
        #endregion
    }

    public static void AllowTwoFactorEnabledAccounts(WebApplicationBuilder builder)
    {
        #region HttpBasic_AllowTwoFactorEnabledAccounts
        builder.Services
            .AddAuthentication(HttpBasicAuthenticationDefaults.AuthenticationScheme)
            .AddHttpBasicIdentity<IdentityUser>(options => options.AllowTwoFactorEnabledAccounts = true);
        #endregion
    }

    public static void LockoutOnFailure(WebApplicationBuilder builder)
    {
        #region HttpBasic_LockoutOnFailure
        builder.Services
            .AddAuthentication(HttpBasicAuthenticationDefaults.AuthenticationScheme)
            .AddHttpBasicIdentity<IdentityUser>(options => options.Realm = "My application", lockoutOnFailure: true);
        #endregion
    }
}
