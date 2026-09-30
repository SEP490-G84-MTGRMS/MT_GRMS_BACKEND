# MT-GRMS BACKEND (.NET 8 CLEAN ARCHITECTURE & CODE-FIRST)

> **Dự án**: Multi-Tenant Grocery Retail Management System (MT-GRMS)  
> **Nhóm thực hiện**: SEP490-G84 | Đại học FPT Hà Nội  
> **Kiến trúc**: Clean Architecture / N-Tier Decoupled Architecture  
> **Phương pháp tiếp cận Database**: **EF Core Code-First**  
> **Tài liệu tham chiếu**: [Report-4_Software_Design_Specification.md](../../Document/report/Report-4_Software_Design_Specification.md)

---

## 1. Nguyên Tắc Thiết Kế Cốt Lõi (Core Architectural Rules)

Hệ thống tuân thủ 4 nguyên tắc kiến trúc nghiêm ngặt nhằm đảm bảo tính toàn vẹn dữ liệu, bảo mật đa người thuê (Multi-Tenancy) và dễ kiểm thử:

1. **Đóng gói Thực thể Miền (Entity Encapsulation)**:
   - Tầng `Domain` chứa các C# POCO Entities thiết kế cấu trúc Database (kế thừa `BaseTenantEntity`).
   - **CHỈ DUY NHẤT** tầng **`Service` (Application)** và **`DbContext` / Repositories (Infrastructure)** được phép trỏ tới và làm việc trực tiếp với Domain Entity.
   - Tầng `Api` (Controller) **tuyệt đối không được reference hay trả Entity ra ngoài**, ngăn chặn triệt để lộ thông tin nhạy cảm (`PasswordHash`, `TenantId`) và lỗi vòng lặp tuần hoàn JSON (Circular Reference).

2. **Giao tiếp Hoàn toàn bằng DTO (DTO-First Contract)**:
   - Mọi phương thức trong `Service` chỉ nhận đầu vào là **Request DTO** (hoặc primitive types như `Guid`, `int`).
   - Mọi kết quả trả về từ `Service` ra ngoài Controller 100% là **Response DTO** (hoặc `IReadOnlyList<ResponseDto>`).
   - `Service` là nơi thực hiện chuyển đổi hai chiều: $\text{RequestDTO} \rightarrow \text{Entity}$ và $\text{Entity} \rightarrow \text{ResponseDTO}$.

3. **Tiếp cận Database bằng EF Core Code-First**:
   - Mã nguồn C# (Entities + Fluent API Configurations) là **nguồn chân lý duy nhất (Single Source of Truth)**.
   - Cấu hình từng bảng đặt trong thư mục `Infrastructure/Persistence/Configurations/` thông qua `IEntityTypeConfiguration<T>` (chuẩn hóa độ dài, kiểu `decimal(18,2)` cho tiền tệ, chỉ mục B-Tree, quan hệ khóa ngoại).
   - `AppDbContext` tự động nạp cấu hình qua `ApplyConfigurationsFromAssembly` và sinh cơ sở dữ liệu qua **EF Core Migrations**.

4. **Cô lập Đa người thuê Tự động (Automated Multi-Tenant Isolation)**:
   - Mọi thực thể nghiệp vụ kế thừa `BaseTenantEntity` (`Id`, `TenantId`, `CreatedAt`, `IsDeleted`).
   - `AppDbContext` áp dụng `Global Query Filter` động:
     $$\text{Filter: } \text{IsDeleted} == \text{false} \;\land\; (\text{TenantId} == \text{CurrentTenantId})$$
   - Bộ lọc được đánh giá động tại runtime, bảo đảm không bị rò rỉ dữ liệu chéo giữa các cửa hàng/tenant.

---

## 2. Sơ đồ Luồng Dữ liệu (End-to-End Data Flow)

```text
[HTTP Client / React Frontend]
              │
              │  1. Gửi HTTP Request (JSON Body)
              ▼
┌─────────────────────────────────────────────────────────────┐
│  MT_GRMS.Api (Presentation Layer)                           │
│  - Controller mỏng (Thin Controller)                        │
│  - CHỈ sử dụng RequestDTO & ResponseDTO                     │
│  - TUYỆT ĐỐI KHÔNG using MT_GRMS.Domain.Entities            │
└──────────────────────────────┬──────────────────────────────┘
                               │
                               │  2. Gọi Service truyền RequestDTO
                               ▼
┌─────────────────────────────────────────────────────────────┐
│  MT_GRMS.Application (Service Layer)                        │
│  - Nhận RequestDTO từ Controller                            │
│  - Map RequestDTO -> Domain Entity                          │
│  - Xử lý Business Logic (Tính giá, FEFO, Kiểm tra tồn kho) │
│  - Gọi Repository & UnitOfWork để thao tác DB               │
│  - Map Domain Entity đã lưu -> ResponseDTO                  │
└──────────────────────────────┬──────────────────────────────┘
                               │
                               │  3. Thao tác Domain Entity
                               ▼
┌─────────────────────────────────────────────────────────────┐
│  MT_GRMS.Infrastructure (Data Access & Persistence)         │
│  - AppDbContext (Đăng ký DbSet, Global Query Filter)        │
│  - Configurations/ (Fluent API Code-First cho từng bảng)    │
│  - GenericRepository<T> & IUnitOfWork                       │
│  - EF Core Migrations quản lý lược đồ PostgreSQL            │
└──────────────────────────────┬──────────────────────────────┘
                               │
                               │  4. Đọc/Ghi qua SQL
                               ▼
┌─────────────────────────────────────────────────────────────┐
│  PostgreSQL Database (mtgrms_db)                            │
└─────────────────────────────────────────────────────────────┘
```

