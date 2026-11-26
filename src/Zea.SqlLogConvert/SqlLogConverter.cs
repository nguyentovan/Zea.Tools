using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Zea.SqlLogConvert;

internal static class SqlLogConverter
{
    private static readonly string[] LogDateTimeFormats =
    {
        "M/d/yyyy h:mm:ss tt",
        "M/d/yyyy h:mm:ss.f tt",
        "M/d/yyyy h:mm:ss.ff tt",
        "M/d/yyyy h:mm:ss.fff tt",
        "yyyy-MM-dd",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss.f",
        "yyyy-MM-dd HH:mm:ss.ff",
        "yyyy-MM-dd HH:mm:ss.fff",
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm:ss.f",
        "yyyy-MM-dd'T'HH:mm:ss.ff",
        "yyyy-MM-dd'T'HH:mm:ss.fff"
    };

    private const string LoggerPrefix =
        @"(?:[0-9]{4}-[0-9]{2}-[0-9]{2}[ \t]+[^ \t\r\n]+[ \t]+\[[^\r\n\]]+\][ \t]+)?[A-Z]+[ \t]+-[ \t]+";

    private static readonly Regex RecordStart = new(
        @"^(?:(?:" + LoggerPrefix + @"(?:(?<metadataLabel>[^:\[\r\n]+):[ \t]+\[(?<metadataValue>[^\]\r\n]+)\][ \t]+)?)?" +
        @"(?<kind>PreparedStatement|Parameters|Types)[ \t]*:[ \t]*\[|" + LoggerPrefix + ")",
        RegexOptions.Multiline | RegexOptions.NonBacktracking | RegexOptions.CultureInvariant);

    private const string ParameterName = @"@[\p{L}_][\p{L}\p{M}\p{Nd}_$#@]*";

    private static readonly Regex Entry = new(
        @"\G\s*(?<name>" + ParameterName + @")=\[(?<body>.*?)\](?:,\s*(?=" + ParameterName + @"=\[)|\s*\z)",
        RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string ConvertLog(string log)
    {
        var (statement, statementId, parameters, types) = ReadBlock(log);
        var sql = statement;
        if (parameters.Count != 0)
        {
            sql = SubstituteParameters(statement, parameters, types);
        }

        if (statementId != null)
        {
            sql = $"--{statementId}{Environment.NewLine}{sql}";
        }

        return sql;
    }

    private static (string Statement, string? StatementId, Dictionary<string, string> Parameters, Dictionary<string, string> Types) ReadBlock(string log)
    {
        string? statement = null;
        string? statementId = null;
        string? parameterText = null;
        string? typeText = null;
        var records = RecordStart.Matches(log);
        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            if (!record.Groups["kind"].Success)
            {
                continue;
            }

            var start = record.Index + record.Length;
            var end = index + 1 < records.Count ? records[index + 1].Index : log.Length;
            var payload = log.AsSpan(start, end - start).TrimEnd();
            if (payload.IsEmpty || payload[^1] != ']')
            {
                throw new FormatException("Input contains an incomplete SQL log record.");
            }

            var body = payload[..^1].ToString();
            switch (record.Groups["kind"].Value)
            {
                case "PreparedStatement" when statement == null:
                    statement = body;
                    var label = record.Groups["metadataLabel"].ValueSpan.Trim();
                    if (label.Equals("Statement Id", StringComparison.OrdinalIgnoreCase))
                    {
                        var id = record.Groups["metadataValue"].ValueSpan.Trim();
                        // Only a single-line identifier may be included in a SQL comment.
                        if (!id.IsEmpty && id.IndexOfAny("\0\r\n\v\f\u001c\u001d\u001e\u0085\u2028\u2029") < 0)
                        {
                            statementId = id.ToString();
                        }
                    }

                    break;
                case "Parameters" when parameterText == null:
                    parameterText = body;
                    break;
                case "Types" when typeText == null:
                    typeText = body;
                    break;
                default:
                    throw new FormatException("Input contains duplicate SQL log records. Pass one log block at a time.");
            }
        }

        if (string.IsNullOrWhiteSpace(statement))
        {
            throw new FormatException("Input does not contain a complete PreparedStatement.");
        }

        var parameters = ReadEntries(parameterText, readTypes: false);
        var types = ReadEntries(typeText, readTypes: true);
        return (statement, statementId, parameters, types);
    }

    private static Dictionary<string, string> ReadEntries(string? input, bool readTypes)
    {
        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(input))
        {
            return entries;
        }

        var offset = 0;
        foreach (Match entry in Entry.Matches(input))
        {
            var raw = entry.Groups["body"].Value;
            var comma = raw.IndexOf(',');
            var value = raw;
            if (comma >= 0)
            {
                value = readTypes ? raw[..comma] : raw[(comma + 1)..];
            }

            if (!entries.TryAdd(entry.Groups["name"].Value, value))
            {
                throw new FormatException("A parameter appears more than once in the SQL log record.");
            }

            offset = entry.Index + entry.Length;
        }

        if (offset != input.Length)
        {
            throw new FormatException("Input contains incomplete parameter metadata.");
        }

