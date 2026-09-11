# Meziantou.AspNetCore.Authentication.HttpBasic

ASP.NET Core authentication handler for HTTP Basic authentication.

> [!WARNING]
> HTTP Basic sends the password on **every** request, encoded with Base64, which is reversible and not encryption. Serve these endpoints over HTTPS only. Unlike a session cookie, a leaked Basic credential is the password itself and stays replayable until it is changed.

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

### Authentication type

The principal is built by `SignInManager<TUser>.CreateUserPrincipalAsync`, so `User.Identity.AuthenticationType` is Identity's own `"Identity.Application"`, not the Basic scheme name:

```csharp
app.MapGet("/", (ClaimsPrincipal user) => user.Identity?.AuthenticationType);
// => "Identity.Application"
```

Authorization policies are keyed on the authentication *scheme*, so `RequireAuthorization` and `AddAuthenticationSchemes(...)` behave as expected. Only code that branches on `AuthenticationType` is affected — audit logging, "how did this user sign in" checks, or an application that mixes cookie and Basic authentication would attribute these requests to the cookie scheme. Use the scheme name from the authentication ticket if you need to tell them apart.

### Two-factor authentication

Accounts that require two-factor authentication are rejected, even when the password is correct. A Basic `Authorization` header carries only a username and a password, so it cannot satisfy a second factor. Accepting the password alone would let anyone who knows it skip the second factor that Identity's interactive sign-in enforces.

An account requires two-factor authentication when `SignInManager<TUser>.IsTwoFactorEnabledAsync` returns `true`. This is the same check `PasswordSignInAsync` uses: the user store implements `IUserTwoFactorStore<TUser>`, two-factor authentication is enabled for the user, and at least one two-factor provider can produce a token for them. A remembered two-factor client (the `Identity.TwoFactorRememberMe` cookie) does not exempt the account.

The rejection is reported to the client as an ordinary `401 Unauthorized`, the same as a wrong password, so the response does not reveal that the password was correct. It does not count as a failed attempt for lockout. Clients such as scripts or services that act for these accounts need credentials meant for them, for example scoped and revocable API keys or tokens, validated by `AddHttpBasic` with your own `ValidateCredentials` delegate or by another authentication scheme.

If you accept the risk, set `AllowTwoFactorEnabledAccounts` to authenticate these accounts with their password alone:

```csharp
builder.Services
    .AddAuthentication(HttpBasicAuthenticationDefaults.AuthenticationScheme)
    .AddHttpBasicIdentity<IdentityUser>(options => options.AllowTwoFactorEnabledAccounts = true);
```

> [!WARNING]
> With `AllowTwoFactorEnabledAccounts` enabled, anyone who knows the password of a two-factor account can use this endpoint without the second factor. Only enable it when the resources behind the Basic scheme do not need the protection the second factor gives, or when an authorization policy independently requires evidence of multi-factor authentication.

Identity's other sign-in checks still apply: locked-out accounts and accounts that fail `IdentityOptions.SignIn` confirmation requirements (`RequireConfirmedAccount`, `RequireConfirmedEmail`, `RequireConfirmedPhoneNumber`) are rejected.

## Security options

- `MaxCredentialLength` limits the size (in characters) of the Base64 credential payload in the `Authorization` header. The limit is applied before the payload is decoded.
- `Realm` accepts printable ASCII only (U+0020 to U+007E). Other characters cannot be written to a `WWW-Authenticate` header, so they are rejected when the option is set rather than failing later on every challenge.

### Use HTTPS

Credentials travel in cleartext on every request. Do not expose a Basic endpoint over plain HTTP outside of loopback.

### Rate limit the endpoint

HTTP Basic is an easy brute-force target: there is no CSRF token, no session, and no interactive step, so an attacker can replay guesses as fast as the server answers. Put the endpoint behind [ASP.NET Core rate limiting](https://learn.microsoft.com/aspnet/core/performance/rate-limit).

This matters for throughput too. Credentials are revalidated from scratch on every request, and with ASP.NET Core Identity that means a full password hash each time — on the order of tens of milliseconds of CPU per request. A single client can consume a disproportionate amount of CPU.

### Account lockout with ASP.NET Core Identity

`AddHttpBasicIdentity<TUser>` does **not** record failed sign-in attempts by default, so Identity's lockout never triggers no matter how `IdentityOptions.Lockout` is configured. Pass `lockoutOnFailure: true` to opt in:

```csharp
builder.Services
    .AddAuthentication(HttpBasicAuthenticationDefaults.AuthenticationScheme)
    .AddHttpBasicIdentity<IdentityUser>(options => options.Realm = "My application", lockoutOnFailure: true);
```

The default is `false` because lockout on an endpoint with no interactive step lets a third party lock accounts out on purpose simply by sending bad passwords. Neither default is safe on its own: choose `lockoutOnFailure: true` to bound guessing per account, or keep `false` and rely on rate limiting to bound guessing per caller. Doing neither leaves an unthrottled password oracle.
