# Zea.Tweakr

Ứng dụng Windows của [Zea.Tools](../README.md), chạy ở khay hệ thống và cung cấp phím tắt cho File Explorer, SSMS và DBeaver.

Tải ZIP portable từ [GitHub Releases](https://github.com/nguyentovan/Zea.Tools/releases/latest), giải nén toàn bộ và chạy `Zea.Tweakr.exe` ở gốc. Xem [hướng dẫn portable và cập nhật](releases.md) để biết cấu trúc thư mục và cách phát hành.

## Sử dụng

| Phím tắt | Thao tác |
| --- | --- |
| **Win+E** | Mở File Explorer hoặc thêm tab vào cửa sổ hiện có. |
| **Ctrl+K, Ctrl+Y** | Định dạng SQL đang chọn. |
| **Ctrl+K, Ctrl+G** | Chuyển log iBatis đang chọn thành SQL. Định dạng kết quả nếu tính năng Format được bật. |

Mở **Cài đặt** từ biểu tượng khay để bật hoặc tắt từng chức năng. Giữ **Ctrl**, nhấn **K**, rồi nhấn **Y** hoặc **G** để dùng phím tắt SQL.

SSMS sử dụng dialect T-SQL. Với DBeaver, ứng dụng nhận diện dialect từ thông tin của tab. Xem [công cụ SQL](sql-tools.md) để chạy formatter và converter độc lập.

## Tự chạy cùng Windows

Mở **Cài đặt** từ biểu tượng khay, chọn **Khởi động cùng Windows**, rồi chọn **Lưu cài đặt**. Tùy chọn mặc định tắt và chỉ áp dụng cho tài khoản Windows hiện tại, không cần quyền quản trị. Khi đăng nhập, ứng dụng chạy trong khay hệ thống.

Để tắt, bỏ chọn **Khởi động cùng Windows** rồi lưu. Nếu di chuyển thư mục portable, mở ứng dụng ở vị trí mới và lưu lại tùy chọn để cập nhật đường dẫn. Nếu đã tạo shortcut thủ công trong `shell:startup`, xóa shortcut đó để tránh chạy hai lần.
