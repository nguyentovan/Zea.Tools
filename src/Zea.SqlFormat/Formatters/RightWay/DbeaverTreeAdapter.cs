using System.Text;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Zea.SqlFormat.Parsing;

namespace Zea.SqlFormat.Formatters.RightWay;

internal sealed class DbeaverTreeAdapter
{
    private static readonly Dictionary<string, string> GroupKinds = new(StringComparer.Ordinal)
    {
        ["sqlQuery"] = "statement",
        ["proceduralStatement"] = "statement",
        ["sqliteTriggerStatement"] = "statement",
        ["selectStatement"] = "select_statement",
        ["fromClause"] = "from_clause",
        ["whereClause"] = "where_clause",
        ["groupByClause"] = "groupby_clause",
        ["groupingSetList"] = "grouping_sets_clause",
        ["groupingOperation"] = "cube_rollup_clause",
        ["groupingSet"] = "bracketed",
        ["groupingOperationItem"] = "bracketed",
        ["forClause"] = "for_clause",
        ["havingClause"] = "having_clause",
        ["orderByClause"] = "orderby_clause",
        ["qualifyClause"] = "qualify_clause",
        ["limitClause"] = "limit_clause",
        ["offsetClause"] = "offset_clause",
        ["fetchClause"] = "fetch_clause",
        ["selectIntoClause"] = "into_table_clause",
        ["tableValueConstructor"] = "values_clause",
        ["setClauseList"] = "set_clause_list",
        ["conflictClause"] = "on_conflict_clause",
        ["outputClause"] = "output_clause",
        ["mergeOutputClause"] = "output_clause",
        ["returningClause"] = "returning_clause",
        ["windowClause"] = "window_clause",
        ["windowSpecification"] = "window_specification",
        ["overClause"] = "over_clause",
        ["tableReferenceHints"] = "post_table_expression",
        ["tsqlLegacyTableHints"] = "post_table_expression",
        ["sqliteIndexHint"] = "post_table_expression",
        ["optionClause"] = "option_clause",
        ["fromClauseTerm"] = "from_expression",
        ["naturalJoinTerm"] = "join_clause",
        ["crossJoinTerm"] = "join_clause",
        ["applyJoinTerm"] = "join_clause",
        ["openJsonWithClause"] = "openjson_with_clause",
        ["tsqlQuotedOption"] = "bracketed",
        ["joinCondition"] = "join_on_condition",
        ["tableName"] = "table_reference",
        ["nonjoinedTableReference"] = "from_expression_element",
        ["derivedTable"] = "table_expression",
        ["asClause"] = "alias_expression",
        ["correlationSpecification"] = "alias_expression",
        ["functionCallTargetName"] = "function_name",
        ["dataType"] = "data_type",
        ["typeArguments"] = "bracketed",
        ["subquery"] = "bracketed",
        ["nonJoinQueryPrimary"] = "bracketed",
        ["inPredicateValue"] = "bracketed",
        ["valueExpressionAtom"] = "bracketed",
        ["booleanPrimary"] = "bracketed",
        ["rowValueConstructor"] = "bracketed",
        ["valueRefNestedExpr"] = "bracketed",
        ["parenthesizedJoinedTable"] = "bracketed",
        ["postgresQuantifiedValue"] = "bracketed",
        ["postgresPostfixOperand"] = "bracketed",
        ["cteDataStatement"] = "bracketed",
        ["distinctOnClause"] = "bracketed",
        ["postgresTableSample"] = "post_table_expression",
        ["greenplumDistributionClause"] = "post_table_expression",
        ["anyProperty"] = "bracketed",
        ["sqlitePragmaArgument"] = "bracketed",
        ["sqliteJoinedSource"] = "bracketed",
        ["tableElementList"] = "table_element_list",
        ["insertColumnsAndSource"] = "table_element_list",
        ["searchCondition"] = "expression",
        ["nativeProceduralCondition"] = "expression",
        ["simpleCase"] = "case_expression",
        ["searchedCase"] = "case_expression",
        ["simpleWhenClause"] = "when_clause",
        ["searchedWhenClause"] = "when_clause",
        ["elseClause"] = "else_clause",
        ["with_list_element"] = "common_table_expression",
        ["valueRefIndexingStep"] = "array_accessor",
        ["parameterName"] = "bind_variable",
        ["namedParameter"] = "bind_variable",
        ["createTableStatement"] = "create_table_statement",
        ["sqliteVirtualTableStatement"] = "create_table_statement",
        ["alterTableStatement"] = "alter_table_statement",
        ["createIndexStatement"] = "create_index_statement",
        ["createViewStatement"] = "create_view_statement",
        ["createTriggerStatement"] = "create_trigger",
        ["dropTableStatement"] = "drop_table_statement",
        ["dropProcedureStatement"] = "drop_procedure_statement",
        ["tsqlRoutineArguments"] = "procedure_parameter_list",
        ["insertStatement"] = "insert_statement",
        ["deleteStatement"] = "delete_statement",
        ["proceduralBlock"] = "begin_end_block",
        ["sqliteTriggerBlock"] = "begin_end_block",
        ["proceduralTryCatch"] = "try_catch",
        ["proceduralIf"] = "if_then_statement",
        ["proceduralLoop"] = "while_statement",
        ["callStatement"] = "execute_script_statement",
        ["goStatement"] = "go_statement",
        ["goBatchSeparator"] = "go_statement",
        ["slashBufferExecutor"] = "slash_buffer_executor",
        ["delimiterStatement"] = "delimiter_statement",
        ["compOp"] = "comparison_operator",
        ["mySqlDuplicateKey"] = "upsert_clause",
        ["mariaDbSystemTime"] = "post_table_expression",
        ["queryHint"] = "query_hint_segment",
    };

