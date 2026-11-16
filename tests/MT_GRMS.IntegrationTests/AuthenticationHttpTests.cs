using System.IdentityModel.Tokens.Jwt;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using MT_GRMS.Api.Authorization;
using MT_GRMS.Api.Middlewares;
using MT_GRMS.API.Controllers;
using MT_GRMS.Application.Auth;
using MT_GRMS.Application.Auth.Dtos;
using System.Net.Http.Json;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.IntegrationTests;

// Real loopback HTTP + production JWT registration/middleware. Identity storage is isolated in memory.
public class AuthenticationHttpTests : IAsyncLifetime
{
    private const string Key = "HttpAuthenticationTestKey_0123456789_ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly IdentityRepository _repo = new();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = Key });
        builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);
        var databaseName = "auth-http-" + Guid.NewGuid().ToString("N");
        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        builder.Services.AddSingleton<IUserRepository>(_repo);
        builder.Services.AddScoped<AuthService>();
        builder.Services.AddScoped<IAuthService>(services => services.GetRequiredService<AuthService>());
        builder.Services.AddScoped<ITenantContext, TenantContext>();
        builder.Services.AddSessionAuthentication(builder.Configuration);
        builder.Services.AddAuthorization(options => options.AddPolicy("TenantOwner", p => p.RequireRole("STORE_OWNER")));
        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseMiddleware<MultiTenantMiddleware>();
        _app.UseMiddleware<SubscriptionAccessMiddleware>();
        _app.UseAuthorization();
        _app.MapGet("/context", (ITenantContext context) => Results.Text(context.CurrentTenantId + "|" + context.CurrentRole)).RequireAuthorization();
        _app.MapGet("/owner", () => Results.Ok()).RequireAuthorization("TenantOwner");
        _app.MapPost("/login", async (LoginRequest request, AuthService auth) => await auth.LoginAsync(request));
        _app.MapPost("/refresh", async (RefreshTokenRequest request, AuthService auth) => await auth.RefreshTokenAsync(request));
        _app.MapControllers();
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Tenants.Add(new Tenant
            {
                Id = "tenant-a",
                Name = "Authentication Test Tenant",
                Subdomain = "auth-test",
                Code = "AUTH",
                Status = "ACTIVE",
                SubscriptionStatus = "ACTIVE",
                SubscriptionExpiresAt = DateTime.UtcNow.AddDays(1)
            });
            await db.SaveChangesAsync();
        }
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
    }

    public async Task DisposeAsync() { _client.Dispose(); await _app.StopAsync(); await _app.DisposeAsync(); }

    private string Token(string role = "STORE_OWNER", string? stamp = "stamp", string? tenant = "tenant-a", Guid? branch = null, string? key = null, bool expired = false, string? extraRole = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "user"), new(ClaimTypes.Role, role) };
        if (stamp != null) claims.Add(new("security_stamp", stamp));
        if (tenant != null) claims.Add(new("tenant_id", tenant));
        if (branch != null) claims.Add(new("branch_id", branch.ToString()!));
        if (extraRole != null) claims.Add(new("role", extraRole));
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("MTGRMS_API", "MTGRMS_CLIENT", claims,
            expires: expired ? DateTime.UtcNow.AddMinutes(-5) : DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key ?? Key)), SecurityAlgorithms.HmacSha256)));
    }

    private async Task<HttpResponseMessage> Send(string? token, string path = "/context", string? headerTenant = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (headerTenant != null) request.Headers.Add("X-Tenant-Id", headerTenant);
        return await _client.SendAsync(request);
    }

    private static string Cookie(HttpResponseMessage response, string name)
    {
        var prefix = name + "=";
        var header = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(prefix, StringComparison.Ordinal));
        return header.Split(';', 2)[0];
    }

    [Fact]
    public async Task AuthController_CookieFlow_WorksThroughApplicationMiddlewares()
    {
        using var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest("owner", "StrongPassword123!", "tenant-a"));
        login.EnsureSuccessStatusCode();
        var accessCookie = Cookie(login, "access_token");
        var refreshCookie = Cookie(login, "refresh_token");

        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meRequest.Headers.Add("Cookie", accessCookie);
        using var me = await _client.SendAsync(meRequest);
        me.EnsureSuccessStatusCode();

        using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh")
        {
            Content = JsonContent.Create<object?>(null)
        };
        refreshRequest.Headers.Add("Cookie", refreshCookie);
        using var refresh = await _client.SendAsync(refreshRequest);
        refresh.EnsureSuccessStatusCode();
        var refreshedAccessCookie = Cookie(refresh, "access_token");
        Assert.NotEqual(refreshCookie, Cookie(refresh, "refresh_token"));

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutRequest.Headers.Add("Cookie", refreshedAccessCookie);
        using var logout = await _client.SendAsync(logoutRequest);
        logout.EnsureSuccessStatusCode();
        Assert.Null(_repo.User.RefreshToken);
        Assert.Null(_repo.User.RefreshTokenExpiryTime);
    }

    [Fact]
    public async Task LoginAndRefresh_TokensWorkThroughJwtPipeline()
    {
        using var loginResponse = await _client.PostAsJsonAsync("/login", new LoginRequest("owner", "StrongPassword123!", "tenant-a"));
        loginResponse.EnsureSuccessStatusCode();
        var login = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!;
        using var authorized = await Send(login.AccessToken, "/owner");
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        using var refreshResponse = await _client.PostAsJsonAsync("/refresh", new RefreshTokenRequest(login.RefreshToken));
        refreshResponse.EnsureSuccessStatusCode();
        var refresh = (await refreshResponse.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.NotEqual(login.RefreshToken, refresh.RefreshToken);
        using var refreshed = await Send(refresh.AccessToken, "/owner");
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
    }

    [Fact]
    public async Task OwnerContext_IgnoresSpoofedHeader()
    {
        using var response = await Send(Token(), headerTenant: "tenant-b");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("tenant-a|STORE_OWNER", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("anonymous")]
    [InlineData("missing-stamp")]
    [InlineData("old-stamp")]
    [InlineData("signature")]
    [InlineData("expired")]
    [InlineData("tenant")]
    [InlineData("alias")]
    [InlineData("conflicting-role")]
    [InlineData("inactive")]
    [InlineData("removed-membership")]
    public async Task InvalidSession_Returns401(string scenario)
    {
        var token = scenario switch
        {
            "anonymous" => null,
            "missing-stamp" => Token(stamp: null),
            "old-stamp" => Token(stamp: "old"),
            "signature" => Token(key: Key + "wrong"),
            "expired" => Token(expired: true),
            "tenant" => Token(tenant: "tenant-b"),
            "alias" => Token(role: "OWNER"),
            "conflicting-role" => Token(extraRole: "ADMIN"),
            _ => Token()
        };
        if (scenario == "inactive") _repo.User.IsActive = false;
        if (scenario == "removed-membership") _repo.User.TenantUsers.Clear();
        using var response = await Send(token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_HasNoTenant_AndCannotCallOwnerEndpoint()
    {
        _repo.User.IsAdmin = true;
        var token = Token(role: "ADMIN", tenant: null);
        using var context = await Send(token, headerTenant: "tenant-b");
        Assert.Equal("|ADMIN", await context.Content.ReadAsStringAsync());
        using var forbidden = await Send(token, "/owner");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var tenantToken = await Send(Token(role: "ADMIN"));
        Assert.Equal(HttpStatusCode.Unauthorized, tenantToken.StatusCode);
    }

    [Fact]
    public async Task StaffAssignmentChange_RejectsOldTokenWithoutRelyingOnStampRotation()
    {
        _repo.User.TenantUsers.Single().Role = "STAFF";
        var token = Token(role: "STAFF", branch: _repo.Branch);
        using var valid = await Send(token);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        _repo.Branch = Guid.NewGuid();
        using var stale = await Send(token);
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
    }

    [Fact]
    public async Task RoleChangeAndStampRotation_RejectExistingAccessToken()
    {
        var token = Token();
        _repo.User.SecurityStamp = "rotated";
        using var revoked = await Send(token);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        _repo.User.SecurityStamp = "stamp";
        _repo.User.TenantUsers.Single().Role = "STAFF";
        using var demoted = await Send(token);
        Assert.Equal(HttpStatusCode.Unauthorized, demoted.StatusCode);
    }

    private sealed class IdentityRepository : IUserRepository
    {
        public User User { get; } = new() { Id = "user", Username = "owner", Password = "StrongPassword123!", IsTwoFactorEnabled = false, IsActive = true, SecurityStamp = "stamp", TenantUsers = new List<TenantUser> { new() { TenantId = "tenant-a", UserId = "user", Role = "STORE_OWNER" } } };
        public Guid Branch { get; set; } = Guid.NewGuid();
        public Task<User?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(id == User.Id ? User : null);
        public Task<Guid?> GetEmployeeBranchIdAsync(string userId, string tenantId, CancellationToken cancellationToken = default) => Task.FromResult<Guid?>(Branch);
        public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default) => Task.FromResult(username == User.Username ? User : null);
        public Task<IReadOnlyList<User>> FindAsync(Expression<Func<User, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<User>>(new[] { User }.Where(predicate.Compile()).ToList());
        public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddAsync(User user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Update(User user) { }
        public void Delete(User user) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ExistsByPhoneOrEmailAsync(string phone, string? email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User> RegisterStoreOwnerAsync(User user, Tenant tenant, Branch branch, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
