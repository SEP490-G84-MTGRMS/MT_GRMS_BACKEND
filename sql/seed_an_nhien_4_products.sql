-- Seed 4 real products for tenant_an_nhien_02 (Trần Quốc An - Chi nhánh Ba Đình)
-- Products: G7 3in1, Choco-Pie, Muối Hảo Hảo, Bia Hà Nội Sleek
BEGIN;

-- 1. Cập nhật lại BaseUnit chuẩn UTF-8 trong Products
UPDATE "Products" SET "BaseUnit" = 'Hộp', "Name" = 'Cà phê hòa tan G7 3in1 Hộp 18 gói' WHERE "ProductID" = '01a191cf-1c93-7a8d-87f0-ea3e96376a0a';
UPDATE "Products" SET "BaseUnit" = 'Hộp', "Name" = 'Bánh Orion Choco-Pie truyền thống Hộp 12 cái' WHERE "ProductID" = '01a191cf-1c95-7289-8d60-f54c655bc304';
UPDATE "Products" SET "BaseUnit" = 'Hũ', "Name" = 'Muối chấm Hảo Hảo tôm chua cay Hũ 120g' WHERE "ProductID" = '01a191cf-1c95-75e3-96e9-f0b8fe5528db';
UPDATE "Products" SET "BaseUnit" = 'Lon', "Name" = 'Bia Hà Nội Sleek Lon cao 330ml' WHERE "ProductID" = '01a191cf-1c95-77c5-bfde-6c6830e2c411';

-- 2. Dọn dữ liệu cũ nếu có
DELETE FROM "BranchProductUnitPrices" WHERE "BranchProductId" IN (
    SELECT "Id" FROM "BranchProducts" WHERE "BranchId" = 'b1000000-0000-0000-0000-000000000001' 
    AND "ProductId" IN ('01a191cf-1c93-7a8d-87f0-ea3e96376a0a', '01a191cf-1c95-7289-8d60-f54c655bc304', '01a191cf-1c95-75e3-96e9-f0b8fe5528db', '01a191cf-1c95-77c5-bfde-6c6830e2c411')
);
DELETE FROM "BranchStocks" WHERE "BranchId" = 'b1000000-0000-0000-0000-000000000001' 
    AND "ProductId" IN ('01a191cf-1c93-7a8d-87f0-ea3e96376a0a', '01a191cf-1c95-7289-8d60-f54c655bc304', '01a191cf-1c95-75e3-96e9-f0b8fe5528db', '01a191cf-1c95-77c5-bfde-6c6830e2c411');
DELETE FROM "BranchProducts" WHERE "BranchId" = 'b1000000-0000-0000-0000-000000000001' 
    AND "ProductId" IN ('01a191cf-1c93-7a8d-87f0-ea3e96376a0a', '01a191cf-1c95-7289-8d60-f54c655bc304', '01a191cf-1c95-75e3-96e9-f0b8fe5528db', '01a191cf-1c95-77c5-bfde-6c6830e2c411');
DELETE FROM "ProductUnits" WHERE "ProductID" IN ('01a191cf-1c93-7a8d-87f0-ea3e96376a0a', '01a191cf-1c95-7289-8d60-f54c655bc304', '01a191cf-1c95-75e3-96e9-f0b8fe5528db', '01a191cf-1c95-77c5-bfde-6c6830e2c411');

-- 3. Tạo ProductUnits
-- G7 3in1
INSERT INTO "ProductUnits" ("UnitID", "ProductID", "UnitName", "ConversionRate", "Price", "Barcode", "IsBaseUnit", "CreatedAt", "IsDeleted")
VALUES 
('a1000001-0000-0000-0000-000000000001', '01a191cf-1c93-7a8d-87f0-ea3e96376a0a', 'Hộp', 1.0, 58000, '8935024122310', true, NOW(), false),
('a1000001-0000-0000-0000-000000000002', '01a191cf-1c93-7a8d-87f0-ea3e96376a0a', 'Thùng', 24.0, 1350000, '8935024122310-THUNG', false, NOW(), false);

-- Choco-Pie
INSERT INTO "ProductUnits" ("UnitID", "ProductID", "UnitName", "ConversionRate", "Price", "Barcode", "IsBaseUnit", "CreatedAt", "IsDeleted")
VALUES 
('a1000002-0000-0000-0000-000000000001', '01a191cf-1c95-7289-8d60-f54c655bc304', 'Hộp', 1.0, 55000, '8936036020380', true, NOW(), false),
('a1000002-0000-0000-0000-000000000002', '01a191cf-1c95-7289-8d60-f54c655bc304', 'Thùng', 8.0, 420000, '8936036020380-THUNG', false, NOW(), false);