        return entries;
    }

    private static string SubstituteParameters(string statement, Dictionary<string, string> parameters, Dictionary<string, string> types)
    {
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        var fragment = parser.Parse(new StringReader(statement), out var errors);
        if (errors.Count != 0)
        {
            // The original diagnostics matter more than guessing whether a variable is a target or a value.
            return statement;
        }

        var targets = new VariableTargets();
        fragment.Accept(targets);
        var sql = new StringBuilder(statement.Length);
        var position = 0;
        foreach (var token in fragment.ScriptTokenStream)
        {
            if (token.TokenType != TSqlTokenType.Variable || targets.Offsets.Contains(token.Offset) ||
                !parameters.TryGetValue(token.Text, out var value))
            {
                continue;
            }

            types.TryGetValue(token.Text, out var type);
            var literal = RenderValue(value, type, token.Text);
            sql.Append(statement, position, token.Offset - position);
            if (token.Offset > 0 && statement[token.Offset - 1] == '-' && literal.StartsWith('-'))
            {
                // Do not merge a subtraction and a logged negative value into a line comment.
                sql.Append(' ');
            }

            var numericBoolean = (literal is "0" or "1") && literal != value;
            var unicodeLiteral = literal.StartsWith("N'", StringComparison.Ordinal) && literal != value;
            if ((numericBoolean || unicodeLiteral) && token.Offset > 0 && CanJoinLiteral(statement[token.Offset - 1]))
            {
                // Literal digits and the N prefix can merge with a preceding token.
                sql.Append(' ');
            }

            sql.Append(literal);
            position = token.Offset + token.Text.Length;
            if (numericBoolean && position < statement.Length && CanJoinLiteral(statement[position]))
            {
                sql.Append(' ');
            }

        }

        sql.Append(statement, position, statement.Length - position);
        var result = sql.ToString();
        return result;
    }

    private static bool CanJoinLiteral(char character)
    {
        var joinsLiteral = char.IsLetterOrDigit(character) || character is '_' or '@' or '#' or '$' or '.';
        return joinsLiteral;
    }

    private sealed class VariableTargets : TSqlFragmentVisitor
    {
        public HashSet<int> Offsets { get; } = new HashSet<int>();

        private void Protect(TSqlFragment? variable)
        {
            if (variable != null)
            {
                Offsets.Add(variable.StartOffset);
            }
        }

        public override void Visit(ExecutableProcedureReference node)
        {
            Protect(node.ProcedureReference.ProcedureVariable);
        }

        public override void Visit(ExecuteSpecification node)
        {
            Protect(node.Variable);
        }

        public override void Visit(ExecuteParameter node)
        {
            Protect(node.Variable);
            if (node.IsOutput && node.ParameterValue is VariableReference variable)
            {
                Protect(variable);
            }
        }

        public override void Visit(SetVariableStatement node)
        {
            Protect(node.Variable);
        }

        public override void Visit(SelectSetVariable node)
        {
            Protect(node.Variable);
        }

        public override void Visit(AssignmentSetClause node)
        {
            Protect(node.Variable);
        }

        public override void Visit(DeclareVariableElement node)
        {
            Protect(node.VariableName);
        }

        public override void Visit(DeclareTableVariableBody node)
        {
            Protect(node.VariableName);
        }

        public override void Visit(VariableTableReference node)
        {
            Protect(node.Variable);
        }

        public override void Visit(VariableMethodCallTableReference node)
        {
            Protect(node.Variable);
        }

        public override void Visit(CursorId node)
        {
            Protect(node.Name);
        }

        public override void Visit(ReceiveStatement node)
        {
            Protect(node.Into);
        }

        public override void Visit(BeginDialogStatement node)
        {
            Protect(node.Handle);
        }

        public override void Visit(ExecuteAsStatement node)
        {
            Protect(node.Cookie);
        }

        public override void Visit(VariableValuePair node)
        {
            Protect(node.Variable);
        }

        public override void Visit(OpenXmlTableReference node)
        {
            Protect(node.Variable);
        }

        public override void Visit(GetConversationGroupStatement node)
        {
            Protect(node.GroupId);
        }

        public override void Visit(FetchCursorStatement node)
        {
            foreach (var variable in node.IntoVariables)
            {
                Protect(variable);
            }
        }
    }

    private static string RenderValue(string raw, string? typeName, string parameterName)
    {
        var type = typeName?.Trim().ToLowerInvariant();
        if (type != null && type.StartsWith("system.", StringComparison.Ordinal))
        {
            type = type["system.".Length..];
        }

        if ((type is "boolean" or "bool" or "bit") && bool.TryParse(raw, out var boolean))
        {
            return boolean ? "1" : "0";
        }

        if (type == "datetime")
        {
            if (!DateTime.TryParseExact(raw.Trim(), LogDateTimeFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var value))
            {
                throw new FormatException($"Unsupported DateTime format for parameter {parameterName} in the SQL log.");
            }

            var formatted = value.ToString("yyyy-MM-dd'T'HH:mm:ss.FFF", CultureInfo.InvariantCulture);
            var literal = "'" + formatted + "'";
            return literal;
        }

        var unicode = type is "string" or "stringfixedlength" or "nvarchar" or "nchar" or "ntext";
        if (unicode || type is "ansistring" or "ansistringfixedlength" or "char" or "varchar" or "text" or
            "guid" or "uniqueidentifier" or "datetime2" or "datetimeoffset" or
            "smalldatetime" or "date" or "time" or "xml")
        {
            var prefix = unicode ? "N'" : "'";
            var quoted = prefix + raw.Replace("'", "''", StringComparison.Ordinal) + "'";
            return quoted;
        }

        return raw;
    }
}
