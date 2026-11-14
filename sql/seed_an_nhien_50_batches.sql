-- Seed ~50 lô FEFO cho tenant An Nhiên (màn /store-owner/batches).
-- Idempotent: xóa lô/GRN prefix ANLOT rồi insert lại.
-- Một SKU nhiều lô, HSD khác nhau (hết hạn / khẩn cấp / cảnh báo / an toàn).

BEGIN;

CREATE TEMP TABLE seed_lots (
  seq int PRIMARY KEY,
  product_code text NOT NULL,
  branch_code text NOT NULL,
  batch_code text NOT NULL,
  days_left int NOT NULL,
  qty numeric(18,3) NOT NULL,
  shelf text NOT NULL,
  zone text NOT NULL,
  mfg_days int NOT NULL
);

INSERT INTO seed_lots (seq, product_code, branch_code, batch_code, days_left, qty, shelf, zone, mfg_days) VALUES
-- Sữa tươi — 12 lô, 3 chi nhánh
(1,  'AN-SUA-01',   'BR-AN-01', 'ANLOT-SUA-01',   -5,  8,  'TM-A-01', 'Tủ mát',     21),
(2,  'AN-SUA-01',   'BR-AN-01', 'ANLOT-SUA-02',    1, 12,  'TM-A-01', 'Tủ mát',     21),
(3,  'AN-SUA-01',   'BR-AN-01', 'ANLOT-SUA-03',    3, 18,  'TM-A-02', 'Tủ mát',     21),
(4,  'AN-SUA-01',   'BR-AN-01', 'ANLOT-SUA-04',    8, 24,  'TM-A-02', 'Tủ mát',     21),
(5,  'AN-SUA-01',   'BR-AN-01', 'ANLOT-SUA-05',   15, 30,  'TM-A-03', 'Tủ mát',     21),
(6,  'AN-SUA-01',   'BR-AN-01', 'ANLOT-SUA-06',   45, 40,  'TM-A-03', 'Tủ mát',     21),
(7,  'AN-SUA-01',   'BR-AN-02', 'ANLOT-SUA-07',    2, 10,  'TM-A-01', 'Tủ mát',     21),
(8,  'AN-SUA-01',   'BR-AN-02', 'ANLOT-SUA-08',   12, 22,  'TM-A-01', 'Tủ mát',     21),
(9,  'AN-SUA-01',   'BR-AN-02', 'ANLOT-SUA-09',   90, 35,  'TM-A-02', 'Tủ mát',     21),
(10, 'AN-SUA-01',   'BR-AN-03', 'ANLOT-SUA-10',    0,  6,  'TM-A-01', 'Tủ mát',     21),
(11, 'AN-SUA-01',   'BR-AN-03', 'ANLOT-SUA-11',   20, 16,  'TM-A-01', 'Tủ mát',     21),
(12, 'AN-SUA-01',   'BR-AN-03', 'ANLOT-SUA-12',   60, 28,  'TM-A-02', 'Tủ mát',     21),
-- Trứng — 10 lô
(13, 'AN-TRUNG-01', 'BR-AN-01', 'ANLOT-TRUNG-01', -3, 10,  'TM-B-01', 'Tủ mát',     21),
(14, 'AN-TRUNG-01', 'BR-AN-01', 'ANLOT-TRUNG-02',  1, 14,  'TM-B-01', 'Tủ mát',     21),
(15, 'AN-TRUNG-01', 'BR-AN-01', 'ANLOT-TRUNG-03',  4, 18,  'TM-B-02', 'Tủ mát',     21),
(16, 'AN-TRUNG-01', 'BR-AN-01', 'ANLOT-TRUNG-04', 10, 22,  'TM-B-02', 'Tủ mát',     21),
(17, 'AN-TRUNG-01', 'BR-AN-01', 'ANLOT-TRUNG-05', 40, 30,  'TM-B-03', 'Tủ mát',     21),
(18, 'AN-TRUNG-01', 'BR-AN-02', 'ANLOT-TRUNG-06',  2, 12,  'TM-B-01', 'Tủ mát',     21),
(19, 'AN-TRUNG-01', 'BR-AN-02', 'ANLOT-TRUNG-07',  7, 16,  'TM-B-01', 'Tủ mát',     21),
(20, 'AN-TRUNG-01', 'BR-AN-02', 'ANLOT-TRUNG-08', 90, 28,  'TM-B-02', 'Tủ mát',     21),
(21, 'AN-TRUNG-01', 'BR-AN-03', 'ANLOT-TRUNG-09', 14, 15,  'TM-B-01', 'Tủ mát',     21),
(22, 'AN-TRUNG-01', 'BR-AN-03', 'ANLOT-TRUNG-10', 25, 20,  'TM-B-01', 'Tủ mát',     21),
-- Táo — 10 lô
(23, 'AN-TAO-01',   'BR-AN-01', 'ANLOT-TAO-01',   -1,  5,  'F-A-01',  'Kệ tươi',    40),
(24, 'AN-TAO-01',   'BR-AN-01', 'ANLOT-TAO-02',    3,  8,  'F-A-01',  'Kệ tươi',    40),
(25, 'AN-TAO-01',   'BR-AN-01', 'ANLOT-TAO-03',    6, 12,  'F-A-02',  'Kệ tươi',    40),
(26, 'AN-TAO-01',   'BR-AN-01', 'ANLOT-TAO-04',   21, 15,  'F-A-02',  'Kệ tươi',    40),
(27, 'AN-TAO-01',   'BR-AN-01', 'ANLOT-TAO-05',   50, 20,  'F-A-03',  'Kệ tươi',    40),
(28, 'AN-TAO-01',   'BR-AN-02', 'ANLOT-TAO-06',    1,  6,  'F-A-01',  'Kệ tươi',    40),
(29, 'AN-TAO-01',   'BR-AN-02', 'ANLOT-TAO-07',   18, 10,  'F-A-01',  'Kệ tươi',    40),
(30, 'AN-TAO-01',   'BR-AN-02', 'ANLOT-TAO-08',   80, 18,  'F-A-02',  'Kệ tươi',    40),
(31, 'AN-TAO-01',   'BR-AN-03', 'ANLOT-TAO-09',    9,  7,  'F-A-01',  'Kệ tươi',    40),
(32, 'AN-TAO-01',   'BR-AN-03', 'ANLOT-TAO-10',  120, 14,  'F-A-01',  'Kệ tươi',    40),
-- Gạo — 10 lô
(33, 'AN-GAO-01',   'BR-AN-01', 'ANLOT-GAO-01',   20, 25,  'A-A-01',  'Kệ khô',    180),
(34, 'AN-GAO-01',   'BR-AN-01', 'ANLOT-GAO-02',   45, 40,  'A-A-01',  'Kệ khô',    180),
(35, 'AN-GAO-01',   'BR-AN-01', 'ANLOT-GAO-03',   90, 55,  'A-A-02',  'Kệ khô',    180),
(36, 'AN-GAO-01',   'BR-AN-01', 'ANLOT-GAO-04',  180, 70,  'A-A-02',  'Kệ khô',    180),
(37, 'AN-GAO-01',   'BR-AN-01', 'ANLOT-GAO-05',  300, 80,  'A-A-03',  'Kệ khô',    180),
(38, 'AN-GAO-01',   'BR-AN-02', 'ANLOT-GAO-06',   12, 30,  'A-A-01',  'Kệ khô',    180),
(39, 'AN-GAO-01',   'BR-AN-02', 'ANLOT-GAO-07',   60, 45,  'A-A-01',  'Kệ khô',    180),
(40, 'AN-GAO-01',   'BR-AN-02', 'ANLOT-GAO-08',  240, 60,  'A-A-02',  'Kệ khô',    180),
(41, 'AN-GAO-01',   'BR-AN-03', 'ANLOT-GAO-09',    8, 20,  'A-A-01',  'Kệ khô',    180),
(42, 'AN-GAO-01',   'BR-AN-03', 'ANLOT-GAO-10',  365, 50,  'A-A-01',  'Kệ khô',    180),
-- Nước mắm — 8 lô
(43, 'AN-MAM-01',   'BR-AN-01', 'ANLOT-MAM-01',    5, 18,  'A-B-01',  'Kệ khô',    180),
(44, 'AN-MAM-01',   'BR-AN-01', 'ANLOT-MAM-02',   30, 24,  'A-B-01',  'Kệ khô',    180),
(45, 'AN-MAM-01',   'BR-AN-01', 'ANLOT-MAM-03',   90, 36,  'A-B-02',  'Kệ khô',    180),
(46, 'AN-MAM-01',   'BR-AN-01', 'ANLOT-MAM-04',  180, 40,  'A-B-02',  'Kệ khô',    180),
(47, 'AN-MAM-01',   'BR-AN-02', 'ANLOT-MAM-05',    2, 12,  'A-B-01',  'Kệ khô',    180),
(48, 'AN-MAM-01',   'BR-AN-02', 'ANLOT-MAM-06',   60, 22,  'A-B-01',  'Kệ khô',    180),
(49, 'AN-MAM-01',   'BR-AN-03', 'ANLOT-MAM-07',   15, 16,  'A-B-01',  'Kệ khô',    180),
(50, 'AN-MAM-01',   'BR-AN-03', 'ANLOT-MAM-08',  200, 30,  'A-B-01',  'Kệ khô',    180);

