# Công cụ SQL

`Zea.SqlFormat` định dạng SQL theo dialect. `Zea.SqlLogConvert` chuyển một khối log iBatis thành T-SQL bằng cách thay giá trị tham số.

Hai CLI đọc stdin và ghi kết quả ra stdout bằng UTF-8, không cần kết nối cơ sở dữ liệu. Lỗi được ghi ra stderr.

## Build và chạy

Dùng PowerShell 7 và .NET 10 SDK. Chạy từ thư mục gốc repository.

```powershell
dotnet build src/Zea.SqlFormat -c Release
dotnet build src/Zea.SqlLogConvert -c Release
$format = './src/Zea.SqlFormat/bin/Release/net10.0/Zea.SqlFormat.dll'
$convert = './src/Zea.SqlLogConvert/bin/Release/net10.0/Zea.SqlLogConvert.dll'
```

## Định dạng SQL

```powershell
dotnet $format --dialects
'select 1, 2;' | dotnet $format --dialect tsql
'select cast(''1'' as integer), ''1''::integer;' | dotnet $format --dialect postgres
```

| Tùy chọn | Chức năng |
| --- | --- |
| `--dialect <name>` | Chọn dialect, không phân biệt hoa thường. `postgresql` là tên thay thế cho `postgres`. |
| `--preserve-tokens` | Giữ token gốc khi căn khoảng trắng và xuống dòng. |
| `--validate` | Bật bước kiểm tra của formatter. Dùng sau `--preserve-tokens`. |
| `--require-right-way` | Yêu cầu layout từ Right Way. Dùng sau `--preserve-tokens`, thay cho `--validate`. |
| `--dialects` | Liệt kê dialect được hỗ trợ. |
| `--help`, `-h` | Hiển thị hướng dẫn. |

```powershell
'select a,b from t where a=1;' | dotnet $format --dialect tsql --preserve-tokens --validate
```

Với PostgreSQL, cả chế độ mặc định và chế độ giữ token chỉ thay khoảng trắng được phép. Formatter giữ nguyên keyword, identifier, literal, comment, toán tử và cú pháp ép kiểu `CAST` hoặc `::`. Thân hàm và `DO` trong dollar-quote được giữ nguyên.

PostgreSQL `--validate` kiểm tra ranh giới token, dấu phân cách SQL và việc bảo toàn đầu ra. Tùy chọn này không kiểm tra toàn bộ cú pháp PostgreSQL hoặc kết quả thực thi trên DB. Dữ liệu `COPY` nội tuyến và các khối lệnh psql không được định dạng lại.

## Chuyển log thành SQL

Converter nhận một khối `PreparedStatement` cùng metadata `Parameters` và `Types` nếu có.

```powershell
$log = @"
DEBUG - PreparedStatement : [select @param0, @param1]
DEBUG - Parameters: [@param0=[NAME,O'Brien], @param1=[ID,42]]
DEBUG - Types: [@param0=[String, System.String], @param1=[Int32, System.Int32]]
"@
$log | dotnet $convert | dotnet $format --dialect tsql
```

Ví dụ trên tạo câu SELECT có hai giá trị `N'O''Brien'` và `42`.

Converter đối chiếu tên tham số không phân biệt hoa thường, xử lý dấu nháy theo kiểu dữ liệu và giữ token trong chuỗi hoặc comment. Nếu thiếu metadata kiểu hoặc kiểu chưa được hỗ trợ, converter giữ giá trị trong metadata. Nếu parser báo lỗi cú pháp T-SQL, converter trả câu lệnh gốc.

Converter chỉ có quy tắc chuyển giá trị cho T-SQL. Đổi dialect của formatter không chuyển câu lệnh sang một hệ quản trị khác.

## Mã thoát

| Mã | Ý nghĩa |
| --- | --- |
| `0` | Thành công, hiển thị hướng dẫn hoặc liệt kê dialect. |
| `1` | Lỗi đọc hoặc xử lý input. |
| `2` | Đối số không hợp lệ hoặc dialect không được hỗ trợ. |

## Sử dụng trong ứng dụng Windows

**Ctrl+K, Ctrl+Y** chạy formatter ở chế độ giữ token và kiểm tra. **Ctrl+K, Ctrl+G** chạy converter, rồi formatter nếu tính năng Format được bật.

Ứng dụng kiểm tra lại vùng chọn và tab trước khi dán kết quả. Nếu công cụ báo lỗi hoặc vùng chọn đã đổi, ứng dụng dừng xử lý. Mỗi CLI có thời hạn 20 giây.

## Phát triển formatter

PostgreSQL dùng formatter riêng trong `src/Zea.SqlFormat/Postgres`, giữ nguyên token và chỉ sửa khoảng trắng được phép. Nếu không xác định được ranh giới chuỗi do thiếu thông tin cấu hình session, formatter giữ nguyên input.

Các dialect khác dùng lexer ANTLR của DBeaver, grammar SELECT port từ SQLFluff và grammar DBeaver đầy đủ. Các parser tạo cùng mô hình segment để áp dụng layout Right Way.

### Cập nhật grammar

Source có sẵn lexer và parser C# được sinh tự động. Sau khi sửa grammar, chạy ANTLR 4.13.1 từ thư mục `src/Zea.SqlFormat/Parsing` và cập nhật metadata T-SQL.

```powershell
java -jar 'C:/path/to/antlr-4.13.1-complete.jar' -Dlanguage=CSharp -package Zea.SqlFormat.Parsing -no-listener -o Generated SQLStandardLexer.g4 SQLStandardParser.g4
./Update-TsqlPredicateMetadata.ps1
```

Giữ các file C# sinh ra trong `Generated`. Xem [nguồn gốc thành phần và giấy phép](licensing.md).
