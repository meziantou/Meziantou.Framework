using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;

namespace Meziantou.AspNetCore.Authentication.HttpBasic;

/// <summary>Options for HTTP Basic authentication.</summary>
public sealed class HttpBasicAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>The default maximum length (in characters) of the Base64 credential payload.</summary>
    public const int DefaultMaxCredentialLength = 4096;

    /// <summary>Gets or sets the value of the <c>realm</c> parameter in the <c>WWW-Authenticate</c> header. Set to <see langword="null"/> to omit the parameter.</summary>
    public string? Realm { get; set; } = "Restricted";

    /// <summary>
    /// Gets or sets the maximum length (in characters) of the Base64 credential payload.
    /// Requests exceeding this limit fail authentication.
    /// </summary>
    public int MaxCredentialLength
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, 0);
            field = value;
        }
    } = DefaultMaxCredentialLength;

    /// <summary>
    /// Gets or sets the delegate used to validate credentials.
    /// </summary>
    public HttpBasicCredentialValidator ValidateCredentials { get; set; } = static (_, _, _) => ValueTask.FromResult(false);

    /// <summary>
    /// Gets or sets the delegate used to create the <see cref="ClaimsPrincipal"/> for an authenticated user.
    /// Returning <see langword="null"/> fails authentication.
    /// </summary>
    public HttpBasicPrincipalFactory CreatePrincipal { get; set; } = static (_, authenticationScheme, username) => ValueTask.FromResult<ClaimsPrincipal?>(CreateDefaultPrincipal(authenticationScheme, username));

    private static ClaimsPrincipal CreateDefaultPrincipal(string authenticationScheme, string username)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.NameIdentifier, username),
        };

        var identity = new ClaimsIdentity(claims, authenticationType: authenticationScheme);
        return new ClaimsPrincipal(identity);
    }
}
