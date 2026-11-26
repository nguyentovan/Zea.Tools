using System.Runtime.CompilerServices;
using System.Text;
using Antlr4.Runtime;
using Antlr4.Runtime.Atn;

namespace Zea.SqlFormat.Parsing;

partial class SQLStandardLexer
{
    internal string Dialect { get; set; } = string.Empty;

    private bool AllowsBackticks => Dialect is "mysql" or "mariadb" or "sqlite" or "bigquery" or
        "databricks" or "sparksql" or "hive" or "athena" or "clickhouse" or "doris" or "starrocks";
    private bool IsMySqlDialect => Dialect is "mysql" or "mariadb";
    private bool AllowsBrackets => Dialect is "tsql" or "sqlite";
    private bool AllowsHashComments => Dialect is "mysql" or "mariadb" or "bigquery";
    private bool AllowsTempTables => Dialect == "tsql";
    private bool AllowsNestedComments => Dialect is "tsql" or "postgres" or "greenplum" or "db2";
    private bool AllowsPositionalParameters => Dialect is "postgres" or "greenplum" or "duckdb";
    private bool AllowsBackslashEscapes => Dialect is "mysql" or "mariadb" or "sparksql" or "hive" or "databricks" or "bigquery";
    private bool AllowsOracleAlternativeLiteral => Dialect == "oracle";
    private bool AllowsPostgresLiterals => Dialect is "postgres" or "greenplum";

    private void ReadDollarQuotedString()
    {
        var stream = (ICharStream)InputStream;
        // Dollar quotes can contain arbitrary characters and other delimiters.
        var delimiter = Text;
        while (stream.LA(1) != IntStreamConstants.EOF)
        {
            if (stream.LA(1) == '$')
            {
                var matched = 1;
                while (matched < delimiter.Length && stream.LA(matched + 1) == delimiter[matched])
                {
                    matched++;
                }

                if (matched == delimiter.Length)
                {
                    for (var index = 0; index < delimiter.Length; index++)
                    {
                        Interpreter.Consume(stream);
                    }

                    return;
                }
            }

            Interpreter.Consume(stream);
        }

        throw new FormatException($"Could not parse {Dialect}. Unterminated dollar-quoted string.");
    }

    private string _mySqlScriptDelimiter = ";";

    public override IToken NextToken()
    {
        if (_mySqlScriptDelimiter != ";" && AtMySqlScriptDelimiter())
        {
            var start = InputStream.Index;
            var line = Line;
            var column = Column;
            var input = (ICharStream)InputStream;
            foreach (var character in _mySqlScriptDelimiter)
            {
                Interpreter.Consume(input);
            }

            var tokenSource = Tuple.Create<ITokenSource, ICharStream>(this, input);
            var token = TokenFactory.Create(tokenSource, MySqlScriptDelimiter, _mySqlScriptDelimiter,
                TokenConstants.DefaultChannel, start, InputStream.Index - 1, line, column);
            return token;
        }

        if (Interpreter is TsqlStartStateCachingSimulator simulator)
        {
            simulator.Prepare();
        }

        var result = base.NextToken();
        return result;
    }

    private bool AtMySqlScriptDelimiter()
    {
        for (var index = 0; index < _mySqlScriptDelimiter.Length; index++)
        {
            if (InputStream.LA(index + 1) != _mySqlScriptDelimiter[index])
            {
                return false;
            }
        }

        return true;
    }

    private void SetMySqlScriptDelimiter()
    {
        var delimiter = Text["DELIMITER".Length..].TrimStart(' ', '\t');
        if (delimiter[0] is '\'' or '"' or '\x60')
        {
            var unescaped = new StringBuilder();
            for (var index = 1; index < delimiter.Length - 1; index++)
            {
                if (delimiter[index] == '\\')
                {
                    index++;
                }

                unescaped.Append(delimiter[index]);
            }

            delimiter = unescaped.ToString();
        }

        if (delimiter.Length == 0)
        {
            throw new FormatException($"Could not parse {Dialect}. DELIMITER requires a nonempty separator.");
        }

        _mySqlScriptDelimiter = delimiter;
    }

    private void MarkMySqlBodySemicolon()
    {
        if (_mySqlScriptDelimiter != ";")
        {
            Type = MySqlBodySemicolon;
        }
    }