    private readonly IReadOnlyList<IToken> _tokens;
    private readonly bool _useTsqlFormattingRules;
    private readonly bool _useMySqlFormattingRules;
    private readonly bool _preserveSqliteMetadata;
    private int _nextToken;
    private string? _pendingWhitespace;
    private bool _keepSqliteScript;

    private DbeaverTreeAdapter(IReadOnlyList<IToken> tokens, string dialect)
    {
        _tokens = tokens;
        _useTsqlFormattingRules = dialect == "tsql";
        _useMySqlFormattingRules = dialect is "mysql" or "mariadb";
        _preserveSqliteMetadata = dialect == "sqlite";
    }

    internal static SqlSegment Convert(SQLStandardParser.SqlScriptContext root, IReadOnlyList<IToken> tokens, string dialect)
    {
        var adapter = new DbeaverTreeAdapter(tokens, dialect);
        var parts = new List<SqlSegment>();
        adapter.Append(root, parts, null);
        adapter.AppendTrivia(parts, tokens.Count);
        string kind = "file";
        if (adapter._keepSqliteScript)
        {
            kind = "verbatim_sqlite";
        }

        var result = Group(kind, parts);
        return result;
    }

    private void Append(IParseTree node, List<SqlSegment> destination, string? nameKind)
    {
        if (node is ITerminalNode terminal)
        {
            if (terminal.Symbol.Type != TokenConstants.EOF)
            {
                int index = terminal.Symbol.TokenIndex;
                AppendTrivia(destination, index);
                var segment = ConvertToken(terminal.Symbol, nameKind);
                destination.Add(segment);
                _nextToken = index + 1;
            }

            return;
        }

        if (node is not ParserRuleContext rule || rule.Start == null || rule.Stop == null ||
            rule.Stop.TokenIndex < rule.Start.TokenIndex)
        {
            return;
        }

        string name = SQLStandardParser.ruleNames[rule.RuleIndex];
        switch (name)
        {
            case "signedNumericLiteral" when _useMySqlFormattingRules && rule.ChildCount == 2:
                AppendSignedNumericLiteral(rule, destination);
                return;
            case "dollarQuotedBody":
            case "soqlRelativeDate":
                AppendAtomicLiteral(rule, destination);
                return;
            case "forClause" when _useTsqlFormattingRules:
                AppendTsqlForClause(rule, destination);
                return;
            case "applyJoinTerm":
                AppendApplyJoin(rule, destination);
                return;
            case "tsqlForXml":
            case "tsqlForJson":
                AppendTsqlForOutput(rule, destination);
                return;
            case "queryHint" when _useTsqlFormattingRules:
                AppendQueryHint(rule, destination);
                return;
            case "proceduralTerminator" when _useMySqlFormattingRules && destination.Count > 0 &&
                destination[^1].Kind == "statement":
                var routineTerminator = new List<SqlSegment>();
                AppendChildren(rule, routineTerminator, null);
                routineTerminator[^1] = routineTerminator[^1] with { Kind = "procedural_statement_terminator" };
                destination[^1] = AttachTerminator(destination[^1], routineTerminator, attachRoutineStatement: true);
                return;
            case "scriptSeparator":
                AppendScriptSeparator(rule, destination);
                return;
            case "derivedColumn" when _preserveSqliteMetadata && !HasDirectRule(rule, "asClause"):
                AppendSqliteText(rule, destination, includeTrailingComments: true);
                return;
            case "sqliteDefaultValue":
            case "sqliteDeclaredType":
                AppendSqliteText(rule, destination, includeTrailingComments: false);
                return;
            case "querySpecification":
            case "selectStatementSingleRow":
                AppendSelect(rule, destination);
                return;
            case "unionTerm":
            case "exceptTerm":
            case "intersectTerm":
                AppendSetTerm(rule, destination);
                return;
            case "directSqlDataStatement":
            case "directSubqueryExpression":
            case "mySqlInsertSource":
                AppendWithStatement(rule, destination);
                return;
            case "cursorDeclaration" when _useTsqlFormattingRules:
                AppendTsqlGrammar(rule, destination, "declare_segment");
                return;
            case "tsqlIdentity":
                AppendTsqlGrammar(rule, destination, "identity_grammar", "keyword");
                return;
            case "defaultClause" when _useTsqlFormattingRules:
                AppendTsqlGrammar(rule, destination, "column_constraint_segment");
                return;
            case "checkConstraintDefinition" when _useTsqlFormattingRules:
                AppendTsqlGrammar(rule, destination, "table_constraint");
                return;
            case "openJsonTable":
                AppendOpenJson(rule, destination);
                return;
            case "tableSubquery" when _useTsqlFormattingRules && rule.Parent is SQLStandardParser.DerivedTableContext:
                AppendDerivedTableQuery(rule, destination);
                return;
            case "mySqlSpecialFunction":
            case "mySqlJsonTable":
            case "functionCallExpression":
            case "aggregateExpression":
            case "caseAbbreviation":
            case "countAllExpression":
            case "castSpecification":
            case "extractExpression":
            case "postgresXmlElement":
            case "postgresXmlAttributes":
                AppendFunction(rule, destination);
                return;
            case "updateStatement":
                AppendUpdate(rule, destination);
                return;
            case "mergeStatement" when _useTsqlFormattingRules:
                AppendMerge(rule, destination);
                return;
            case "mergeWhenClause" when _useTsqlFormattingRules:
                AppendMergeWhen(rule, destination);
                return;
            case "execArgument" when _useTsqlFormattingRules:
                AppendExecArgument(rule, destination);
                return;
            case "windowFrame" when _useTsqlFormattingRules:
                AppendBetween(rule, destination, "frame_clause");
                return;
            case "betweenPredicate":
                AppendBetween(rule, destination, "expression");
                return;
            case "createNamedObjectStatement" when _useTsqlFormattingRules:
                AppendCreateObject(rule, destination);
                return;
            case "routineBodyContent" when _useTsqlFormattingRules:
                AppendRoutineBody(rule, destination);
                return;
            case "identifier":
            case "actualIdentifier":
                nameKind ??= "naked_identifier";
                break;
            case "tsqlIndexKind":
            case "tsqlJoinHint":
            case "tsqlCursorOption":
            case "tsqlRaiseErrorOption":
            case "tsqlLegacyTableHints":
            case "postgresKeyword":
            case "postgresUnicodeEscape":
                nameKind = "keyword";
                break;
            case "functionCallTargetName":
                nameKind = "function_name_identifier";
                break;
            case "castDataType":
            case "dataType":
                nameKind = "data_type_identifier";
                break;
        }

        if (!GroupKinds.TryGetValue(name, out var kind))
        {
            AppendChildren(rule, destination, nameKind);
            return;
        }

        AppendTrivia(destination, rule.Start.TokenIndex);
        var children = new List<SqlSegment>();
        AppendChildren(rule, children, nameKind);
        if (kind == "bracketed")
        {
            WrapParentheses(children);
            destination.AddRange(children);
            return;
        }

        if (kind == "if_then_statement" && _useTsqlFormattingRules)
        {
            children = GroupIfClauses(children);
        }

        if (kind is "common_table_expression" or "table_element_list" or "over_clause" or "alias_expression" or
            "insert_statement" or "on_conflict_clause" or "output_clause" or "option_clause" or "post_table_expression" or
            "grouping_sets_clause" or "cube_rollup_clause" or "openjson_with_clause")
        {
            WrapParentheses(children);
        }

        if (kind == "statement" && children.Count == 1 &&
            children[0].Kind is "go_statement" or "delimiter_statement" or "slash_buffer_executor")
        {
            destination.Add(children[0]);
            return;
        }

        if (kind == "statement" && children.Count > 0 && children[0].Text != null)
        {
            var clause = Group("execute_script_statement", children);
            children = new List<SqlSegment> { clause };
        }

        if (children.Count > 0)
        {
            var segment = Group(kind, children);
            destination.Add(segment);
        }
    }

