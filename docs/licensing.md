# Giấy phép

`Zea.SqlFormat` được cấp phép theo [GNU Affero General Public License v3.0](../LICENSE). Formatter sử dụng mã Right Way SQL Formatter, dựa trên Poor Man's T-SQL Formatter của Tao Klerks.

Phạm vi cấp phép trên áp dụng cho `Zea.SqlFormat`. Các project khác chưa có tuyên bố cấp phép riêng cho mã tự viết. Giấy phép của thành phần bên thứ ba được giữ theo upstream.

## Thành phần bên thứ ba

| Thành phần | Sử dụng | Giấy phép |
| --- | --- | --- |
| [Right Way SQL Formatter](https://github.com/BadMonkeySoftware/right-way-sql-formatter) | Renderer, tokenizer và parser T-SQL | [AGPL v3](../src/Zea.SqlFormat/Formatters/RightWay/LICENSE.txt) |
| [DBeaver](https://github.com/dbeaver/dbeaver) | Grammar SQL | [Apache 2.0](../src/Zea.SqlFormat/Parsing/LICENSE.DBeaver.txt) |
| [ANTLR](https://github.com/antlr/antlr4) | Runtime và lexer, parser sinh tự động | [BSD 3-Clause](../src/Zea.SqlFormat/Parsing/LICENSE.Antlr.txt) |
| [SQLFluff](https://github.com/sqlfluff/sqlfluff/tree/4.5.0) | Grammar SELECT, JOIN và WITH port sang C# | [MIT](../src/Zea.SqlFormat/licenses/SQLFluff.txt) |
| [Microsoft ScriptDom](https://github.com/microsoft/SqlScriptDOM) | Phân tích T-SQL | [MIT](../src/Zea.SqlFormat/licenses/ScriptDom.txt) |
| [Velopack](https://github.com/velopack/velopack) | Đóng gói portable và cập nhật ứng dụng | [MIT](../src/Zea.Tweakr/licenses/Velopack.txt) |
| [Material3.Avalonia](https://github.com/greepar/Material3.Avalonia) | Theme Material Design 3 | [Apache 2.0](../src/Zea.Tweakr/licenses/Material3.Avalonia.txt), [NOTICE](../src/Zea.Tweakr/licenses/Material3.Avalonia.NOTICE.txt) |

Bảng liệt kê các thành phần có file license lưu trong source. Các package NuGet khác có giấy phép riêng. Xem [phiên bản dependency](../Directory.Packages.props) và các `.csproj` để tra package đang dùng.

## Nguồn gốc mã nguồn

Grammar SELECT, JOIN và WITH được chuyển từ ANSI dialect của [SQLFluff 4.5.0](https://github.com/sqlfluff/sqlfluff/tree/4.5.0). Grammar DBeaver được chuyển từ `plugins/org.jkiss.dbeaver.model.lsm/grammar` trong [DBeaver](https://github.com/dbeaver/dbeaver).

Renderer, tokenizer và parser T-SQL được lấy từ Right Way SQL Formatter, dựa trên Poor Man's T-SQL Formatter của Tao Klerks. Tokenizer và parser sử dụng [commit 5ebd5d6](https://github.com/BadMonkeySoftware/right-way-sql-formatter/tree/5ebd5d6328a6a22fdbf3aa954024991e99cbe258). Các file được giữ thông báo upstream.

## Phân phối

Giữ các file license, thông báo bản quyền và thông báo sửa đổi áp dụng cho thành phần được phân phối. Các project SQL chép license liên quan vào thư mục build và publish.

Khi phân phối bản thực thi thuộc AGPL, cung cấp mã nguồn tương ứng theo điều 6. Mã nguồn cần khớp phiên bản phát hành và bao gồm các file cần để build, cài đặt, chạy và sửa chương trình theo định nghĩa của giấy phép.

Với phiên bản AGPL đã sửa đổi có hỗ trợ tương tác từ xa qua mạng, điều 13 yêu cầu cung cấp cho người dùng tương tác cơ hội nhận mã nguồn tương ứng miễn phí.

Xem [toàn văn GNU AGPL](https://www.gnu.org/licenses/agpl-3.0.html) để biết các điều kiện đầy đủ.