    private bool StartsOnOwnLine()
    {
        for (var offset = -1; ; offset--)
        {
            var character = InputStream.LA(offset);
            if (character == IntStreamConstants.EOF || character is '\n' or '\r')
            {
                return true;
            }

            if (character is not (' ' or '\t'))
            {
                return false;
            }
        }
    }

    private bool IsSqliteSyntax()
    {
        var isSqlite = Dialect == "sqlite";
        return isSqlite;
    }

    private bool AtIdentifierEnd()
    {
        int next = InputStream.LA(1);
        var category = char.GetUnicodeCategory((char)next);
        return category is not (System.Globalization.UnicodeCategory.UppercaseLetter or
            System.Globalization.UnicodeCategory.LowercaseLetter or System.Globalization.UnicodeCategory.TitlecaseLetter or
            System.Globalization.UnicodeCategory.ModifierLetter or System.Globalization.UnicodeCategory.OtherLetter or
            System.Globalization.UnicodeCategory.LetterNumber or System.Globalization.UnicodeCategory.NonSpacingMark or
            System.Globalization.UnicodeCategory.SpacingCombiningMark or System.Globalization.UnicodeCategory.DecimalDigitNumber or
            System.Globalization.UnicodeCategory.ConnectorPunctuation);
    }

    private bool IsBatchSeparatorAhead()
    {
        if (Dialect != "tsql")
        {
            return false;
        }

        for (var offset = -1; ; offset--)
        {
            var character = InputStream.LA(offset);
            if (character == IntStreamConstants.EOF || character is '\n' or '\r')
            {
                break;
            }

            if (character is not (' ' or '\t'))
            {
                return false;
            }
        }

        var afterKeyword = 3;
        while (InputStream.LA(afterKeyword) is ' ' or '\t')
        {
            afterKeyword++;
        }

        while (InputStream.LA(afterKeyword) is >= '0' and <= '9')
        {
            afterKeyword++;
        }

        var endsLine = IsEndOfBatchLine(afterKeyword);
        return endsLine;
    }

    private bool IsEndOfBatchLine(int offset)
    {
        while (true)
        {
            var character = InputStream.LA(offset);
            var following = InputStream.LA(offset + 1);
            if (character is IntStreamConstants.EOF or '\n' or '\r' || character == '-' && following == '-')
            {
                return true;
            }

            if (character is ' ' or '\t')
            {
                offset++;
                continue;
            }

            if (character != '/' || following != '*')
            {
                return false;
            }

            offset += 2;
            var depth = 1;
            while (depth != 0)
            {
                character = InputStream.LA(offset);
                if (character is IntStreamConstants.EOF or '\n' or '\r')
                {
                    return true;
                }

                following = InputStream.LA(offset + 1);
                if (character == '/' && following == '*')
                {
                    depth++;
                    offset += 2;
                }
                else if (character == '*' && following == '/')
                {
                    depth--;
                    offset += 2;
                }
                else
                {
                    offset++;
                }
            }
        }
    }
}