    private void AppendChildren(ParserRuleContext rule, List<SqlSegment> destination, string? nameKind)
    {
        for (int index = 0; index < rule.ChildCount; index++)
        {
            Append(rule.GetChild(index), destination, nameKind);
        }
    }

    private void AppendScriptSeparator(ParserRuleContext rule, List<SqlSegment> destination)
    {
        switch (rule.Start.Type)
        {
            case SQLStandardLexer.Semicolon:
            case SQLStandardLexer.MySqlScriptDelimiter:
                int previousIndex = destination.Count - 1;
                if (_useTsqlFormattingRules && previousIndex >= 0 && destination[previousIndex].Kind == "statement")
                {
                    var terminator = new List<SqlSegment>();
                    AppendChildren(rule, terminator, null);
                    destination[previousIndex] = AttachTerminator(destination[previousIndex], terminator);
                }
                else
                {
                    AppendChildren(rule, destination, null);
                }

                return;
            case SQLStandardLexer.Solidus:
                AppendTrivia(destination, rule.Start.TokenIndex);
                var slash = new List<SqlSegment>();
                AppendChildren(rule, slash, null);
                destination.Add(Group("slash_buffer_executor", slash));
                return;
            case SQLStandardLexer.GO:
                AppendTrivia(destination, rule.Start.TokenIndex);
                var batch = new List<SqlSegment>();
                AppendChildren(rule, batch, null);
                AppendTrailingBatchComment(batch);
                destination.Add(Group("go_statement", batch));
                return;
            default:
                AppendChildren(rule, destination, null);
                return;
        }
    }

    private void AppendTrailingBatchComment(List<SqlSegment> batch)
    {
        int end = _nextToken;
        for (int index = _nextToken; index < _tokens.Count; index++)
        {
            var token = _tokens[index];
            if (token.Channel == TokenConstants.DefaultChannel)
            {
                break;
            }

            if (token.Type == SQLStandardLexer.Comment)
            {
                end = index + 1;
            }

            if (token.Text.Contains('\r') || token.Text.Contains('\n'))
            {
                break;
            }
        }

        AppendTrivia(batch, end);
    }

