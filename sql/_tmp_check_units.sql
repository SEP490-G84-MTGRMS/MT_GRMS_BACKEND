SELECT column_name FROM information_schema.columns
WHERE table_name = 'Products' AND column_name ILIKE '%id%'
ORDER BY 1;

SELECT column_name FROM information_schema.columns
WHERE table_name = 'ProductUnits'
ORDER BY ordinal_position;

SELECT p."ProductCode", p."Name", p."BaseUnit",
       u."UnitName", u."IsBaseUnit", u."IsPurchaseUnit", u."ConversionRate"
FROM "Products" p
LEFT JOIN "ProductUnits" u ON u."ProductID" = p."ProductID" AND COALESCE(u."IsDeleted", false) = false
WHERE p."Name" ILIKE '%Number One%' OR p."ProductCode" = 'SP000001'
ORDER BY p."ProductCode", u."IsBaseUnit" DESC NULLS LAST, u."UnitName";