---

## 3. Cấu trúc Giải pháp (Solution Architecture)

Solution: [`MT_GRMS_BACKEND.sln`](./MT_GRMS_BACKEND.sln) gồm 4 projects chuẩn Clean Architecture:

```text
MT_GRMS_BACKEND/
├── MT_GRMS_BACKEND.sln
│
├── src/
│   ├── MT_GRMS.Domain/                         # [TẦNG 1] Thực thể Miền cốt lõi (Entities & Enums)
│   │   ├── Common/                             # BaseTenantEntity (Id, TenantId, CreatedAt, IsDeleted)
│   │   ├── Entities/                           # POCO Entities thiết kế Database theo Bounded Context
│   │   │   ├── Catalog/                        # Product.cs, Category.cs
│   │   │   ├── Inventory/                      # Batch.cs, StockOnHand.cs
│   │   │   ├── Sales/                          # Order.cs, OrderItem.cs
│   │   │   ├── Identity/                       # User.cs, Role.cs
│   │   │   ├── Finance/                        # CashbookTransaction.cs
│   │   │   └── SaaS/                           # Tenant, Subscription, Branch (dự kiến)
│   │   ├── Enums/                              # POStatus, PaymentMethod, PaymentStatus, LockMode...
│   │   ├── Constants/                          # ErrorCodes.cs
│   │   └── Exceptions/                         # BusinessException (422), NotFoundException (404)...
│   │
│   ├── MT_GRMS.Application/                    # [TẦNG 2] Business Logic theo tính năng (Feature-First)
│   │   ├── Common/                             # ApiResponse<T>, PagedResult<T>, ITenantContext, IRepository<T>, IUnitOfWork
│   │   ├── Catalog/                            # [MẪU CHUẨN] ICatalogService.cs, CatalogService.cs
│   │   │   ├── Dtos/                           # ProductCreateRequest, ProductResponse (DTOs)
│   │   │   └── Validators/                     # ProductCreateRequestValidator (FluentValidation)
│   │   ├── Batches/                            # FE-02: IBatchService, BatchDtos
│   │   ├── PosOrders/                          # FE-12: IOrderService, OrderDtos
│   │   ├── Inventory/                          # FE-04: IInventoryService, InventoryDtos
│   │   ├── Cashbook/                           # FE-17: ICashbookService, CashbookDtos
│   │   └── AiPipeline/                         # FE-21: IAiRecommendationService, AiDtos
│   │
│   ├── MT_GRMS.Infrastructure/                 # [TẦNG 3] Hạ tầng dữ liệu & EF Core Code-First
│   │   ├── Persistence/
│   │   │   ├── AppDbContext.cs                 # DbContext chính, tự động load Fluent API & Global Query Filter
│   │   │   ├── UnitOfWork.cs                   # Cài đặt IUnitOfWork
│   │   │   └── Configurations/                 # [CODE-FIRST FLUENT API]
│   │   │       ├── ProductConfiguration.cs     # Cấu hình bảng products (indexes, types, relations)
│   │   │       └── ...                         # Các file cấu hình bảng tiếp theo
│   │   └── Repositories/                       # GenericRepository<T> & specialized repositories
│   │
│   └── MT_GRMS.Api/                            # [TẦNG 4] HTTP API (Thin Controllers)
│       ├── Controllers/                        # BaseApiController, HealthController
│       │   ├── ProductsController.cs           # [MẪU CHUẨN] Controller chỉ làm việc với DTO
│       │   └── OrdersController.cs
│       ├── Middlewares/                        # MultiTenantMiddleware, ExceptionHandlingMiddleware
│       ├── appsettings.json                    # ConnectionString PostgreSQL, cấu hình JWT
│       └── Program.cs                          # DI Container & Middleware Pipeline
│
└── tests/
    ├── MT_GRMS.UnitTests/                      # Test nghiệp vụ đơn vị (xUnit, Moq)
    └── MT_GRMS.IntegrationTests/               # Test tích hợp Web API
```

