using Zea.SqlFormat.Formatters.RightWay.ParseStructure;
using static Zea.SqlFormat.Formatters.RightWay.Interfaces.SqlStructureConstants;

namespace Zea.SqlFormat.Formatters.RightWay;

internal sealed partial class RightWayTreeAdapter
{
    private readonly bool _useTsqlFormattingRules;

    private RightWayTreeAdapter(bool useTsqlFormattingRules)
    {
        _useTsqlFormattingRules = useTsqlFormattingRules;
    }

    public static Node Convert(SqlSegment tree, bool useTsqlFormattingRules)
    {
        var adapter = new RightWayTreeAdapter(useTsqlFormattingRules);
        var root = new NodeImpl { Name = ENAME_SQL_ROOT };
        adapter.Append(tree, root, "");
        if (useTsqlFormattingRules && root.Children.LastOrDefault(child => child.Name != ENAME_WHITESPACE)?.Name == ENAME_BATCH_SEPARATOR)
        {
            var nextStatement = Add(root, ENAME_SQL_STATEMENT);
            Add(nextStatement, ENAME_SQL_CLAUSE);
        }

        adapter.CheckDepth(root, 0);
        return root;
    }

    public static void CheckEquivalent(SqlSegment source, SqlSegment formatted, bool useTsqlFormattingRules)
    {
        bool matches;
        if (useTsqlFormattingRules)
        {
            // Keep statement boundaries and code token order after Right Way's
            // intentional T-SQL keyword standardization.
            matches = StandardizedTsqlTokens(source).SequenceEqual(
                StandardizedTsqlTokens(formatted), StringComparer.Ordinal);
        }
        else
        {
            matches = Equivalent(source, formatted);
        }

        if (!matches)
        {
            throw new FormatException("Right Way formatting would change the SQL syntax.");
        }
    }

    private static IEnumerable<string> StandardizedTsqlTokens(SqlSegment segment)
    {
        if (segment.Text is string text)
        {
            if (IsTrivia(segment) || text.Length == 0)
            {
                yield break;
            }

            switch (segment.Kind)
            {
                case "keyword":
                case "null_literal":
                case "boolean_literal":
                case "data_type_identifier" when SqlServerFormattingKeywords.IsDataType(text):
                    text = StandardizeTsqlKeyword(text);
                    break;
                case "binary_operator":
                case var kind when IsProceduralLayoutKeyword(kind, text):
                case "function_name_identifier" when SqlServerFormattingKeywords.Contains(text):
                case "numeric_literal":
                case "integer_literal":
                case "binary_literal":
                    text = text.ToUpperInvariant();
                    break;
                case "quoted_literal" when text.StartsWith("n'", StringComparison.OrdinalIgnoreCase):
                    text = "N" + text[1..];
                    break;
            }

            yield return text;
            yield break;
        }

        if (segment.Kind == "data_type")
        {
            foreach (var token in StandardizedTsqlDataTypeTokens(segment))
            {
                yield return token;
            }

            yield break;
        }

        if (TryGetStaticTypeMethod(segment, out var receiver, out var call))
        {
            foreach (var token in StandardizedStaticTypeMethodTokens(segment, receiver, call))
            {
                yield return token;
            }

            yield break;
        }

        int start = 0;
        if (segment.Kind is "join_clause" or "query_hint_segment")
        {
            var prefix = ReadJoinPrefix(segment.Parts);
            if (prefix.Length != 0)
            {
                yield return StandardizeTsqlKeyword(prefix.Keywords);
                start = prefix.Length;
            }
        }

        foreach (var part in segment.Parts.Skip(start))
        {
            foreach (var token in StandardizedTsqlTokens(part))
            {
                yield return token;
            }
        }
    }

    private static string StandardizeTsqlKeyword(string text)
    {
        var upper = text.ToUpperInvariant();
        if (StandardKeywordRemapping.Instance.TryGetValue(upper, out var replacement))
        {
            return replacement;
        }

        return upper;
    }

