# Bản portable và cập nhật

Zea.Tweakr phát hành dưới dạng ZIP portable cho Windows x64. Gói có sẵn .NET Runtime và hai công cụ SQL, nên máy dùng không cần cài .NET riêng.

## Tải và chạy

1. Mở [GitHub Releases](https://github.com/nguyentovan/Zea.Tools/releases/latest) và tải `Zea.Tweakr.App-stable-Portable.zip`.
2. Giải nén toàn bộ ZIP vào một thư mục có quyền ghi, chẳng hạn `D:\Tools\Zea.Tweakr`.
3. Chạy `Zea.Tweakr.exe` ở thư mục gốc vừa giải nén. Ứng dụng xuất hiện trong khay hệ thống. Mở **Cài đặt** từ biểu tượng khay để xem phiên bản trên thanh tiêu đề và kiểm tra cập nhật.

Giữ nguyên cấu trúc thư mục của ZIP.

```text
Zea.Tweakr/
├── Zea.Tweakr.exe
├── Update.exe
├── .portable
└── current/
    ├── Zea.Tweakr.exe
    ├── Zea.SqlFormat.exe
    ├── Zea.SqlLogConvert.exe
    ├── licenses/
    └── ...
```

File `Zea.Tweakr.exe` ở gốc là launcher ổn định. Dùng file này khi tạo shortcut hoặc cấu hình tự chạy cùng Windows. Thư mục `current` chứa chương trình đang dùng và được thay thế khi cập nhật. Velopack có thể tạo thêm thư mục `packages` để lưu gói cập nhật.

Cài đặt phím tắt vẫn lưu ở `%LOCALAPPDATA%\Zea.Tweakr\settings.json`, giữ nguyên khi cập nhật hoặc di chuyển thư mục portable trên cùng tài khoản Windows.

## Cập nhật

Ứng dụng kiểm tra GitHub Releases khi khởi động và mỗi 15 phút. Bản portable chỉ nhận bản stable mới hơn từ repository công khai và tự tải gói về nền. Có thể chọn **Kiểm tra cập nhật** trong Cài đặt hoặc menu khay để kiểm tra ngay. Kết quả kiểm tra thủ công hiện trong toast.

Sau khi tải xong, phiên bản mới được áp dụng ở lần mở ứng dụng tiếp theo. Muốn dùng ngay, chọn **Cập nhật và khởi động lại**. Ứng dụng dừng tác vụ SQL và Explorer đang chạy trước khi áp dụng gói. Ứng dụng chỉ khởi động lại khi bạn chọn nút này hoặc chủ động mở lại ứng dụng.

Nếu mất mạng hoặc tải lỗi, phím tắt và biểu tượng khay tiếp tục hoạt động. Các lần kiểm tra nền sẽ thử lại theo lịch. Khi kiểm tra hoặc áp dụng cập nhật thủ công gặp lỗi, ứng dụng hiện toast để thử lại sau. Gói chưa tải xong hoặc không qua kiểm tra checksum sẽ không được áp dụng.

Bản chạy bằng `dotnet run`, file build thông thường hoặc thư mục publish thô cũng kiểm tra phiên bản, nhưng không tải hoặc áp dụng gói cập nhật. Khi có phiên bản mới, tải bản ZIP đầy đủ từ GitHub Releases để dùng tự cập nhật. Không chạy trực tiếp từ bên trong ZIP hoặc chỉ sao chép một file EXE ra ngoài.

## Build một bản portable

Dùng Windows x64, PowerShell 7 và .NET 10 SDK. Từ thư mục gốc repository, chạy bộ kiểm tra hành vi rồi đóng gói.

```powershell
dotnet run --project scripts/UpdateChecks -c Release
./scripts/Build-Release.ps1 -Version 0.1.0
```

Script khôi phục CLI Velopack đã ghim phiên bản, publish self-contained ba chương trình, chạy mẫu SQL trên chính EXE đã publish rồi tạo ZIP và feed cập nhật. Script kiểm tra phiên bản, runtimeconfig, license, cấu trúc ZIP và checksum gói trước khi báo thành công.

Để kiểm tra thay phiên bản và khởi động lại thật trong thư mục thử riêng, chạy `./scripts/update-smoke/Verify-PortableUpdate.ps1` sau khi build. Script kiểm tra gói lỗi, mất mạng, tải bản mới và giữ nguyên dữ liệu thử. Xem [hướng dẫn kiểm tra portable](../scripts/update-smoke/README.md).

Kết quả nằm trong `artifacts/0.1.0/Releases`. Mỗi lần build cần thư mục đầu ra mới để tránh trộn gói. Có thể chọn một thư mục mới bằng `-OutputDirectory`.

| File | Mục đích |
| --- | --- |
| `Zea.Tweakr.App-stable-Portable.zip` | Người dùng tải, giải nén và chạy. |
| `*-full.nupkg` | Gói mà ứng dụng tải để cập nhật. |
| `releases.stable.json` | Feed phiên bản, kích thước và checksum cho kênh stable. |
| Các file metadata khác do Velopack tạo | Giữ cùng bộ artifact khi phát hành. |

SDK và CLI cùng ghim Velopack `1.2.161`. Package ID là `Zea.Tweakr.App`, runtime `win-x64`, kênh `stable`. Package ID khác thư mục cấu hình `Zea.Tweakr` để giữ dữ liệu người dùng ngoài phần chương trình được cập nhật.

## Phát hành trên GitHub

Workflow [release.yml](../.github/workflows/release.yml) chạy khi đẩy tag dạng `vMAJOR.MINOR.PATCH`, chẳng hạn `v0.1.0`. Phiên bản phải lớn hơn bản stable đang phát hành. Không dùng hậu tố prerelease cho workflow này.

Trước lần phát hành đầu, đặt repository `nguyentovan/Zea.Tools` thành công khai. Bộ cập nhật truy cập release bằng yêu cầu không xác thực, nên repository và artifact cần tải được công khai.

Sau khi hoàn tất thay đổi và commit, tạo rồi đẩy tag.

```powershell
git tag v0.1.0
git push origin v0.1.0
```

Workflow kiểm tra bộ điều khiển cập nhật, build và xác minh toàn bộ artifact trên Windows. Sau đó CLI tải đầy đủ artifact lên GitHub và mới công khai release. Token chỉ lấy từ `GITHUB_TOKEN` của workflow với quyền `contents: write`; ứng dụng người dùng không chứa token. Các lần phát hành kênh stable chạy tuần tự.

Có thể chạy **Actions → Release Zea.Tweakr → Run workflow**, nhập `0.1.0` để build thử. Lần chạy thủ công chỉ lưu artifact trong Actions, không tạo GitHub Release.

Nếu bước upload thất bại, kiểm tra log và release draft cùng tag trước khi chạy lại. Giữ ZIP, gói full và feed trong cùng một release. Không chỉ tải ZIP lên riêng vì ứng dụng cần feed và gói full để cập nhật.

Với bản cần sửa sau phát hành, phát hành phiên bản cao hơn. Giữ tag trỏ đúng mã nguồn đã build và các thông báo giấy phép trong gói. Xem [phạm vi giấy phép](licensing.md).
