using Microsoft.AspNetCore.Http;

namespace Meziantou.AspNetCore.Authentication.HttpBasic;

/// <summary>Delegate used to validate HTTP Basic credentials.</summary>
/// <param name="httpContext">The current HTTP context.</param>
/// <param name="username">The username from the Authorization header.</param>
/// <param name="password">The password from the Authorization header.</param>
/// <returns><see langword="true"/> if credentials are valid; otherwise, <see langword="false"/>.</returns>
public delegate ValueTask<bool> HttpBasicCredentialValidator(HttpContext httpContext, string username, string password);
