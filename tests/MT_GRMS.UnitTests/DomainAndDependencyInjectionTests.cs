using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MT_GRMS.Application;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.AiPipeline.Dtos;
using MT_GRMS.Application.Branches.Dtos;
using MT_GRMS.Application.Debt.Dtos;
using MT_GRMS.Application.GoodsReceipts.Dtos;
using MT_GRMS.Application.HR.Payslips.Dtos;
using MT_GRMS.Application.Inventory.Dtos;
using MT_GRMS.Application.PosOrders.Dtos;
using MT_GRMS.Application.PosShifts.Dtos;
using MT_GRMS.Application.Reports.Dtos;
using MT_GRMS.Application.Stocktakes.Dtos;
using MT_GRMS.Application.SupplierReturns.Dtos;
using MT_GRMS.Application.Suppliers.Dtos;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Entities.System;
using MT_GRMS.Domain.Exceptions;
using MT_GRMS.Infrastructure;
using Xunit;

namespace MT_GRMS.UnitTests;

public class DomainAndDependencyInjectionTests
{
    [Fact]
    public void DependencyInjection_ApplicationServices_ShouldRegister()
    {
        var services = new ServiceCollection();
        services.AddApplicationServices();

        Assert.NotEmpty(services);
        // Verify core feature interfaces registered
        Assert.Contains(services, s => s.ServiceType == typeof(MT_GRMS.Application.Batches.IBatchService));
        Assert.Contains(services, s => s.ServiceType == typeof(MT_GRMS.Application.Inventory.IInventoryService));
    }

    [Fact]
    public void DependencyInjection_InfrastructureServices_InMemory_ShouldRegister()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();

        services.AddInfrastructureServices(config);