partial class SQLStandardParser
{
    private static readonly HashSet<string> KnownMultiwordTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "BIT VARYING",
        "CHAR VARYING",
        "CHARACTER VARYING",
        "DOUBLE PRECISION",
        "NATIONAL CHAR",
        "NATIONAL CHARACTER",
        "NCHAR VARYING",
        "LONG RAW",
        "LONG VARCHAR",
        "TIME WITHOUT TIME ZONE",
        "TIME WITH TIME ZONE",
        "TIMESTAMP WITHOUT TIME ZONE",
        "TIMESTAMP WITH TIME ZONE",
        "CHARACTER LARGE OBJECT",
        "BINARY LARGE OBJECT",
    };

    internal string Dialect { get; set; } = string.Empty;

    public override void EnterRule(ParserRuleContext localContext, int state, int ruleIndex)
    {
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            throw new FormatException($"Could not parse {Dialect}. SQL nesting exceeds the available parser stack.");
        }

        base.EnterRule(localContext, state, ruleIndex);
    }

    internal bool HasValueExpressionOperators { get; set; } = true;

    internal void UseContextualPrediction(TsqlPredictionGraph budget, ATN? graph = null, bool contextual = true)
    {
        Interpreter = new ContextualSqlPrediction(this, graph ?? Interpreter.atn, budget, contextual);
    }

    internal bool IsFixedTsqlPredicate(int predicateIndex)
    {
        var result = false;
        ClassifyTsqlPredicate(predicateIndex, ref result);
        return result;
    }

    partial void ClassifyTsqlPredicate(int predicateIndex, ref bool result);

    private bool StartsQualifiedIdentifier()
    {
        if (!IsTsqlProceduralSyntax())
        {
            return false;
        }

        var next = TokenStream.LA(1);
        var startsQualified = next is Identifier or DelimitedIdentifier && TokenStream.LA(2) == Period;
        return startsQualified;
    }

    private void RequireImplicitProcedureCallAtBatchStart()
    {
        var previous = TokenStream.LT(-1);
        if (previous == null || previous.Type == GO ||
            previous.Type == UnsignedInteger && TokenStream.LT(-2)?.Type == GO)
        {
            return;
        }

        NotifyErrorListeners(CurrentToken, "Use EXECUTE when a procedure call is not the first statement of a batch.", null);
    }

    private bool AllowsConflictClause()
    {
        var allowsConflict = Dialect is "postgres" or "sqlite" or "greenplum" or "duckdb";
        return allowsConflict;
    }

    private bool AllowsReturning()
    {
        var allowsReturning = Dialect is "postgres" or "sqlite" or "greenplum" or "duckdb" or "mariadb";
        return allowsReturning;
    }

    private bool AllowsTypeCast()
    {
        var allowsTypeCast = Dialect is not ("tsql" or "mysql" or "mariadb" or "sqlite" or "oracle");
        return allowsTypeCast;
    }

    private bool AllowsTop()
    {
        var allowsTop = Dialect is "tsql" or "snowflake" or "vertica" or "teradata" or "sybase";
        return allowsTop;
    }

    private bool AllowsMerge()
    {
        var allowsMerge = Dialect is not ("mysql" or "mariadb" or "sqlite");
        return allowsMerge;
    }

    private const bool AllowsNamedParameters = true;
    private const bool AllowsAnonymousParameters = true;

    private bool AllowsLimit()
    {
        var allowsLimit = Dialect is not ("tsql" or "oracle" or "db2");
        return allowsLimit;
    }

    private bool IsOracleSyntax()
    {
        var oracle = Dialect == "oracle";
        return oracle;
    }

    private bool IsMySqlSyntax()
    {
        var mysql = Dialect is "mysql" or "mariadb";
        return mysql;
    }

    private bool IsCreateTableTailAllowed()
    {
        var allowsTail = !IsSqliteSyntax() && !IsSqliteTableOptionAhead() && (IsMySqlSyntax() || !NextWordIs("ENGINE"));
        return allowsTail;
    }

    private bool IsTableElementSuffixAllowed()
    {
        if (TokenStream.LA(1) == CONSTRAINT)
        {
            return false;
        }

        if (IsMySqlSyntax())
        {
            return true;
        }

        for (var offset = 1; ; offset++)
        {
            switch (TokenStream.LA(offset))
            {
                case AUTO_INCREMENT:
                    return false;
                case Comma:
                case RightParen:
                case Semicolon:
                case TokenConstants.EOF:
                    return true;
            }
        }
    }

    private bool AllowsMergeByTarget()
    {
        return IsTsqlProceduralSyntax() || IsPostgresSyntax();
    }

    private bool HasLegacyTableHintAhead()
    {
        if (!IsTsqlProceduralSyntax() || TokenStream.LA(1) != LeftParen || TokenStream.LA(3) != RightParen)
        {
            return false;
        }

        var hint = TokenStream.LT(2).Text.ToUpperInvariant();
        var knownHint = hint is "NOLOCK" or "HOLDLOCK" or "PAGLOCK" or "READCOMMITTED" or
            "READPAST" or "READUNCOMMITTED" or "REPEATABLEREAD" or "ROWLOCK" or "SERIALIZABLE" or
            "TABLOCK" or "TABLOCKX" or "UPDLOCK" or "XLOCK";
        return knownHint;
    }

    private bool AllowsNullOrder()
    {
        var allowsOrder = Dialect is not ("tsql" or "mysql" or "mariadb");
        return allowsOrder;
    }

    private bool AllowsGroupingSets()
    {
        var allowsGrouping = Dialect is not ("mysql" or "mariadb" or "sqlite" or "soql");
        return allowsGrouping;
    }

    private bool AllowsAggregateFilter()
    {
        var allowsFilter = Dialect is not ("tsql" or "mysql" or "mariadb" or "oracle" or "soql");
        return allowsFilter;
    }

    private bool AllowsWindowExclusion()
    {
        var allowsExclusion = Dialect is "ansi" or "postgres" or "greenplum" or "sqlite" or "duckdb";
        return allowsExclusion;
    }

    private bool AllowsDistinctComparison()
    {
        var allowsComparison = Dialect is "ansi" or "postgres" or "greenplum" or "duckdb" or
            "bigquery" or "trino" or "snowflake" or "sqlite" or "redshift" or "athena";
        return allowsComparison;
    }

    private bool AllowsArrayLiteral()
    {
        var allowsArray = Dialect is "bigquery" or "postgres" or "greenplum" or "duckdb" or
            "trino" or "athena" or "snowflake" or "clickhouse";
        return allowsArray;
    }

    private bool IsSqliteSyntax()
    {
        var isSqlite = Dialect == "sqlite";
        return isSqlite;
    }

    private bool IsPostgresSyntax()
    {
        var isPostgres = Dialect is "postgres" or "greenplum";
        return isPostgres;
    }

    private bool IsSqliteTableOptionAhead()
    {
        var isTableOption = NextWordIs("STRICT") || NextWordIs("WITHOUT") && NextWordIs("ROWID", 2);
        return isTableOption;
    }

    private bool IsRowLevelSecurityAhead()
    {
        var isPolicy = (NextWordIs("ENABLE") || NextWordIs("DISABLE") || NextWordIs("FORCE")) &&
            NextWordIs("ROW", 2) && NextWordIs("LEVEL", 3) && NextWordIs("SECURITY", 4);
        return isPolicy;
    }

    private bool IsExplainOptionAllowed()
    {
        if (NextWordIs("QUERY") && NextWordIs("PLAN", 2))
        {
            return IsSqliteSyntax();
        }

        var allowed = !NextWordIs("BUFFERS") || IsPostgresSyntax();
        return allowed;
    }

    private bool NextWordIs(string value, int offset = 1)
    {
        var matches = string.Equals(TokenStream.LT(offset).Text, value, StringComparison.OrdinalIgnoreCase);
        return matches;
    }

    private bool IsGenericProceduralSyntax()
    {
        var generic = Dialect is not ("tsql" or "firebird");
        return generic;
    }

    private bool IsTsqlProceduralSyntax()
    {
        var tsql = Dialect == "tsql";
        return tsql;
    }

    private bool IsFirebirdProceduralSyntax()
    {
        var firebird = Dialect == "firebird";
        return firebird;
    }

    private bool TrailingSelectIntoAllowed()
    {
        if (Dialect is not ("mysql" or "mariadb" or "postgres" or "greenplum" or "oracle" or "firebird"))
        {
            return false;
        }

        for (RuleContext? rule = Context; rule != null; rule = rule.Parent)
        {
            if (rule.RuleIndex == RULE_proceduralStatement)
            {
                return true;
            }
        }

        var sessionVariable = Dialect is "mysql" or "mariadb" && TokenStream.LA(2) == BatchVariableName;
        return sessionVariable;
    }

    private bool IsTsqlForOutputClauseAhead()
    {
        return IsTsqlProceduralSyntax() && TokenStream.LA(1) == FOR &&
            (NextWordIs("XML", 2) || NextWordIs("JSON", 2));
    }

    private bool ValidCorrelationNameFollows()
    {
        var next = TokenStream.LA(1);
        var allowsTableAlias = !IsPostgresSyntax() || AllowsImplicitCorrelationName(Dialect, TokenStream.LT(1).Text);
        var valid = next is not (CROSS or JOIN or INNER or OUTER or UNION or LEFT or RIGHT or FULL or NATURAL or
            STRAIGHT_JOIN or DK_ASOF or DK_FINAL or DK_PREWHERE or DK_START or DK_CONNECT or DK_PREFERRING or
            DK_DISTRIBUTE or DK_SEGMENTED or DK_VERSION) && !IsTsqlForOutputClauseAhead() && allowsTableAlias;
        if (IsSqliteSyntax() && next == SQLITE_INDEXED && TokenStream.LA(2) == BY)
        {
            return false;
        }

        return valid;
    }

    internal static bool AllowsImplicitCorrelationName(string dialect, string word)
    {
        var allowed = dialect is not ("postgres" or "greenplum") ||
            !string.Equals(word, "TABLESAMPLE", StringComparison.OrdinalIgnoreCase);
        return allowed;
    }

    private bool IsSqliteTypeWordAhead()
    {
        var isTypeWord = TokenStream.LA(1) is not (AS or GENERATED or CONSTRAINT or PRIMARY or NOT or NULL or
            UNIQUE or CHECK or DEFAULT or REFERENCES) && !NextWordIs("COLLATE");
        return isTypeWord;
    }

    private bool AllowsCteMaterialization()
    {
        var allowsMaterialization = IsSqliteSyntax() || IsPostgresSyntax() || Dialect is "greenplum" or "duckdb";
        return allowsMaterialization;
    }

    private bool IsProceduralDeclarationAhead()
    {
        if (TokenStream.LA(1) != DECLARE)
        {
            return false;
        }

        if (TokenStream.LA(2) is GLOBAL or LOCAL)
        {
            return true;
        }

        for (var offset = 2; offset < 12; offset++)
        {
            var token = TokenStream.LA(offset);
            if (token is CURSOR or TABLE)
            {
                return true;
            }

            if (token is Semicolon or DEFAULT or EqualsOperator or TokenConstants.EOF)
            {
                break;
            }
        }

        return false;
    }

    private bool ReturnClauseAhead()
    {
        var isReturnClause = TokenStream.LA(1) is RETURN or RETURNS;
        return isReturnClause;
    }

    private bool ModifierAhead(bool signature)
    {
        if (signature)
        {
            var isSignatureModifier = SignatureModifierAt(1);
            return isSignatureModifier;
        }

        var isTypeModifier = TypeModifierAt(1);
        return isTypeModifier;
    }

    private bool SignatureModifierAt(int offset)
    {
        var text = TokenStream.LT(offset).Text;
        var isModifier = text.Equals("IN", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("OUT", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("INOUT", StringComparison.OrdinalIgnoreCase);
        return isModifier;
    }

    private bool TypeModifierAt(int offset)
    {
        var text = TokenStream.LT(offset).Text.ToUpperInvariant();
        if (Dialect is "mysql" or "mariadb" && text is "SIGNED" or "UNSIGNED" or "ZEROFILL")
        {
            return true;
        }

        var isModifier = text is "PRECISION" or "VARYING" or "LARGE" or "OBJECT" or "RAW";
        return isModifier;
    }

    private bool NamedRoutineArgumentAhead()
    {
        var offset = 1;
        while (SignatureModifierAt(offset))
        {
            offset++;
        }

        if (TokenStream.LA(offset) == TokenConstants.EOF ||
            TokenStream.LA(offset + 1) is TokenConstants.EOF or Comma or RightParen or LeftParen or Period)
        {
            return false;
        }

        var phrase = new StringBuilder();
        for (var index = offset; index < offset + 5 && IsUnquotedWord(index); index++)
        {
            if (index > offset)
            {
                phrase.Append(' ');
            }

            phrase.Append(TokenStream.LT(index).Text);
            if (index > offset && KnownMultiwordTypes.Contains(phrase.ToString()))
            {
                return false;
            }
        }

        if (TypeModifierAt(offset + 1))
        {
            return false;
        }

        if (SignatureModifierAt(offset + 1))
        {
            var typeOffset = offset + 2;
            while (SignatureModifierAt(typeOffset))
            {
                typeOffset++;
            }

            var hasType = IsUnquotedWord(typeOffset) || TokenStream.LA(typeOffset) is DelimitedIdentifier or Quotted;
            return hasType;
        }

        return true;
    }

    private bool IsUnquotedWord(int offset)
    {
        var type = TokenStream.LA(offset);
        var isUnquotedWord = type is not (TokenConstants.EOF or DelimitedIdentifier or Quotted or Period or
            Comma or LeftParen or RightParen or Semicolon or EqualsOperator);
        return isUnquotedWord;
    }

    private bool AtDollarQuoteDelimiter(string? opening)
    {
        if (TokenStream.LA(1) == DoubleDollar)
        {
            var isClosing = opening == "$$";
            return isClosing;
        }

        if (TokenStream.LA(1) == Dollar && TokenStream.LA(3) == Dollar)
        {
            var start = TokenStream.LT(1);
            var tag = TokenStream.LT(2);
            var end = TokenStream.LT(3);
            var isClosing = start.StopIndex + 1 == tag.StartIndex && tag.StopIndex + 1 == end.StartIndex &&
                opening == "$" + tag.Text + "$";
            return isClosing;
        }

        return false;
    }
}