    private static SqlSegment AttachTerminator(SqlSegment statement, List<SqlSegment> terminator,
        bool attachRoutineStatement = false)
    {
        var parts = new List<SqlSegment>(statement.Parts);
        if (parts.Count > 0 && parts[^1].Kind == "select_statement" &&
            HasInlineBlockComment(terminator))
        {
            var select = parts[^1];
            var clauses = new List<SqlSegment>(select.Parts);
            if (clauses.Count > 0 && clauses[^1].Kind.EndsWith("_clause", StringComparison.Ordinal))
            {
                clauses[^1] = AttachTerminator(clauses[^1], terminator, attachRoutineStatement);
                parts[^1] = Group(select.Kind, clauses);
                var withInlineComment = Group(statement.Kind, parts);
                return withInlineComment;
            }
        }

        if (parts.Count > 0)
        {
            var lastPart = parts[^1];
            bool isNestedStatement = lastPart.Kind is "if_then_statement" or "while_statement" or "try_catch" or
                "create_procedure_statement" or "create_function_statement" or "procedure_statement" or "create_trigger";
            bool attachSelectTerminator = lastPart.Kind == "select_statement" &&
                (attachRoutineStatement || lastPart.Parts.Any(part => part.Kind == "fetch_clause"));
            if (isNestedStatement || attachSelectTerminator || attachRoutineStatement && lastPart.Kind == "statement")
            {
                parts[^1] = AttachTerminator(lastPart, terminator, attachRoutineStatement);
                var nestedResult = Group(statement.Kind, parts);
                return nestedResult;
            }
        }

        parts.AddRange(terminator);
        var result = Group(statement.Kind, parts);
        return result;
    }

    private static bool HasInlineBlockComment(List<SqlSegment> terminator)
    {
        bool hasComment = false;
        for (int index = 0; index < terminator.Count - 1; index++)
        {
            var part = terminator[index];
            if (part.Kind == "block_comment")
            {
                hasComment = true;
            }
            else if (part.Kind != "whitespace" || part.Text!.Contains('\n') || part.Text.Contains('\r'))
            {
                return false;
            }
        }

        return hasComment;
    }

    private void AppendSelect(ParserRuleContext rule, List<SqlSegment> destination)
    {
        AppendTrivia(destination, rule.Start.TokenIndex);
        var select = new List<SqlSegment>();
        bool hasSelectClause = false;
        for (int index = 0; index < rule.ChildCount; index++)
        {
            var child = rule.GetChild(index);
            if (!hasSelectClause && child is ParserRuleContext context &&
                SQLStandardParser.ruleNames[context.RuleIndex] is "tableExpression" or "selectIntoClause" or
                    "fromClause" or "whereClause" or "groupByClause" or "orderByClause" or "limitClause" or
                    "havingClause" or "qualifyClause" or "windowClause" or "offsetClause" or "fetchClause")
            {
                destination.Add(Group("select_clause", select));
                hasSelectClause = true;
            }

            if (hasSelectClause)
            {
                Append(child, destination, null);
            }
            else
            {
                Append(child, select, null);
            }
        }

        if (!hasSelectClause)
        {
            destination.Add(Group("select_clause", select));
        }
    }

    private void AppendSetTerm(ParserRuleContext rule, List<SqlSegment> destination)
    {
        AppendTrivia(destination, rule.Start.TokenIndex);
        var prefix = new List<SqlSegment>();
        for (int index = 0; index < rule.ChildCount; index++)
        {
            var child = rule.GetChild(index);
            if (child is ParserRuleContext context &&
                SQLStandardParser.ruleNames[context.RuleIndex] is "queryTerm" or "queryPrimary")
            {
                destination.Add(Group("set_operator", prefix));
                for (; index < rule.ChildCount; index++)
                {
                    Append(rule.GetChild(index), destination, null);
                }

                return;
            }

            Append(child, prefix, null);
        }

        destination.Add(Group("set_operator", prefix));
    }

    private void AppendBetween(ParserRuleContext rule, List<SqlSegment> destination, string kind)
    {
        AppendTrivia(destination, rule.Start.TokenIndex);
        var parts = new List<SqlSegment>();
        for (int index = 0; index < rule.ChildCount; index++)
        {
            var child = rule.GetChild(index);
            Append(child, parts, null);
            if (child is ITerminalNode terminal &&
                terminal.Symbol.Text.Equals("AND", StringComparison.OrdinalIgnoreCase))
            {
                parts[^1] = new SqlSegment("keyword", terminal.Symbol.Text, null);
            }
        }

        destination.Add(Group(kind, parts));
    }

    private void AppendWithStatement(ParserRuleContext rule, List<SqlSegment> destination)
    {
        if (rule.ChildCount == 0 || rule.GetChild(0) is not ParserRuleContext prefix ||
            SQLStandardParser.ruleNames[prefix.RuleIndex] != "withClause")
        {
            AppendChildren(rule, destination, null);
            return;
        }

        AppendTrivia(destination, rule.Start.TokenIndex);
        var parts = new List<SqlSegment>();
        Append(prefix, parts, null);
        if (_useTsqlFormattingRules)
        {
            GroupXmlNamespaces(parts);
        }
        for (int index = 1; index < rule.ChildCount; index++)
        {
            var child = rule.GetChild(index);
            var statement = new List<SqlSegment>();
            Append(child, statement, null);
            if (statement.Count > 0)
            {
                parts.Add(Group("query_statement", statement));
            }
        }

        destination.Add(Group("with_compound_statement", parts));
    }