-- Nước mắm cũng quản lý theo lô (nếu không, màn Batches gom 1 dòng không HSD)
UPDATE "Products"
SET "HasBatches" = true, "UpdatedAt" = NOW()
WHERE "TenantID" = 'tenant_an_nhien_02'
  AND "ProductCode" IN ('AN-SUA-01', 'AN-TRUNG-01', 'AN-TAO-01', 'AN-GAO-01', 'AN-MAM-01')
  AND "IsDeleted" = false;

-- 3 NCC
INSERT INTO "Suppliers" (
  "Id", "TenantId", "Code", "Name", "TradeName", "SupplierType", "ContactPerson", "Phone",
  "Email", "Address", "PaymentTermDays", "CreditLimit", "Status",
  "CreatedAt", "IsDeleted"
)
SELECT v."Id", 'tenant_an_nhien_02', v."Code", v."Name", v."TradeName", 1, v."ContactPerson", v."Phone",
       v."Email", v."Address", 15, 50000000, 1, NOW(), false
FROM (VALUES
  ('b50c5555-0000-4000-8000-000000000001'::uuid, 'SUP-AN-DAIRY', 'HTX Sữa Mộc Châu', 'Mộc Châu Dairy',
   'Nguyễn Thị Hoa', '02163888801', 'dairy@mocchau.vn', 'Mộc Châu, Sơn La'),
  ('b50c5555-0000-4000-8000-000000000002'::uuid, 'SUP-AN-FARM', 'Nông sản sạch An Lạc', 'An Lạc Farm',
   'Phạm Văn Bình', '02439990002', 'farm@anlac.vn', 'Ba Vì, Hà Nội'),
  ('b50c5555-0000-4000-8000-000000000003'::uuid, 'SUP-AN-SAUCE', 'Nước mắm Phú Quốc Hồng Đức', 'Hồng Đức',
   'Lê Minh Đức', '02973990003', 'sauce@hongduc.vn', 'Phú Quốc, Kiên Giang')
) AS v("Id", "Code", "Name", "TradeName", "ContactPerson", "Phone", "Email", "Address")
WHERE NOT EXISTS (
  SELECT 1 FROM "Suppliers" s
  WHERE s."TenantId" = 'tenant_an_nhien_02' AND s."Code" = v."Code" AND s."IsDeleted" = false
);

