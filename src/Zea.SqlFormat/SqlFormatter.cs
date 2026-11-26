using Zea.SqlFormat.Formatters.RightWay;
using Zea.SqlFormat.Formatters.RightWay.ParseStructure;
using Zea.SqlFormat.Formatters.RightWay.Parsers;
using Zea.SqlFormat.Formatters.RightWay.Tokenizers;
using Zea.SqlFormat.Parsing;

namespace Zea.SqlFormat;

internal static class SqlFormatter
{
    public static string[] GetDialects()
    {
        var dialects = new string[]
        {
            "ansi", "athena", "bigquery", "clickhouse", "databricks", "db2", "doris", "duckdb",
            "exasol", "flink", "greenplum", "hive", "impala", "mariadb", "materialize", "mysql",
            "oracle", "postgres", "redshift", "snowflake", "soql", "sparksql", "sqlite", "starrocks",
            "teradata", "trino", "tsql", "vertica"
        };
        return dialects;
    }

    public static string FormatSql(string sql, string dialect)
    {
        bool useTsqlFormattingRules = dialect == "tsql";
        if (Array.IndexOf(GetDialects(), dialect) < 0)
        {
            throw new UnsupportedSqlDialectException($"Unsupported SQL dialect: {dialect}");
        }

        if (dialect == "postgres")
        {
            return Postgres.PostgresFormatter.Format(sql);
        }

        try
        {
            var result = FormatWithGrammar(sql, dialect, useTsqlFormattingRules);
            return result;
        }
        catch (FormatException error) when (useTsqlFormattingRules)
        {
            var recovery = TsqlWhitespaceFormatter.TryFormat(sql, preferRecoveryDiagnostic: error is TsqlPredictionLimitException);
            if (recovery == null)
            {
                throw;
            }

            return recovery;
        }
    }

    public static string FormatPreservingTokens(string sql, string dialect, bool validate, bool requireRightWay)
    {
        if (Array.IndexOf(GetDialects(), dialect) < 0)
        {
            throw new UnsupportedSqlDialectException($"Unsupported SQL dialect: {dialect}");
        }

        if (dialect == "postgres" && !requireRightWay)
        {
            return Postgres.PostgresFormatter.Format(sql);
        }

        try
        {
            var reference = FormatWithGrammar(sql, dialect, dialect == "tsql", preserveTokens: true);
            var layout = TraceSqlLayout.TryApplyReference(sql, reference, dialect);
            if (layout != null)
            {
                return layout;
            }
        }
        catch (FormatException)
        {
            if (validate)
            {
                // The regular path supplies the syntax diagnostic or accepts the existing parser fallback.
                _ = FormatSql(sql, dialect);
            }
        }

        if (dialect == "tsql")
        {
            try
            {
                var tokenizer = new TSqlStandardTokenizer();
                var parser = new TSqlStandardParser();
                Node Parse(string source)
                {
                    var tree = parser.ParseSQL(tokenizer.TokenizeSQL(source));
                    return tree;
                }

                var formatter = new RightWayFormatter();
                formatter.ErrorOutputPrefix = string.Empty;
                var reference = formatter.FormatSQLTree(Parse(sql), Parse);
                var layout = TraceSqlLayout.TryApplyReference(sql, reference, dialect);
                if (layout != null)
                {
                    return layout;
                }
            }
            catch (FormatException)
            {
                // Leave unsupported lexical forms to the other recovery paths.
            }
        }

        if (requireRightWay)
        {
            throw new FormatException("Right Way layout unavailable.");
        }

        if (dialect == "tsql")
        {
            try
            {
                var layout = TraceSqlLayout.TryApplyTsqlLayout(sql);
                if (layout != null)
                {
                    return layout;
                }
            }
            catch (FormatException)
            {
                // The fallback template can use syntax the lossless lexer does not support.
            }
        }

        var fallback = TraceSqlLayout.Format(sql, dialect);
        return fallback;
    }

    private static string FormatWithGrammar(string sql, string dialect, bool useTsqlFormattingRules, bool preserveTokens = false)
    {
        var tsqlGraph = useTsqlFormattingRules ? new TsqlPredictionGraph() : null;
        SqlSegment Parse(string input)
        {
            var converted = DbeaverSqlParser.Parse(input, dialect, tsqlGraph);
            if (!string.Equals(input, converted.Raw(), StringComparison.Ordinal))
            {
                throw new FormatException("Right Way formatting would change the SQL syntax.");
            }

            return converted;
        }
        var tree = Parse(sql);
        if (tree.Kind == "verbatim_sqlite")
        {
            return sql;
        }

        Node Reparse(string formatted)
        {
            var parsed = Parse(formatted);
            var converted = RightWayTreeAdapter.Convert(parsed, useTsqlFormattingRules);
            return converted;
        }

        var options = new RightWayFormatterOptions();
        options.KeywordStandardization = useTsqlFormattingRules && !preserveTokens;
        var formatter = new RightWayFormatter(options);
        var sqlTree = RightWayTreeAdapter.Convert(tree, useTsqlFormattingRules);
        var result = formatter.FormatSQLTree(sqlTree, Reparse);

        if (!preserveTokens && !string.Equals(sql, result, StringComparison.Ordinal))
        {
            var reparsed = Parse(result);
            RightWayTreeAdapter.CheckEquivalent(tree, reparsed, useTsqlFormattingRules);
        }

        return result;
    }
}
