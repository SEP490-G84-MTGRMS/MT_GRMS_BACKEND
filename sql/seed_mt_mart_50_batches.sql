-- Seed ~50 lô FEFO cho tenant MT-Mart (màn /store-owner/batches khi login MT-Mart).
-- Idempotent: xóa lô/GRN prefix MTLOT rồi insert lại; gỡ tồn không-lô của 5 SKU demo.
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
  mfg_days int NOT NULL,
  supplier_code text NOT NULL
);

INSERT INTO seed_lots (seq, product_code, branch_code, batch_code, days_left, qty, shelf, zone, mfg_days, supplier_code) VALUES
-- Xúc xích C.P — 12 lô (Thanh Hóa nhiều nhất)
(1,  'SP-XUCXICH-CP-500',           'BR-DN-01',  'MTLOT-XX-01',  -4,  6,  'TM-C-01', 'Tủ mát',  30, 'SUP-20261111-0001'),
(2,  'SP-XUCXICH-CP-500',           'BR-DN-01',  'MTLOT-XX-02',   1,  8,  'TM-C-01', 'Tủ mát',  30, 'SUP-20261111-0001'),
(3,  'SP-XUCXICH-CP-500',           'BR-DN-01',  'MTLOT-XX-03',   3, 10,  'TM-C-02', 'Tủ mát',  30, 'SUP-20261111-0001'),
(4,  'SP-XUCXICH-CP-500',           'BR-DN-01',  'MTLOT-XX-04',   7, 12,  'TM-C-02', 'Tủ mát',  30, 'SUP-20261111-0001'),
(5,  'SP-XUCXICH-CP-500',           'BR-DN-01',  'MTLOT-XX-05',  14, 15,  'TM-C-03', 'Tủ mát',  30, 'SUP-20261111-0001'),
(6,  'SP-XUCXICH-CP-500',           'BR-DN-01',  'MTLOT-XX-06',  45, 18,  'TM-C-03', 'Tủ mát',  30, 'SUP-20261111-0001'),
(7,  'SP-XUCXICH-CP-500',           'BR-HCM-01', 'MTLOT-XX-07',   2,  9,  'TM-C-01', 'Tủ mát',  30, 'SUP-20261111-0001'),
(8,  'SP-XUCXICH-CP-500',           'BR-HCM-01', 'MTLOT-XX-08',  10, 11,  'TM-C-01', 'Tủ mát',  30, 'SUP-20261111-0001'),
(9,  'SP-XUCXICH-CP-500',           'BR-HCM-01', 'MTLOT-XX-09',  60, 14,  'TM-C-02', 'Tủ mát',  30, 'SUP-20261111-0001'),
(10, 'SP-XUCXICH-CP-500',           'BR-HN-01',  'MTLOT-XX-10',   0,  5,  'TM-C-01', 'Tủ mát',  30, 'SUP-20261111-0001'),
(11, 'SP-XUCXICH-CP-500',           'BR-HN-01',  'MTLOT-XX-11',  21, 13,  'TM-C-01', 'Tủ mát',  30, 'SUP-20261111-0001'),
(12, 'SP-XUCXICH-CP-500',           'BR-HN-01',  'MTLOT-XX-12',  90, 16,  'TM-C-02', 'Tủ mát',  30, 'SUP-20261111-0001'),
-- Snack Lay's — 10 lô
(13, 'SP-SNACK-LAY-NATURAL',        'BR-DN-01',  'MTLOT-SN-01',  -2,  7,  'A-S-01',  'Kệ khô',  90, 'SUP-20261111-0001'),
(14, 'SP-SNACK-LAY-NATURAL',        'BR-DN-01',  'MTLOT-SN-02',   2, 10,  'A-S-01',  'Kệ khô',  90, 'SUP-20261111-0001'),
(15, 'SP-SNACK-LAY-NATURAL',        'BR-DN-01',  'MTLOT-SN-03',   5, 12,  'A-S-02',  'Kệ khô',  90, 'SUP-20261111-0001'),
(16, 'SP-SNACK-LAY-NATURAL',        'BR-DN-01',  'MTLOT-SN-04',  12, 14,  'A-S-02',  'Kệ khô',  90, 'SUP-20261111-0001'),
(17, 'SP-SNACK-LAY-NATURAL',        'BR-DN-01',  'MTLOT-SN-05',  40, 18,  'A-S-03',  'Kệ khô',  90, 'SUP-20261111-0001'),
(18, 'SP-SNACK-LAY-NATURAL',        'BR-HCM-01', 'MTLOT-SN-06',   1,  8,  'A-S-01',  'Kệ khô',  90, 'SUP-20261111-0001'),
(19, 'SP-SNACK-LAY-NATURAL',        'BR-HCM-01', 'MTLOT-SN-07',  18, 15,  'A-S-01',  'Kệ khô',  90, 'SUP-20261111-0001'),
(20, 'SP-SNACK-LAY-NATURAL',        'BR-HCM-01', 'MTLOT-SN-08', 100, 20,  'A-S-02',  'Kệ khô',  90, 'SUP-20261111-0001'),
(21, 'SP-SNACK-LAY-NATURAL',        'BR-HN-01',  'MTLOT-SN-09',   8, 11,  'A-S-01',  'Kệ khô',  90, 'SUP-20261111-0001'),
(22, 'SP-SNACK-LAY-NATURAL',        'BR-HN-01',  'MTLOT-SN-10',  55, 16,  'A-S-01',  'Kệ khô',  90, 'SUP-20261111-0001'),
-- Sunlight — 8 lô
(23, 'SP-NUOCRUACHEN-SUNLIGHT-750', 'BR-DN-01',  'MTLOT-SL-01',   4, 10,  'A-C-01',  'Kệ khô', 180, 'SUP-20261111-0001'),
(24, 'SP-NUOCRUACHEN-SUNLIGHT-750', 'BR-DN-01',  'MTLOT-SL-02',  20, 14,  'A-C-01',  'Kệ khô', 180, 'SUP-20261111-0001'),
(25, 'SP-NUOCRUACHEN-SUNLIGHT-750', 'BR-DN-01',  'MTLOT-SL-03',  60, 18,  'A-C-02',  'Kệ khô', 180, 'SUP-20261111-0001'),
(26, 'SP-NUOCRUACHEN-SUNLIGHT-750', 'BR-DN-01',  'MTLOT-SL-04', 200, 22,  'A-C-02',  'Kệ khô', 180, 'SUP-20261111-0001'),
(27, 'SP-NUOCRUACHEN-SUNLIGHT-750', 'BR-HCM-01', 'MTLOT-SL-05',   9, 12,  'A-C-01',  'Kệ khô', 180, 'SUP-20261111-0001'),
(28, 'SP-NUOCRUACHEN-SUNLIGHT-750', 'BR-HCM-01', 'MTLOT-SL-06', 120, 16,  'A-C-01',  'Kệ khô', 180, 'SUP-20261111-0001'),
(29, 'SP-NUOCRUACHEN-SUNLIGHT-750', 'BR-HN-01',  'MTLOT-SL-07',  25, 13,  'A-C-01',  'Kệ khô', 180, 'SUP-20261111-0001'),
(30, 'SP-NUOCRUACHEN-SUNLIGHT-750', 'BR-HN-01',  'MTLOT-SL-08', 300, 20,  'A-C-01',  'Kệ khô', 180, 'SUP-20261111-0001'),
-- Nước mắm Chinsu — 10 lô (Minh Tâm)
(31, 'SP-NUOCMAM-CHINSU-500',       'BR-DN-01',  'MTLOT-MM-01',  -1,  5,  'A-B-01',  'Kệ khô', 180, 'SUP-20261111-0002'),
(32, 'SP-NUOCMAM-CHINSU-500',       'BR-DN-01',  'MTLOT-MM-02',   3,  8,  'A-B-01',  'Kệ khô', 180, 'SUP-20261111-0002'),
(33, 'SP-NUOCMAM-CHINSU-500',       'BR-DN-01',  'MTLOT-MM-03',  10, 12,  'A-B-02',  'Kệ khô', 180, 'SUP-20261111-0002'),
(34, 'SP-NUOCMAM-CHINSU-500',       'BR-DN-01',  'MTLOT-MM-04',  28, 15,  'A-B-02',  'Kệ khô', 180, 'SUP-20261111-0002'),
(35, 'SP-NUOCMAM-CHINSU-500',       'BR-DN-01',  'MTLOT-MM-05',  90, 20,  'A-B-03',  'Kệ khô', 180, 'SUP-20261111-0002'),
(36, 'SP-NUOCMAM-CHINSU-500',       'BR-HCM-01', 'MTLOT-MM-06',   2,  7,  'A-B-01',  'Kệ khô', 180, 'SUP-20261111-0002'),
(37, 'SP-NUOCMAM-CHINSU-500',       'BR-HCM-01', 'MTLOT-MM-07',  15, 11,  'A-B-01',  'Kệ khô', 180, 'SUP-20261111-0002'),
(38, 'SP-NUOCMAM-CHINSU-500',       'BR-HCM-01', 'MTLOT-MM-08', 150, 18,  'A-B-02',  'Kệ khô', 180, 'SUP-20261111-0002'),
(39, 'SP-NUOCMAM-CHINSU-500',       'BR-HN-01',  'MTLOT-MM-09',   6,  9,  'A-B-01',  'Kệ khô', 180, 'SUP-20261111-0002'),
(40, 'SP-NUOCMAM-CHINSU-500',       'BR-HN-01',  'MTLOT-MM-10', 240, 17,  'A-B-01',  'Kệ khô', 180, 'SUP-20261111-0002'),
-- Dầu Simply — 10 lô (Minh Tâm)
(41, 'SP-DAUAN-SIMPLY-1L',          'BR-DN-01',  'MTLOT-DA-01',   5,  8,  'A-D-01',  'Kệ khô', 240, 'SUP-20261111-0002'),
(42, 'SP-DAUAN-SIMPLY-1L',          'BR-DN-01',  'MTLOT-DA-02',  16, 10,  'A-D-01',  'Kệ khô', 240, 'SUP-20261111-0002'),
(43, 'SP-DAUAN-SIMPLY-1L',          'BR-DN-01',  'MTLOT-DA-03',  35, 12,  'A-D-02',  'Kệ khô', 240, 'SUP-20261111-0002'),
(44, 'SP-DAUAN-SIMPLY-1L',          'BR-DN-01',  'MTLOT-DA-04', 120, 15,  'A-D-02',  'Kệ khô', 240, 'SUP-20261111-0002'),
(45, 'SP-DAUAN-SIMPLY-1L',          'BR-DN-01',  'MTLOT-DA-05', 365, 20,  'A-D-03',  'Kệ khô', 240, 'SUP-20261111-0002'),
(46, 'SP-DAUAN-SIMPLY-1L',          'BR-HCM-01', 'MTLOT-DA-06',   1,  6,  'A-D-01',  'Kệ khô', 240, 'SUP-20261111-0002'),
(47, 'SP-DAUAN-SIMPLY-1L',          'BR-HCM-01', 'MTLOT-DA-07',  45, 11,  'A-D-01',  'Kệ khô', 240, 'SUP-20261111-0002'),
(48, 'SP-DAUAN-SIMPLY-1L',          'BR-HCM-01', 'MTLOT-DA-08', 200, 14,  'A-D-02',  'Kệ khô', 240, 'SUP-20261111-0002'),
(49, 'SP-DAUAN-SIMPLY-1L',          'BR-HN-01',  'MTLOT-DA-09',  11,  9,  'A-D-01',  'Kệ khô', 240, 'SUP-20261111-0002'),
(50, 'SP-DAUAN-SIMPLY-1L',          'BR-HN-01',  'MTLOT-DA-10', 180, 16,  'A-D-01',  'Kệ khô', 240, 'SUP-20261111-0002');