    private static bool Equivalent(SqlSegment source, SqlSegment formatted)
    {
        if (source.Kind != formatted.Kind)
        {
            return false;
        }

        if (source.Text != null)
        {
            var comparison = StringComparison.Ordinal;
            if (source.Kind is "keyword" or "binary_operator" or "null_literal" or "boolean_literal")
            {
                comparison = StringComparison.OrdinalIgnoreCase;
            }

            var equal = string.Equals(source.Text, formatted.Text, comparison);
            return equal;
        }

        var left = source.Parts.Where(part => !IsWhitespace(part)).ToArray();
        var right = formatted.Parts.Where(part => !IsWhitespace(part)).ToArray();
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Length; i++)
        {
            if (!Equivalent(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    private void Append(SqlSegment segment, Node parent, string context)
    {
        if (_useTsqlFormattingRules &&
            (TryAppendTsqlClauseLayout(segment, parent, context) ||
             TryAppendDdlLayout(segment, parent, context) ||
             TryAppendProceduralLayout(segment, parent, context) ||
             TryAppendQueryLayout(segment, parent, context)))
        {
            return;
        }


        if (segment.Text != null)
        {
            if (context == "table_reference" && segment.Kind == "at_sign")
            {
                Add(parent, ENAME_PERIOD, segment.Text);
            }
            else if (_useTsqlFormattingRules && context == "output_clause" &&
                (IsKeyword(segment, "inserted") || IsKeyword(segment, "deleted")))
            {
                Add(parent, ENAME_OTHERNODE, segment.Text);
            }
            else
            {
                AppendToken(segment.Kind, segment.Text, parent);
            }

            return;
        }

        switch (segment.Kind)
        {
            case "statement":
                if (_useTsqlFormattingRules && segment.Parts.Count == 1 && segment.Parts[0].Kind == "try_catch" &&
                    AppendTryCatch(segment.Parts[0], parent))
                {
                    return;
                }

                var statement = Add(parent, ENAME_SQL_STATEMENT);
                AppendParts(segment, statement, context);
                return;
            case "from_clause":
                var from = Add(parent, ENAME_SQL_CLAUSE);
                AppendFrom(segment, parent, from);
                return;
            case "limit_clause":
                AppendLimit(segment, parent);
                return;
            case "output_clause" when _useTsqlFormattingRules:
                AppendOutputClause(segment, parent);
                return;
            case "select_clause":
            case "where_clause":
            case "groupby_clause":
            case "having_clause":
            case "orderby_clause":
            case "qualify_clause":
            case "fetch_clause":
            case "offset_clause":
            case "values_clause":
            case "set_clause_list":
            case "into_clause":
            case "into_table_clause":
            case "returning_clause":
            case "output_clause":
            case "on_conflict_clause":
            case "upsert_clause":
            case "window_clause":
                var clause = Add(parent, ENAME_SQL_CLAUSE);
                AppendParts(segment, clause, segment.Kind);
                return;
            case "set_operator":
                if (_useTsqlFormattingRules && parent.Name == ENAME_SELECTIONTARGET_PARENS)
                {
                    parent = Add(parent, ENAME_SQL_CLAUSE);
                }

                var setOperator = Add(parent, ENAME_SET_OPERATOR_CLAUSE);
                AppendParts(segment, setOperator, context);
                return;
            case "go_statement":
            case "slash_buffer_executor":
            case "delimiter_statement":
                var batch = Add(parent, ENAME_BATCH_SEPARATOR);
                AppendParts(segment, batch, context);
                return;
            case "begin_end_block":
            case "compound_statement":
                AppendBlock(segment, parent);
                return;
            case "if_then_statement" when _useTsqlFormattingRules:
            case "while_statement" when _useTsqlFormattingRules:
                AppendControlFlow(segment, parent);
                return;
            case "merge_statement" when _useTsqlFormattingRules:
                AppendMerge(segment, parent);
                return;
            case "execute_script_statement":
            case "set_local_variable_segment":
            case "declare_segment":
            case "null_statement":
                var singleClause = Add(parent, ENAME_SQL_CLAUSE);
                AppendParts(segment, singleClause, segment.Kind);
                return;
            case "function_name":
                var nameParts = segment.Parts;
                if (_useTsqlFormattingRules && nameParts.Count == 1 && nameParts[0].Text is string functionName &&
                    nameParts[0].Kind == "function_name_identifier" &&
                    IsTsqlBuiltInFunction(functionName))
                {
                    Add(parent, ENAME_FUNCTION_KEYWORD, functionName);
                }
                else
                {
                    AppendParts(segment, parent, segment.Kind);
                }

                return;
            case "table_reference":
            case "data_type":
            case "function_contents":
            case "bracketed_arguments":
                AppendParts(segment, parent, segment.Kind);
                return;
            case "function":
                AppendParts(segment, parent, "function");
                return;
            case "bracketed":
                AppendBrackets(segment, parent, context);
                return;
            case "numeric_literal" when !_useTsqlFormattingRules && segment.Parts.Count == 2 &&
                segment.Parts[0].Kind is "sign_indicator" or "dot" && segment.Parts[1].Kind == "numeric_literal":
                Add(parent, ENAME_OTHERNODE, segment.Raw());
                return;
            case "binary_operator":
            case "comparison_operator":
            case "assignment_operator":
                var operatorText = segment.Raw();
                if (segment.Parts.All(part => !IsWhitespace(part) && !part.Kind.EndsWith("comment", StringComparison.Ordinal)))
                {
                    Add(parent, ENAME_OTHEROPERATOR, operatorText);
                    return;
                }

                break;
            case "pattern_expression":
                // Row-pattern quantifiers have their own punctuation rules.
                Add(parent, ENAME_OTHERNODE, segment.Raw());
                return;
            case "plsql_label":
                Add(parent, ENAME_LABEL, segment.Raw());
                return;
            case "case_expression":
                AppendCase(segment, parent);
                return;
            case "expression":
                AppendExpression(segment, parent, context);
                return;
            case "common_table_expression":
                AppendCte(segment, parent);
                return;
            case "with_compound_statement":
                var with = Add(parent, ENAME_SQL_CLAUSE);
                foreach (var part in segment.Parts)
                {
                    if (part.Kind.EndsWith("_statement", StringComparison.Ordinal) ||
                        (_useTsqlFormattingRules && part.Kind == "set_expression"))
                    {
                        Append(part, parent, context);
                        with = parent.Children.Last();
                    }
                    else
                    {
                        Append(part, with, context);
                    }
                }

                return;
            case "array_accessor":
                Add(parent, ENAME_RAW_SUFFIX, segment.Raw());
                return;
            case "bind_variable":
            case "semi_structured_expression":
            case "postgres_variable":
                Add(parent, ENAME_OTHERNODE, segment.Raw());
                return;
            case "create_table_statement":
            case "insert_statement":
            case "update_statement":
            case "delete_statement":
                AppendStatementParts(segment, parent);
                return;
        }

        AppendParts(segment, parent, context);
    }

    private void AppendParts(SqlSegment segment, Node parent, string context)
    {
        AppendSequence(segment.Parts, 0, segment.Parts.Count, parent, context);
    }

    private void AppendSequence(IReadOnlyList<SqlSegment> parts, int start, int stop, Node parent, string context)
    {
        for (int i = start; i < stop; i++)
        {
            if (_useTsqlFormattingRules && parts[i].Kind == "newline")
            {
                i = AppendStatementTrivia(parts, i, stop, parent, context);
            }
            else if (_useTsqlFormattingRules && parts[i].Kind == "inline_comment" &&
                     parent.Name is ENAME_SQL_ROOT or ENAME_CONTAINER_MULTISTATEMENT &&
                     TryAppendTrailingCommentRun(parts, start, stop, parent, ref i))
            {
                continue;
            }
            else
            {
                i = AppendPart(parts, i, parent, context, stop);
            }
        }
    }

    private bool TryAppendTrailingCommentRun(IReadOnlyList<SqlSegment> parts, int start, int stop,
        Node parent, ref int index)
    {
        int previous = index - 1;
        while (previous >= start && parts[previous].Kind == "whitespace")
        {
            previous--;
        }

        if (previous < start || IsTrivia(parts[previous]))
        {
            return false;
        }

        int last = index;
        while (last + 1 < stop && parts[last + 1].Kind == "newline")
        {
            int next = last + 2;
            while (next < stop && parts[next].Kind == "whitespace")
            {
                next++;
            }

            if (next == stop || parts[next].Kind != "inline_comment")
            {
                break;
            }

            last = next;
        }

        // Right Way keeps a run that starts on the same line as SQL in that
        // clause. An intervening blank line starts a new comment group.
        if (last == index || LastStatementClause(parent) is not Node clause)
        {
            return false;
        }

        AppendRange(parts, index, last + 1, clause);
        index = last;
        return true;
    }

    private int AppendStatementTrivia(IReadOnlyList<SqlSegment> parts, int start, int stop, Node parent,
        string context)
    {
        int statementIndex = start + 1;
        while (statementIndex < stop && IsWhitespace(parts[statementIndex]))
        {
            statementIndex++;
        }

        int firstComment = statementIndex;
        while (statementIndex < stop && IsTrivia(parts[statementIndex]))
        {
            statementIndex++;
        }

        if (firstComment != statementIndex && statementIndex == stop)
        {
            if (TryAppendClosingBlockTrivia(parts, start, stop, parent, context))
            {
                return stop - 1;
            }

            if (parent.Name == ENAME_SQL_ROOT && parts[firstComment].Kind == "block_comment" &&
                LastStatementClause(parent) is Node clause)
            {
                for (int i = start; i < stop; i++)
                {
                    Append(parts[i], clause, context);
                }

                return stop - 1;
            }
        }

        if (firstComment == statementIndex || statementIndex == stop ||
            parts[statementIndex].Kind is not ("statement" or "batch"))
        {
            for (int i = start; i < statementIndex; i++)
            {
                Append(parts[i], parent, context);
            }

            return statementIndex - 1;
        }

        // Standalone comments introducing a statement belong after its visual
        // separator. Same-line comments take the normal trailing-comment path.
        var previous = parent.Children.LastOrDefault();
        Append(parts[statementIndex], parent, context);
        var next = previous == null ? parent.Children.FirstOrDefault() : previous.NextSibling();
        var destination = next?.Name == ENAME_SQL_STATEMENT ? next : parent;
        var first = destination == parent ? next : destination.Children.FirstOrDefault();
        var comments = new NodeImpl { Name = ENAME_SQL_STATEMENT };
        for (int i = start; i < statementIndex; i++)
        {
            Append(parts[i], comments, context);
        }

        foreach (var comment in comments.Children.ToArray())
        {
            comments.RemoveChild(comment);
            if (first == null)
            {
                destination.AddChild(comment);
            }
            else
            {
                destination.InsertChildBefore(comment, first);
            }
        }

        return statementIndex;
    }

    private void AppendExpression(SqlSegment segment, Node parent, string context)
    {
        var parts = segment.Parts;
        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            if (!IsKeyword(part, "between"))
            {
                i = AppendPart(parts, i, parent, context);
                continue;
            }

            int separator = i + 1;
            while (separator < parts.Count && !IsKeyword(parts[separator], "and"))
            {
                separator++;
            }

            if (separator == parts.Count)
            {
                i = AppendPart(parts, i, parent, context);
                continue;
            }

            var between = Add(parent, ENAME_BETWEEN_CONDITION);
            var open = Add(between, ENAME_CONTAINER_OPEN);
            Append(part, open, "");
            var lower = Add(between, ENAME_BETWEEN_LOWERBOUND);
            for (int bound = i + 1; bound < separator; bound++)
            {
                bound = AppendPart(parts, bound, lower, context, separator);
            }

            var close = Add(between, ENAME_CONTAINER_CLOSE);
            Append(parts[separator], close, "");
            var upper = Add(between, ENAME_BETWEEN_UPPERBOUND);
            i = separator + 1;
            while (i < parts.Count && (parts[i].Kind != "binary_operator" ||
                (!string.Equals(parts[i].Text, "and", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(parts[i].Text, "or", StringComparison.OrdinalIgnoreCase))))
            {
                i = AppendPart(parts, i, upper, context);
                i++;
            }

            i--;
        }
    }

    private int AppendPart(IReadOnlyList<SqlSegment> parts, int index, Node parent, string context, int? limit = null)
    {
        if (!_useTsqlFormattingRules && parts[index].Kind == "quoted_literal")
        {
            int end = limit ?? parts.Count;
            int lastLiteral = index;
            for (int i = index + 1; i < end; i++)
            {
                if (parts[i].Kind == "quoted_literal")
                {
                    lastLiteral = i;
                }
                else if (!IsTrivia(parts[i]))
                {
                    break;
                }
            }

            if (lastLiteral > index)
            {
                // PostgreSQL requires a newline between adjacent string constants.
                // Keep the whole sequence, including comments and source whitespace.
                var raw = string.Concat(parts.Skip(index).Take(lastLiteral - index + 1).Select(part => part.Raw()));
                Add(parent, ENAME_OTHERNODE, raw);
                return lastLiteral;
            }
        }

        if (parts[index].Kind == "bracketed")
        {
            for (int i = index - 1; i >= 0; i--)
            {
                if (!IsWhitespace(parts[i]) && !parts[i].Kind.EndsWith("comment", StringComparison.Ordinal))
                {
                    if (IsKeyword(parts[i], "in"))
                    {
                        context = "in_list";
                    }

                    break;
                }
            }
        }

        Append(parts[index], parent, context);
        return index;
    }

    private void AppendStatementParts(SqlSegment segment, Node parent)
    {
        var firstClause = Add(parent, ENAME_SQL_CLAUSE);
        var current = firstClause;
        foreach (var part in segment.Parts)
        {
            if ((_useTsqlFormattingRules && segment.Kind == "delete_statement" && IsKeyword(part, "from") && current == firstClause) ||
                (segment.Kind == "merge_insert_clause" && IsKeyword(part, "values")))
            {
                current = Add(parent, ENAME_SQL_CLAUSE);
                Append(part, current, segment.Kind);
            }
            else if (_useTsqlFormattingRules && segment.Kind == "insert_statement" && part.Kind == "post_table_expression")
            {
                current = Add(parent, ENAME_SQL_CLAUSE);
                AppendParts(part, current, "");
            }
            else if (_useTsqlFormattingRules && segment.Kind == "insert_statement" && part.Kind == "bracketed" &&
                     current.Children.FirstOrDefault(node => node.Name != ENAME_WHITESPACE)?.TextValue is string starter &&
                     starter.Equals("WITH", StringComparison.OrdinalIgnoreCase))
            {
                Append(part, current, "");
            }
            else if (part.Kind is "select_statement" or "with_compound_statement" or "set_expression" ||
                     (_useTsqlFormattingRules && segment.Kind == "insert_statement" && part.Kind == "execute_script_statement"))
            {
                Append(part, parent, segment.Kind);
                current = parent.Children.Last();
            }
            else if (part.Kind is "values_clause" or "from_clause" or "where_clause" or "set_clause_list"
                or "returning_clause" or "output_clause" or "on_conflict_clause" or "upsert_clause")
            {
                Append(part, parent, segment.Kind);
                current = parent.Children.Last();
            }
            else
            {
                Append(part, current, segment.Kind);
            }
        }
    }

    private void AppendOutputClause(SqlSegment segment, Node parent)
    {
        var clause = Add(parent, ENAME_SQL_CLAUSE);
        bool inTarget = false;
        foreach (var part in segment.Parts)
        {
            if (IsKeyword(part, "into"))
            {
                clause = Add(parent, ENAME_SQL_CLAUSE);
                inTarget = true;
            }

            var context = inTarget ? "function" : segment.Kind;
            Append(part, clause, context);
        }
    }

    private void AppendLimit(SqlSegment segment, Node parent)
    {
        var clause = Add(parent, ENAME_SQL_CLAUSE);
        foreach (var part in segment.Parts)
        {
            if (part.Kind == "keyword" && string.Equals(part.Text, "offset", StringComparison.OrdinalIgnoreCase))
            {
                clause = Add(parent, ENAME_SQL_CLAUSE);
            }

            Append(part, clause, segment.Kind);
        }
    }

    private Node AppendFrom(SqlSegment segment, Node parent, Node clause)
    {
        foreach (var part in segment.Parts)
        {
            if (part.Kind == "from_expression")
            {
                clause = AppendFrom(part, parent, clause);
            }
            else if (part.Kind == "join_clause")
            {
                if (_useTsqlFormattingRules)
                {
                    clause = AppendTsqlJoinLayout(part, parent);
                }
                else
                {
                    clause = Add(parent, ENAME_SQL_CLAUSE);
                    AppendJoin(part, clause);
                }
            }
            else if (_useTsqlFormattingRules && part.Kind == "from_pivot_expression")
            {
                clause = Add(parent, ENAME_SQL_CLAUSE);
                Append(part, clause, segment.Kind);
            }
            else if (_useTsqlFormattingRules && HasTemporalQuery(part))
            {
                if (part.Kind == "temporal_query")
                {
                    clause = Add(parent, ENAME_SQL_CLAUSE);
                    AppendParts(part, clause, part.Kind);
                }
                else
                {
                    clause = AppendFrom(part, parent, clause);
                }
            }
            else
            {
                Append(part, clause, segment.Kind);
            }
        }

        return clause;
    }

    private void AppendJoin(SqlSegment segment, Node parent)
    {
        int index = 0;
        if (_useTsqlFormattingRules)
        {
            var prefix = ReadJoinPrefix(segment.Parts);
            index = prefix.Length;
            if (index != 0)
            {
                var compound = Add(parent, ENAME_COMPOUNDKEYWORD);
                compound.SetAttribute(ANAME_SIMPLETEXT, prefix.Keywords);
                AppendRange(segment.Parts, 0, index, compound);
            }
        }

        for (; index < segment.Parts.Count; index++)
        {
            var part = segment.Parts[index];
            Append(part, parent, "join_clause");
        }
    }

    private static (int Length, string Keywords) ReadJoinPrefix(IReadOnlyList<SqlSegment> parts)
    {
        var keywords = new List<string>();
        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            if (part.Kind == "keyword" && part.Text != null)
            {
                keywords.Add(part.Text);
                if (IsKeyword(part, "join"))
                {
                    var joined = string.Join(" ", keywords);
                    return (i + 1, joined);
                }
            }
            else if (!IsTrivia(part))
            {
                break;
            }
        }

        return (0, "");
    }

    private static bool IsTsqlBuiltInFunction(string name)
    {
        var builtIn = SqlServerFormattingKeywords.IsBuiltInFunction(name);
        return builtIn;
    }

    private void AppendBrackets(SqlSegment segment, Node parent, string context)
    {
        var parts = segment.Parts;
        if (parts[0].Text != "(" || parts[^1].Text != ")")
        {
            Add(parent, ENAME_OTHERNODE, segment.Raw());
            return;
        }

        var name = ENAME_EXPRESSION_PARENS;
        switch (context)
        {
            case "function":
            case "function_contents":
                name = ENAME_FUNCTION_PARENS;
                break;
            case "data_type":
            case "bracketed_arguments":
                name = ENAME_DDLDETAIL_PARENS;
                break;
            case "create_table_statement":
            case "insert_statement":
            case "merge_insert_clause":
            case "values_clause":
                name = ENAME_DDL_PARENS;
                break;
            case "in_list":
                name = ENAME_IN_PARENS;
                break;
            case "cte_body":
                name = ENAME_SELECTIONTARGET_PARENS;
                break;
        }

        var bracket = Add(parent, name);
        for (int i = 1; i < parts.Count - 1; i++)
        {
            Append(parts[i], bracket, "");
        }
    }

    private void AppendMerge(SqlSegment segment, Node parent)
    {
        var clause = Add(parent, ENAME_SQL_CLAUSE);
        var merge = Add(clause, ENAME_MERGE_CLAUSE);
        var current = Add(merge, ENAME_MERGE_TARGET);

        foreach (var part in MergeParts(segment))
        {
            if (IsKeyword(part, "using"))
            {
                current = Add(merge, ENAME_MERGE_USING);
                Append(part, current, "");
            }
            else if (part.Kind == "join_on_condition")
            {
                current = Add(merge, ENAME_MERGE_CONDITION);
                AppendParts(part, current, "");
            }
            else if (part.Kind is "merge_when_matched_clause" or "merge_when_not_matched_clause")
            {
                AppendMergeMatch(part, merge);
                current = merge;
            }
            else if (part.Kind == "output_clause" || (_useTsqlFormattingRules && part.Kind == "option_clause"))
            {
                Append(part, parent, "");
                current = parent.Children.Last();
            }
            else if (_useTsqlFormattingRules && part.Kind == "bracketed" && current.Name == ENAME_MERGE_USING)
            {
                AppendBrackets(part, current, "cte_body");
            }
            else
            {
                Append(part, current, "");
            }
        }
    }

    private static IEnumerable<SqlSegment> MergeParts(SqlSegment segment)
    {
        foreach (var part in segment.Parts)
        {
            if (part.Kind == "merge_match")
            {
                foreach (var child in part.Parts)
                {
                    yield return child;
                }
            }
            else
            {
                yield return part;
            }
        }
    }

    private void AppendMergeMatch(SqlSegment segment, Node parent)
    {
        var when = Add(parent, ENAME_MERGE_WHEN);
        var current = when;
        foreach (var part in segment.Parts)
        {
            if (IsKeyword(part, "then"))
            {
                current = Add(when, ENAME_MERGE_THEN);
                Append(part, current, "");
            }
            else if (part.Kind is "merge_update_clause" or "merge_insert_clause" or "merge_delete_clause")
            {
                var action = Add(current, ENAME_MERGE_ACTION);
                var statement = Add(action, ENAME_SQL_STATEMENT);
                AppendStatementParts(part, statement);
                current = statement.Children.Last();
            }
            else
            {
                Append(part, current, "");
            }
        }

    }

    private void AppendControlFlow(SqlSegment segment, Node parent)
    {
        var clause = Add(parent, ENAME_SQL_CLAUSE);
        var name = ENAME_IF_STATEMENT;
        if (segment.Kind == "while_statement")
        {
            name = ENAME_WHILE_LOOP;
        }

        var control = Add(clause, name);
        var current = control;
        foreach (var part in segment.Parts)
        {
            if (IsKeyword(part, "else"))
            {
                var alternative = Add(control, ENAME_ELSE_CLAUSE);
                var open = Add(alternative, ENAME_CONTAINER_OPEN);
                Append(part, open, "");
                current = Add(alternative, ENAME_CONTAINER_SINGLESTATEMENT);
            }
            else if (part.Kind == "if_clause" && current.Name == ENAME_CONTAINER_SINGLESTATEMENT)
            {
                var statement = Add(current, ENAME_SQL_STATEMENT);
                var nestedClause = Add(statement, ENAME_SQL_CLAUSE);
                control = Add(nestedClause, ENAME_IF_STATEMENT);
                current = control;
                Append(part, current, "");
            }
            else
            {
                if (part.Kind == "statement" && current == control)
                {
                    current = Add(control, ENAME_CONTAINER_SINGLESTATEMENT);
                }

                Append(part, current, "");
            }
        }
    }

    private bool AppendTryCatch(SqlSegment segment, Node parent)
    {
        var parts = segment.Parts;
        int endTry = FindKeyword(parts, 0, "end");
        int beginCatch = FindKeyword(parts, endTry + 1, "begin");
        int endCatch = FindKeyword(parts, beginCatch + 1, "end");
        if (endTry < 0 || beginCatch < 0 || endCatch < 0)
        {
            return false;
        }

        AppendTryOrCatch(parts, 0, endTry, beginCatch, parent, ENAME_TRY_BLOCK);
        AppendTryOrCatch(parts, beginCatch, endCatch, parts.Count, parent, ENAME_CATCH_BLOCK);
        return true;
    }

    private void AppendTryOrCatch(
        IReadOnlyList<SqlSegment> parts, int start, int end, int stop, Node parent, string name)
    {
        string modifier = "try";
        if (name == ENAME_CATCH_BLOCK)
        {
            modifier = "catch";
        }

        int openEnd = FindKeyword(parts, start + 1, modifier);
        int closeEnd = FindKeyword(parts, end + 1, modifier);
        var statement = Add(parent, ENAME_SQL_STATEMENT);
        var clause = Add(statement, ENAME_SQL_CLAUSE);
        var block = Add(clause, name);
        var open = Add(block, ENAME_CONTAINER_OPEN);
        AppendRange(parts, start, openEnd + 1, open);
        var body = Add(block, ENAME_CONTAINER_MULTISTATEMENT);
        AppendRange(parts, openEnd + 1, end, body);
        var close = Add(block, ENAME_CONTAINER_CLOSE);
        AppendRange(parts, end, closeEnd + 1, close);
        AppendRange(parts, closeEnd + 1, stop, clause);
    }

    private void AppendRange(IReadOnlyList<SqlSegment> parts, int start, int stop, Node parent)
    {
        AppendSequence(parts, start, stop, parent, "");
    }

    private static int FindKeyword(IReadOnlyList<SqlSegment> parts, int start, string keyword)
    {
        for (int i = start; i < parts.Count; i++)
        {
            if (IsKeyword(parts[i], keyword))
            {
                return i;
            }
        }

        return -1;
    }

    private void AppendBlock(SqlSegment segment, Node parent)
    {
        var parts = segment.Parts;
        if (!IsKeyword(parts[0], "begin") || !IsKeyword(parts[^1], "end"))
        {
            AppendParts(segment, parent, "");
            return;
        }

        var clause = Add(parent, ENAME_SQL_CLAUSE);
        var block = Add(clause, ENAME_BEGIN_END_BLOCK);
        var open = Add(block, ENAME_CONTAINER_OPEN);
        Append(parts[0], open, "");
        var body = Add(block, ENAME_CONTAINER_MULTISTATEMENT);
        AppendRange(parts, 1, parts.Count - 1, body);

        var close = Add(block, ENAME_CONTAINER_CLOSE);
        Append(parts[^1], close, "");
    }

    private void AppendCte(SqlSegment segment, Node parent)
    {
        var alias = Add(parent, ENAME_CTE_ALIAS);
        var current = alias;
        foreach (var part in segment.Parts)
        {
            if (part.Kind == "keyword" && string.Equals(part.Text, "as", StringComparison.OrdinalIgnoreCase))
            {
                var asBlock = Add(parent, ENAME_CTE_AS_BLOCK);
                var open = Add(asBlock, ENAME_CONTAINER_OPEN);
                Append(part, open, "");
                current = Add(asBlock, ENAME_CONTAINER_GENERALCONTENT);
            }
            else
            {
                Append(part, current, "cte_body");
            }
        }
    }

    private void AppendCase(SqlSegment segment, Node parent)
    {
        var block = Add(parent, ENAME_CASE_STATEMENT);
        var open = Add(block, ENAME_CONTAINER_OPEN);
        var current = Add(block, ENAME_CASE_INPUT);
        Append(segment.Parts[0], open, "");
        for (int i = 1; i < segment.Parts.Count - 1; i++)
        {
            var part = segment.Parts[i];
            if (part.Kind == "when_clause" || part.Kind == "else_clause")
            {
                current = AppendCaseBranch(part, block);
            }
            else
            {
                Append(part, current, "");
            }
        }

        var close = Add(block, ENAME_CONTAINER_CLOSE);
        Append(segment.Parts[^1], close, "");
    }

    private Node AppendCaseBranch(SqlSegment segment, Node parent)
    {
        var name = ENAME_CASE_WHEN;
        if (segment.Kind == "else_clause")
        {
            name = ENAME_CASE_ELSE;
        }

        var branch = Add(parent, name);
        var open = Add(branch, ENAME_CONTAINER_OPEN);
        Append(segment.Parts[0], open, "");
        var body = Add(branch, ENAME_CONTAINER_GENERALCONTENT);
        foreach (var part in segment.Parts.Skip(1))
        {
            if (part.Kind == "keyword" && string.Equals(part.Text, "then", StringComparison.OrdinalIgnoreCase))
            {
                var then = Add(branch, ENAME_CASE_THEN);
                var thenOpen = Add(then, ENAME_CONTAINER_OPEN);
                Append(part, thenOpen, "");
                body = Add(then, ENAME_CONTAINER_GENERALCONTENT);
            }
            else
            {
                Append(part, body, "");
            }
        }
        return body;
    }

    private void AppendToken(string kind, string text, Node parent)
    {
        if (text.Length == 0)
        {
            return;
        }

        var name = ENAME_OTHERNODE;
        switch (kind)
        {
            case "whitespace":
            case "newline":
                name = ENAME_WHITESPACE;
                break;
            case "procedural_statement_terminator":
            case "statement_terminator":
            case "semicolon":
                name = ENAME_SEMICOLON;
                if (parent.Name == ENAME_SQL_ROOT)
                {
                    var trailingClause = LastStatementClause(parent);
                    if (trailingClause != null)
                    {
                        parent = trailingClause.Parent!;
                    }
                    else if (_useTsqlFormattingRules && !parent.Children.Any(child => child.Name == ENAME_SQL_STATEMENT))
                    {
                        parent = Add(Add(parent, ENAME_SQL_STATEMENT), ENAME_SQL_CLAUSE);
                    }
                }

                if (parent.Name is ENAME_SQL_ROOT or ENAME_CONTAINER_MULTISTATEMENT or ENAME_CONTAINER_SINGLESTATEMENT &&
                    parent.Children.LastOrDefault()?.Name == ENAME_SQL_STATEMENT)
                {
                    parent = parent.Children.Last();
                }

                var lastClause = parent.Children.LastOrDefault();
                if (kind == "procedural_statement_terminator")
                {
                    lastClause = parent.Children.LastOrDefault(child => child.Name != ENAME_WHITESPACE);
                }

                if (parent.Name == ENAME_SQL_STATEMENT && lastClause?.Name == ENAME_SQL_CLAUSE)
                {
                    parent = lastClause;
                    var merge = parent.Children.LastOrDefault();
                    if (merge?.Name == ENAME_MERGE_CLAUSE)
                    {
                        var actionClause = merge.Children.LastOrDefault(node => node.Name == ENAME_MERGE_WHEN)?
                            .ChildByName(ENAME_MERGE_THEN)?.ChildByName(ENAME_MERGE_ACTION)?
                            .ChildByName(ENAME_SQL_STATEMENT)?.Children.LastOrDefault();
                        if (actionClause != null)
                        {
                            parent = actionClause;
                        }
                    }
                }

                if (_useTsqlFormattingRules)
                {
                    parent = FindProceduralTerminatorTarget(parent);
                }

                break;
            case "comma":
                name = ENAME_COMMA;
                break;
            case "dot":
            case "casting_operator":
            case "scope_resolution_operator":
                name = ENAME_PERIOD;
                break;
            case "keyword":
            case "null_literal":
            case "boolean_literal":
            case "data_type_identifier" when _useTsqlFormattingRules && SqlServerFormattingKeywords.IsDataType(text):
                name = ENAME_OTHERKEYWORD;

                break;
            case "binary_literal":
                if (_useTsqlFormattingRules && text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    name = ENAME_BINARY_VALUE;
                }

                break;
            case "numeric_literal":
            case "integer_literal":
                if (_useTsqlFormattingRules)
                {
                    name = ENAME_NUMBER_VALUE;
                }

                break;
            case "binary_operator":
                var op = text.ToUpperInvariant();
                if (op is "AND" or "OR")
                {
                    var operation = Add(parent, op == "AND" ? ENAME_AND_OPERATOR : ENAME_OR_OPERATOR);
                    Add(operation, ENAME_OTHERKEYWORD, text);
                    return;
                }

                name = ENAME_OTHEROPERATOR;
                break;
            case "raw_comparison_operator":
            case "comparison_operator":
            case "star":
                name = ENAME_OTHEROPERATOR;
                break;
            case "inline_comment":
                if (_useTsqlFormattingRules && parent.Name == ENAME_SQL_ROOT)
                {
                    parent = LastStatementClause(parent) ?? parent;
                }

                name = ENAME_RAW_SINGLELINE_COMMENT;
                if (_useTsqlFormattingRules)
                {
                    if (text.StartsWith("--", StringComparison.Ordinal))
                    {
                        name = ENAME_COMMENT_SINGLELINE;
                        text = text[2..];
                    }
                    else if (text.StartsWith("//", StringComparison.Ordinal))
                    {
                        name = ENAME_COMMENT_SINGLELINE_CSTYLE;
                        text = text[2..];
                    }
                }

                break;
            case "block_comment":
                if (text.StartsWith("/*", StringComparison.Ordinal) && text.EndsWith("*/", StringComparison.Ordinal))
                {
                    name = ENAME_COMMENT_MULTILINE;
                    text = text[2..^2];
                }

                break;
            case "quoted_literal":
                if (_useTsqlFormattingRules && text.StartsWith("n'", StringComparison.OrdinalIgnoreCase))
                {
                    text = "N" + text[1..];
                }

                break;
        }

        Add(parent, name, text);
    }

    private static bool IsKeyword(SqlSegment segment, string value)
    {
        var matches = segment.Kind == "keyword" &&
            string.Equals(segment.Text, value, StringComparison.OrdinalIgnoreCase);
        return matches;
    }

    private static bool IsTrivia(SqlSegment segment)
    {
        var isTrivia = IsWhitespace(segment) || segment.Kind.EndsWith("comment", StringComparison.Ordinal);
        return isTrivia;
    }

    private static bool IsWhitespace(SqlSegment segment)
    {
        var isWhitespace = segment.Kind is "whitespace" or "newline" or "end_of_file";
        return isWhitespace;
    }

    private static Node Add(Node parent, string name, string? text = null)
    {
        var child = new NodeImpl { Name = name, TextValue = text };
        parent.AddChild(child);
        return child;
    }

    private void CheckDepth(Node node, int depth)
    {
        if (depth > MAX_NESTING_DEPTH)
        {
            throw new FormatException("SQL nesting exceeds the Right Way formatter limit.");
        }

        foreach (var child in node.Children)
        {
            CheckDepth(child, depth + 1);
        }
    }
}