-- Policy trả hàng: dairy/farm cho NEAR_EXPIRY; nước mắm không cho cận hạn (demo HSD alert)
INSERT INTO "SupplierReturnPolicies" (
  "Id", "TenantId", "SupplierId", "AllowReturn", "AllowedReturnTypesJson", "AllowedResolutionTypesJson",
  "DefaultResolutionType", "NearExpiryMinDaysLeft", "RequireEvidenceImage", "MaxReturnDaysAfterReceipt",
  "CostShareEnabled", "CostShareBase", "RequireStorePaymentBeforeReplacement", "IsActive",
  "CreatedAt", "IsDeleted"
)
SELECT
  ('b50c4444-0000-4000-8000-' || lpad(row_number() OVER (ORDER BY s."Code")::text, 12, '0'))::uuid,
  'tenant_an_nhien_02',
  s."Id",
  true,
  CASE WHEN s."Code" = 'SUP-AN-SAUCE'
       THEN '["QUALITY_DEFECT","WRONG_ITEM"]'
       ELSE '["QUALITY_DEFECT","NEAR_EXPIRY","WRONG_ITEM"]' END,
  '["REDUCE_PAYABLE","EXCHANGE","REFUND"]',
  'REDUCE_PAYABLE',
  CASE WHEN s."Code" = 'SUP-AN-DAIRY' THEN 21
       WHEN s."Code" = 'SUP-AN-FARM' THEN 14
       ELSE NULL END,
  false,
  30,
  false,
  'REPLACEMENT_VALUE',
  true,
  true,
  NOW(),
  false
