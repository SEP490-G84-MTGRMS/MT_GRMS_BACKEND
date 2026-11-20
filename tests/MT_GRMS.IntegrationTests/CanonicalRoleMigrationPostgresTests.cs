using Npgsql;
using Xunit;

namespace MT_GRMS.IntegrationTests;

public sealed class CanonicalRolePostgresFactAttribute : FactAttribute
{
    public CanonicalRolePostgresFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_POSTGRES_RBAC_AUDIT"), "1", StringComparison.Ordinal))
        {
            Skip = "Set RUN_POSTGRES_RBAC_AUDIT=1 to audit the configured PostgreSQL database.";
        }
    }
}

public sealed class CanonicalRoleMigrationPostgresTests
{
    [CanonicalRolePostgresFact]
    public async Task ProductDevDatabase_HasOnlyCanonicalTenantRoles_AndNoAdminMembership()
    {
        var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
            ?? throw new InvalidOperationException("DB_CONNECTION_STRING is required for the RBAC audit.");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                COUNT(*) FILTER (WHERE tu."Role" NOT IN ('STORE_OWNER', 'STAFF')) AS invalid_roles,
                COUNT(*) FILTER (WHERE u."IsAdmin" = TRUE) AS admin_memberships,
                COUNT(*) FILTER (WHERE tu."Role" = 'STORE_OWNER') AS owner_memberships,
                COUNT(*) FILTER (WHERE tu."Role" = 'STAFF') AS staff_memberships,
                EXISTS (
                    SELECT 1
                    FROM pg_constraint
                    WHERE conname = 'CK_TenantUsers_Role_Canonical'
                      AND conrelid = '"TenantUsers"'::regclass
                ) AS has_canonical_constraint
            FROM "TenantUsers" tu
            INNER JOIN "Users" u ON u."UserID" = tu."UserID";
            """;

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        var invalidRoles = reader.GetInt64(0);
        var adminMemberships = reader.GetInt64(1);
        var ownerMemberships = reader.GetInt64(2);
        var staffMemberships = reader.GetInt64(3);
        var hasCanonicalConstraint = reader.GetBoolean(4);

        Assert.Equal(0, invalidRoles);
        Assert.Equal(0, adminMemberships);
        Assert.True(ownerMemberships > 0);
        Assert.True(staffMemberships > 0);
        Assert.True(hasCanonicalConstraint);
    }
}
