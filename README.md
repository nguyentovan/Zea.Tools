# Zea.Tools

Bộ công cụ hỗ trợ thao tác trên Windows và xử lý SQL. Ứng dụng `Zea.Tweakr` chạy ở khay hệ thống, tích hợp với File Explorer, SSMS và DBeaver.

## Tính năng

| Phím tắt | Chức năng |
| --- | --- |
| **Win+E** | Mở File Explorer hoặc thêm tab vào cửa sổ hiện có. |
| **Ctrl+K, Ctrl+Y** | Định dạng SQL đang chọn trong SSMS hoặc DBeaver. |
| **Ctrl+K, Ctrl+G** | Chuyển log iBatis đang chọn thành SQL, rồi định dạng nếu tính năng được bật. |

Mở **Cài đặt** từ biểu tượng khay hệ thống để bật hoặc tắt từng chức năng. Với phím tắt SQL, giữ **Ctrl**, nhấn **K**, rồi nhấn **Y** hoặc **G**.

Các công cụ `Zea.SqlFormat` và `Zea.SqlLogConvert` cũng có thể chạy độc lập qua dòng lệnh. Formatter hỗ trợ T-SQL, PostgreSQL và các dialect khác, không cần kết nối cơ sở dữ liệu.

## Chạy từ mã nguồn

Yêu cầu Windows và .NET 10 SDK. Chạy từ thư mục gốc repository.

```powershell
dotnet run --project src/Zea.Tweakr
```

## Build bản Release

```powershell
dotnet build src/Zea.Tweakr -c Release
```

Chạy `Zea.Tweakr.exe` trong `src/Zea.Tweakr/bin/Release/net10.0-windows`. Khi sao chép sang máy khác, giữ nguyên các file trong thư mục build. Máy đích cần .NET 10 Runtime.

## Tài liệu

- [Ứng dụng Windows](docs/Zea.Tweakr.md).
- [Chuyển log và định dạng SQL](docs/sql-tools.md).

## Giấy phép

`Zea.SqlFormat` sử dụng [GNU AGPL v3.0](LICENSE). Xem [phạm vi giấy phép và thành phần bên thứ ba](docs/licensing.md).
