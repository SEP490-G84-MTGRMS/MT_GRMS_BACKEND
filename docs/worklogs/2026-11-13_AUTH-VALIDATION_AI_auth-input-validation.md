# AUTH-VALIDATION — Chuẩn hóa validation đăng nhập và đăng ký

## Lý do

Validation cũ chỉ yêu cầu mật khẩu tối thiểu 6 ký tự và phần hiển thị độ mạnh trên frontend không chặn đăng ký. Một số trường chưa có giới hạn đồng nhất giữa frontend và backend.

## Thay đổi

- Bổ sung FluentValidation cho login, login OTP, registration OTP và register.
- Mật khẩu đăng ký bắt buộc 8–64 ký tự, đủ chữ hoa, chữ thường, số, ký tự đặc biệt và không có whitespace.
- Kiểm tra họ tên, tên cửa hàng, email tối đa 100 ký tự, số điện thoại Việt Nam canonical, OTP 6 chữ số ASCII và `AgreeTerms = true`.
- Validation chạy trước repository, password hash và challenge mutation.
- Bổ sung unit tests backend và frontend cho boundary, password yếu, phone/email, terms và OTP.

## File chính

- `src/MT_GRMS.Application/Auth/AuthService.cs`
- `src/MT_GRMS.Application/Auth/Dtos/AuthDtos.cs`
- `src/MT_GRMS.Application/Auth/Validators/LoginRequestValidator.cs`
- `tests/MT_GRMS.UnitTests/AuthRequestValidatorTests.cs`

## Kiểm chứng

- Backend Application build bằng .NET SDK 8: PASS.
- Frontend validation unit tests: 9/9 PASS.
- Backend test assembly build: PASS; test runner của môi trường bị treo sau discovery, cần chạy lại trong bước `/sep-test`.

## Ghi chú cho Lead

`AgreeTerms` đổi mặc định từ `true` thành `false`; client phải gửi rõ `true` khi người dùng chủ động đồng ý.