-- Bật quản lý theo lô (không bật thì màn Batches gom 1 dòng không HSD)
UPDATE "Products"
SET "HasBatches" = true, "UpdatedAt" = NOW()
WHERE "TenantID" = 'tenant_mt_mart_01'
  AND "ProductCode" IN (
    'SP-XUCXICH-CP-500',
    'SP-SNACK-LAY-NATURAL',
    'SP-NUOCRUACHEN-SUNLIGHT-750',
    'SP-NUOCMAM-CHINSU-500',
    'SP-DAUAN-SIMPLY-1L'
  )
  AND "IsDeleted" = false;

-- Policy trả hàng NEAR_EXPIRY cho 2 NCC hiện có
INSERT INTO "SupplierReturnPolicies" (
  "Id", "TenantId", "SupplierId", "AllowReturn", "AllowedReturnTypesJson", "AllowedResolutionTypesJson",
  "DefaultResolutionType", "NearExpiryMinDaysLeft", "RequireEvidenceImage", "MaxReturnDaysAfterReceipt",
  "CostShareEnabled", "CostShareBase", "RequireStorePaymentBeforeReplacement", "IsActive",
  "CreatedAt", "IsDeleted"
)
SELECT
  ('c50d4444-0000-4000-8000-' || lpad(ROW_NUMBER() OVER (ORDER BY s."Code")::text, 12, '0'))::uuid,
  'tenant_mt_mart_01',
  s."Id",
  true,
  '["QUALITY_DEFECT","NEAR_EXPIRY","WRONG_ITEM"]',
  '["REDUCE_PAYABLE","EXCHANGE","REFUND"]',
  'REDUCE_PAYABLE',
  CASE WHEN s."Code" = 'SUP-20261111-0001' THEN 21 ELSE 14 END,
  false,
  30,
  false,
  'REPLACEMENT_VALUE',
  true,
  true,
  NOW(),
  false
