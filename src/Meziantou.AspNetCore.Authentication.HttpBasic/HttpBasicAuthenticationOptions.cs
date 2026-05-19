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
    /// Gets or sets the delegate used to validate credentials and create the <see cref="ClaimsPrincipal"/>.
    /// Returning <see langword="null"/> fails authentication.
    /// </summary>
    public HttpBasicCredentialValidator ValidateCredentials { get; set; } = (_, _, _) => ValueTask.FromResult<ClaimsPrincipal?>(null);
}
