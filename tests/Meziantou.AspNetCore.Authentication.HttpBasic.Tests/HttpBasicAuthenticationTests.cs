using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

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
            options.ValidateCredentials = (_, username, password) => ValidateCredentials("custom", "secret", username, password);
        });

        await application.SendAndAssert("/", "custom", "secret", response =>
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
            options.ValidateCredentials = (_, username, password) => ValidateCredentials("myName", "myPassword", username, password);
        });

        await application.SendAndAssert("/", "myName", "invalid", response =>
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains(response.Headers.WwwAuthenticate, value =>
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
            options.ValidateCredentials = (_, username, password) => ValidateCredentials("myName", "myPassword", username, password);
        });

        await application.SendAndAssert("/", "myName", "invalid", response =>
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains(response.Headers.WwwAuthenticate, value =>
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
            options.ValidateCredentials = (_, username, password) => ValidateCredentials("a", "b", username, password);
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
            options.ValidateCredentials = (_, _, _) => throw new InvalidOperationException("The validator must not run once the length limit is exceeded");
        });

        await application.SendAndAssert("/", "myName", "myPassword", response =>
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

        await application.SendAndAssert("/", "myName", "invalid", response =>
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        });
    }

    [Fact]
    public async Task MalformedUtf8Credentials_AreRejected()
    {
        await using var application = await TestApplication.CreateAsync(options =>
        {
            options.ValidateCredentials = (_, username, password) => ValidateCredentials("victim", "\uFFFD\uFFFD\uFFFD", username, password);
        });

        byte[] credentials = [.. "victim:"u8, 0xFF, 0xFE, 0xC0];
        await application.SendRawAndAssert("/", credentials, response =>
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        });
    }

    [Fact]
    public async Task MissingAuthorizationHeader_IsChallenged()
    {
        await using var application = await TestApplication.CreateAsync("myName", "myPassword");
        await application.SendRawHeaderAndAssert("/", authorizationHeader: null, response =>
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains(response.Headers.WwwAuthenticate, value =>
                string.Equals(value.Scheme, HttpBasicAuthenticationDefaults.AuthenticationScheme, StringComparison.OrdinalIgnoreCase));
        });
    }

    [Theory]
    [InlineData("Bearer some-token")]
    [InlineData("Negotiate some-token")]
    [InlineData("not-a-valid-header-value")]
    public async Task NonBasicAuthorizationHeader_IsIgnoredAndChallenged(string authorizationHeader)
    {
        await using var application = await TestApplication.CreateAsync("myName", "myPassword");
        await application.SendRawHeaderAndAssert("/", authorizationHeader, response =>
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        });
    }

    [Theory]
    [InlineData("Basic")]
    [InlineData("Basic ")]
    [InlineData("Basic !!!not-base64!!!")]
    [InlineData("Basic YWJj=====")]
    public async Task MalformedCredentialPayload_IsRejected(string authorizationHeader)
    {
        await using var application = await TestApplication.CreateAsync("myName", "myPassword");
        await application.SendRawHeaderAndAssert("/", authorizationHeader, response =>
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        });
    }

    [Fact]
    public async Task CredentialsWithoutSeparator_AreRejected()
    {
        await using var application = await TestApplication.CreateAsync(options =>
        {
            options.ValidateCredentials = (_, _, _) => throw new InvalidOperationException("The validator must not run for malformed credentials");
        });

        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("no-separator-here"));
        await application.SendRawHeaderAndAssert("/", "Basic " + payload, response =>
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        });
    }

    [Fact]
    public async Task MalformedUtf8Credentials_DoNotCollideOnReplacementCharacter()
    {
        await using var application = await TestApplication.CreateAsync(options =>
        {
            options.ValidateCredentials = (_, username, password) => ValidateCredentials("victim", "\uFFFD\uFFFD\uFFFD", username, password);
        });

        byte[] credentials = [.. "victim:"u8, 0x80, 0x81, 0x82];
        await application.SendRawAndAssert("/", credentials, response =>
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        });
    }

    [Fact]
    public async Task NonAsciiUtf8Credentials_AreAccepted()
    {
        await using var application = await TestApplication.CreateAsync("\u00DCn\u00EFc\u00F8de", "p\u00E4ssw\u00F6rd");
        await application.SendAndAssert("/", "\u00DCn\u00EFc\u00F8de", "p\u00E4ssw\u00F6rd", async response =>
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("\u00DCn\u00EFc\u00F8de", await response.Content.ReadAsStringAsync(XunitCancellationToken));
        });
    }

    [Fact]
    public async Task LargeCredentials_UseThePooledBuffer()
    {
        var username = new string('u', 500);
        var password = new string('p', 500);
        await using var application = await TestApplication.CreateAsync(username, password);

        await application.SendAndAssert("/", username, password, async response =>
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(username, await response.Content.ReadAsStringAsync(XunitCancellationToken));
        });
    }

    [Fact]
    public async Task Challenge_PreservesChallengeWrittenByAnotherScheme()
    {
        await using var application = await TestApplication.CreateWithSecondSchemeAsync(options =>
        {
            options.Realm = "My API";
            options.ValidateCredentials = (_, username, password) => ValidateCredentials("myName", "myPassword", username, password);
        });

        await application.SendAndAssert("/", username: null, password: null, response =>
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var challenges = response.Headers.WwwAuthenticate.Select(value => $"{value.Scheme} {value.Parameter}").ToArray();
            Assert.Equal(["Bearer realm=\"api\"", "Basic realm=\"My API\", charset=\"UTF-8\""], challenges);
        });
    }

    [Fact]
    public async Task CustomAuthenticationSchemeName_StillReadsTheBasicHeader()
    {
        await using var application = await TestApplication.CreateAsync(
            options => options.ValidateCredentials = (_, username, password) => ValidateCredentials("myName", "myPassword", username, password),
            authenticationScheme: "MyBasicScheme");

        await application.SendAndAssert("/", "myName", "myPassword", async response =>
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("myName", await response.Content.ReadAsStringAsync(XunitCancellationToken));
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void MaxCredentialLength_RejectsNonPositiveValues(int value)
    {
        var options = new HttpBasicAuthenticationOptions();
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => options.MaxCredentialLength = value);
        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void MaxCredentialLength_DefaultsToTheDocumentedValue()
    {
        var options = new HttpBasicAuthenticationOptions();
        Assert.Equal(HttpBasicAuthenticationOptions.DefaultMaxCredentialLength, options.MaxCredentialLength);
    }

    [Fact]
    public async Task AspNetCoreIdentity_LockoutOnFailure_RecordsFailedAttempts()
    {
        var user = CreateIdentityUser(id: "user-id", username: "myName", password: "myPassword");
        user.LockoutEnabled = true;
        await using var application = await TestApplication.CreateWithIdentityAsync([user], _ => { }, lockoutOnFailure: true);

        await application.SendAndAssert("/", "myName", "invalid", response =>
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        });

        Assert.Equal(1, user.AccessFailedCount);
    }

    [Fact]
    public async Task AspNetCoreIdentity_WithoutLockoutOnFailure_DoesNotRecordFailedAttempts()
    {
        var user = CreateIdentityUser(id: "user-id", username: "myName", password: "myPassword");
        user.LockoutEnabled = true;
        await using var application = await TestApplication.CreateWithIdentityAsync([user], _ => { }, lockoutOnFailure: false);

        await application.SendAndAssert("/", "myName", "invalid", response =>
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        });

        Assert.Equal(0, user.AccessFailedCount);
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

    private static ValueTask<ClaimsPrincipal?> ValidateCredentials(string expectedUsername, string expectedPassword, string username, string password)
    {
        if ((username, password) != (expectedUsername, expectedPassword))
            return ValueTask.FromResult<ClaimsPrincipal?>(null);

        return ValueTask.FromResult<ClaimsPrincipal?>(CreatePrincipal(username));
    }

    private static ClaimsPrincipal CreatePrincipal(string username)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.NameIdentifier, username),
        };

        var identity = new ClaimsIdentity(claims, authenticationType: HttpBasicAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }

    private sealed class FakeBearerAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public FakeBearerAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            Response.Headers[HeaderNames.WWWAuthenticate] = StringValues.Concat(Response.Headers[HeaderNames.WWWAuthenticate], "Bearer realm=\"api\"");
            return Task.CompletedTask;
        }
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
            return await CreateAsync(options => options.ValidateCredentials = (_, u, p) => ValidateCredentials(username, password, u, p));
        }

        public static async Task<TestApplication> CreateAsync(Action<HttpBasicAuthenticationOptions> configureOptions, string authenticationScheme = HttpBasicAuthenticationDefaults.AuthenticationScheme)
        {
            ArgumentNullException.ThrowIfNull(configureOptions);

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddAuthentication(authenticationScheme)
                            .AddHttpBasic(authenticationScheme, configureOptions);
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

        public static async Task<TestApplication> CreateWithIdentityAsync(IReadOnlyCollection<IdentityUser> users, Action<HttpBasicAuthenticationOptions> configureOptions, bool lockoutOnFailure = false)
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
                            .AddHttpBasicIdentity<IdentityUser>(configureOptions, lockoutOnFailure);
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

        public static async Task<TestApplication> CreateWithSecondSchemeAsync(Action<HttpBasicAuthenticationOptions> configureOptions)
        {
            ArgumentNullException.ThrowIfNull(configureOptions);

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddAuthentication("Bearer")
                            .AddScheme<AuthenticationSchemeOptions, FakeBearerAuthenticationHandler>("Bearer", displayName: null, _ => { })
                            .AddHttpBasic(HttpBasicAuthenticationDefaults.AuthenticationScheme, configureOptions);
            builder.Services.AddAuthorization(options =>
            {
                options.DefaultPolicy = new AuthorizationPolicyBuilder("Bearer", HttpBasicAuthenticationDefaults.AuthenticationScheme)
                    .RequireAuthenticatedUser()
                    .Build();
            });

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

        public async Task SendAndAssert(string url, string? username, string? password, Func<HttpResponseMessage, Task> assert)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (username is not null && password is not null)
            {
                request.Headers.Authorization = CreateAuthorizationHeader(username, password);
            }

            using var response = await Client.SendAsync(request);
            await assert(response);
        }

        public async Task SendAndAssert(string url, string? username, string? password, Action<HttpResponseMessage> assert)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (username is not null && password is not null)
            {
                request.Headers.Authorization = CreateAuthorizationHeader(username, password);
            }

            using var response = await Client.SendAsync(request);
            assert(response);
        }

        public async Task SendRawAndAssert(string url, byte[] credentials, Action<HttpResponseMessage> assert)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Authorization", HttpBasicAuthenticationDefaults.AuthenticationScheme + " " + Convert.ToBase64String(credentials));

            using var response = await Client.SendAsync(request);
            assert(response);
        }

        public async Task SendRawHeaderAndAssert(string url, string? authorizationHeader, Action<HttpResponseMessage> assert)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (authorizationHeader is not null)
            {
                request.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);
            }

            using var response = await Client.SendAsync(request);
            assert(response);
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

#nullable enable
    private sealed class InMemoryIdentityUserStore : IUserPasswordStore<IdentityUser>, IUserLockoutStore<IdentityUser>
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

        public Task<int> GetAccessFailedCountAsync(IdentityUser user, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(user.AccessFailedCount);
        }

        public Task<bool> GetLockoutEnabledAsync(IdentityUser user, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(user.LockoutEnabled);
        }

        public Task<DateTimeOffset?> GetLockoutEndDateAsync(IdentityUser user, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(user.LockoutEnd);
        }

        public Task<int> IncrementAccessFailedCountAsync(IdentityUser user, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(++user.AccessFailedCount);
        }

        public Task ResetAccessFailedCountAsync(IdentityUser user, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            user.AccessFailedCount = 0;
            return Task.CompletedTask;
        }

        public Task SetLockoutEnabledAsync(IdentityUser user, bool enabled, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            user.LockoutEnabled = enabled;
            return Task.CompletedTask;
        }

        public Task SetLockoutEndDateAsync(IdentityUser user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            user.LockoutEnd = lockoutEnd;
            return Task.CompletedTask;
        }

        public Task<IdentityResult> UpdateAsync(IdentityUser user, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(IdentityResult.Success);
        }
    }
}
