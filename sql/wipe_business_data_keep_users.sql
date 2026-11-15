-- Wipe all business data; keep auth/org scaffolding.
-- KEEP: Users, TenantUsers, Tenants, Branches, Employees, SubscriptionPlans, __EFMigrationsHistory

BEGIN;

SELECT 'BEFORE' AS phase,
       (SELECT COUNT(*) FROM "Users") AS users,
       (SELECT COUNT(*) FROM "Products") AS products,
       (SELECT COUNT(*) FROM "Categories") AS categories,
       (SELECT COUNT(*) FROM "BranchStocks") AS stocks,
       (SELECT COUNT(*) FROM "Stocktakes") AS stocktakes,
       (SELECT COUNT(*) FROM "PurchaseOrders") AS pos,
       (SELECT COUNT(*) FROM "Suppliers") AS suppliers;

DO $$
DECLARE
  r RECORD;
  keep_tables text[] := ARRAY[
    'Users',
    'TenantUsers',
    'Tenants',
    'Branches',
    'Employees',
    'SubscriptionPlans',
    '__EFMigrationsHistory'
  ];
BEGIN
  FOR r IN
    SELECT c.relname AS tablename
    FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname = 'public'
      AND c.relkind = 'r'
      AND NOT (c.relname = ANY (keep_tables))
    ORDER BY c.relname
  LOOP
    EXECUTE format('TRUNCATE TABLE %I RESTART IDENTITY CASCADE', r.tablename);
    RAISE NOTICE 'Truncated %', r.tablename;
  END LOOP;
END $$;

SELECT 'AFTER' AS phase,
       (SELECT COUNT(*) FROM "Users") AS users,
       (SELECT COUNT(*) FROM "TenantUsers") AS tenant_users,
       (SELECT COUNT(*) FROM "Tenants") AS tenants,
       (SELECT COUNT(*) FROM "Branches") AS branches,
       (SELECT COUNT(*) FROM "Employees") AS employees,
       (SELECT COUNT(*) FROM "Products") AS products,
       (SELECT COUNT(*) FROM "Categories") AS categories,
       (SELECT COUNT(*) FROM "BranchStocks") AS stocks,
       (SELECT COUNT(*) FROM "Stocktakes") AS stocktakes,
       (SELECT COUNT(*) FROM "PurchaseOrders") AS pos,
       (SELECT COUNT(*) FROM "Suppliers") AS suppliers,
       (SELECT COUNT(*) FROM "Orders") AS orders;

COMMIT;
