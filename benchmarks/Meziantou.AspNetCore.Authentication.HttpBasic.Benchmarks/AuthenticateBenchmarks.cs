using System.Security.Claims;
using System.Text;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meziantou.AspNetCore.Authentication.HttpBasic.Benchmarks;

/// <summary>One request authenticated by the HTTP Basic scheme, from a new request scope to the authentication result.</summary>
[MemoryDiagnoser]
public class AuthenticateBenchmarks : IDisposable
{
    private static readonly ClaimsPrincipal Principal = new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "user")], HttpBasicAuthenticationDefaults.AuthenticationScheme));

    private ServiceProvider _services = null!;
    private string _validCredentials = null!;
    private string _longCredentials = null!;
    private string _wrongPassword = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddAuthentication().AddHttpBasic(options =>
        {
            options.ValidateCredentials = (_, username, password) =>
            {
                var valid = (username is "user" or "a-much-longer-user-name@example.com") && (password is "password" or "Correct-Horse-Battery-Staple-0123456789");
                return ValueTask.FromResult(valid ? Principal : null);
            };
        });

        _services = services.BuildServiceProvider();
        _validCredentials = Encode("user", "password");
        _longCredentials = Encode("a-much-longer-user-name@example.com", "Correct-Horse-Battery-Staple-0123456789");
        _wrongPassword = Encode("user", "wrong-password");
    }

    [GlobalCleanup]
    public void Dispose() => _services.Dispose();

    [Benchmark]
    public Task<bool> NoHeader() => AuthenticateAsync(authorization: null);

    [Benchmark]
    public Task<bool> BearerHeader() => AuthenticateAsync("Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0In0.c2lnbmF0dXJl");

    [Benchmark]
    public Task<bool> ValidCredentials() => AuthenticateAsync(_validCredentials);

    [Benchmark]
    public Task<bool> ValidLongCredentials() => AuthenticateAsync(_longCredentials);

    [Benchmark]
    public Task<bool> WrongPassword() => AuthenticateAsync(_wrongPassword);

    [Benchmark]
    public Task<bool> InvalidBase64() => AuthenticateAsync("Basic !!!not-base64!!!");

    private async Task<bool> AuthenticateAsync(string? authorization)
    {
        await using var scope = _services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        if (authorization is not null)
        {
            context.Request.Headers.Authorization = authorization;
        }

        var result = await context.AuthenticateAsync(HttpBasicAuthenticationDefaults.AuthenticationScheme);
        return result.Succeeded;
    }

    private static string Encode(string username, string password) => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + password));
}
