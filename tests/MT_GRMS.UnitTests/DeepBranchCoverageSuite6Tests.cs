using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Auth;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.Interfaces;
using MT_GRMS.Application.GoodsReceipts.Dtos;
using MT_GRMS.Application.HR.Employees;
using MT_GRMS.Application.HR.Employees.Dtos;
using MT_GRMS.Application.Promotions;
using MT_GRMS.Application.Promotions.Dtos;
using MT_GRMS.Application.PurchaseOrders;
using MT_GRMS.Application.PurchaseOrders.Dispatch;
using MT_GRMS.Application.PurchaseOrders.Dtos;
using MT_GRMS.Application.Suppliers;
using MT_GRMS.Application.Suppliers.Dtos;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using MT_GRMS.Infrastructure.Persistence;
using Xunit;

namespace MT_GRMS.UnitTests;

public class DeepBranchCoverageSuite6Tests
{
    #region Fakes & Test Helpers

    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-suite6";
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public string CurrentRole { get; set; } = "STORE_OWNER";
        public Guid? CurrentBranchId { get; set; } = Guid.NewGuid();
        public string? CustomRawUserId { get; set; } = null;
        public string CurrentRawUserId => CustomRawUserId ?? (CurrentUserId != Guid.Empty ? CurrentUserId.ToString() : string.Empty);

        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }

        public void SetRole(string? role) => CurrentRole = role ?? string.Empty;
    }

    private class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var fakeTenantContext = new FakeTenantContext();
        return new AppDbContext(options, fakeTenantContext);
    }

    #endregion

    // =========================================================================
    // SECTION 1: PurchaseOrderService - Status Transitions & Receipts Branches
    // =========================================================================

    [Fact]
    public async Task PurchaseOrderService_StatusTransitions_And_Receipts_Branches()
    {
        var tenantContext = new FakeTenantContext();
        var uow = new FakeUnitOfWork();
        var poRepoMock = new Mock<IPurchaseOrderRepository>();
        var dispatchPkgMock = new Mock<ISupplierDispatchPackageService>();

        var service = new PurchaseOrderService(poRepoMock.Object, uow, tenantContext, dispatchPkgMock.Object);

        // 1. PO Not Found throws NotFoundException
        var missingId = Guid.NewGuid();
        poRepoMock.Setup(r => r.GetPurchaseOrderByIdAsync(missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PurchaseOrder?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.UpdatePurchaseOrderStatusAsync(missingId, "APPROVED"));

        // 2. Status APPROVED sets ApprovedAtUtc
        var po = new PurchaseOrder
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.CurrentTenantId,
            Code = "PO-202611-001",
            Status = "DRAFT",
            Details = new List<PurchaseOrderDetail>()
        };
        poRepoMock.Setup(r => r.GetPurchaseOrderByIdAsync(po.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(po);
        poRepoMock.Setup(r => r.GetActiveGoodsReceiptsByPoIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, (Guid ReceiptId, string Code, string Status)>());

        var approved = await service.UpdatePurchaseOrderStatusAsync(po.Id, "APPROVED");
        Assert.Equal("APPROVED", approved.Status);
        Assert.NotNull(po.ApprovedAtUtc);

        // 3. Status REJECTED sets RejectionReason
        var rejected = await service.UpdatePurchaseOrderStatusAsync(po.Id, "REJECTED", reason: "Khong du kinh phi");
        Assert.Equal("REJECTED", rejected.Status);
        Assert.Equal("Khong du kinh phi", po.RejectionReason);

        // 4. Status DISPATCHED sets DispatchedAtUtc
        var dispatched = await service.UpdatePurchaseOrderStatusAsync(po.Id, "DISPATCHED");
        Assert.Equal("DISPATCHED", dispatched.Status);
        Assert.NotNull(po.DispatchedAtUtc);

        // 5. Status CONFIRMED sets ConfirmedBySupplierAtUtc
        var confirmed = await service.UpdatePurchaseOrderStatusAsync(po.Id, "CONFIRMED");
        Assert.Equal("CONFIRMED", confirmed.Status);
        Assert.NotNull(po.ConfirmedBySupplierAtUtc);

        // 6. GetPurchaseOrderByIdAsync not found throws NotFoundException
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetPurchaseOrderByIdAsync(missingId));
    }

    // =========================================================================
    // SECTION 2: SupplierService - Status, DeliveryConfig & AP Summary Branches
    // =========================================================================

    [Fact]
    public async Task SupplierService_Status_DeliveryConfig_And_APSummary_Branches()
    {
        var tenantContext = new FakeTenantContext();
        var uow = new FakeUnitOfWork();
        var supplierRepoMock = new Mock<ISupplierRepository>();

        var service = new SupplierService(supplierRepoMock.Object, uow, tenantContext);

        // 1. Supplier not found on UpdateStatusAsync throws NotFoundException
        var missingSupplierId = Guid.NewGuid();
        supplierRepoMock.Setup(r => r.GetByIdAsync(missingSupplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Supplier?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.UpdateStatusAsync(missingSupplierId, new UpdateSupplierStatusRequest { Status = SupplierStatus.Inactive }));

        // 2. Active supplier status update
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.CurrentTenantId,
            Code = "SUP-TEST",
            Name = "Nha Cung Cap Test",
            Status = SupplierStatus.Active
        };
        supplierRepoMock.Setup(r => r.GetByIdWithDetailsAsync(supplier.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(supplier);
        supplierRepoMock.Setup(r => r.GetByIdAsync(supplier.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(supplier);

        var updated = await service.UpdateStatusAsync(supplier.Id, new UpdateSupplierStatusRequest { Status = SupplierStatus.Inactive });
        Assert.Equal(SupplierStatus.Inactive, updated.Status);

        // 3. GetDeliveryConfigAsync not found throws NotFoundException
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.GetDeliveryConfigAsync(missingSupplierId));

        // 4. GetApSummaryAsync: non-owner throws BusinessException("FORBIDDEN")
        tenantContext.SetRole("STAFF");
        var exForbidden = await Assert.ThrowsAsync<BusinessException>(() => service.GetApSummaryAsync(supplier.Id));
        Assert.Equal("FORBIDDEN", exForbidden.ErrorCode);

        // When STORE_OWNER, returns summary
        tenantContext.SetRole("STORE_OWNER");
        var apSummary = await service.GetApSummaryAsync(supplier.Id);
        Assert.NotNull(apSummary);
        Assert.Equal(0m, apSummary.CurrentDebt);

        // 5. GetSearchSuggestionsAsync returns matches
        supplierRepoMock.Setup(r => r.GetSearchSuggestionsAsync("Test", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupplierSearchSuggestionsDto
            {
                Suppliers = new List<SupplierSuggestionItemDto>
                {
                    new SupplierSuggestionItemDto { Id = supplier.Id, Code = supplier.Code, Name = supplier.Name }
                }
            });

        var suggestions = await service.GetSearchSuggestionsAsync("Test", 10);
        Assert.NotEmpty(suggestions.Suppliers);
    }

    // =========================================================================
    // SECTION 3: PromotionService - ToggleStatus, DeleteRestrictions & Redeem
    // =========================================================================

    [Fact]
    public async Task PromotionService_ToggleStatus_DeleteRestrictions_And_Redeem_Branches()
    {
        var tenantContext = new FakeTenantContext();
        var uow = new FakeUnitOfWork();
        var promoRepoMock = new Mock<IPromotionRepository>();

        var service = new PromotionService(promoRepoMock.Object, uow, tenantContext);

        // 1. TogglePromotionStatusAsync: not found throws NotFoundException
        var missingId = Guid.NewGuid();
        promoRepoMock.Setup(r => r.GetByIdAsync(missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Promotion?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.TogglePromotionStatusAsync(missingId));

        // 2. TogglePromotionStatusAsync: toggles IsActive
        var promo = new Promotion
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.CurrentTenantId,
            PromotionCode = "PROMO-001",
            PromotionName = "Giam Gia Mua Xuan",
            IsActive = true,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(5)
        };
        promoRepoMock.Setup(r => r.GetByIdAsync(promo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(promo);

        var toggled = await service.TogglePromotionStatusAsync(promo.Id);
        Assert.False(toggled);
        Assert.False(promo.IsActive);

        // 3. DeletePromotionAsync: running active promotion throws BusinessException("PROMOTION_IS_RUNNING")
        promo.IsActive = true;
        var exRunning = await Assert.ThrowsAsync<BusinessException>(() =>
            service.DeletePromotionAsync(promo.Id));
        Assert.Equal("PROMOTION_IS_RUNNING", exRunning.ErrorCode);

        // 4. DeletePromotionAsync: inactive promotion deleted successfully
        promo.IsActive = false;
        var deleted = await service.DeletePromotionAsync(promo.Id);
        Assert.True(deleted);
        Assert.True(promo.IsDeleted);

        // 5. GenerateVoucherCodesAsync: batch not found throws NotFoundException
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.GenerateVoucherCodesAsync(missingId, new GenerateVoucherCodesRequest(5, "TEST")));

        // 6. RedeemVoucherAsync: redemption failure returns success=false
        promoRepoMock.Setup(r => r.RedeemVoucherCodeAtomicAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var redeemRes = await service.RedeemVoucherAsync(new RedeemVoucherRequest("VC-INVALID", Guid.NewGuid(), "ORD-001"));
        Assert.False(redeemRes.Success);
    }

    // =========================================================================
    // SECTION 4: EmployeeService - Face Registration & Identity Account Branches
    // =========================================================================

    [Fact]
    public async Task EmployeeService_FaceAndIdentityAccount_Branches()
    {
        var tenantContext = new FakeTenantContext();
        var uow = new FakeUnitOfWork();
        var empRepoMock = new Mock<IEmployeeRepository>();
        var userRepoMock = new Mock<IUserRepository>();

        var service = new EmployeeService(empRepoMock.Object, userRepoMock.Object, uow, tenantContext);

        // 1. RegisterFaceDescriptorAsync: not found throws NotFoundException
        var missingEmpId = Guid.NewGuid();
        empRepoMock.Setup(r => r.GetByIdAsync(missingEmpId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Employee?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.RegisterFaceDescriptorAsync(missingEmpId, "descriptor-123", "https://img.face.jpg"));

        // 2. RegisterFaceDescriptorAsync: saves successfully
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.CurrentTenantId,
            EmployeeCode = "NV001",
            FullName = "Nguyen Van A",
            Status = EmployeeStatus.Active
        };
        empRepoMock.Setup(r => r.GetByIdAsync(employee.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);

        var faceOk = await service.RegisterFaceDescriptorAsync(employee.Id, "descriptor-xyz", "https://image.url");
        Assert.True(faceOk);
        Assert.Equal("descriptor-xyz", employee.FaceDescriptor);
        Assert.NotNull(employee.FaceRegisteredAt);

        // 3. CreateIdentityAccountAsync: empty username or password throws ArgumentException
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateIdentityAccountAsync(employee.Id, new CreateIdentityAccountRequest("", "Pass123!")));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateIdentityAccountAsync(employee.Id, new CreateIdentityAccountRequest("validuser", "")));

        // 4. CreateIdentityAccountAsync: inactive employee throws BusinessException("EMPLOYEE_INACTIVE")
        employee.Status = EmployeeStatus.Inactive;
        var exInactive = await Assert.ThrowsAsync<BusinessException>(() =>
            service.CreateIdentityAccountAsync(employee.Id, new CreateIdentityAccountRequest("validuser", "Pass123!")));
        Assert.Equal("EMPLOYEE_INACTIVE", exInactive.ErrorCode);

        // 5. CreateIdentityAccountAsync: employee already has account throws BusinessException("CONFLICT")
        employee.Status = EmployeeStatus.Active;
        employee.UserId = Guid.NewGuid().ToString();
        var exConflict = await Assert.ThrowsAsync<BusinessException>(() =>
            service.CreateIdentityAccountAsync(employee.Id, new CreateIdentityAccountRequest("validuser", "Pass123!")));
        Assert.Equal("CONFLICT", exConflict.ErrorCode);

        // 6. UpdateIdentityAccountAsync: empty username throws ArgumentException
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateIdentityAccountAsync(employee.Id, new CreateIdentityAccountRequest("", "Pass123!")));
    }

    // =========================================================================
    // SECTION 5: PaymentsController - SavePaymentConfig Validation Branches
    // =========================================================================

    [Fact]
    public async Task PaymentsController_SavePaymentConfig_Validation_Branches()
    {
        using var context = CreateInMemoryDbContext();
        var loggerMock = new Mock<ILogger<PaymentsController>>();
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        var configMock = new Mock<IConfiguration>();
        var tenantContext = new FakeTenantContext();

        var controller = new PaymentsController(
            context,
            httpClientFactoryMock.Object,
            configMock.Object,
            loggerMock.Object,
            tenantContext);

        // 1. Caller not STORE_OWNER / ADMIN -> 403 Forbidden
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Role, "EMPLOYEE")
                }, "TestAuth"))
            }
        };

        var req = new SavePaymentConfigRequest
        {
            BankAccountNumber = "1234567890",
            BankAccountName = "CONG TY TNHH TEST",
            SepayApiKey = "sepay_api_key_valid_1234567890",
            BranchIds = new List<Guid>()
        };

        var resForbidden = await controller.SavePaymentConfig(req, CancellationToken.None);
        var objForbidden = Assert.IsType<ObjectResult>(resForbidden);
        Assert.Equal(StatusCodes.Status403Forbidden, objForbidden.StatusCode);

        // Switch to STORE_OWNER
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "STORE_OWNER"),
            new Claim("tenantId", tenantContext.CurrentTenantId)
        }, "TestAuth"));

        // 2. Empty BankAccountNumber -> 400 BadRequest
        var reqEmptyAccount = new SavePaymentConfigRequest
        {
            BankAccountNumber = "",
            BankAccountName = "CONG TY TEST",
            SepayApiKey = "sepay_api_key_valid_1234567890",
            BranchIds = new List<Guid>()
        };
        var resEmpty = await controller.SavePaymentConfig(reqEmptyAccount, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resEmpty);

        // 3. Invalid BankAccountNumber format (contains letters) -> 400 BadRequest
        var reqBadFormat = new SavePaymentConfigRequest
        {
            BankAccountNumber = "12345ABCD",
            BankAccountName = "CONG TY TEST",
            SepayApiKey = "sepay_api_key_valid_1234567890",
            BranchIds = new List<Guid>()
        };
        var resBadFormat = await controller.SavePaymentConfig(reqBadFormat, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resBadFormat);

        // 4. Overlong BankAccountName (> 150 chars) -> 400 BadRequest
        var reqLongName = new SavePaymentConfigRequest
        {
            BankAccountNumber = "1234567890",
            BankAccountName = new string('A', 151),
            SepayApiKey = "sepay_api_key_valid_1234567890",
            BranchIds = new List<Guid>()
        };
        var resLongName = await controller.SavePaymentConfig(reqLongName, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resLongName);

        // 5. Short SePay API Key (< 20 chars) -> 400 BadRequest
        var reqShortKey = new SavePaymentConfigRequest
        {
            BankAccountNumber = "1234567890",
            BankAccountName = "CONG TY TEST",
            SepayApiKey = "short_key",
            BranchIds = new List<Guid>()
        };
        var resShortKey = await controller.SavePaymentConfig(reqShortKey, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resShortKey);

        // 6. Non-existent branch id -> 400 BadRequest
        var reqBadBranch = new SavePaymentConfigRequest
        {
            BankAccountNumber = "1234567890",
            BankAccountName = "CONG TY TEST",
            SepayApiKey = "sepay_api_key_valid_1234567890",
            BranchIds = new List<Guid> { Guid.NewGuid() }
        };
        var resBadBranch = await controller.SavePaymentConfig(reqBadBranch, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resBadBranch);
    }
}
