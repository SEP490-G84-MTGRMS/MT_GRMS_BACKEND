using MT_GRMS.Application.Common;
using MT_GRMS.Application.Transfers;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Enums;
using Moq;

namespace MT_GRMS.UnitTests;

public class TransferAuthorizationTests
{
    [Fact]
    public async Task SaaSAdmin_CannotEnterTransferServiceEvenWithForgedTenantAndBranchContext()
    {
        var branchId = Guid.NewGuid();
        var transfer = new Transfer
        {
            TenantId = "tenant-1",
            FromBranchId = branchId,
            ToBranchId = Guid.NewGuid(),
            Status = TransferStatus.InTransit
        };
        var transfers = new Mock<IRepository<Transfer>>();
        transfers.Setup(x => x.GetByIdAsync(transfer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(transfer);
        var tenant = new TestTenantContext("ADMIN", branchId);
        var service = CreateService(transfers.Object, tenant);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.CancelTransferAsync(transfer.Id, new CancelTransferRequest("test"), CancellationToken.None));

        transfers.Verify(x => x.Update(It.IsAny<Transfer>()), Times.Never);
    }

    [Fact]
    public async Task LegacyEmployeeAlias_IsRejectedBeforeTransferStateIsEvaluated()
    {
        var branchId = Guid.NewGuid();
        var transfer = new Transfer
        {
            TenantId = "tenant-1",
            FromBranchId = branchId,
            ToBranchId = Guid.NewGuid(),
            Status = TransferStatus.InTransit
        };
        var transfers = new Mock<IRepository<Transfer>>();
        transfers.Setup(x => x.GetByIdAsync(transfer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(transfer);
        var details = new Mock<IRepository<TransferDetail>>();
        details.Setup(x => x.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<TransferDetail, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TransferDetail>());
        var branches = new Mock<IRepository<Branch>>();
        branches.Setup(x => x.GetByIdAsync(transfer.FromBranchId, It.IsAny<CancellationToken>())).ReturnsAsync(new Branch { Name = "From" });
        branches.Setup(x => x.GetByIdAsync(transfer.ToBranchId, It.IsAny<CancellationToken>())).ReturnsAsync(new Branch { Name = "To" });
        var unitOfWork = new Mock<IUnitOfWork>();
        var service = CreateService(transfers.Object, new TestTenantContext("EMPLOYEE", branchId), details.Object, branches.Object, unitOfWork.Object);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.CancelTransferAsync(transfer.Id, new CancelTransferRequest("approved by sending branch"), CancellationToken.None));
        unitOfWork.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("CASHIER")]
    [InlineData("BRANCH_MANAGER")]
    [InlineData("EMPLOYEE")]
    [InlineData("OWNER")]
    [InlineData("StoreOwner")]
    public async Task LegacyRoleAliases_CannotListTransfers(string role)
    {
        var transfers = new Mock<IRepository<Transfer>>();
        var service = CreateService(transfers.Object, new TestTenantContext(role, Guid.NewGuid()));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetTransfersAsync(
            new TransferFilterParams(null, null, null, null, null, null), CancellationToken.None));
        transfers.Verify(x => x.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Staff_CannotCreateDirectTransfer()
    {
        var branchId = Guid.NewGuid();
        var service = CreateService(Mock.Of<IRepository<Transfer>>(), new TestTenantContext("STAFF", branchId));
        var request = new CreateTransferRequest(
            Guid.NewGuid(), branchId, false, null, null,
            [new TransferLineRequest(Guid.NewGuid(), 1, "Cái", null, null, 0)],
            TransferOrigin.StaffUrgentRequest);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.CreateTransferAsync(request, CancellationToken.None));
    }

    private static TransferService CreateService(
        IRepository<Transfer> transfers,
        ITenantContext tenant,
        IRepository<TransferDetail>? details = null,
        IRepository<Branch>? branches = null,
        IUnitOfWork? unitOfWork = null)
        => new(
            transfers,
            details ?? Mock.Of<IRepository<TransferDetail>>(),
            Mock.Of<IRepository<BranchStock>>(),
            Mock.Of<IRepository<Product>>(),
            branches ?? Mock.Of<IRepository<Branch>>(),
            Mock.Of<IRepository<TransferStatusHistory>>(),
            tenant,
            unitOfWork ?? Mock.Of<IUnitOfWork>());

    private sealed class TestTenantContext(string role, Guid branchId) : ITenantContext
    {
        public string CurrentTenantId => "tenant-1";
        public Guid CurrentUserId => Guid.NewGuid();
        public Guid? CurrentBranchId => branchId;
        public string CurrentRole => role;
        public void SetContext(string tenantId, Guid userId, Guid? currentBranchId) { }
        public void SetRole(string? currentRole) { }
    }
}
