CREATE TABLE IF NOT EXISTS "Batches" (
    "Id" uuid PRIMARY KEY,
    "ProductId" uuid NOT NULL,
    "BranchId" uuid NOT NULL,
    "BatchCode" text NOT NULL DEFAULT '',
    "ManufactureDate" timestamp with time zone NOT NULL,
    "ExpiryDate" timestamp with time zone NOT NULL,
    "InitialQuantity" numeric(18,3) NOT NULL DEFAULT 0,
    "LandedCostPerUnit" numeric(18,2) NOT NULL DEFAULT 0,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    "TenantId" text NOT NULL DEFAULT '',
    "CreatedBy" uuid NULL
);

CREATE TABLE IF NOT EXISTS "StockOnHands" (
    "Id" uuid PRIMARY KEY,
    "BranchId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "BatchId" uuid NULL,
    "Quantity" numeric(18,3) NOT NULL DEFAULT 0,
    "ReservedQuantity" numeric(18,3) NOT NULL DEFAULT 0,
    "MinAlertThreshold" numeric(18,3) NOT NULL DEFAULT 10,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    "TenantId" text NOT NULL DEFAULT '',
    "CreatedBy" uuid NULL
);

CREATE TABLE IF NOT EXISTS supplier_branch_assignments (
    "Id" uuid PRIMARY KEY,
    "SupplierId" uuid NOT NULL,
    "BranchId" uuid NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    "TenantId" text NOT NULL DEFAULT '',
    "CreatedBy" uuid NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_supplier_branch_assignments_TenantId_SupplierId_BranchId"
    ON supplier_branch_assignments ("TenantId", "SupplierId", "BranchId");

INSERT INTO supplier_branch_assignments ("Id", "SupplierId", "BranchId", "CreatedAt", "IsDeleted", "TenantId")
SELECT gen_random_uuid(), s."Id", b."BranchID", NOW(), FALSE, s."TenantId"
FROM "Suppliers" s
CROSS JOIN "Branches" b
WHERE s."IsDeleted" = FALSE
  AND b."IsDeleted" = FALSE
  AND s."TenantId" = b."TenantID"
  AND NOT EXISTS (
        SELECT 1
        FROM supplier_branch_assignments x
        WHERE x."SupplierId" = s."Id"
          AND x."BranchId" = b."BranchID"
          AND x."TenantId" = s."TenantId"
  );

INSERT INTO "Batches" (
    "Id", "ProductId", "BranchId", "BatchCode", "ManufactureDate", "ExpiryDate",
    "InitialQuantity", "LandedCostPerUnit", "CreatedAt", "IsDeleted", "TenantId"
)
SELECT
    gen_random_uuid(),
    bs."ProductId",
    bs."BranchId",
    COALESCE(bs."BatchCode", ''),
    COALESCE(bs."ManufactureDate", NOW()),
    COALESCE(bs."ExpiryDate", NOW() + INTERVAL '180 days'),
    bs."QuantityOnHand",
    bs."LandedCostPerUnit",
    NOW(),
    FALSE,
    bs."TenantId"
FROM "BranchStocks" bs
WHERE bs."IsDeleted" = FALSE
  AND NOT EXISTS (
        SELECT 1
        FROM "Batches" b
        WHERE b."ProductId" = bs."ProductId"
          AND b."BranchId" = bs."BranchId"
          AND b."BatchCode" = COALESCE(bs."BatchCode", '')
          AND b."TenantId" = bs."TenantId"
  );
