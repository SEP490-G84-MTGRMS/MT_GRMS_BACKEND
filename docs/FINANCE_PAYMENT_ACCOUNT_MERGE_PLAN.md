# Phương án gộp tài khoản thanh toán: một chi nhánh một tài khoản hiện hành

**Ngày cập nhật:** 17/11/2026  
**Trạng thái:** Phương án thiết kế và chuyển đổi dữ liệu; chưa sửa entity, API hoặc database.

## 1. Quyết định nghiệp vụ

- **Mỗi chi nhánh có tối đa một tài khoản ngân hàng đang được chọn để nhận thanh toán.** Khi chưa cấu hình thì không thể phát QR cho chi nhánh. Giao diện POS không cho chọn giữa nhiều tài khoản.
- **Một số tài khoản ngân hàng chỉ thuộc một chi nhánh trong cùng tenant.** Nếu thực tế một số tài khoản phải dùng chung ở hai chi nhánh, phương án gộp một dòng FinanceAccount cho một tài khoản vật lý không còn phù hợp và cần thiết kế lại.
- Gộp PaymentAccount vào FinanceAccount: dòng AccountType = Bank chứa thông tin ngân hàng và số dư sổ nội bộ. Không còn hai nơi lưu số tài khoản.
- Chuyển cấu hình SePay và chính sách chi cấp cửa hàng từ StorePaymentConfig sang Tenant; bỏ các trường ngân hàng trên StorePaymentConfig thay vì chuyển chúng sang Tenant.
- Đưa **liên kết tài khoản nhận tiền hiện hành** từ BranchPaymentAccount vào Branch.CurrentBankFinanceAccountId. Bỏ BranchPaymentAccount sau khi chuyển dữ liệu.
- Giữ dòng FinanceAccount Bank cũ khi chi nhánh đổi tài khoản. “Một chi nhánh một tài khoản” ở đây là **một tài khoản hiện hành tại một thời điểm**, không phải chỉ một dòng tài khoản được phép tồn tại mãi mãi.

CashbookEntry, PaymentTransaction, Order, CashInTransit, ShiftZReport và BusinessDayClosing vẫn là bảng nghiệp vụ riêng vì mỗi bảng ghi một sự kiện hoặc chứng từ khác nhau.

## 2. Mô hình dữ liệu đích và lý do

### 2.1 Branch và FinanceAccount

| Vị trí | Dữ liệu lưu | Vai trò |
|---|---|---|
| Branch.CurrentBankFinanceAccountId (nullable FK) | ID tài khoản Bank đang nhận thanh toán cho chi nhánh | Chọn duy nhất tài khoản để phát QR **mới**. NULL nghĩa là chưa cấu hình/đang dừng QR. |
| FinanceAccount.BranchId | Chi nhánh sở hữu dòng sổ Bank, kể cả sau khi ngừng nhận QR | Giữ phân quyền, báo cáo và lịch sử theo chi nhánh. |
| FinanceAccount BankCode, BankName, AccountNumber, AccountHolderName | Danh tính ngân hàng | Một nguồn thông tin ngân hàng duy nhất sau khi gộp PaymentAccount. |
| FinanceAccount.CurrentBalance, RowVersion | Số dư **sổ nội bộ** và kiểm soát cập nhật đồng thời | Không coi CurrentBalance là số dư ngân hàng thực do SePay cung cấp. |

Hai FK trên **không cùng nghĩa**: FinanceAccount.BranchId nói tài khoản thuộc chi nhánh nào; Branch.CurrentBankFinanceAccountId nói chi nhánh đang chọn tài khoản nào. Một chi nhánh có thể có nhiều Bank trong **lịch sử**, nhưng chỉ một Bank được chọn hiện hành. Không cần IsDefaultForVietQr hoặc bảng nối để lặp lại lựa chọn đó.

Ràng buộc cần có:

1. CurrentBankFinanceAccountId phải trỏ đến Bank cùng TenantId và cùng BranchId; kiểm tra trong transaction khi đổi tài khoản và ràng buộc database tương ứng nếu schema/EF cho phép. FK xóa dùng Restrict/NoAction để tránh xóa dây chuyền hoặc vòng cascade.
2. Dòng Bank có BranchId, BankCode, AccountNumber, AccountHolderName hợp lệ. Dòng Till/Safe không thể được chọn làm tài khoản nhận QR.
3. Unique danh tính Bank theo tenant + BankCode + AccountNumber sau chuẩn hóa, kể cả khi dòng đã ngừng nhận QR; cùng một số tài khoản không được nhân bản thành sổ của chi nhánh khác. Cần chính sách xử lý soft delete phù hợp với lịch sử.
4. **Không** đặt unique vô điều kiện trên FinanceAccount(TenantId, BranchId) cho mọi dòng Bank: quy tắc đó ngăn giữ MB cũ sau khi đổi sang VCB. Một FK đơn trên Branch đã bảo đảm chỉ có một lựa chọn hiện hành.
5. Không sửa BankCode/AccountNumber trên dòng Bank đã có giao dịch để “đổi tài khoản”; tạo dòng Bank mới, chuyển con trỏ trên Branch, giữ dòng cũ cho lịch sử.
6. IsActive của FinanceAccount là trạng thái sổ; không dùng nó thay cho con trỏ nhận QR. Tài khoản cũ có thể vẫn cần xử lý đơn đã phát QR và tiền đến muộn.

Nếu chủ cửa hàng muốn một tài khoản được gán lại cho chi nhánh khác, phải định nghĩa quy trình chuyển quyền sở hữu và đối soát lịch sử riêng; kế hoạch hiện tại không hỗ trợ thao tác đó.

### 2.2 Tenant

| Trường đề xuất trên Tenant | Ý nghĩa |
|---|---|
| SepayApiKeyEncrypted, SepayWebhookSecret | Khóa tích hợp SePay chung cho tenant; không trả giá trị gốc/ciphertext qua API công khai. |
| IsSepayEnabled | Bật/tắt cổng SePay cấp cửa hàng. |
| CashOutApprovalThreshold | Ngưỡng một khoản chi trong ca cần duyệt. |
| CashOutShiftTotalLimit | Hạn mức tổng chi trong ca; phải chốt rõ cách xử lý khi vượt. |

BankCode, BankName, BankAccountNumber, BankAccountName của StorePaymentConfig chuyển về đúng dòng FinanceAccount Bank của chi nhánh, **không** chuyển sang Tenant. Một tenant có nhiều chi nhánh, nên đặt một số tài khoản lên Tenant sẽ lại gây trùng hoặc nhầm phạm vi.

Điều kiện của việc đưa khóa SePay lên Tenant: một cặp key/secret dùng chung cho cửa hàng. Nếu SePay cấp key khác nhau cho từng ngân hàng/chi nhánh, cần thực thể kết nối SePay riêng, không thể ép các key đó vào một cặp cột trên Tenant. Tenant không có global tenant filter như BaseTenantEntity, nên API phải kiểm tra tenant và vai trò tường minh.

### 2.3 Tham chiếu lịch sử

Order.PaymentAccountId và PaymentTransaction.PaymentAccountId được chuyển sang FinanceAccountId bằng migration có ánh xạ dữ liệu. CashbookEntry.AccountId và CashInTransit.TargetBankAccountId đã tham chiếu FinanceAccount và tiếp tục như vậy. Đơn/giao dịch/bút toán cũ giữ ID Bank cũ; tuyệt đối không suy lại tài khoản từ Branch.CurrentBankFinanceAccountId tại thời điểm đọc báo cáo.

**Ví dụ:** Ngày 01, chi nhánh A nhận qua MB 111; đơn #01 và bút toán #01 trỏ FinanceAccount MB 111. Ngày 10, chủ cửa hàng đổi sang VCB 222 bằng cách tạo Bank VCB và cập nhật Branch.CurrentBankFinanceAccountId. Đơn #11 mới dùng VCB 222; đơn #01 vẫn dùng MB 111. A chỉ có **một tài khoản cho QR mới** ở mỗi ngày, nhưng database có hai dòng Bank vì cần lịch sử.