    private static void GroupXmlNamespaces(List<SqlSegment> parts)
    {
        int nameIndex = parts.FindIndex(part => string.Equals(part.Text, "XMLNAMESPACES", StringComparison.OrdinalIgnoreCase));
        if (nameIndex < 0)
        {
            return;
        }

        WrapParentheses(parts);
        int endIndex = parts.FindIndex(nameIndex + 1, part => part.Kind == "bracketed");
        if (endIndex >= 0)
        {
            int count = endIndex - nameIndex + 1;
            var namespaces = parts.GetRange(nameIndex, count);
            parts.RemoveRange(nameIndex, count);
            parts.Insert(nameIndex, Group("xml_namespaces_clause", namespaces));
        }
    }

    private void AppendTsqlForClause(ParserRuleContext rule, List<SqlSegment> destination)
    {
        AppendTrivia(destination, rule.Start.TokenIndex);
        var parts = new List<SqlSegment>();
        for (int index = 0; index < rule.ChildCount; index++)
        {
            var child = rule.GetChild(index);
            if (index == 1 && child is ParserRuleContext context &&
                SQLStandardParser.ruleNames[context.RuleIndex] == "anyWord")
            {
                Append(child, parts, "keyword");
            }
            else
            {
                Append(child, parts, null);
            }
        }

        WrapParentheses(parts);
        destination.Add(Group("for_clause", parts));
    }

    private void AppendSignedNumericLiteral(ParserRuleContext rule, List<SqlSegment> destination)
    {
        AppendTrivia(destination, rule.Start.TokenIndex);
        var parts = new List<SqlSegment>();
        AppendChildren(rule, parts, null);
        if (parts.Count == 2)
        {
            parts[0] = new SqlSegment("sign_indicator", parts[0].Text, null);
            destination.Add(Group("numeric_literal", parts));
            return;
        }

        destination.AddRange(parts);
    }

    private void AppendApplyJoin(ParserRuleContext rule, List<SqlSegment> destination)
    {
        AppendTrivia(destination, rule.Start.TokenIndex);
        var parts = new List<SqlSegment>();
        Append(rule.GetChild(0), parts, null);
        Append(rule.GetChild(1), parts, "keyword");
        Append(rule.GetChild(2), parts, null);
        destination.Add(Group("join_clause", parts));
    }

    private void AppendTsqlForOutput(ParserRuleContext rule, List<SqlSegment> destination)
    {
        Append(rule.GetChild(0), destination, "keyword");
        for (int index = 1; index < rule.ChildCount; index++)
        {
            var child = rule.GetChild(index);
            Append(child, destination, null);
        }
    }

    private void AppendTsqlGrammar(ParserRuleContext rule, List<SqlSegment> destination, string kind, string? nameKind = null)
    {
        AppendTrivia(destination, rule.Start.TokenIndex);
        var parts = new List<SqlSegment>();
        AppendChildren(rule, parts, nameKind);
        WrapParentheses(parts);
        destination.Add(Group(kind, parts));
    }

    private void AppendOpenJson(ParserRuleContext rule, List<SqlSegment> destination)
    {
        AppendTrivia(destination, rule.Start.TokenIndex);
        var parts = new List<SqlSegment>();
        Append(rule.GetChild(0), parts, "keyword");
        for (int index = 1; index < rule.ChildCount; index++)
        {
            var child = rule.GetChild(index);
            Append(child, parts, null);
        }

        WrapParentheses(parts);
        destination.Add(Group("openjson_segment", parts));
    }

    private void AppendDerivedTableQuery(ParserRuleContext rule, List<SqlSegment> destination)
    {
        var subquery = (SQLStandardParser.SubqueryContext)rule.GetChild(0);
        AppendTrivia(destination, rule.Start.TokenIndex);
        var parts = new List<SqlSegment>();
        Append(subquery.GetChild(0), parts, null);
        var query = new List<SqlSegment>();
        Append(subquery.GetChild(1), query, null);
        parts.Add(Group("select_statement", query));
        Append(subquery.GetChild(2), parts, null);
        destination.Add(Group("bracketed", parts));
    }

    private void AppendFunction(ParserRuleContext rule, List<SqlSegment> destination)
    {
        AppendTrivia(destination, rule.Start.TokenIndex);
        var parts = new List<SqlSegment>();
        AppendChildren(rule, parts, null);
        if (parts.Count > 0 && parts[0].Text != null)
        {
            var first = parts[0];
            var identifier = new SqlSegment("function_name_identifier", first.Text, null);
            var name = new SqlSegment("function_name", null, new[] { identifier });
            parts[0] = name;
        }

        WrapParentheses(parts);
        destination.Add(Group("function", parts));
    }

