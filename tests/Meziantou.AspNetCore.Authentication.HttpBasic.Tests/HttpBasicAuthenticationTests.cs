using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meziantou.AspNetCore.Authentication.HttpBasic.Tests;

public sealed class HttpBasicAuthenticationTests
{
    [Fact]
    public async Task PlainTextPassword_IsAccepted()
    {
        await using var application = await TestApplication.CreateAsync("myName", "myPassword");
        await application.SendAndAssert("/", "myName", "myPassword", async response =>
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("myName", await response.Content.ReadAsStringAsync(XunitCancellationToken));
        });
    }

    [Fact]
    public async Task CustomCredentialValidator_IsUsed()
    {
        await using var application = await TestApplication.CreateAsync(options =>
        {
            options.ValidateCredentials = static (_, username, password) =>
            {
                var isValid = string.Equals(username, "custom", StringComparison.Ordinal) &&
                              string.Equals(password, "secret", StringComparison.Ordinal);
                return ValueTask.FromResult(isValid);
            };
        });

        await application.SendAndAssert("/", "custom", "secret", async response =>
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        });
    }

    [Fact]
    public async Task InvalidPassword_ReturnsUnauthorizedAndChallengeHeader()
    {
        await using var application = await TestApplication.CreateAsync(options =>
        {
            options.Realm = "My API";
            options.ValidateCredentials = (_, username, password) => ValueTask.FromResult((username, password) == ("myName", "myPassword"));
        });

        await application.SendAndAssert("/", "myName", "invalid", async response =>
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains(response.Headers.WwwAuthenticate, static value =>
            string.Equals(value.Scheme, HttpBasicAuthenticationDefaults.AuthenticationScheme, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(value.Parameter, "realm=\"My API\", charset=\"UTF-8\"", StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task InvalidPassword_ReturnsUnauthorizedAndChallengeHeader_NoRealm()
    {
        await using var application = await TestApplication.CreateAsync(options =>
        {
            options.Realm = null;
            options.ValidateCredentials = (_, username, password) => ValueTask.FromResult((username, password) == ("myName", "myPassword"));
        });

        await application.SendAndAssert("/", "myName", "invalid", async response =>
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains(response.Headers.WwwAuthenticate, static value =>
            string.Equals(value.Scheme, HttpBasicAuthenticationDefaults.AuthenticationScheme, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(value.Parameter, "charset=\"UTF-8\"", StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task CredentialPayloadAtLimit_IsAccepted()
    {
        await using var application = await TestApplication.CreateAsync(options =>
        {
            options.MaxCredentialLength = 4; // "a:b" => "YTpi"
            options.ValidateCredentials = (_, username, password) => ValueTask.FromResult((username, password) == ("a", "b"));
        });

        await application.SendAndAssert("/", "a", "b", async response =>
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("a", await response.Content.ReadAsStringAsync(XunitCancellationToken));
        });
    }

    [Fact]
    public async Task CredentialPayloadAboveLimit_IsRejected()
    {
        await using var application = await TestApplication.CreateAsync(options =>
        {
            options.MaxCredentialLength = 4;
            options.ValidateCredentials = (_, username, password) => ValueTask.FromResult((username, password) == ("myName", "myPassword"));
        });

        await application.SendAndAssert("/", "myName", "myPassword", async response =>
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        });
    }

    private sealed class TestApplication : IAsyncDisposable
    {
        private TestApplication(WebApplication app, HttpClient client)
        {
            App = app;
            Client = client;
        }

        public WebApplication App { get; }

        public HttpClient Client { get; }

        public static async Task<TestApplication> CreateAsync(string username, string password)
        {
            return await CreateAsync(options => options.ValidateCredentials = (_, u, p) => ValueTask.FromResult((username, password) == (u, p)));
        }

        public static async Task<TestApplication> CreateAsync(Action<HttpBasicAuthenticationOptions> configureOptions)
        {
            ArgumentNullException.ThrowIfNull(configureOptions);

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddAuthentication(HttpBasicAuthenticationDefaults.AuthenticationScheme)
                            .AddHttpBasic(configureOptions);
            builder.Services.AddAuthorization();

            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapGet("/", (ClaimsPrincipal user) => user.Identity?.Name ?? "anonymous")
                .RequireAuthorization();
            await app.StartAsync(XunitCancellationToken);

            var client = app.GetTestClient();
            return new TestApplication(app, client);
        }

        public Task SendAndAssert(string url, Func<HttpResponseMessage, Task> assert)
        {
            return SendAndAssert(url, null, null, assert);
        }

        public async Task SendAndAssert(string url, string username, string password, Func<HttpResponseMessage, Task> assert)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (username != null && password != null)
            {
                request.Headers.Authorization = CreateAuthorizationHeader(username, password);
            }

            using var response = await Client.SendAsync(request);
            await assert(response);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.DisposeAsync();
        }

        private static AuthenticationHeaderValue CreateAuthorizationHeader(string username, string password)
        {
            var value = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            return new AuthenticationHeaderValue(HttpBasicAuthenticationDefaults.AuthenticationScheme, value);
        }
    }
}