FROM "Suppliers" s
WHERE s."TenantId" = 'tenant_mt_mart_01'
  AND s."Code" IN ('SUP-20261111-0001', 'SUP-20261111-0002')
  AND s."IsDeleted" = false
  AND NOT EXISTS (
    SELECT 1 FROM "SupplierReturnPolicies" p
    WHERE p."TenantId" = 'tenant_mt_mart_01' AND p."SupplierId" = s."Id" AND p."IsDeleted" = false
  );

-- Gán SP vào 3 chi nhánh demo
INSERT INTO "BranchProducts" (
  "Id", "TenantId", "BranchId", "ProductId", "SellingPriceOverride", "Status", "ShelfLocation",
  "CreatedAt", "IsDeleted"
)
SELECT
  ('c50d6666-0000-4000-8000-' || lpad((ROW_NUMBER() OVER (ORDER BY b."BranchCode", p."ProductCode"))::text, 12, '0'))::uuid,
  'tenant_mt_mart_01',
  b."BranchID",
  p."ProductID",
  p."SellingPrice",
  'Active',
  CASE p."ProductCode"
    WHEN 'SP-XUCXICH-CP-500' THEN 'TM-C-01'
    WHEN 'SP-SNACK-LAY-NATURAL' THEN 'A-S-01'
    WHEN 'SP-NUOCRUACHEN-SUNLIGHT-750' THEN 'A-C-01'
    WHEN 'SP-NUOCMAM-CHINSU-500' THEN 'A-B-01'
    ELSE 'A-D-01'
  END,
  NOW(),
  false
