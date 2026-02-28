# Meziantou.AspNetCore.Authentication.HttpBasic

ASP.NET Core authentication handler for HTTP Basic authentication.

Credential validation is delegate-based through `options.ValidateCredentials`.

## Usage

```csharp
using Meziantou.AspNetCore.Authentication.HttpBasic;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddAuthentication(HttpBasicAuthenticationDefaults.AuthenticationScheme)
    .AddHttpBasic(options =>
    {
        options.Realm = "My application";
        options.MaxCredentialLength = 4096;
        options.ValidateCredentials = static (context, username, password) =>
        {
            var isValid = username == "admin" && password == "secret";
            return ValueTask.FromResult(isValid);
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

## Security options

- `MaxCredentialLength` limits the size (in characters) of the Base64 credential payload in the `Authorization` header.