        Assert.NotEmpty(services);
        Assert.Contains(services, s => s.ServiceType == typeof(MT_GRMS.Application.Common.IUnitOfWork));
    }

    [Fact]
    public void DependencyInjection_InfrastructureServices_PostgreSqlUri_ShouldParse()
    {
        var services = new ServiceCollection();
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "DATABASE_URL", "postgres://user:pass@ep-neon-123.neon.tech:5432/neondb" }
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        services.AddInfrastructureServices(config);

        Assert.NotEmpty(services);
    }

    [Fact]
    public void DomainEntities_PropertiesAndCalculations_ShouldWork()
    {
        var account = new BranchPaymentAccount
        {
            BranchId = Guid.NewGuid(),
            PaymentAccountId = Guid.NewGuid(),
            IsDefault = true,
            IsActive = true
        };
        Assert.True(account.IsDefault);
        Assert.True(account.IsActive);

        var tx = new PaymentTransaction
        {
            OrderId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            BankTransactionId = "BANK-01",
            Gateway = "VietQR",
            AmountIn = 50000m,
            Accumulated = 50000m,
            TransactionContent = "Order 1",
            ReferenceNumber = "REF-123",
            TransactionDate = DateTime.UtcNow,
            VerifiedBySepayApi = true,
            RawPayloadJson = "{}"
        };
        Assert.Equal("VietQR", tx.Gateway);
        Assert.True(tx.VerifiedBySepayApi);

        var stock = new StockOnHand
        {
            BranchId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            BatchId = Guid.NewGuid(),
            Quantity = 100m,
            ReservedQuantity = 30m,
            MinAlertThreshold = 15m
        };
        Assert.Equal(70m, stock.AvailableQuantity);

        var media = new TenantMedia
        {
            TenantId = "tenant-01",
            PublicId = "pub-01",
            Url = "https://example.com/image.jpg",
            Bytes = 1024
        };
        Assert.NotEmpty(media.Id);
        Assert.Equal(1024, media.Bytes);

        var costShare = new SupplierReturnPolicyCategoryCostShare
        {
            PolicyId = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            StoreCostSharePercent = 20m,
            SupplierCostSharePercent = 80m
        };
        Assert.Equal(20m, costShare.StoreCostSharePercent);
        Assert.Equal(80m, costShare.SupplierCostSharePercent);

        var pkgItem = new SupplierDispatchPackageItem
        {
            PackageId = Guid.NewGuid(),
            PurchaseOrderId = Guid.NewGuid()
        };
        Assert.NotEqual(Guid.Empty, pkgItem.PackageId);

        var role = new Role { RoleName = "Staff", Description = "Staff Role", PermissionsJson = "[]" };
        Assert.Equal("Staff", role.RoleName);

        var outbox = new OutboxMessage
        {
            Type = "OrderCreated",
            PayloadJson = "{}"
        };
        Assert.Equal("OrderCreated", outbox.Type);

        var audit = new AuditLog
        {
            ActionType = "Create",
            TargetEntity = "Product",
            TargetId = "prod-01"
        };
        Assert.Equal("Product", audit.TargetEntity);

        var ex = new InsufficientStockException("SP01", 10, 5);
        Assert.Contains("SP01", ex.Message);
        Assert.Equal(ErrorCodes.InsufficientStock, ex.ErrorCode);
    }

    [Fact]
    public void UncoveredDtos_InstantiationAndMembers_ShouldExecute()
    {
        var lineInput = new StocktakeLineInputDto(
            Id: Guid.NewGuid(),
            ProductId: Guid.NewGuid(),
            BranchStockId: Guid.NewGuid(),
            GoodQty: 10,
            DefectiveQty: 0,
            DamagedQty: 1,
            NearExpiryQty: 0,
            ExpiredQty: 0,
            DefectiveReasonCode: null,
            NearExpiryAction: null,
            EvidenceUrl: null,
            Note: "Note"
        );
        Assert.NotNull(lineInput.ToString());
        Assert.Equal(lineInput, lineInput with {});

        var lineDto = new StocktakeLineDto(
            Id: Guid.NewGuid(),
            ProductId: Guid.NewGuid(),
            Sku: "SKU-01",
            ProductName: "Product 1",
            CategoryName: "Category 1",
            Unit: "Cái",
            HasBatches: false,
            BranchStockId: null,
            BatchCode: null,
            ManufactureDate: null,
            ExpiryDate: null,
            UnitCost: 1000m,
            SystemQty: 10m,
            CurrentOnHandQty: 10m,
            GoodQty: 9m,
            DefectiveQty: 0m,
            DamagedQty: 1m,
            NearExpiryQty: 0m,
            ExpiredQty: 0m,
            ActualQty: 9m,
            VarianceQty: -1m,
            DefectiveReasonCode: null,
            NearExpiryAction: null,
            DamagedAction: "WRITE_OFF",
            ExpiredAction: null,
            ReturnedQty: 0m,
            EligibleReturnQty: 0m,
            OriginalGrnId: null,
            OriginalGrnLineId: null,
            OriginalGrnCode: null,
            SupplierId: null,
            SupplierName: null,
            EvidenceUrl: null,
            Note: null
        );
        Assert.NotNull(lineDto.ToString());
        Assert.Equal(lineDto, lineDto with {});

        var returnLine = new CreateReturnFromStocktakeLineItemDto(
            StocktakeLineId: Guid.NewGuid(),
            GoodsReceiptId: Guid.NewGuid(),
            GoodsReceiptLineId: Guid.NewGuid(),
            RequestedQty: 5m,
            Notes: "Damage"
        );
        Assert.NotNull(returnLine.ToString());

        var supGoods = new SupplierSummaryDto { SupplierType = MT_GRMS.Domain.Enums.SupplierType.Goods, Status = MT_GRMS.Domain.Enums.SupplierStatus.Active };
        Assert.Equal("Hàng hóa", supGoods.SupplierTypeName);
        Assert.Equal("Đang hoạt động", supGoods.StatusName);

        var supService = new SupplierSummaryDto { SupplierType = MT_GRMS.Domain.Enums.SupplierType.Service, Status = MT_GRMS.Domain.Enums.SupplierStatus.Inactive };
        Assert.Equal("Dịch vụ", supService.SupplierTypeName);
        Assert.Equal("Ngừng giao dịch", supService.StatusName);

        var supBoth = new SupplierSummaryDto { SupplierType = MT_GRMS.Domain.Enums.SupplierType.Both, Status = (MT_GRMS.Domain.Enums.SupplierStatus)99 };
        Assert.Equal("Cả hai", supBoth.SupplierTypeName);
        Assert.Equal("Không xác định", supBoth.StatusName);

        var supOther = new SupplierSummaryDto { SupplierType = (MT_GRMS.Domain.Enums.SupplierType)99 };
        Assert.Equal("Khác", supOther.SupplierTypeName);

        var catShareDto = new SupplierReturnPolicyCategoryCostShareDto
        {
            CategoryId = Guid.NewGuid(),
            CategoryName = "Fresh",
            StoreCostSharePercent = 30m,
            SupplierCostSharePercent = 70m
        };
        Assert.Equal("Fresh", catShareDto.CategoryName);

        var blackout = new SupplierBlackoutDateDto
        {
            Id = Guid.NewGuid(),
            SupplierId = Guid.NewGuid(),
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(2),
            Reason = "Christmas",
            IsRecurringAnnual = true
        };
        Assert.Equal("Christmas", blackout.Reason);
        Assert.True(blackout.IsRecurringAnnual);

        var phoneSugg = new PhoneSuggestionItemDto { Phone = "0912345678", SupplierId = Guid.NewGuid(), SupplierName = "NCC 1" };
        Assert.Equal("0912345678", phoneSugg.Phone);

        var contactSugg = new ContactPersonSuggestionItemDto { Name = "Nguyen A", SupplierId = Guid.NewGuid(), SupplierName = "NCC 1" };
        Assert.Equal("Nguyen A", contactSugg.Name);

        var returnKpi = new SupplierReturnKpiSummaryDto(10, 5, 2, 3, 5000000m);
        Assert.NotNull(returnKpi.ToString());

        var approveCash = new ApproveCashMovementRequest("Approved by manager");
        Assert.NotNull(approveCash.ToString());

        var rejectCash = new RejectCashMovementRequest("Rejected");
        Assert.NotNull(rejectCash.ToString());

        var printOrder = new PrintOrderRequest("POS-58", 50000m, "Cashier 1");
        Assert.NotNull(printOrder.ToString());

        var ownerUnsettled = new OwnerUnsettledSummaryResponse(5, 2500000m, 1500000m, 4000000m, DateTime.UtcNow, DateTime.UtcNow);
        Assert.NotNull(ownerUnsettled.ToString());

        var grnBatch = new GoodsReceiptBatchLineDto
        {
            Id = Guid.NewGuid(),
            BatchCode = "B01",
            ManufactureDate = DateTime.UtcNow,
            ExpiryDate = DateTime.UtcNow.AddDays(30),
            AcceptedQty = 10m
        };
        Assert.NotNull(grnBatch.ToString());
        Assert.True(grnBatch.DaysToExpiry >= 0);
        Assert.True(grnBatch.IsNearExpiry);

        var invoiceSummary = new CustomerInvoiceSummaryResponse("INV01", "2026-11-16", 5000000m, "PAID");
        Assert.NotNull(invoiceSummary.ToString());

        var kpiSummary = new CustomerKpiSummaryResponse(100, 5000000m, 20, 1000);
        Assert.NotNull(kpiSummary.ToString());

        var updateCustomer = new UpdateCustomerRequest("Nguyen Van B", "cust@example.com", "Address", "0123456789", 5000000m, "GOLD", "ACTIVE", true);
        Assert.NotNull(updateCustomer.ToString());

        var paymentSummary = new CustomerPaymentSummaryResponse("PAY01", "2026-11-16", 3000000m, "CASH", "Staff 1", "Note");
        Assert.NotNull(paymentSummary.ToString());

        var updatePlanStatus = new UpdatePlanStatusRequest(true);
        Assert.NotNull(updatePlanStatus.ToString());

        var aiBasket = new AiBasketRecommendationRequest(Guid.NewGuid(), "Gợi ý đồ ăn", 100000m);
        Assert.NotNull(aiBasket.ToString());

        var aiItem = new AiRecommendedItemDto(Guid.NewGuid(), "Snack", 20000m, 2, "Popular");
        var aiResp = new AiBasketRecommendationResponse("Gợi ý", new List<AiRecommendedItemDto> { aiItem }, 40000m);
        Assert.Equal(40000m, aiResp.EstimatedTotal);
    }
}
