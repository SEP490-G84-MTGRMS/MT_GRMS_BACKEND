SELECT COUNT(*) AS suppliers FROM "Suppliers";
SELECT tablename FROM pg_tables WHERE schemaname='public' AND tablename ILIKE '%supplier%' ORDER BY 1;