FROM "Branches" b
CROSS JOIN "Products" p
WHERE b."TenantID" = 'tenant_mt_mart_01' AND b."IsDeleted" = false
  AND b."BranchCode" IN ('BR-DN-01', 'BR-HCM-01', 'BR-HN-01')
  AND p."TenantID" = 'tenant_mt_mart_01' AND p."IsDeleted" = false
  AND p."ProductCode" IN (
    'SP-XUCXICH-CP-500',
    'SP-SNACK-LAY-NATURAL',
    'SP-NUOCRUACHEN-SUNLIGHT-750',
    'SP-NUOCMAM-CHINSU-500',
    'SP-DAUAN-SIMPLY-1L'
  )
  AND NOT EXISTS (
    SELECT 1 FROM "BranchProducts" bp
    WHERE bp."TenantId" = 'tenant_mt_mart_01'
      AND bp."BranchId" = b."BranchID"
      AND bp."ProductId" = p."ProductID"
      AND bp."IsDeleted" = false
  );

-- Xóa seed cũ + tồn không-lô của 5 SKU (đang hiện "-" trên UI)
DELETE FROM "GoodsReceiptLines" gl
USING "GoodsReceipts" g
WHERE gl."GoodsReceiptId" = g."Id"
  AND g."TenantId" = 'tenant_mt_mart_01'
  AND g."Code" LIKE 'GRN-MTLOT-%';