---

## 4. Khuôn Mẫu Mẫu Chuẩn Cho Nhóm Phát Triển (Reference Template)

Module **Catalog / Product** được triển khai làm khuôn mẫu rỗng chuẩn mực (Gold Standard Template). Nhóm sinh viên dựa vào module này để nhân bản cho các tính năng khác:

| Thành phần | Vị trí File Mẫu | Quy tắc thực hiện |
| :--- | :--- | :--- |
| **1. Entity** | [`Domain/Entities/Catalog/Product.cs`](file:///d:/FPT_University/Final/Grade/new/project/MT_GRMS_BACKEND/src/MT_GRMS.Domain/Entities/Catalog/Product.cs) | Khai báo các cột dữ liệu, kế thừa `BaseTenantEntity`. |
| **2. Code-First Fluent API** | [`Infrastructure/Persistence/Configurations/ProductConfiguration.cs`](file:///d:/FPT_University/Final/Grade/new/project/MT_GRMS_BACKEND/src/MT_GRMS.Infrastructure/Persistence/Configurations/ProductConfiguration.cs) | Định nghĩa tên bảng, kiểu `decimal(18,2)`, chỉ mục B-Tree, khóa ngoại. |
| **3. DTOs** | [`Application/Catalog/Dtos/ProductDtos.cs`](file:///d:/FPT_University/Final/Grade/new/project/MT_GRMS_BACKEND/src/MT_GRMS.Application/Catalog/Dtos/ProductDtos.cs) | Định nghĩa Request DTO (dữ liệu nhận) và Response DTO (dữ liệu trả về). |
| **4. Service Interface** | [`Application/Catalog/ICatalogService.cs`](file:///d:/FPT_University/Final/Grade/new/project/MT_GRMS_BACKEND/src/MT_GRMS.Application/Catalog/ICatalogService.cs) | Interface chỉ nhận DTO và chỉ trả DTO. |
| **5. Service Implementation**| [`Application/Catalog/CatalogService.cs`](file:///d:/FPT_University/Final/Grade/new/project/MT_GRMS_BACKEND/src/MT_GRMS.Application/Catalog/CatalogService.cs) | Inject `IRepository<T>` & `IUnitOfWork`. Nhận DTO $\rightarrow$ xử lý Entity $\rightarrow$ trả DTO. |
| **6. API Controller** | [`Api/Controllers/ProductsController.cs`](file:///d:/FPT_University/Final/Grade/new/project/MT_GRMS_BACKEND/src/MT_GRMS.Api/Controllers/ProductsController.cs) | Controller mỏng, nhận DTO, gọi Service, trả `ApiResponse<DTO>`. |

---

## 5. Lệnh Quản Trị & Kiểm Tra (CLI Commands)

### 5.1. Biên dịch & Chạy Kiểm Thử
```bash
# Biên dịch toàn bộ Solution (đảm bảo 0 Error, 0 Warning)
dotnet build MT_GRMS_BACKEND.sln

# Chạy toàn bộ Unit Tests & Integration Tests
dotnet test MT_GRMS_BACKEND.sln
```

### 5.2. Quản Trị Cơ Sở Dữ Liệu EF Core Code-First
Khi bổ sung Entity mới hoặc thay đổi Fluent API Configuration trong `Infrastructure`:

```bash
# Cài đặt công cụ ef core CLI (nếu máy chưa có)
dotnet tool install --global dotnet-ef

# 1. Tạo migration mới từ Code C# (Code-First)
dotnet ef migrations add InitialCreate --project src/MT_GRMS.Infrastructure --startup-project src/MT_GRMS.Api

# 2. Cập nhật lược đồ bảng xuống Database PostgreSQL
dotnet ef database update --project src/MT_GRMS.Infrastructure --startup-project src/MT_GRMS.Api

# 3. Xóa migration gần nhất (nếu cần sửa đổi)
dotnet ef migrations remove --project src/MT_GRMS.Infrastructure --startup-project src/MT_GRMS.Api
```

---

## 6. Hướng Dẫn Khởi Chạy Dự Án

1. Mở file [`MT_GRMS_BACKEND.sln`](./MT_GRMS_BACKEND.sln) bằng **Visual Studio 2022** (v17.8+ hỗ trợ .NET 8).
2. Kiểm tra chuỗi kết nối PostgreSQL trong [`appsettings.json`](./src/MT_GRMS.Api/appsettings.json) (mặc định: `Host=localhost;Port=5432;Database=mtgrms_db;...`).
3. Chuột phải vào project **`MT_GRMS.Api`** $\rightarrow$ Chọn **Set as Startup Project**.
4. Bấm **F5** (hoặc `Ctrl + F5`) để khởi chạy.
5. Truy cập Swagger UI kiểm thử: `http://localhost:5000/swagger`.
