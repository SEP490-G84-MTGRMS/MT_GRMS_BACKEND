namespace MT_GRMS.Infrastructure.Persistence;

public sealed class HouseholdTaxSnapshotSchemaException(Exception innerException)
    : Exception("Không thể chuẩn hóa schema hồ sơ thuế. Backend đã dừng để tránh phục vụ dữ liệu lỗi.", innerException);
