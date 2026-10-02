# Script tạo nhanh file Worklog tóm tắt cho Backend (MT_GRMS_BACKEND)
# Cách dùng:
#   powershell -File .\scripts\New-Worklog.ps1 -Feature "FE-02" -Author "Duc" -Desc "fefo-batch-engine"

param(
    [string]$Feature = "FE-TASK",
    [string]$Author = $env:USERNAME,
    [string]$Desc = "feature-update"
)

$today = Get-Date -Format "yyyy-MM-dd"
$safeDesc = $Desc.ToLower() -replace '[^a-z0-9\-]', '-'
$fileName = "${today}_${Feature}_${Author}_${safeDesc}.md"
$worklogDir = Join-Path $PSScriptRoot "..\docs\worklogs"
$targetPath = Join-Path $worklogDir $fileName

# Lấy git branch hiện tại nếu có
$branch = "unknown"
try {
    $branch = (git branch --show-current 2>$null).Trim()
    if (-not $branch) { $branch = "feat/$Feature" }
} catch {
    $branch = "feat/$Feature"
}

if (-not (Test-Path $worklogDir)) {
    New-Item -ItemType Directory -Path $worklogDir -Force | Out-Null
}

if (Test-Path $targetPath) {
    Write-Host "[THÔNG BÁO] File worklog đã tồn tại: $targetPath" -ForegroundColor Yellow
    exit 0
}

$template = @"
# WORKLOG: [$Feature] - $Desc

- **Ngày thực hiện**: $today
- **Người thực hiện**: $Author
- **Nhánh Git (Branch)**: `$branch`
- **Mã tính năng liên kết**: [$Feature](../../../DOCS_MT_GRMS/specs/$Feature/spec.md)
- **Loại thay đổi**: Feature / Bugfix / Refactor / Architecture

---

### 1. Mục Đích Thay Đổi (Why)
- [Mô tả ngắn gọn 1-2 câu giải thích lý do thay đổi và bài toán được giải quyết]

### 2. Danh Sách Tệp Đã Sửa Đổi & Thêm Mới (What)
- `src/...`: [Mô tả chi tiết sửa gì, thêm class/method nào]

### 3. API Endpoints & Contract
- **Method & Route**: `[GET / POST / PUT / DELETE] /api/v1/...`
- **Request Body (DTO)**:
  ```json
  {}
  ```
- **Response Data (DTO)**:
  ```json
  {}
  ```
- **Database Migration**: [Tên migration hoặc Không có]
- **Breaking Changes**: [Không có hoặc mô tả cụ thể]

### 4. Kiểm Thử & Xác Nhận (Verification)
- [ ] Đã chạy `dotnet build MT_GRMS_BACKEND.sln`: Thành công (0 lỗi, 0 cảnh báo).
- [ ] Đã chạy `dotnet test MT_GRMS_BACKEND.sln`: Đạt 100%.
- [ ] Đã kiểm tra thử nghiệm qua Swagger UI.

---

### 5. Chỉ Dẫn Cho Manager Khi Duyệt & Merge (Manager Merge Notes)
- **Mức độ rủi ro (Risk Level)**: Thấp (Low) / Trung bình (Medium) / Cao (High)
- **File cần Manager review kỹ nhất**: `[Tên file và lý do]`
- **Hành động sau khi Merge**:
  - [ ] Chạy `dotnet ef database update` (nếu có migration).
  - [ ] Cập nhật file cấu hình `appsettings.json` / biến môi trường (nếu có).
  - [ ] Thông báo cho Frontend team về endpoint mới / sửa đổi.
"@

Set-Content -Path $targetPath -Value $template -Encoding UTF8
Write-Host "[THÀNH CÔNG] Đã tạo file worklog tại:" -ForegroundColor Green
Write-Host "  $targetPath" -ForegroundColor Cyan
Write-Host "Vui lòng cập nhật nội dung và git add cùng với code!" -ForegroundColor Yellow