-- Muối Hảo Hảo
INSERT INTO "ProductUnits" ("UnitID", "ProductID", "UnitName", "ConversionRate", "Price", "Barcode", "IsBaseUnit", "CreatedAt", "IsDeleted")
VALUES 
('a1000003-0000-0000-0000-000000000001', '01a191cf-1c95-75e3-96e9-f0b8fe5528db', 'Hũ', 1.0, 16000, '8934563065249', true, NOW(), false),
('a1000003-0000-0000-0000-000000000002', '01a191cf-1c95-75e3-96e9-f0b8fe5528db', 'Lốc', 6.0, 90000, '8934563065249-LOC', false, NOW(), false),
('a1000003-0000-0000-0000-000000000003', '01a191cf-1c95-75e3-96e9-f0b8fe5528db', 'Thùng', 30.0, 430000, '8934563065249-THUNG', false, NOW(), false);

-- Bia Hà Nội Sleek
INSERT INTO "ProductUnits" ("UnitID", "ProductID", "UnitName", "ConversionRate", "Price", "Barcode", "IsBaseUnit", "CreatedAt", "IsDeleted")
VALUES 
('a1000004-0000-0000-0000-000000000001', '01a191cf-1c95-77c5-bfde-6c6830e2c411', 'Lon', 1.0, 14000, '8936000440824', true, NOW(), false),
('a1000004-0000-0000-0000-000000000002', '01a191cf-1c95-77c5-bfde-6c6830e2c411', 'Lốc', 6.0, 82000, '8936000440824-LOC', false, NOW(), false),
('a1000004-0000-0000-0000-000000000003', '01a191cf-1c95-77c5-bfde-6c6830e2c411', 'Thùng', 24.0, 320000, '8936000440824-THUNG', false, NOW(), false);

-- 4. Tạo BranchProducts cho An Nhiên (b1000000-0000-0000-0000-000000000001)
INSERT INTO "BranchProducts" ("Id", "TenantId", "BranchId", "ProductId", "SellingPriceOverride", "Status", "ShelfLocation", "CreatedAt", "IsDeleted")
VALUES 
('ba000001-0000-0000-0000-000000000001', 'tenant_an_nhien_02', 'b1000000-0000-0000-0000-000000000001', '01a191cf-1c93-7a8d-87f0-ea3e96376a0a', 58000, 'Active', 'Kệ Đồ Uống 01', NOW(), false),
('ba000002-0000-0000-0000-000000000001', 'tenant_an_nhien_02', 'b1000000-0000-0000-0000-000000000001', '01a191cf-1c95-7289-8d60-f54c655bc304', 55000, 'Active', 'Kệ Bánh Kẹo 01', NOW(), false),
('ba000003-0000-0000-0000-000000000001', 'tenant_an_nhien_02', 'b1000000-0000-0000-0000-000000000001', '01a191cf-1c95-75e3-96e9-f0b8fe5528db', 16000, 'Active', 'Kệ Gia Vị 01', NOW(), false),
('ba000004-0000-0000-0000-000000000001', 'tenant_an_nhien_02', 'b1000000-0000-0000-0000-000000000001', '01a191cf-1c95-77c5-bfde-6c6830e2c411', 14000, 'Active', 'Tủ Mát Đồ Uống', NOW(), false);

-- 5. Tạo BranchProductUnitPrices (override giá ĐVT theo chi nhánh)
INSERT INTO "BranchProductUnitPrices" ("Id", "TenantId", "BranchProductId", "ProductUnitId", "SellingPriceOverride", "CreatedAt", "IsDeleted")
VALUES 
-- G7 Thùng
('ca000001-0000-0000-0000-000000000001', 'tenant_an_nhien_02', 'ba000001-0000-0000-0000-000000000001', 'a1000001-0000-0000-0000-000000000002', 1350000, NOW(), false),
-- ChocoPie Thùng
('ca000002-0000-0000-0000-000000000001', 'tenant_an_nhien_02', 'ba000002-0000-0000-0000-000000000001', 'a1000002-0000-0000-0000-000000000002', 420000, NOW(), false),
-- Muối Hảo Hảo Lốc & Thùng
('ca000003-0000-0000-0000-000000000001', 'tenant_an_nhien_02', 'ba000003-0000-0000-0000-000000000001', 'a1000003-0000-0000-0000-000000000002', 90000, NOW(), false),
('ca000003-0000-0000-0000-000000000002', 'tenant_an_nhien_02', 'ba000003-0000-0000-0000-000000000001', 'a1000003-0000-0000-0000-000000000003', 430000, NOW(), false),
-- Bia Hà Nội Lốc & Thùng
('ca000004-0000-0000-0000-000000000001', 'tenant_an_nhien_02', 'ba000004-0000-0000-0000-000000000001', 'a1000004-0000-0000-0000-000000000002', 82000, NOW(), false),
('ca000004-0000-0000-0000-000000000002', 'tenant_an_nhien_02', 'ba000004-0000-0000-0000-000000000001', 'a1000004-0000-0000-0000-000000000003', 320000, NOW(), false);

