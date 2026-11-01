-- Xóa các bản ghi mẫu tạm dummy cũ nếu có
DELETE FROM "BranchStocks" WHERE "BatchCode" LIKE 'LOT-%';
DELETE FROM "Products" WHERE "ProductCode" IN ('HH-TOM-CAY', 'COCA-320ML', 'REDBULL-250', 'LAVIE-500ML', 'VNM-100PCT', 'KEN-330ML', 'CHOCO-6P', 'SIMPLY-1L', 'GAO-ST25-5K', 'SUNLIGHT-750', 'AJI-454G', 'NAMNGU-900', 'OSTAR-90G', 'BOBBY-80T', 'COLGATE-180');
DELETE FROM "Customers" WHERE "CustomerCode" LIKE 'KH-POS%';

DO $$
DECLARE
    v_tenant_id VARCHAR(50) := 'tenant_mt_mart_01';
    v_cat_bev UUID := '01a185f3-d1c9-740d-9986-d284fa2a7179';   -- Nước & Đồ uống
    v_cat_dry UUID := '01a185f3-d1c9-7ebd-8434-a5678a68f1cc';   -- Bách hóa & Gạo hạt
    v_branch_hn UUID := '01a185f3-d197-72c1-b6e7-cdb5d5363467'; -- Chi nhánh Cầu Giấy (Hà Nội)

    -- UUIDv7 chuẩn RFC 9562 cho 15 sản phẩm tạp hóa
    p_haohao UUID := '01a19193-3445-771b-9bad-b226907aeb0a';
    p_coca UUID    := '01a19193-3498-72bb-90b8-0b37ebf66ff3';
    p_redbull UUID := '01a19193-34ae-7803-af4d-825a29166124';
    p_lavie UUID   := '01a19193-34be-79fa-9e52-9bd2d1c611ea';
    p_vnm UUID     := '01a19193-34cc-7cbe-a8d6-979c68c2df8c';
    p_ken UUID     := '01a19193-34d6-7ce2-8866-dc1144c6742a';
    p_choco UUID   := '01a19193-34e6-76d7-902b-f08497e88919';
    p_simply UUID  := '01a19193-34f5-7338-af59-0fa84b0f71f2';
    p_st25 UUID    := '01a19193-3508-7672-a864-de9e19891916';
    p_sunlight UUID:= '01a19193-3517-7be0-a7b4-b775ba3732aa';
    p_aji UUID     := '01a19193-3526-7765-b334-457dc420ce09';
    p_namngu UUID  := '01a19193-353a-7253-a9df-9c8884e57b59';
    p_ostar UUID   := '01a19193-3549-7763-8481-ca4e57baff8b';
    p_bobby UUID   := '01a19193-3560-7396-9ac5-fbb3b80bd877';
    p_colgate UUID := '01a19193-357b-78d1-9253-2cd824b853c4';

    -- UUIDv7 chuẩn RFC 9562 cho khách hàng
    c_son UUID     := '01a19193-358a-7a50-91db-c0497e2bd815';
    c_lan UUID     := '01a19193-3594-7596-9b7f-2542c0d94b6c';
    c_ba UUID      := '01a19193-35ae-798a-a530-d9c9953d13fd';
    c_dung UUID    := '01a19193-35b5-7c06-92ef-05c29f131a17';

