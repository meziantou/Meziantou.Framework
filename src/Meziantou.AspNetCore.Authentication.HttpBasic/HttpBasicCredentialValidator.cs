using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace Meziantou.AspNetCore.Authentication.HttpBasic;

/// <summary>Delegate used to validate HTTP Basic credentials.</summary>
/// <param name="httpContext">The current HTTP context.</param>
/// <param name="username">The username from the Authorization header.</param>
/// <param name="password">The password from the Authorization header.</param>
/// <returns>The principal for authenticated credentials, or <see langword="null"/> to fail authentication.</returns>
public delegate ValueTask<ClaimsPrincipal?> HttpBasicCredentialValidator(HttpContext httpContext, string username, string password);
