using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Api.Controllers;

namespace MT_GRMS.UnitTests;

public class FinanceControllerAuthorizationTests
{
    [Fact]
    public void ShiftEndpoints_RequireAuthenticatedUser()
    {
        Assert.NotNull(typeof(ShiftsController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(typeof(ShiftsController).GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public void PaymentEndpoints_RequireAuthentication_ExceptWebhook()
    {
        Assert.NotNull(typeof(PaymentsController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(typeof(PaymentsController).GetCustomAttribute<AllowAnonymousAttribute>());

        var webhook = typeof(PaymentsController).GetMethod(nameof(PaymentsController.HandleSePayWebhook));
        Assert.NotNull(webhook);
        Assert.NotNull(webhook!.GetCustomAttribute<AllowAnonymousAttribute>());

        var configRead = typeof(PaymentsController).GetMethod(nameof(PaymentsController.GetPaymentConfig));
        var configWrite = typeof(PaymentsController).GetMethod(nameof(PaymentsController.SavePaymentConfig));
        Assert.NotNull(configRead);
        Assert.NotNull(configWrite);
        Assert.Null(configRead!.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(configWrite!.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public void PaymentConfigActions_ContainOwnerOrAdminRoleGuard()
    {
        var source = File.ReadAllText(GetControllerSourcePath());

        Assert.Contains("if (!IsStoreOwnerOrAdmin())", source, StringComparison.Ordinal);
        Assert.Contains("private bool IsStoreOwnerOrAdmin()", source, StringComparison.Ordinal);
        Assert.Contains("User.IsInRole(\"STORE_OWNER\")", source, StringComparison.Ordinal);
        Assert.Contains("User.IsInRole(\"ADMIN\")", source, StringComparison.Ordinal);
    }

    private static string GetControllerSourcePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "MT_GRMS.sln")))
        {
            directory = directory.Parent;
        }

        // Unit test execution may run from bin/Debug where the solution is not copied.
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "MT_GRMS.Api", "Controllers", "PaymentsController.cs"),
            Path.Combine(Environment.CurrentDirectory, "src", "MT_GRMS.Api", "Controllers", "PaymentsController.cs")
        };

        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("Could not locate PaymentsController.cs for authorization guard test.");
    }
}