## 3. Chức năng cần thay đổi

### 3.1 Trang cấu hình tài khoản và SePay

- Trang StorePaymentConfigPage.tsx vẫn là nơi chủ cửa hàng cấu hình, nhưng hiển thị mỗi chi nhánh một tài khoản nhận tiền hiện hành và lịch sử tài khoản đã thay. Không còn danh sách chọn nhiều tài khoản để nhận QR đồng thời.
- Tách SavePaymentConfig của PaymentsController: phần key/secret, bật SePay và hạn mức ghi lên Tenant; phần ngân hàng tạo hoặc chọn FinanceAccount Bank rồi cập nhật Branch.CurrentBankFinanceAccountId trong một transaction. Không ghi lại số tài khoản vào Tenant hay một bảng cấu hình thứ hai.
- Trả trạng thái hasApiKey/masked; không đưa secret vào response, log hoặc localStorage. Các chức năng revoke, đồng bộ danh sách ngân hàng từ SePay và test bảo mật đọc cấu hình Tenant; dữ liệu đồng bộ chỉ là gợi ý cho người dùng chọn đúng tài khoản.
- Khi đổi tài khoản, UI cần hiển thị tác động: QR mới dùng tài khoản mới; QR/đơn đã phát vẫn đối soát trên tài khoản cũ. Không xóa hoặc ghi đè Bank cũ.
- Route frontend hiện tại là /store-owner/payment-config. API public-qr-config lấy tài khoản từ Branch.CurrentBankFinanceAccountId, không fallback sang số tài khoản ở cấu hình tenant.

### 3.2 POS, VietQR và đơn hàng

- VietQrModal.tsx chỉ nhận một tài khoản hiện hành của chi nhánh; không cần bộ chọn Bank.
- Khi tạo đơn/ý định thanh toán, backend chốt FinanceAccountId vào Order **trước khi phát QR**. Dữ liệu QR phải lấy từ chính ID đó. Tải lại giao diện không được âm thầm thay QR của đơn cũ khi Branch đổi tài khoản.
- Nếu Branch chưa có tài khoản hợp lệ hoặc SePay bị tắt, trả lỗi rõ ràng và không phát QR. Các giao dịch tiền mặt vẫn theo luồng riêng.
- Cần quy định hạn thanh toán QR và xử lý tiền vào tài khoản cũ sau khi đổi: đơn cũ còn hiệu lực được đối soát; khoản không khớp/đến muộn chuyển vào hàng chờ đối soát, không gạch nợ nhầm đơn mới.

### 3.3 Webhook SePay và sổ quỹ

- Xác thực webhook bằng key/secret của Tenant; chống replay bằng định danh giao dịch SePay có unique phù hợp. Xác định Order, đọc Order.FinanceAccountId đã khóa lúc tạo QR và đối chiếu tài khoản thực nhận từ payload hoặc dữ liệu đối soát tin cậy. Nếu không có dữ liệu đủ tin cậy để xác nhận tài khoản thụ hưởng, phải thiết kế bước đối soát bổ sung trước khi tự động gạch nợ.
- Không dùng Branch.CurrentBankFinanceAccountId để sửa tài khoản của đơn cũ. Webhook chuyển vào tài khoản khác phải được ghi nhận và xử lý đối soát thủ công.
- Một luồng duy nhất chịu trách nhiệm ghi thu ngân hàng. Phương án đề xuất: trong một transaction/idempotency boundary, tạo PaymentTransaction, cập nhật Order, tạo đúng một CashbookEntry và tăng CurrentBalance của Bank tương ứng. Nếu code chọn ghi sổ vào lúc khác, phải bỏ bước ghi ở webhook; chốt ca/ngày không được ghi lặp.
- Các khoản chuyển khoản thủ công cần xác nhận đã nhận tiền trước khi ghi vào Bank; không giả định mọi đơn non-cash đều đã về ngân hàng.