FROM "Suppliers" s
WHERE s."TenantId" = 'tenant_an_nhien_02'
  AND s."Code" IN ('SUP-AN-DAIRY', 'SUP-AN-FARM', 'SUP-AN-SAUCE')
  AND s."IsDeleted" = false
  AND NOT EXISTS (
    SELECT 1 FROM "SupplierReturnPolicies" p
    WHERE p."TenantId" = 'tenant_an_nhien_02' AND p."SupplierId" = s."Id" AND p."IsDeleted" = false
  );

-- Gán SP vào 3 chi nhánh (catalog tồn kho)
INSERT INTO "BranchProducts" (
  "Id", "TenantId", "BranchId", "ProductId", "SellingPriceOverride", "Status", "ShelfLocation",
  "CreatedAt", "IsDeleted"
)
SELECT
  ('b50c6666-0000-4000-8000-' || lpad((ROW_NUMBER() OVER (ORDER BY b."BranchCode", p."ProductCode"))::text, 12, '0'))::uuid,
  'tenant_an_nhien_02',
  b."BranchID",
  p."ProductID",
  p."SellingPrice",
  'Active',
  CASE p."ProductCode"
    WHEN 'AN-SUA-01' THEN 'TM-A-01'
    WHEN 'AN-TRUNG-01' THEN 'TM-B-01'
    WHEN 'AN-TAO-01' THEN 'F-A-01'
    WHEN 'AN-GAO-01' THEN 'A-A-01'
    ELSE 'A-B-01'
  END,
  NOW(),
  false
FROM "Branches" b
CROSS JOIN "Products" p
WHERE b."TenantID" = 'tenant_an_nhien_02' AND b."IsDeleted" = false
  AND p."TenantID" = 'tenant_an_nhien_02' AND p."IsDeleted" = false
  AND p."ProductCode" IN ('AN-SUA-01', 'AN-TRUNG-01', 'AN-TAO-01', 'AN-GAO-01', 'AN-MAM-01')
  AND NOT EXISTS (
    SELECT 1 FROM "BranchProducts" bp
    WHERE bp."TenantId" = 'tenant_an_nhien_02'
      AND bp."BranchId" = b."BranchID"
      AND bp."ProductId" = p."ProductID"
      AND bp."IsDeleted" = false
  );

-- Xóa seed cũ (line → GRN → stock)
DELETE FROM "GoodsReceiptLines" gl
USING "GoodsReceipts" g
WHERE gl."GoodsReceiptId" = g."Id"
  AND g."TenantId" = 'tenant_an_nhien_02'
  AND g."Code" LIKE 'GRN-ANLOT-%';

DELETE FROM "GoodsReceipts"
WHERE "TenantId" = 'tenant_an_nhien_02' AND "Code" LIKE 'GRN-ANLOT-%';