-- 6. Tạo BranchStocks FEFO (2 lô mỗi sản phẩm: 1 lô cận date, 1 lô dài ngày)
-- G7 3in1
INSERT INTO "BranchStocks" ("Id", "TenantId", "BranchId", "ProductId", "BatchCode", "ManufactureDate", "ExpiryDate", "QuantityOnHand", "QuantityReserved", "LandedCostPerUnit", "ShelfLocation", "ZoneArea", "RowVersion", "CreatedAt", "IsDeleted")
VALUES 
('da000001-0000-0000-0000-000000000001', 'tenant_an_nhien_02', 'b1000000-0000-0000-0000-000000000001', '01a191cf-1c93-7a8d-87f0-ea3e96376a0a', 'LOT-G7-202612', NOW() - INTERVAL '3 months', NOW() + INTERVAL '40 days', 15.0, 0.0, 42000, 'Kệ Đồ Uống 01', 'Khu Bán Lẻ', 1, NOW(), false),
('da000001-0000-0000-0000-000000000002', 'tenant_an_nhien_02', 'b1000000-0000-0000-0000-000000000001', '01a191cf-1c93-7a8d-87f0-ea3e96376a0a', 'LOT-G7-202706', NOW() - INTERVAL '1 month', NOW() + INTERVAL '240 days', 80.0, 0.0, 42000, 'Kệ Đồ Uống 01', 'Khu Bán Lẻ', 1, NOW(), false);

-- Choco-Pie
INSERT INTO "BranchStocks" ("Id", "TenantId", "BranchId", "ProductId", "BatchCode", "ManufactureDate", "ExpiryDate", "QuantityOnHand", "QuantityReserved", "LandedCostPerUnit", "ShelfLocation", "ZoneArea", "RowVersion", "CreatedAt", "IsDeleted")
VALUES 
('da000002-0000-0000-0000-000000000001', 'tenant_an_nhien_02', 'b1000000-0000-0000-0000-000000000001', '01a191cf-1c95-7289-8d60-f54c655bc304', 'LOT-CP-202612', NOW() - INTERVAL '2 months', NOW() + INTERVAL '55 days', 20.0, 0.0, 40000, 'Kệ Bánh Kẹo 01', 'Khu Bán Lẻ', 1, NOW(), false),
('da000002-0000-0000-0000-000000000002', 'tenant_an_nhien_02', 'b1000000-0000-0000-0000-000000000001', '01a191cf-1c95-7289-8d60-f54c655bc304', 'LOT-CP-202708', NOW() - INTERVAL '1 month', NOW() + INTERVAL '280 days', 60.0, 0.0, 40000, 'Kệ Bánh Kẹo 01', 'Khu Bán Lẻ', 1, NOW(), false);

-- Muối Hảo Hảo
INSERT INTO "BranchStocks" ("Id", "TenantId", "BranchId", "ProductId", "BatchCode", "ManufactureDate", "ExpiryDate", "QuantityOnHand", "QuantityReserved", "LandedCostPerUnit", "ShelfLocation", "ZoneArea", "RowVersion", "CreatedAt", "IsDeleted")
VALUES 
('da000003-0000-0000-0000-000000000001', 'tenant_an_nhien_02', 'b1000000-0000-0000-0000-000000000001', '01a191cf-1c95-75e3-96e9-f0b8fe5528db', 'LOT-HH-202611', NOW() - INTERVAL '4 months', NOW() + INTERVAL '24 days', 25.0, 0.0, 11000, 'Kệ Gia Vị 01', 'Khu Bán Lẻ', 1, NOW(), false),
('da000003-0000-0000-0000-000000000002', 'tenant_an_nhien_02', 'b1000000-0000-0000-0000-000000000001', '01a191cf-1c95-75e3-96e9-f0b8fe5528db', 'LOT-HH-202705', NOW() - INTERVAL '1 month', NOW() + INTERVAL '190 days', 100.0, 0.0, 11000, 'Kệ Gia Vị 01', 'Khu Bán Lẻ', 1, NOW(), false);

-- Bia Hà Nội Sleek
INSERT INTO "BranchStocks" ("Id", "TenantId", "BranchId", "ProductId", "BatchCode", "ManufactureDate", "ExpiryDate", "QuantityOnHand", "QuantityReserved", "LandedCostPerUnit", "ShelfLocation", "ZoneArea", "RowVersion", "CreatedAt", "IsDeleted")
VALUES 
('da000004-0000-0000-0000-000000000001', 'tenant_an_nhien_02', 'b1000000-0000-0000-0000-000000000001', '01a191cf-1c95-77c5-bfde-6c6830e2c411', 'LOT-BIA-202612', NOW() - INTERVAL '2 months', NOW() + INTERVAL '35 days', 30.0, 0.0, 10500, 'Tủ Mát Đồ Uống', 'Khu Bán Lẻ', 1, NOW(), false),
('da000004-0000-0000-0000-000000000002', 'tenant_an_nhien_02', 'b1000000-0000-0000-0000-000000000001', '01a191cf-1c95-77c5-bfde-6c6830e2c411', 'LOT-BIA-202707', NOW() - INTERVAL '1 month', NOW() + INTERVAL '260 days', 120.0, 0.0, 10500, 'Tủ Mát Đồ Uống', 'Khu Bán Lẻ', 1, NOW(), false);

COMMIT;

