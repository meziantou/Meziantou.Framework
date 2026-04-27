using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
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

    [Fact]
    public async Task AspNetCoreIdentity_IsUsedToValidateCredentials_AndCreatePrincipal()
    {
        var user = CreateIdentityUser(id: "user-id", username: "myName", password: "myPassword");
        await using var application = await TestApplication.CreateWithIdentityAsync([user], _ => { });

        await application.SendAndAssert("/", "myName", "myPassword", async response =>
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("myName|user-id", await response.Content.ReadAsStringAsync(XunitCancellationToken));
        });
    }

    [Fact]
    public async Task AspNetCoreIdentity_InvalidPassword_IsRejected()
    {
        var user = CreateIdentityUser(id: "user-id", username: "myName", password: "myPassword");
        await using var application = await TestApplication.CreateWithIdentityAsync([user], _ => { });

        await application.SendAndAssert("/", "myName", "invalid", async response =>
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        });
    }

    private static IdentityUser CreateIdentityUser(string id, string username, string password)
    {
        var user = new IdentityUser
        {
            Id = id,
            UserName = username,
            NormalizedUserName = username.ToUpperInvariant(),
        };

        var passwordHasher = new PasswordHasher<IdentityUser>();
        user.PasswordHash = passwordHasher.HashPassword(user, password);
        return user;
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

        public static async Task<TestApplication> CreateWithIdentityAsync(IReadOnlyCollection<IdentityUser> users, Action<HttpBasicAuthenticationOptions> configureOptions)
        {
            ArgumentNullException.ThrowIfNull(users);
            ArgumentNullException.ThrowIfNull(configureOptions);

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(sp => new InMemoryIdentityUserStore(users));
            builder.Services.AddSingleton<IUserStore<IdentityUser>>(sp => sp.GetRequiredService<InMemoryIdentityUserStore>());
            builder.Services.AddIdentityCore<IdentityUser>()
                            .AddSignInManager();
            builder.Services.AddAuthentication(HttpBasicAuthenticationDefaults.AuthenticationScheme)
                            .AddHttpBasicIdentity<IdentityUser>(configureOptions);
            builder.Services.AddAuthorization();

            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapGet("/", (ClaimsPrincipal user) => $"{user.Identity?.Name}|{user.FindFirstValue(ClaimTypes.NameIdentifier)}")
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

    private sealed class InMemoryIdentityUserStore : IUserPasswordStore<IdentityUser>
    {
        private readonly List<IdentityUser> _users;

        public InMemoryIdentityUserStore(IReadOnlyCollection<IdentityUser> users)
        {
            _users = [.. users];
        }

        public Task<IdentityResult> CreateAsync(IdentityUser user, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _users.Add(user);
            return Task.FromResult(IdentityResult.Success);
        }

        public Task<IdentityResult> DeleteAsync(IdentityUser user, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _users.Remove(user);
            return Task.FromResult(IdentityResult.Success);
        }

        public void Dispose()
        {
        }

        public Task<IdentityUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var user = _users.FirstOrDefault(x => string.Equals(x.Id, userId, StringComparison.Ordinal));
            return Task.FromResult(user);
        }

        public Task<IdentityUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var user = _users.FirstOrDefault(x => string.Equals(x.NormalizedUserName, normalizedUserName, StringComparison.Ordinal));
            return Task.FromResult(user);
        }

        public Task<string?> GetNormalizedUserNameAsync(IdentityUser user, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(user.NormalizedUserName);
        }

        public Task<string?> GetPasswordHashAsync(IdentityUser user, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(user.PasswordHash);
        }

        public Task<string> GetUserIdAsync(IdentityUser user, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(user.Id);
        }

        public Task<string?> GetUserNameAsync(IdentityUser user, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(user.UserName);
        }

        public Task<bool> HasPasswordAsync(IdentityUser user, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(user.PasswordHash is not null);
        }

        public Task SetNormalizedUserNameAsync(IdentityUser user, string? normalizedName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            user.NormalizedUserName = normalizedName;
            return Task.CompletedTask;
        }

        public Task SetPasswordHashAsync(IdentityUser user, string? passwordHash, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            user.PasswordHash = passwordHash;
            return Task.CompletedTask;
        }

        public Task SetUserNameAsync(IdentityUser user, string? userName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            user.UserName = userName;
            return Task.CompletedTask;
        }

        public Task<IdentityResult> UpdateAsync(IdentityUser user, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(IdentityResult.Success);
        }
    }
}