DELETE FROM "GoodsReceipts"
WHERE "TenantId" = 'tenant_mt_mart_01' AND "Code" LIKE 'GRN-MTLOT-%';

-- Gỡ FK từ GRN cũ trước khi xóa tồn không-lô
UPDATE "GoodsReceiptLines" gl
SET "BranchStockId" = NULL, "UpdatedAt" = NOW()
FROM "BranchStocks" bs
JOIN "Products" p ON p."ProductID" = bs."ProductId"
WHERE gl."BranchStockId" = bs."Id"
  AND bs."TenantId" = 'tenant_mt_mart_01'
  AND p."TenantID" = 'tenant_mt_mart_01'
  AND p."ProductCode" IN (
    'SP-XUCXICH-CP-500',
    'SP-SNACK-LAY-NATURAL',
    'SP-NUOCRUACHEN-SUNLIGHT-750',
    'SP-NUOCMAM-CHINSU-500',
    'SP-DAUAN-SIMPLY-1L'
  )
  AND (
    bs."BatchCode" LIKE 'MTLOT-%'
    OR bs."BatchCode" IS NULL
    OR btrim(bs."BatchCode") = ''
    OR bs."BatchCode" = '-'
  );

DELETE FROM "BranchStocks" bs
USING "Products" p
WHERE bs."ProductId" = p."ProductID"
  AND bs."TenantId" = 'tenant_mt_mart_01'
  AND p."TenantID" = 'tenant_mt_mart_01'
  AND p."ProductCode" IN (
    'SP-XUCXICH-CP-500',
    'SP-SNACK-LAY-NATURAL',
    'SP-NUOCRUACHEN-SUNLIGHT-750',
    'SP-NUOCMAM-CHINSU-500',
    'SP-DAUAN-SIMPLY-1L'
  )
  AND (
    bs."BatchCode" LIKE 'MTLOT-%'
    OR bs."BatchCode" IS NULL
    OR btrim(bs."BatchCode") = ''
    OR bs."BatchCode" = '-'
  );

-- 50 BranchStocks
INSERT INTO "BranchStocks" (
  "Id", "TenantId", "BranchId", "ProductId", "SupplierId", "BatchCode",
  "ManufactureDate", "ExpiryDate", "QuantityOnHand", "QuantityReserved", "QuantityQuarantined",
  "LandedCostPerUnit", "RetailPrice", "Status", "ShelfLocation", "ZoneArea",
  "RowVersion", "CreatedAt", "IsDeleted"
)
SELECT
  ('c50d1111-0000-4000-8000-' || lpad(l.seq::text, 12, '0'))::uuid,
  'tenant_mt_mart_01',
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
  ON p."TenantID" = 'tenant_mt_mart_01' AND p."ProductCode" = l.product_code AND p."IsDeleted" = false
JOIN "Branches" b
  ON b."TenantID" = 'tenant_mt_mart_01' AND b."BranchCode" = l.branch_code AND b."IsDeleted" = false
JOIN "Suppliers" s
  ON s."TenantId" = 'tenant_mt_mart_01' AND s."Code" = l.supplier_code AND s."IsDeleted" = false;

