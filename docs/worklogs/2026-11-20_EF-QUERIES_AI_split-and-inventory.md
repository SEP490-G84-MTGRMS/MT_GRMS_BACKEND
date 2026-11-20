# Tối ưu truy vấn EF Core

Áp dụng split query tại các truy vấn tải nhiều danh sách của tenant, nhà cung cấp và hàng hóa theo chi nhánh. Phân trang tenant có khóa phụ Id. Tổng hợp tồn kho lọc chi nhánh tại database và nhóm lô, dòng trả hàng để tra cứu theo cặp chi nhánh/sản phẩm. Công thức và thứ tự phân loại tồn kho được giữ nguyên.
