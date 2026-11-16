using Moq;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Transfers;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.UnitTests;

public sealed class TransferRequestAuthorizationTests
{
    [Theory]
    [InlineData("ADMIN")]
    [InlineData("SYSTEMADMIN")]
    [InlineData("EMPLOYEE")]
    [InlineData("CASHIER")]
    [InlineData("BRANCH_MANAGER")]
    [InlineData("OWNER")]
    public async Task NonCanonicalOrPlatformRole_CannotPreviewAllocation(string role)
    {
        var branchId = Guid.NewGuid();
        var service = CreateService(new TestTenantContext("tenant-a", role, branchId));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.PreviewAllocationAsync(Request(branchId), CancellationToken.None));
    }

    [Fact]
    public async Task Staff_CannotRequestForAnotherBranch()
    {
        var service = CreateService(new TestTenantContext("tenant-a", "STAFF", Guid.NewGuid()));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.PreviewAllocationAsync(Request(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task OwnerWithoutTenant_CannotPreviewAllocation()
    {
        var service = CreateService(new TestTenantContext(string.Empty, "STORE_OWNER", null));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.PreviewAllocationAsync(Request(Guid.NewGuid()), CancellationToken.None));
    }

    private static CreateTransferRequestGroupRequest Request(Guid destinationBranchId) => new(
        destinationBranchId, null, null, null,
        [new TransferDemandLineRequest(Guid.NewGuid(), 1, "Cái")]);

    private static TransferRequestService CreateService(ITenantContext tenant) => new(
        Mock.Of<IRepository<TransferRequest>>(),
        Mock.Of<IRepository<TransferRequestDetail>>(),
        Mock.Of<IRepository<Transfer>>(),
        Mock.Of<IRepository<TransferDetail>>(),
        Mock.Of<IRepository<TransferStatusHistory>>(),
        Mock.Of<IRepository<BranchStock>>(),
        Mock.Of<IRepository<Product>>(),
        Mock.Of<IRepository<Branch>>(),
        Mock.Of<ITransferService>(),
        tenant,
        Mock.Of<IUnitOfWork>());

    private sealed class TestTenantContext(string tenantId, string role, Guid? branchId) : ITenantContext
    {
        public string CurrentTenantId => tenantId;
        public Guid CurrentUserId => Guid.NewGuid();
        public Guid? CurrentBranchId => branchId;
        public string CurrentRole => role;
        public void SetContext(string value, Guid userId, Guid? currentBranchId) { }
        public void SetRole(string? currentRole) { }
    }
}
