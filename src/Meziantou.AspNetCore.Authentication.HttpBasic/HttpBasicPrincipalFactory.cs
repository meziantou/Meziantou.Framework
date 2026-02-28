using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace Meziantou.AspNetCore.Authentication.HttpBasic;

/// <summary>
/// Delegate used to create the authenticated principal for HTTP Basic authentication.
/// </summary>
/// <param name="httpContext">The current <see cref="HttpContext"/>.</param>
/// <param name="authenticationScheme">The current authentication scheme name.</param>
/// <param name="username">The username from the credentials.</param>
/// <returns>The principal to use for the authentication ticket, or <see langword="null"/> to fail authentication.</returns>
public delegate ValueTask<ClaimsPrincipal?> HttpBasicPrincipalFactory(HttpContext httpContext, string authenticationScheme, string username);
