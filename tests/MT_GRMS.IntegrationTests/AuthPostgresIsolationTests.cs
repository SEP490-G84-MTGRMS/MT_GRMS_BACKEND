using Microsoft.EntityFrameworkCore;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Repositories;
using Npgsql;

namespace MT_GRMS.IntegrationTests;

public sealed class AuthPostgresFactAttribute : FactAttribute
{
    public AuthPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RBAC_POSTGRES_TEST_CONNECTION")))
            Skip = "Requires explicit RBAC_POSTGRES_TEST_CONNECTION; no fallback database.";
    }
}

// Copies table definitions only. All writes are confined to a generated schema, removed in DisposeAsync.
public class AuthPostgresIsolationTests : IAsyncLifetime
{
    private readonly string _schema = "rbac_auth_test_" + Guid.NewGuid().ToString("N");
    private string? _adminConnection;
    private string _testConnection = "";

    public async Task InitializeAsync()
    {
        _adminConnection = Environment.GetEnvironmentVariable("RBAC_POSTGRES_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(_adminConnection)) return;
        var builder = new NpgsqlConnectionStringBuilder(_adminConnection) { SearchPath = _schema, Pooling = false, IncludeErrorDetail = false };
        _testConnection = builder.ConnectionString;
        await using var conn = new NpgsqlConnection(_adminConnection);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using var create = new NpgsqlCommand($"CREATE SCHEMA \"{_schema}\"", conn, tx);
        await create.ExecuteNonQueryAsync();
        foreach (var table in new[] { "Users", "TenantUsers", "Employees", "Branches", "Tenants" })
        {
            // Foreign keys copied from public would still target public tables. Keep the real
            // column/default/index shape while isolating every test row from shared data.
            await using var cmd = new NpgsqlCommand($"CREATE TABLE \"{_schema}\".\"{table}\" (LIKE public.\"{table}\" INCLUDING DEFAULTS INCLUDING INDEXES)", conn, tx);
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(_adminConnection)) return;
        // The identifier is generated locally, never supplied by caller or read from the database.
        if (!_schema.StartsWith("rbac_auth_test_", StringComparison.Ordinal) || _schema.Length != 47)
            throw new InvalidOperationException("Invalid isolated schema name.");
        await using var conn = new NpgsqlConnection(_adminConnection);
        await conn.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE", conn);
        await drop.ExecuteNonQueryAsync();
    }

    private AppDbContext Db()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_testConnection).Options);
        db.Database.OpenConnection();
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SET search_path TO \"{_schema}\"";
        command.ExecuteNonQuery();
        command.CommandText = "SELECT current_schema()";
        var actualSchema = command.ExecuteScalar() as string;
        if (!string.Equals(actualSchema, _schema, StringComparison.Ordinal))
        {
            db.Dispose();
            throw new InvalidOperationException("PostgreSQL test connection did not select the isolated schema.");
        }
        return db;
    }

    [AuthPostgresFact]
    public async Task StaffBranchLookup_FiltersForeignAndInactiveBranch_OnPostgres()
    {
        await using var db = Db();
        var branch = new Branch { TenantId = "tenant-a", Name = "Test", BranchCode = "TEST" };
        var employee = new Employee { TenantId = "tenant-a", UserId = "staff", BranchId = branch.Id, FullName = "Test" };
        db.Branches.Add(branch);
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        var repo = new UserRepository(db);
        Assert.Equal(branch.Id, await repo.GetEmployeeBranchIdAsync("staff", "tenant-a"));
        branch.TenantId = "tenant-b";
        await db.SaveChangesAsync();
        Assert.Null(await repo.GetEmployeeBranchIdAsync("staff", "tenant-a"));
        branch.TenantId = "tenant-a";
        branch.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Null(await repo.GetEmployeeBranchIdAsync("staff", "tenant-a"));
    }

    [AuthPostgresFact]
    public async Task ConcurrentRefreshWrites_OnlyOneWins_OnPostgres()
    {
        await using (var seed = Db())
        {
            seed.Users.Add(new User { Id = "race-user", UserName = "race", Email = "race@test.invalid", NormalizedEmail = "race@test.invalid", SecurityStamp = "stamp", RefreshToken = "old", RefreshTokenExpiryTime = DateTime.UtcNow.AddHours(1) });
            await seed.SaveChangesAsync();
        }
        await using var first = Db();
        await using var second = Db();
        var a = new UserRepository(first);
        var b = new UserRepository(second);
        var u1 = (await a.GetByIdAsync("race-user"))!;
        var u2 = (await b.GetByIdAsync("race-user"))!;
        u1.RefreshToken = "first";
        u2.RefreshToken = "second";
        async Task<bool> Save(UserRepository repo)
        {
            try { await repo.SaveChangesAsync(); return true; }
            catch (UnauthorizedAccessException) { return false; }
        }
        var outcomes = await Task.WhenAll(Save(a), Save(b));
        Assert.Single(outcomes.Where(x => x));
    }

    [AuthPostgresFact]
    public async Task RoleChange_RevokesRefreshAndStamp_InDatabase()
    {
        await using var db = Db();
        var user = new User { Id = "revoke-user", UserName = "revoke", Email = "revoke@test.invalid", NormalizedEmail = "revoke@test.invalid", SecurityStamp = "old", RefreshToken = "refresh", RefreshTokenExpiryTime = DateTime.UtcNow.AddHours(1) };
        var membership = new TenantUser { UserId = user.Id, TenantId = "tenant-a", Role = "STORE_OWNER" };
        db.Users.Add(user);
        db.TenantUsers.Add(membership);
        await db.SaveChangesAsync();
        membership.Role = "STAFF";
        await db.SaveChangesAsync();
        await using var verify = Db();
        var stored = (await new UserRepository(verify).GetByIdAsync(user.Id))!;
        Assert.NotEqual("old", stored.SecurityStamp);
        Assert.Null(stored.RefreshToken);
        Assert.Null(stored.RefreshTokenExpiryTime);
    }
}