    private static List<SqlSegment> GroupIfClauses(List<SqlSegment> parts)
    {
        int firstStatement = parts.FindIndex(part => part.Kind == "statement");
        if (firstStatement < 0)
        {
            return parts;
        }

        var condition = parts.GetRange(0, firstStatement);
        var grouped = new List<SqlSegment> { Group("if_clause", condition) };
        for (int index = firstStatement; index < parts.Count; index++)
        {
            var part = parts[index];
            if (part.Kind == "statement" && part.Parts.Count == 1 && part.Parts[0].Kind == "if_then_statement")
            {
                grouped.AddRange(part.Parts[0].Parts);
            }
            else
            {
                grouped.Add(part);
            }
        }

        return grouped;
    }

    private void AppendCreateObject(ParserRuleContext rule, List<SqlSegment> destination)
    {
        string? kind = null;
        for (int index = 0; index < rule.ChildCount; index++)
        {
            if (rule.GetChild(index) is not ParserRuleContext child ||
                SQLStandardParser.ruleNames[child.RuleIndex] != "createObjectKind")
            {
                continue;
            }

            var objectType = child.GetText().ToUpperInvariant();
            switch (objectType)
            {
                case "PROC":
                case "PROCEDURE":
                    kind = "create_procedure_statement";
                    break;
                case "FUNCTION":
                    kind = "create_function_statement";
                    break;
            }

            break;
        }

        if (kind == null)
        {
            AppendChildren(rule, destination, null);
            return;
        }

        AppendTrivia(destination, rule.Start.TokenIndex);
        var parts = new List<SqlSegment>();
        for (int index = 0; index < rule.ChildCount; index++)
        {
            var child = rule.GetChild(index);
            Append(child, parts, null);
            if (index == 1 && child is ITerminalNode terminal && terminal.Symbol.Type == SQLStandardLexer.OR)
            {
                parts[^1] = new SqlSegment("keyword", terminal.Symbol.Text, null);
            }
        }

        destination.Add(Group(kind, parts));
    }

    private void AppendRoutineBody(ParserRuleContext rule, List<SqlSegment> destination)
    {
        AppendTrivia(destination, rule.Start.TokenIndex);
        var parts = new List<SqlSegment>();
        AppendChildren(rule, parts, null);
        if (parts.Count == 1 && parts[0].Kind is "begin_end_block" or "try_catch")
        {
            var statement = Group("statement", parts);
            var body = new List<SqlSegment> { statement };
            destination.Add(Group("procedure_statement", body));
        }
        else
        {
            destination.AddRange(parts);
        }
    }

    private void AppendUpdate(ParserRuleContext rule, List<SqlSegment> destination)
    {
        AppendTrivia(destination, rule.Start.TokenIndex);
        var parts = new List<SqlSegment>();
        AppendChildren(rule, parts, null);
        CombineSetAssignments(parts);
        destination.Add(Group("update_statement", parts));
    }

    private void AppendMerge(ParserRuleContext rule, List<SqlSegment> destination)
    {
        AppendTrivia(destination, rule.Start.TokenIndex);
        var parts = new List<SqlSegment>();
        AppendChildren(rule, parts, null);
        int onIndex = parts.FindIndex(part => IsKeyword(part, "ON"));
        int firstCase = parts.FindIndex(part => part.Kind is "merge_when_matched_clause" or "merge_when_not_matched_clause");
        if (onIndex >= 0 && firstCase > onIndex)
        {
            var condition = parts.GetRange(onIndex, firstCase - onIndex);
            parts.RemoveRange(onIndex, firstCase - onIndex);
            parts.Insert(onIndex, Group("join_on_condition", condition));
        }

        destination.Add(Group("merge_statement", parts));
    }

    private void AppendMergeWhen(ParserRuleContext rule, List<SqlSegment> destination)
    {
        AppendTrivia(destination, rule.Start.TokenIndex);
        var parts = new List<SqlSegment>();
        AppendChildren(rule, parts, null);
        int thenIndex = parts.FindIndex(part => IsKeyword(part, "THEN"));
        if (thenIndex >= 0)
        {
            int actionIndex = parts.FindIndex(thenIndex + 1, part => part.Kind == "keyword");
            if (actionIndex >= 0)
            {
                var action = parts.GetRange(actionIndex, parts.Count - actionIndex);
                parts.RemoveRange(actionIndex, parts.Count - actionIndex);
                string actionType = "merge_delete_clause";
                if (IsKeyword(action[0], "UPDATE"))
                {
                    CombineSetAssignments(action);
                    actionType = "merge_update_clause";
                }
                else if (IsKeyword(action[0], "INSERT"))
                {
                    WrapParentheses(action);
                    actionType = "merge_insert_clause";
                }

                parts.Add(Group(actionType, action));
            }
        }

        string kind = "merge_when_matched_clause";
        if (parts.Count > 0 && parts.Take(thenIndex).Any(part => IsKeyword(part, "NOT")))
        {
            kind = "merge_when_not_matched_clause";
        }

        destination.Add(Group(kind, parts));
    }

    private void AppendExecArgument(ParserRuleContext rule, List<SqlSegment> destination)
    {
        AppendChildren(rule, destination, null);
        if (rule.ChildCount > 1 && rule.GetChild(rule.ChildCount - 1) is ParserRuleContext modifier &&
            SQLStandardParser.ruleNames[modifier.RuleIndex] == "anyWord" &&
            string.Equals(modifier.GetText(), "OUT", StringComparison.OrdinalIgnoreCase))
        {
            var part = destination[^1];
            destination[^1] = new SqlSegment("keyword", part.Text, null);
        }
    }

