-- Fix Unicode encoding for Products
UPDATE "Products" SET "Name" = 'Cà phê hòa tan G7 3in1 Hộp 18 gói', "BaseUnit" = 'Hộp' WHERE "Barcode" = '8935024122310';
UPDATE "Products" SET "Name" = 'Bánh Orion Choco-Pie truyền thống Hộp 12 cái', "BaseUnit" = 'Hộp' WHERE "Barcode" = '8936036020380';
UPDATE "Products" SET "Name" = 'Muối chấm Hảo Hảo tôm chua cay Hũ 120g', "BaseUnit" = 'Hũ' WHERE "Barcode" = '8934563065249';
UPDATE "Products" SET "Name" = 'Bia Hà Nội Sleek Lon cao 330ml', "BaseUnit" = 'Lon' WHERE "Barcode" = '8936000440824';

-- Fix Unicode encoding for ProductUnits
UPDATE "ProductUnits" SET "UnitName" = 'Hộp' WHERE "UnitID" = 'a1000001-0000-0000-0000-000000000001';
UPDATE "ProductUnits" SET "UnitName" = 'Thùng' WHERE "UnitID" = 'a1000001-0000-0000-0000-000000000002';

UPDATE "ProductUnits" SET "UnitName" = 'Hộp' WHERE "UnitID" = 'a1000002-0000-0000-0000-000000000001';
UPDATE "ProductUnits" SET "UnitName" = 'Thùng' WHERE "UnitID" = 'a1000002-0000-0000-0000-000000000002';

UPDATE "ProductUnits" SET "UnitName" = 'Hũ' WHERE "UnitID" = 'a1000003-0000-0000-0000-000000000001';
UPDATE "ProductUnits" SET "UnitName" = 'Lốc' WHERE "UnitID" = 'a1000003-0000-0000-0000-000000000002';
UPDATE "ProductUnits" SET "UnitName" = 'Thùng' WHERE "UnitID" = 'a1000003-0000-0000-0000-000000000003';

UPDATE "ProductUnits" SET "UnitName" = 'Lon' WHERE "UnitID" = 'a1000004-0000-0000-0000-000000000001';
UPDATE "ProductUnits" SET "UnitName" = 'Lốc' WHERE "UnitID" = 'a1000004-0000-0000-0000-000000000002';
UPDATE "ProductUnits" SET "UnitName" = 'Thùng' WHERE "UnitID" = 'a1000004-0000-0000-0000-000000000003';

-- Fix Unicode encoding for BranchProducts
UPDATE "BranchProducts" SET "ShelfLocation" = 'Kệ Đồ Uống 01' WHERE "Id" = 'ba000001-0000-0000-0000-000000000001';
UPDATE "BranchProducts" SET "ShelfLocation" = 'Kệ Bánh Kẹo 01' WHERE "Id" = 'ba000002-0000-0000-0000-000000000001';
UPDATE "BranchProducts" SET "ShelfLocation" = 'Kệ Gia Vị 01' WHERE "Id" = 'ba000003-0000-0000-0000-000000000001';
UPDATE "BranchProducts" SET "ShelfLocation" = 'Tủ Mát Đồ Uống' WHERE "Id" = 'ba000004-0000-0000-0000-000000000001';

-- Fix Unicode encoding for BranchStocks
UPDATE "BranchStocks" SET "ShelfLocation" = 'Kệ Đồ Uống 01' WHERE "ProductId" = '01a191cf-1c93-7a8d-87f0-ea3e96376a0a';
UPDATE "BranchStocks" SET "ShelfLocation" = 'Kệ Bánh Kẹo 01' WHERE "ProductId" = '01a191cf-1c95-7289-8d60-f54c655bc304';
UPDATE "BranchStocks" SET "ShelfLocation" = 'Kệ Gia Vị 01' WHERE "ProductId" = '01a191cf-1c95-75e3-96e9-f0b8fe5528db';
UPDATE "BranchStocks" SET "ShelfLocation" = 'Tủ Mát Đồ Uống' WHERE "ProductId" = '01a191cf-1c95-77c5-bfde-6c6830e2c411';

-- Fix Unicode encoding for OrderDetails
UPDATE "OrderDetails" od
SET "ProductName" = p."Name"
FROM "Products" p
WHERE od."ProductID" = p."ProductID";

