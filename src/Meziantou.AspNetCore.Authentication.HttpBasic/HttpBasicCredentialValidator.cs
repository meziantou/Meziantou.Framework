using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace Meziantou.AspNetCore.Authentication.HttpBasic;

/// <summary>Delegate used to validate HTTP Basic credentials.</summary>
/// <remarks>
/// Credentials containing a control character (U+0000 to U+001F, or U+007F) are rejected before this delegate is called.
/// Other Unicode characters are passed through unchanged, so treat both values as untrusted input and escape them before logging.
/// </remarks>
/// <param name="httpContext">The current HTTP context.</param>
/// <param name="username">The username from the Authorization header. It may be empty and never contains a colon.</param>
/// <param name="password">The password from the Authorization header. It may be empty and may contain colons.</param>
/// <returns>The principal for authenticated credentials, or <see langword="null"/> to fail authentication.</returns>
public delegate ValueTask<ClaimsPrincipal?> HttpBasicCredentialValidator(HttpContext httpContext, string username, string password);