    private static void CombineSetAssignments(List<SqlSegment> parts)
    {
        int setIndex = parts.FindIndex(part => IsKeyword(part, "SET"));
        int assignments = parts.FindIndex(part => part.Kind == "set_clause_list");
        if (setIndex >= 0 && assignments > setIndex)
        {
            var setParts = parts.GetRange(setIndex, assignments - setIndex);
            setParts.AddRange(parts[assignments].Parts);
            parts.RemoveRange(setIndex, assignments - setIndex + 1);
            parts.Insert(setIndex, Group("set_clause_list", setParts));
        }
    }

    private static bool IsKeyword(SqlSegment segment, string text)
    {
        var matches = segment.Kind == "keyword" && string.Equals(segment.Text, text, StringComparison.OrdinalIgnoreCase);
        return matches;
    }

    private static void WrapParentheses(List<SqlSegment> parts)
    {
        int depth = 0;
        int opening = -1;
        for (int index = 0; index < parts.Count; index++)
        {
            if (parts[index].Kind == "start_bracket")
            {
                if (depth == 0)
                {
                    opening = index;
                }

                depth++;
            }
            else if (parts[index].Kind == "end_bracket" && depth > 0)
            {
                depth--;
                if (depth == 0)
                {
                    int count = index - opening + 1;
                    var inner = parts.GetRange(opening, count);
                    parts.RemoveRange(opening, count);
                    parts.Insert(opening, Group("bracketed", inner));
                    index = opening;
                }
            }
        }
    }

    private void AppendQueryHint(ParserRuleContext rule, List<SqlSegment> destination)
    {
        AppendTrivia(destination, rule.Start.TokenIndex);
        var parts = new List<SqlSegment>();
        AppendChildren(rule, parts, null);
        for (int index = 0; index < parts.Count; index++)
        {
            var part = parts[index];
            if (part.Kind == "naked_identifier" && part.Text is string text && SqlServerFormattingKeywords.Contains(text))
            {
                parts[index] = new SqlSegment("keyword", text, null);
            }
        }

        destination.Add(Group("query_hint_segment", parts));
    }

    private void AppendAtomicLiteral(ParserRuleContext rule, List<SqlSegment> destination)
    {
        AppendTrivia(destination, rule.Start.TokenIndex);
        var text = new StringBuilder();
        int end = rule.Stop.TokenIndex + 1;
        while (_nextToken < end)
        {
            text.Append(_tokens[_nextToken].Text);
            _nextToken++;
        }

        var literal = new SqlSegment("quoted_literal", text.ToString(), null);
        destination.Add(literal);
    }

    private static bool HasDirectRule(ParserRuleContext rule, string name)
    {
        for (int index = 0; index < rule.ChildCount; index++)
        {
            if (rule.GetChild(index) is ParserRuleContext child &&
                SQLStandardParser.ruleNames[child.RuleIndex] == name)
            {
                return true;
            }
        }

        return false;
    }

    private void AppendSqliteText(ParserRuleContext rule, List<SqlSegment> destination, bool includeTrailingComments)
    {
        AppendTrivia(destination, rule.Start.TokenIndex);
        int end = rule.Stop.TokenIndex + 1;
        int lastComment = -1;
        int nextCode = end;
        if (includeTrailingComments)
        {
            // SQLite can expose the original projection text as its result name.
            while (nextCode < _tokens.Count && _tokens[nextCode].Channel != TokenConstants.DefaultChannel)
            {
                if (_tokens[nextCode].Type == SQLStandardLexer.Comment)
                {
                    lastComment = nextCode;
                }

                nextCode++;
            }
        }

        if (lastComment >= 0)
        {
            end = lastComment + 1;
        }

        var text = new StringBuilder();
        for (int index = _nextToken; index < end; index++)
        {
            var token = _tokens[index];
            text.Append(token.Text);
            if (token.Type == SQLStandardLexer.Comment && ContainsFormatDirective(token.Text))
            {
                _keepSqliteScript = true;
            }
        }

        _nextToken = end;
        if (lastComment >= 0 && end < nextCode && nextCode < _tokens.Count &&
            !_tokens[lastComment].Text.StartsWith("/*", StringComparison.Ordinal))
        {
            // Carry the terminating newline with a line comment so that an inserted
            // comma or closing bracket cannot become part of the comment.
            string separator = _tokens[end].Text;
            int newlineIndex = separator.IndexOfAny(new[] { '\r', '\n' });
            if (newlineIndex >= 0)
            {
                int length = newlineIndex + 1;
                if (separator[newlineIndex] == '\r' && length < separator.Length && separator[length] == '\n')
                {
                    length++;
                }

                text.Append(separator.AsSpan(0, length));
                _pendingWhitespace = separator[length..];
                _nextToken++;
            }
        }

        var segment = new SqlSegment("sqlite_preserved_text", text.ToString(), null);
        destination.Add(segment);
    }

