# Meziantou.AspNetCore.Authentication.HttpBasic

ASP.NET Core authentication handler for HTTP Basic authentication.

Credential validation is delegate-based through `options.ValidateCredentials`, which returns a `ClaimsPrincipal` for valid credentials and `null` for invalid credentials.

You can also integrate with ASP.NET Core Identity using `AddHttpBasicIdentity<TUser>()`.

## Usage

```csharp
using Meziantou.AspNetCore.Authentication.HttpBasic;
using System.Security.Claims;

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
```

## ASP.NET Core Identity integration

```csharp
using Meziantou.AspNetCore.Authentication.HttpBasic;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddIdentityCore<IdentityUser>()
                .AddSignInManager();

builder.Services
    .AddAuthentication(HttpBasicAuthenticationDefaults.AuthenticationScheme)
    .AddHttpBasicIdentity<IdentityUser>(options =>
    {
        options.Realm = "My application";
    });
```

The principal is built by `SignInManager<TUser>.CreateUserPrincipalAsync`, so `User.Identity.AuthenticationType` is Identity's own `"Identity.Application"`, not the Basic scheme name:

```csharp
app.MapGet("/", (ClaimsPrincipal user) => user.Identity?.AuthenticationType);
// => "Identity.Application"
```

Authorization policies are keyed on the authentication *scheme*, so `RequireAuthorization` and `AddAuthenticationSchemes(...)` behave as expected. Only code that branches on `AuthenticationType` is affected — audit logging, "how did this user sign in" checks, or an application that mixes cookie and Basic authentication would attribute these requests to the cookie scheme. Use the scheme name from the authentication ticket if you need to tell them apart.

## Security options

- `MaxCredentialLength` limits the size (in characters) of the Base64 credential payload in the `Authorization` header.