### 3.4 Sổ quỹ, nộp tiền, chuyển tiền, chi tiền

- CashbookService.GetOrCreateBankAccountAsync hiện chọn Bank đầu tiên hoặc có thể tạo Bank. Sau chuyển đổi, nghiệp vụ nhận tiền mặc định dùng Branch.CurrentBankFinanceAccountId; không tự tạo tài khoản ngân hàng giả và không chọn FirstOrDefault. Với giao dịch đã định danh tài khoản nguồn/đích, dùng ID đã lưu trên chứng từ.
- CashInTransit.TargetBankAccountId tiếp tục trỏ đúng Bank được chọn lúc lập lệnh. Đổi tài khoản nhận QR của Branch không được đổi đích của lệnh nộp tiền đang chờ.
- Chi lương, chi phí, thuế và chuyển quỹ cần xác định Bank theo chính sách đã chốt: dùng tài khoản hiện hành cho giao dịch mới nếu phù hợp, hoặc yêu cầu ID cụ thể cho chứng từ dùng tài khoản cũ. Kiểm tra tenant, branch, AccountType và quyền thao tác.
- ShiftService và ShiftRepository chuyển sang đọc hạn mức chi ở Tenant. CashOutShiftTotalLimit hiện mới được lưu/trả về; khi triển khai phải thực thi tại bước tạo/duyệt phiếu chi với kiểm soát cạnh tranh đồng thời. Hạn mức của ExpenseService là quy tắc khác, không tự gộp.

### 3.5 Chốt ca, chốt ngày và báo cáo

- ShiftZReport.TotalBankRevenue và BusinessDayClosing.TotalBankSales tiếp tục là snapshot tổng của ca/ngày. Truy vấn chi tiết theo Bank dựa trên FinanceAccountId của giao dịch và CashbookEntry.AccountId; không lấy tài khoản hiện hành để gán ngược toàn bộ doanh thu lịch sử.
- Rà soát ShiftRepository.GetShiftSalesMetricsAsync, OrderService.SettleOwnerDailyOrdersAsync, BusinessDayClosingService.CloseAsync và đường RecordShiftCashbookSettlementAsync. Các luồng chốt không post lại khoản VietQR đã được webhook ghi sổ. Trong ngày đổi MB sang VCB, báo cáo tổng ngày vẫn đúng, còn chi tiết tách theo MB/VCB dựa trên giao dịch thực tế.
- Giữ số liệu thuế/snapshot đã chốt; migration FK tài khoản không được làm thay đổi tổng doanh thu, số dư hoặc lịch sử đối soát.

## 4. Chuyển dữ liệu

1. **Kiểm kê:** tìm PaymentAccount gán nhiều chi nhánh, chi nhánh có hơn một gán hiện hành, Bank FinanceAccount trùng số tài khoản/thiếu BranchId, StorePaymentConfig mâu thuẫn với PaymentAccount, Order/PaymentTransaction chưa có tài khoản, và sổ Bank hiện có không khớp giao dịch. Không tự đoán tài khoản đúng.
2. **Giải quyết ngoại lệ:** nếu một số tài khoản dùng chung hai chi nhánh, cần chủ nghiệp vụ chọn tài khoản riêng cho từng nhánh hoặc sửa quyết định mô hình. Nếu một chi nhánh có nhiều tài khoản hiện hành, chọn đúng một tài khoản cho QR mới; những tài khoản còn lại được giữ như lịch sử nếu có giao dịch. Đối chiếu số dư trước khi gộp, không cộng các dòng trùng.
3. **Migration mở rộng:** thêm trường BankCode cho FinanceAccount, trường cấu hình trên Tenant, Branch.CurrentBankFinanceAccountId và cột FinanceAccountId trên Order/PaymentTransaction; thêm FK, index và check. Backfill từng quan hệ cũ qua bảng ánh xạ old PaymentAccountId + BranchId → FinanceAccountId, sau đó chọn con trỏ hiện hành cho từng Branch.
4. **Chuyển ứng dụng:** cập nhật Domain, EF configuration, migrations/model snapshot, DbInitializer, PaymentsController, service/repository và frontend cùng thời điểm tương thích. Tài khoản cũ đã có đơn/giao dịch/bút toán không bị xóa cứng.
5. **Đối chiếu và bỏ bảng:** kiểm tra số lượng/tổng tiền theo tenant, branch và Bank, khóa SePay giải mã được, QR mới đúng Bank, đơn cũ còn đối soát, số dư bằng bút toán, và webhook lặp không ghi hai lần. Sau đó mới ngừng ghi và bỏ PaymentAccounts, BranchPaymentAccounts, StorePaymentConfigs cùng FK cũ. Có phương án khôi phục dữ liệu trước khi drop.

