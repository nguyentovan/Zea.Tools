using System.Text.RegularExpressions;

namespace Zea.SqlIntegration;

internal static class DBeaverSqlDialect
{
    private static readonly Regex DatabaseTypePattern = new(@"^Type:[ \t]*([^\r\n]+)", RegexOptions.Multiline);

    public static string Parse(string helpText)
    {
        var match = DatabaseTypePattern.Match(helpText);
        if (!match.Success || string.IsNullOrWhiteSpace(match.Groups[1].Value))
        {
            throw new InvalidOperationException("Tab SQL đang mở trong DBeaver không có thông tin loại cơ sở dữ liệu.");
        }

        var databaseType = match.Groups[1].Value.Trim();
        var separator = databaseType.LastIndexOf(" / ", StringComparison.Ordinal);
        var driver = databaseType;
        if (separator >= 0)
        {
            driver = databaseType[(separator + 3)..];
        }

        var normalizedDriver = driver.ToLowerInvariant();
        switch (normalizedDriver)
        {
            case "sql server":
            case "ms sql server":
            case "microsoft sql server":
            case "azure sql server":
            case "sql server (deprecated)":
            case "azure sql server (deprecated)":
            case "sql server (old driver, ms)":
            case "sql server (old driver, jtds)":
                return "tsql";
            case "postgresql":
            case "postgresql (old)":
            case "google cloud sql - postgresql":
                return "postgresql";
            case "mysql":
            case "mysql 5 (legacy)":
            case "mysql (old)":
                return "mysql";
            case "athena (legacy)":
                return "athena";
            case "bigquery (legacy)":
            case "google bigquery":
                return "bigquery";
            case "clickhouse (legacy)":
                return "clickhouse";
            case "db2 for luw":
            case "db2 for luw (old 8.x)":
                return "db2";
            case "apache doris":
                return "doris";
            case "apache hive":
            case "apache hive 2":
            case "apache hive 4+":
                return "hive";
            case "cloudera impala":
                return "impala";
            case "apache spark":
                return "sparksql";
            case "mariadb":
            case "oracle":
            case "sqlite":
            case "duckdb":
            case "athena":
            case "bigquery":
            case "clickhouse":
            case "databricks":
            case "db2":
            case "exasol":
            case "greenplum":
            case "materialize":
            case "redshift":
            case "snowflake":
            case "starrocks":
            case "teradata":
            case "trino":
            case "vertica":
                return normalizedDriver;
            default:
                throw new InvalidOperationException($"Loại cơ sở dữ liệu DBeaver chưa được hỗ trợ: {databaseType}.");
        }
    }
}