    private static bool ContainsFormatDirective(string text)
    {
        var containsDirective = text.StartsWith("/*", StringComparison.Ordinal) &&
            (text.Contains("[NOFORMAT]", StringComparison.OrdinalIgnoreCase) ||
             text.Contains("[/NOFORMAT]", StringComparison.OrdinalIgnoreCase) ||
             text.Contains("[MINIFY]", StringComparison.OrdinalIgnoreCase) ||
             text.Contains("[/MINIFY]", StringComparison.OrdinalIgnoreCase));
        return containsDirective;
    }

    private void AppendTrivia(List<SqlSegment> destination, int end)
    {
        if (_pendingWhitespace != null)
        {
            SqlSegment.AppendWhitespace(destination, _pendingWhitespace);
            _pendingWhitespace = null;
        }

        while (_nextToken < end)
        {
            var token = _tokens[_nextToken];
            var segment = ConvertToken(token, null);
            if (segment.Kind == "whitespace")
            {
                SqlSegment.AppendWhitespace(destination, token.Text);
            }
            else
            {
                destination.Add(segment);
            }

            _nextToken++;
        }
    }

    private SqlSegment ConvertToken(IToken token, string? nameKind)
    {
        string type = SQLStandardLexer.DefaultVocabulary.GetSymbolicName(token.Type) ?? "";
        string text = token.Text;
        string kind;
        switch (type)
        {
            case "Separator":
            case "Space":
            case "WS":
                kind = "whitespace";
                break;
            case "Comment":
            case "LineComment":
            case "MultilineComment":
                kind = "inline_comment";
                if (text.StartsWith("/*", StringComparison.Ordinal))
                {
                    kind = "block_comment";
                }
                break;
            case "MySqlIntroducedLiteral":
            case "StringLiteralContent":
            case "NationalCharacterStringLiteral":
            case "BitStringLiteral":
            case "HexStringLiteral":
            case "DollarQuotedStringLiteral":
            case "OracleAlternativeLiteral":
            case "PostgresEscapeStringLiteral":
            case "PostgresUnicodeStringLiteral":
                kind = "quoted_literal";
                break;
            case "DelimitedIdentifier":
            case "PostgresUnicodeIdentifier":
            case "Quotted":
                kind = "quoted_identifier";
                break;
            case "Identifier":
                kind = "naked_identifier";
                if (nameKind == "data_type_identifier" && !_useTsqlFormattingRules &&
                    SqlServerFormattingKeywords.IsDataType(text))
                {
                    kind = "keyword";
                }
                else if (nameKind != null)
                {
                    kind = nameKind;
                }
                break;
            // VALUE can be a T-SQL column or alias, including in a flat procedural statement.
            case "VALUE" when _useTsqlFormattingRules:
            case "SETS" when _useTsqlFormattingRules:
                kind = "naked_identifier";
                break;
            case "DollarAction":
                kind = "action_parameter";
                break;
            case "MySqlSystemVariable":
            case "BatchVariableName":
            case "ClientVariableName":
            case "SqliteParameter":
                kind = "variable";
                break;
            case "UnsignedInteger":
            case "DecimalLiteral":
            case "ApproximateNumericLiteral":
            case "SqliteHexInteger":
                kind = "numeric_literal";
                break;
            case "BinaryLiteral":
            case "HexLiteral":
            case "TsqlBinaryLiteral":
            case "MySqlHexInteger":
            case "MySqlBinaryInteger":
                kind = "binary_literal";
                break;
            case "Semicolon":
            case "MySqlBodySemicolon":
            case "MySqlScriptDelimiter":
                kind = "statement_terminator";
                break;
            case "LeftParen":
                kind = "start_bracket";
                break;
            case "RightParen":
                kind = "end_bracket";
                break;
            case "Comma":
                kind = "comma";
                break;
            case "Period":
                kind = "dot";
                break;
            case "TypeCast":
                kind = "casting_operator";
                break;
            case "At":
                kind = "at_sign";
                break;
            case "Asterisk":
                kind = "star";
                break;
            case "EqualsOperator":
            case "MySqlNullSafeEquals":
            case "NotEqualsOperator":
            case "LessThanOperator":
            case "GreaterThanOperator":
            case "LessThanOrEqualsOperator":
            case "GreaterThanOrEqualsOperator":
                kind = "comparison_operator";
                break;
            case "PlusSign":
            case "MinusSign":
            case "Solidus":
            case "Percent":
            case "OraclePercent":
            case "Ampersand":
            case "VerticalBar":
            case "ConcatenationOperator":
            case "ShiftLeft":
            case "ShiftRight":
            case "JsonArrow":
            case "PostgresJsonPathOperator":
            case "PostgresRegexOperator":
            case "PostgresPredicateOperator":
            case "CompoundAssignment":
            case "Tilda":
            case "AND":
            case "MYSQL_XOR":
            case "OR":
                kind = "binary_operator";
                break;
            default:
                kind = "raw";
                if (type.Length > 0 && type.All(character => character is >= 'A' and <= 'Z' or '_' or >= '0' and <= '9'))
                {
                    kind = nameKind ?? "keyword";
                }
                break;
        }

        var segment = new SqlSegment(kind, text, null);
        return segment;
    }

    private static SqlSegment Group(string kind, List<SqlSegment> parts)
    {
        var segment = new SqlSegment(kind, null, parts.ToArray());
        return segment;
    }
}