## 5. Tình huống kiểm thử bắt buộc

| Tình huống | Kết quả cần có |
|---|---|
| A đang nhận MB 111, đổi sang VCB 222. | QR mới chỉ VCB; đơn/PaymentTransaction/CashbookEntry cũ vẫn chỉ MB; báo cáo theo Bank đúng. |
| Đơn MB 111 phát QR trước lúc đổi, webhook đến sau lúc đổi. | Nếu đơn còn hiệu lực và tiền thực nhận đúng MB, đối soát đơn MB; không chuyển nó sang VCB. |
| Gửi lặp webhook cho cùng giao dịch. | Một PaymentTransaction, một CashbookEntry, một lần tăng số dư và một lần gạch nợ. |
| Webhook cho đơn MB nhưng tiền vào VCB. | Không tự gạch nợ; đưa vào đối soát. |
| A chưa có tài khoản hoặc tài khoản hiện hành không hợp lệ. | Không phát VietQR; thông báo rõ cho chủ cửa hàng/POS. |
| MB 111 của A được gán cho B. | Từ chối theo quy tắc một số tài khoản thuộc một chi nhánh. |
| A đổi Bank trong ngày sau khi đã có doanh thu. | Tổng ngày đúng; chi tiết MB và VCB đúng; chốt ngày không post lặp. |
| Lệnh nộp tiền đang chờ vào MB, sau đó A đổi nhận QR sang VCB. | Lệnh vẫn trỏ MB, không bị cập nhật theo con trỏ mới. |
| Một người khác sửa Branch/Bank cùng lúc đổi tài khoản. | Transaction/concurrency kiểm tra để Branch không trỏ sai tenant, sai chi nhánh hoặc Bank không hợp lệ. |
| Chi trong ca vượt CashOutShiftTotalLimit. | Bị chặn/chuyển duyệt theo quy tắc nghiệp vụ đã chốt, không chỉ lưu giới hạn. |

## 6. Phản biện trước hội đồng

**Cách phát biểu yêu cầu:** “Mỗi chi nhánh có tối đa một tài khoản nhận VietQR hiện hành. Khi đổi tài khoản, tài khoản cũ được giữ để truy vết đơn và bút toán. Một số tài khoản không được dùng đồng thời ở hai chi nhánh.” Cách nói này xác định rõ phạm vi và thời điểm của quy tắc một-một.