BEGIN
    -- 1. Chèn danh mục sản phẩm chuẩn UUIDv7
    INSERT INTO "Products" ("ProductID", "CategoryID", "ProductCode", "Barcode", "Name", "BaseUnit", "CostPrice", "SellingPrice", "MinStock", "MaxStock", "VatRate", "ProductType", "Status", "HasBatches", "IsFavorite", "CreatedAt", "IsDeleted", "TenantID")
    VALUES
    (p_haohao, v_cat_dry, 'HH-TOM-CAY', '8934563138164', 'Mì Hảo Hảo tôm chua cay 75g', 'Gói', 3600, 4500, 10, 500, 1.0, 'STANDARD', 'Active', true, true, NOW(), false, v_tenant_id),
    (p_coca, v_cat_bev, 'COCA-320ML', '8935049500123', 'Nước ngọt Coca-Cola lon 320ml', 'Lon', 7800, 10000, 20, 500, 1.0, 'STANDARD', 'Active', true, true, NOW(), false, v_tenant_id),
    (p_redbull, v_cat_bev, 'REDBULL-250', '8850228000153', 'Nước tăng lực Redbull lon 250ml', 'Lon', 9500, 12000, 10, 300, 1.0, 'STANDARD', 'Active', true, true, NOW(), false, v_tenant_id),
    (p_lavie, v_cat_bev, 'LAVIE-500ML', '8936036010022', 'Nước khoáng Lavie chai 500ml', 'Chai', 4200, 6000, 24, 600, 1.0, 'STANDARD', 'Active', true, false, NOW(), false, v_tenant_id),
    (p_vnm, v_cat_bev, 'VNM-100PCT', '8934673123456', 'Sữa tươi tiệt trùng Vinamilk 100% 180ml', 'Hộp', 6800, 8500, 20, 400, 1.0, 'STANDARD', 'Active', true, true, NOW(), false, v_tenant_id),
    (p_ken, v_cat_bev, 'KEN-330ML', '8934822212345', 'Bia Heineken lon 330ml', 'Lon', 18500, 22000, 12, 300, 1.0, 'STANDARD', 'Active', true, false, NOW(), false, v_tenant_id),
    (p_choco, v_cat_dry, 'CHOCO-6P', '8936036020011', 'Bánh Chocopie Orion hộp 6 cái', 'Hộp', 27000, 35000, 5, 100, 1.0, 'STANDARD', 'Active', true, false, NOW(), false, v_tenant_id),
    (p_simply, v_cat_dry, 'SIMPLY-1L', '8934561230018', 'Dầu ăn đậu nành Simply chai 1 lít', 'Chai', 45000, 55000, 5, 80, 1.0, 'STANDARD', 'Active', true, false, NOW(), false, v_tenant_id),
    (p_st25, v_cat_dry, 'GAO-ST25-5K', '8935001780099', 'Gạo ST25 Ông Cua túi 5kg Thượng Hạng', 'Túi', 150000, 180000, 5, 50, 1.0, 'STANDARD', 'Active', true, true, NOW(), false, v_tenant_id),
    (p_sunlight, v_cat_dry, 'SUNLIGHT-750', '8934561234567', 'Nước rửa chén Sunlight Chanh túi 750g', 'Túi', 24000, 30000, 5, 100, 1.0, 'STANDARD', 'Active', true, false, NOW(), false, v_tenant_id),
    (p_aji, v_cat_dry, 'AJI-454G', '8934568910023', 'Bột ngọt Ajinomoto gói 454g', 'Gói', 26000, 32000, 5, 80, 1.0, 'STANDARD', 'Active', true, false, NOW(), false, v_tenant_id),
    (p_namngu, v_cat_dry, 'NAMNGU-900', '8934567890124', 'Nước mắm Nam Ngư Đệ Nhị 900ml', 'Chai', 22000, 28000, 5, 120, 1.0, 'STANDARD', 'Active', true, false, NOW(), false, v_tenant_id),
    (p_ostar, v_cat_dry, 'OSTAR-90G', '8936036030010', 'Snack khoai tây OStar gói 90g', 'Gói', 14000, 18000, 10, 150, 1.0, 'STANDARD', 'Active', true, false, NOW(), false, v_tenant_id),
    (p_bobby, v_cat_dry, 'BOBBY-80T', '8936036040019', 'Khăn ướt em bé Bobby 80 tờ', 'Gói', 19000, 25000, 10, 100, 1.0, 'STANDARD', 'Active', true, false, NOW(), false, v_tenant_id),
    (p_colgate, v_cat_dry, 'COLGATE-180', '8934569876543', 'Kem đánh răng Colgate Total 180g', 'Tuýp', 34000, 42000, 5, 60, 1.0, 'STANDARD', 'Active', true, false, NOW(), false, v_tenant_id)
    ON CONFLICT ("Barcode") DO UPDATE 
    SET "Name" = EXCLUDED."Name", "SellingPrice" = EXCLUDED."SellingPrice", "CostPrice" = EXCLUDED."CostPrice", "Status" = 'Active', "IsDeleted" = false;

    -- 2. Chèn Tồn kho Chi Nhánh BranchStocks theo FEFO
    -- Lô 1 (hết hạn 11/2026: 25 gói)
    INSERT INTO "BranchStocks" ("Id", "BranchId", "ProductId", "BatchCode", "ManufactureDate", "ExpiryDate", "QuantityOnHand", "QuantityReserved", "LandedCostPerUnit", "ShelfLocation", "ZoneArea", "RowVersion", "CreatedAt", "IsDeleted", "TenantId")
    VALUES 
    ('01a19193-35c4-7a6d-8314-6ed2f5127929', v_branch_hn, p_haohao, 'LOT-HH2611', NOW() - INTERVAL '3 month', NOW() + INTERVAL '1 month', 25.000, 0, 3600, 'Quầy 01', 'Khu Bán Lẻ', 1, NOW(), false, v_tenant_id),
    ('01a19193-35d0-7a4f-a9f6-9615f2b00243', v_branch_hn, p_haohao, 'LOT-HH2705', NOW() - INTERVAL '1 month', NOW() + INTERVAL '7 month', 100.000, 0, 3650, 'Kho Phụ', 'Khu Kho', 1, NOW(), false, v_tenant_id),
    -- Coca-Cola
    ('01a19193-35e0-7a10-b2f7-4fed5c1c5cf9', v_branch_hn, p_coca, 'LOT-COCA2612', NOW() - INTERVAL '2 month', NOW() + INTERVAL '2 month', 40.000, 0, 7800, 'Tủ Mát 01', 'Đồ Uống', 1, NOW(), false, v_tenant_id),
    ('01a19193-35f0-7521-bc6a-3c0f36ee32ee', v_branch_hn, p_coca, 'LOT-COCA2708', NOW() - INTERVAL '1 month', NOW() + INTERVAL '10 month', 120.000, 0, 7800, 'Kho Đồ Uống', 'Khu Kho', 1, NOW(), false, v_tenant_id),
    -- Redbull
    ('01a19193-35ff-7482-8ee1-b5db78035c4f', v_branch_hn, p_redbull, 'LOT-RB2701', NOW() - INTERVAL '2 month', NOW() + INTERVAL '6 month', 180.000, 0, 9500, 'Tủ Mát 01', 'Đồ Uống', 1, NOW(), false, v_tenant_id),
    -- Lavie
    ('01a19193-3612-79f8-8e77-862945340a78', v_branch_hn, p_lavie, 'LOT-LAVIE2703', NOW() - INTERVAL '2 month', NOW() + INTERVAL '8 month', 200.000, 0, 4200, 'Kệ Nước 02', 'Đồ Uống', 1, NOW(), false, v_tenant_id),
    -- Vinamilk
    ('01a19193-3628-7a3e-aa63-4d908f6c8d1a', v_branch_hn, p_vnm, 'LOT-VNM2612', NOW() - INTERVAL '1 month', NOW() + INTERVAL '3 month', 140.000, 0, 6800, 'Tủ Mát Sữa', 'Khu Sữa', 1, NOW(), false, v_tenant_id),
    -- Heineken
    ('01a19193-3634-72de-b923-c0379262e1cd', v_branch_hn, p_ken, 'LOT-KEN2705', NOW() - INTERVAL '2 month', NOW() + INTERVAL '9 month', 90.000, 0, 18500, 'Kệ Bia 01', 'Đồ Uống', 1, NOW(), false, v_tenant_id),
    -- Chocopie
    ('01a19193-3644-720c-89de-7edbc1ae1fdb', v_branch_hn, p_choco, 'LOT-CHOCO2702', NOW() - INTERVAL '1 month', NOW() + INTERVAL '6 month', 75.000, 0, 27000, 'Kệ Bánh 01', 'Bánh Kẹo', 1, NOW(), false, v_tenant_id),
    -- Simply
    ('01a19193-3656-7cb6-bad2-86706739f8c1', v_branch_hn, p_simply, 'LOT-SIMPLY2709', NOW() - INTERVAL '1 month', NOW() + INTERVAL '12 month', 45.000, 0, 45000, 'Kệ Dầu Ăn', 'Gia Vị', 1, NOW(), false, v_tenant_id),
    -- Gạo ST25
    ('01a19193-3667-7b0d-952d-127a72dcf870', v_branch_hn, p_st25, 'LOT-ST25-2704', NOW() - INTERVAL '1 month', NOW() + INTERVAL '7 month', 30.000, 0, 150000, 'Khu Gạo', 'Lương Thực', 1, NOW(), false, v_tenant_id)
    ON CONFLICT DO NOTHING;

    -- 3. Chèn Khách hàng mẫu chuẩn UUIDv7
    INSERT INTO "Customers" ("CustomerID", "CustomerCode", "Name", "Phone", "Email", "Address", "MembershipTier", "RewardPoints", "TotalSpending", "TotalDebt", "DebtLimit", "IsBlockedForDebt", "AllowDebt", "CreatedAt", "IsDeleted", "TenantID")
    VALUES
    (c_son, 'KH-POS-001', 'Nguyễn Văn Khách (Thân Thiết)', '0912345678', 'khach.nguyen@gmail.com', '15 Cầu Giấy, Hà Nội', 'Gold', 250, 5500000, 350000, 5000000, false, true, NOW(), false, v_tenant_id),
    (c_lan, 'KH-POS-002', 'Chị Lan Tạp Hóa Xóm', '0987654321', 'lan.taphoa@gmail.com', 'Ngõ 165 Cầu Giấy, Hà Nội', 'Platinum', 520, 18000000, 2100000, 10000000, false, true, NOW(), false, v_tenant_id),
    (c_ba,  'KH-POS-003', 'Bác Ba (Bị Khóa Nợ)', '0905123456', 'bacba@gmail.com', 'Phố Dịch Vọng, Hà Nội', 'Standard', 20, 1200000, 1200000, 1000000, true, false, NOW(), false, v_tenant_id),
    (c_dung,'KH-POS-004', 'Anh Dũng Khách Lẻ VIP', '0934567890', 'dung.vip@gmail.com', 'Xuân Thủy, Cầu Giấy, Hà Nội', 'VIP', 820, 24000000, 0, 0, false, false, NOW(), false, v_tenant_id)
    ON CONFLICT ("TenantID", "CustomerCode") DO UPDATE
    SET "Name" = EXCLUDED."Name", "Phone" = EXCLUDED."Phone", "RewardPoints" = EXCLUDED."RewardPoints", "TotalDebt" = EXCLUDED."TotalDebt", "DebtLimit" = EXCLUDED."DebtLimit", "AllowDebt" = EXCLUDED."AllowDebt", "IsBlockedForDebt" = EXCLUDED."IsBlockedForDebt";

END $$;

