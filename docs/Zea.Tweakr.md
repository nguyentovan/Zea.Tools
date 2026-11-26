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

Nhấn **Win+R**, nhập `shell:startup` và tạo shortcut đến `Zea.Tweakr.exe` ở gốc thư mục portable trong thư mục vừa mở.