| Câu hỏi hội đồng | Trả lời và minh chứng |
|---|---|
| Vì sao bỏ BranchPaymentAccount? | Bảng đó lưu lựa chọn hiện hành của chi nhánh. Với tối đa một lựa chọn, một FK nullable trên Branch đủ biểu diễn; không cần một dòng bảng nối cho từng Branch. Lịch sử nằm ở FinanceAccount và FK của giao dịch. |
| Sao không chỉ dùng FinanceAccount.BranchId? | BranchId là quyền sở hữu. Sau khi đổi, A có cả Bank cũ MB và Bank mới VCB trong lịch sử; chỉ Branch.CurrentBankFinanceAccountId cho biết Bank nào dùng cho QR mới. |
| Hai FK Branch↔FinanceAccount có trùng dữ liệu? | Không: một FK xác định sở hữu, FK còn lại xác định lựa chọn hiện hành. Hệ thống phải ràng buộc chúng nhất quán trong DB/transaction. Nếu chỉ có một Bank trong toàn bộ lịch sử thì FK ngược mới có thể là dư thừa, nhưng yêu cầu đổi tài khoản an toàn không cho phép xóa lịch sử. |
| Vì sao vẫn có nhiều dòng Bank của A nếu nói một chi nhánh một tài khoản? | Chỉ một dòng được chọn **hiện hành**. Những dòng cũ là chứng cứ của đơn, webhook, bút toán và số dư trước ngày đổi; unique trên BranchId của tất cả Bank sẽ xóa mất khả năng này. |
| Đổi số tài khoản bằng UPDATE dòng Bank cũ có gọn hơn không? | Không. Đơn cũ trỏ cùng ID sẽ bị hiểu sai là đã thanh toán vào số mới. Tạo Bank mới rồi đổi con trỏ Branch bảo toàn ý nghĩa lịch sử. |
| Có thể bỏ PaymentAccount và StorePaymentConfig thật không? | Có với hai điều kiện kiểm chứng: số tài khoản chỉ thuộc một chi nhánh, và SePay dùng một cặp key/secret cho tenant. Bank nằm ở FinanceAccount; chính sách/key ở Tenant. Nếu điều kiện sai thì cần mô hình khác. |
| Tiền của QR cũ đến sau ngày đổi thì sao? | Order giữ FinanceAccountId cũ; webhook đối chiếu đúng Bank cũ trong thời hạn thanh toán, khoản đến muộn hoặc không khớp đi vào đối soát. |
| Tại sao không chỉ lưu một cột Bank trên Tenant? | Tenant có thể có nhiều Branch; một cột Bank trên Tenant không cho biết chi nhánh nào nhận tiền và làm lặp dữ liệu khi thêm cấu hình theo chi nhánh. |
| Chốt ngày có bị sai khi đổi giữa ngày? | Chốt từ giao dịch và bút toán theo Bank ID đã khóa tại thời điểm phát sinh, không dựa vào Bank hiện hành. Kiểm thử ngày đổi tài khoản là bằng chứng. |

**Minh chứng code hiện tại nên trình bày:** PaymentAccountConfiguration.cs giới hạn một liên kết/chi nhánh; PaymentsController.SavePaymentConfig thay gán tài khoản; CashbookService.GetOrCreateBankAccountAsync có nhánh chọn Bank đầu tiên; BusinessDayClosingService.CloseAsync và OrderService.SettleOwnerDailyOrdersAsync cần rà soát việc post doanh thu Bank; Order.PaymentAccountId hiện có thể được gán muộn ở webhook. Phương án đề xuất sửa các điểm này và kiểm thử các tình huống ở mục 5.

## 7. File/luồng khi triển khai

- Domain: Branch.cs, FinanceAccount.cs, PaymentAccount.cs, BranchPaymentAccount.cs, StorePaymentConfig.cs, Tenant.cs, Order.cs, PaymentTransaction.cs.
- EF/database: AppDbContext.cs, cấu hình FinanceAccount/PaymentAccount/Branch, migration, model snapshot, DbInitializer.cs.
- API/application: PaymentsController.cs, CashbookService.cs, ShiftService.cs, ShiftRepository.cs, OrderService.cs, BusinessDayClosingService.cs và các truy vấn báo cáo.
- Frontend: StorePaymentConfigPage.tsx, VietQrModal.tsx, PosCheckoutPage.tsx và API client/DTO liên quan.

Tài liệu này là quyết định thiết kế; triển khai cần kiểm kê dữ liệu thực tế và chốt chính sách thanh toán QR cũ, tiền đến muộn, hạn mức chi trước khi xóa bảng.