DELETE FROM "BranchStocks"
WHERE "TenantId" = 'tenant_an_nhien_02' AND "BatchCode" LIKE 'ANLOT-%';

-- 50 BranchStocks
INSERT INTO "BranchStocks" (
  "Id", "TenantId", "BranchId", "ProductId", "SupplierId", "BatchCode",
  "ManufactureDate", "ExpiryDate", "QuantityOnHand", "QuantityReserved", "QuantityQuarantined",
  "LandedCostPerUnit", "RetailPrice", "Status", "ShelfLocation", "ZoneArea",
  "RowVersion", "CreatedAt", "IsDeleted"
)
SELECT
  ('b50c1111-0000-4000-8000-' || lpad(l.seq::text, 12, '0'))::uuid,
  'tenant_an_nhien_02',
  b."BranchID",
  p."ProductID",
  s."Id",
  l.batch_code,
  ((CURRENT_TIMESTAMP AT TIME ZONE 'UTC')::date + (l.days_left - l.mfg_days) * INTERVAL '1 day'),
  ((CURRENT_TIMESTAMP AT TIME ZONE 'UTC')::date + l.days_left * INTERVAL '1 day'),
  l.qty,
  0,
  0,
  COALESCE(p."CostPrice", 0),
  p."SellingPrice",
  'Active',
  l.shelf,
  l.zone,
  1,
  NOW(),
  false
FROM seed_lots l
JOIN "Products" p
  ON p."TenantID" = 'tenant_an_nhien_02' AND p."ProductCode" = l.product_code AND p."IsDeleted" = false
JOIN "Branches" b
  ON b."TenantID" = 'tenant_an_nhien_02' AND b."BranchCode" = l.branch_code AND b."IsDeleted" = false
JOIN "Suppliers" s
  ON s."TenantId" = 'tenant_an_nhien_02' AND s."IsDeleted" = false
 AND s."Code" = CASE
      WHEN l.product_code IN ('AN-SUA-01', 'AN-TRUNG-01') THEN 'SUP-AN-DAIRY'
      WHEN l.product_code IN ('AN-TAO-01', 'AN-GAO-01') THEN 'SUP-AN-FARM'
      ELSE 'SUP-AN-SAUCE'
    END;

-- 9 GRN COMPLETED (chi nhánh × NCC)
CREATE TEMP TABLE seed_grn_keys AS
SELECT
  b."BranchID" AS branch_id,
  b."BranchCode" AS branch_code,
  s."Id" AS supplier_id,
  s."Code" AS supplier_code,
  ROW_NUMBER() OVER (ORDER BY b."BranchCode", s."Code") AS n
FROM "Branches" b
CROSS JOIN "Suppliers" s
WHERE b."TenantID" = 'tenant_an_nhien_02' AND b."IsDeleted" = false
  AND b."BranchCode" IN ('BR-AN-01', 'BR-AN-02', 'BR-AN-03')
  AND s."TenantId" = 'tenant_an_nhien_02' AND s."IsDeleted" = false
  AND s."Code" IN ('SUP-AN-DAIRY', 'SUP-AN-FARM', 'SUP-AN-SAUCE');

INSERT INTO "GoodsReceipts" (
  "Id", "TenantId", "Code", "SupplierId", "BranchId", "Status",
  "ReceivedAtUtc", "InvoiceNo", "ReceiverUserId", "ConfirmedByUserId", "ConfirmedAtUtc",
  "SubTotal", "GrandTotal", "PaymentMethod", "CreateApOnConfirm", "Note",
  "RowVersion", "CreatedAt", "IsDeleted"
)
SELECT
  ('b50c2222-0000-4000-8000-' || lpad(g.n::text, 12, '0'))::uuid,
  'tenant_an_nhien_02',
  'GRN-ANLOT-' || REPLACE(g.branch_code, 'BR-AN-', '') || '-' || REPLACE(g.supplier_code, 'SUP-AN-', ''),
  g.supplier_id,
  g.branch_id,
  'COMPLETED',
  NOW() - INTERVAL '7 days',
  'INV-ANLOT-' || g.n::text,
  'usr_owner_annhien_02',
  'usr_owner_annhien_02',
  NOW() - INTERVAL '6 days',
  agg.subtotal,
  agg.subtotal,
  'AP',
  false,
  'Seed lô FEFO An Nhiên',
  1,
  NOW(),
  false
