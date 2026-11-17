using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MT_GRMS.Application.Common;

namespace MT_GRMS.Infrastructure.Persistence;

/// <summary>
/// Supplies EF tooling without relaxing the API's runtime configuration validation.
/// Database credentials must still be provided when an EF operation needs a live database.
/// </summary>
public sealed class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=mt_grms_design_time;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        return new AppDbContext(options, new TenantContext());
    }
}