-- GRN COMPLETED theo (chi nhánh × NCC) có lô
CREATE TEMP TABLE seed_grn_keys AS
SELECT
  b."BranchID" AS branch_id,
  b."BranchCode" AS branch_code,
  s."Id" AS supplier_id,
  s."Code" AS supplier_code,
  ROW_NUMBER() OVER (ORDER BY b."BranchCode", s."Code") AS n
FROM "Branches" b
CROSS JOIN "Suppliers" s
WHERE b."TenantID" = 'tenant_mt_mart_01' AND b."IsDeleted" = false
  AND b."BranchCode" IN ('BR-DN-01', 'BR-HCM-01', 'BR-HN-01')
  AND s."TenantId" = 'tenant_mt_mart_01' AND s."IsDeleted" = false
  AND s."Code" IN ('SUP-20261111-0001', 'SUP-20261111-0002')
  AND EXISTS (
    SELECT 1 FROM "BranchStocks" bs
    WHERE bs."TenantId" = 'tenant_mt_mart_01'
      AND bs."BatchCode" LIKE 'MTLOT-%'
      AND bs."BranchId" = b."BranchID"
      AND bs."SupplierId" = s."Id"
      AND bs."IsDeleted" = false
  );

INSERT INTO "GoodsReceipts" (
  "Id", "TenantId", "Code", "SupplierId", "BranchId", "Status",
  "ReceivedAtUtc", "InvoiceNo", "ReceiverUserId", "ConfirmedByUserId", "ConfirmedAtUtc",
  "SubTotal", "GrandTotal", "PaymentMethod", "CreateApOnConfirm", "Note",
  "RowVersion", "CreatedAt", "IsDeleted"
)
SELECT
  ('c50d2222-0000-4000-8000-' || lpad(g.n::text, 12, '0'))::uuid,
  'tenant_mt_mart_01',
  'GRN-MTLOT-' || g.branch_code || '-' || RIGHT(g.supplier_code, 4),
  g.supplier_id,
  g.branch_id,
  'COMPLETED',
  NOW() - INTERVAL '7 days',
  'INV-MTLOT-' || g.n::text,
  'usr_store_owner',
  'usr_store_owner',
  NOW() - INTERVAL '6 days',
  agg.subtotal,
  agg.subtotal,
  'AP',
  false,
  'Seed lô FEFO MT-Mart',
  1,
  NOW(),
  false
FROM seed_grn_keys g
JOIN LATERAL (
  SELECT COALESCE(SUM(bs."QuantityOnHand" * bs."LandedCostPerUnit"), 0) AS subtotal
  FROM "BranchStocks" bs
  WHERE bs."TenantId" = 'tenant_mt_mart_01'
    AND bs."BatchCode" LIKE 'MTLOT-%'
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
  ('c50d3333-0000-4000-8000-' || lpad(l.seq::text, 12, '0'))::uuid,
  'tenant_mt_mart_01',
  ('c50d2222-0000-4000-8000-' || lpad(g.n::text, 12, '0'))::uuid,
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
  'SEED-MTMART-BATCHES-50',
  NOW(),
  false
FROM seed_lots l
JOIN "BranchStocks" bs
  ON bs."TenantId" = 'tenant_mt_mart_01' AND bs."BatchCode" = l.batch_code AND bs."IsDeleted" = false
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
WHERE bs."TenantId" = 'tenant_mt_mart_01' AND bs."BatchCode" LIKE 'MTLOT-%' AND bs."IsDeleted" = false
GROUP BY p."ProductCode"
ORDER BY p."ProductCode";

SELECT b."BranchCode", b."Name", COUNT(*) AS lots
FROM "BranchStocks" bs
JOIN "Branches" b ON b."BranchID" = bs."BranchId"
WHERE bs."TenantId" = 'tenant_mt_mart_01' AND bs."BatchCode" LIKE 'MTLOT-%' AND bs."IsDeleted" = false
GROUP BY b."BranchCode", b."Name"
ORDER BY b."BranchCode";

SELECT COUNT(*) AS total_lots
FROM "BranchStocks"
WHERE "TenantId" = 'tenant_mt_mart_01' AND "BatchCode" LIKE 'MTLOT-%' AND "IsDeleted" = false;