FROM seed_grn_keys g
JOIN LATERAL (
  SELECT COALESCE(SUM(bs."QuantityOnHand" * bs."LandedCostPerUnit"), 0) AS subtotal
  FROM "BranchStocks" bs
  WHERE bs."TenantId" = 'tenant_an_nhien_02'
    AND bs."BatchCode" LIKE 'ANLOT-%'
    AND bs."BranchId" = g.branch_id
    AND bs."SupplierId" = g.supplier_id
    AND bs."IsDeleted" = false
) agg ON true;

INSERT INTO "GoodsReceiptLines" (
  "Id", "TenantId", "GoodsReceiptId", "ProductId", "OrderedQty", "DeliveredQty", "AcceptedQty",
  "RejectedQty", "MissingQty", "UnitPrice", "LineAmount", "BatchCode", "ManufactureDate", "ExpiryDate",
  "LandedUnitCost", "QualityStatus", "BranchStockId", "Note",
  "CreatedAt", "IsDeleted"
)
SELECT
  ('b50c3333-0000-4000-8000-' || lpad(l.seq::text, 12, '0'))::uuid,
  'tenant_an_nhien_02',
  ('b50c2222-0000-4000-8000-' || lpad(g.n::text, 12, '0'))::uuid,
  bs."ProductId",
  bs."QuantityOnHand",
  bs."QuantityOnHand",
  bs."QuantityOnHand",
  0,
  0,
  bs."LandedCostPerUnit",
  bs."QuantityOnHand" * bs."LandedCostPerUnit",
  bs."BatchCode",
  bs."ManufactureDate",
  bs."ExpiryDate",
  bs."LandedCostPerUnit",
  'ACCEPTED',
  bs."Id",
  'SEED-BATCHES-50',
  NOW(),
  false
FROM seed_lots l
JOIN "BranchStocks" bs
  ON bs."TenantId" = 'tenant_an_nhien_02' AND bs."BatchCode" = l.batch_code AND bs."IsDeleted" = false
JOIN seed_grn_keys g
  ON g.branch_id = bs."BranchId" AND g.supplier_id = bs."SupplierId";

COMMIT;

SELECT
  p."ProductCode",
  COUNT(*) AS lot_count,
  COUNT(*) FILTER (WHERE bs."ExpiryDate"::date <= (NOW() AT TIME ZONE 'UTC')::date) AS expired,
  COUNT(*) FILTER (WHERE bs."ExpiryDate"::date BETWEEN (NOW() AT TIME ZONE 'UTC')::date + 1 AND (NOW() AT TIME ZONE 'UTC')::date + 3) AS critical,
  COUNT(*) FILTER (WHERE bs."ExpiryDate"::date BETWEEN (NOW() AT TIME ZONE 'UTC')::date + 4 AND (NOW() AT TIME ZONE 'UTC')::date + 30) AS warning,
  COUNT(*) FILTER (WHERE bs."ExpiryDate"::date > (NOW() AT TIME ZONE 'UTC')::date + 30) AS normal
FROM "BranchStocks" bs
JOIN "Products" p ON p."ProductID" = bs."ProductId"
WHERE bs."TenantId" = 'tenant_an_nhien_02' AND bs."BatchCode" LIKE 'ANLOT-%' AND bs."IsDeleted" = false
GROUP BY p."ProductCode"
ORDER BY p."ProductCode";

SELECT COUNT(*) AS total_lots
FROM "BranchStocks"
WHERE "TenantId" = 'tenant_an_nhien_02' AND "BatchCode" LIKE 'ANLOT-%' AND "IsDeleted" = false;
